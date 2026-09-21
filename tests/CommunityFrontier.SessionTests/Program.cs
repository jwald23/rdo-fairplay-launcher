using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using CommunityFrontier.Core;
using CommunityFrontier.Launcher;

var root = Path.Combine(Path.GetTempPath(), "fairplay-session-tests-" + Guid.NewGuid());
Directory.CreateDirectory(root);
var options = new LauncherSettings { BackendUrl = "https://example.invalid", CommunityId = Guid.NewGuid() };
var store = new SessionStore(root, options.BackendUrl, options.CommunityId);
var credential = new SavedSession("fixture-session-not-a-real-token", DateTimeOffset.UtcNow.AddHours(1));
void Check(bool result, string label) { if (!result) throw new Exception(label); Console.WriteLine("PASS " + label); }
try
{
    store.Save(credential);
    Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root, "discord-session.bin"))).Contains(credential.Token), "Credential is encrypted on disk");
    Check(new SessionStore(root, options.BackendUrl, options.CommunityId).Load() == credential, "New instance restores unexpired sign-in");
    Check(new SessionStore(root, "https://other.invalid", options.CommunityId).Load() == null, "Different backend cannot reuse credential");
    Check(new SessionStore(root, options.BackendUrl, Guid.NewGuid()).Load() == null, "Different community cannot reuse credential");
    using (var client = new FairPlayClient(options, root, new Stub(HttpStatusCode.OK))) {
        Check(client.Connected, "Client restores sign-in on restart");
        var account = await client.GetAccountStatus(default);
        Check(account.AccessEnabled && account.RockstarName == "Cowboy", "Restored session refreshes live verification");
    }
    Check(store.Load() != null, "Closing launcher preserves saved session");
    foreach (var current in new[] { true, false })
    {
        using var client = new FairPlayClient(options, root, new KeyStub(current));
        Check(await client.IsCurrentKey(new string('a', 64), default) == current, current ? "Current key confirmed by backend" : "Rotated key reported outdated");
    }
    using (var client = new FairPlayClient(options, root, new Stub(HttpStatusCode.ServiceUnavailable)))
    {
        try { await client.IsCurrentKey(new string('a', 64), default); throw new Exception("Expected unavailable status"); }
        catch (FriendlyException) { Check(true, "Key check outage does not report current"); }
    }
    using (var client = new FairPlayClient(options, root, new Stub(HttpStatusCode.Unauthorized))) {
        try { await client.GetAccountStatus(default); throw new Exception("Expected rejection"); } catch (FriendlyException) { }
        Check(!client.Connected && store.Load() == null, "Revoked session is cleared");
    }
    store.Save(credential);
    using (var client = new FairPlayClient(options, root, new Stub(HttpStatusCode.ServiceUnavailable))) {
        try { await client.GetAccountStatus(default); } catch (FriendlyException) { }
        Check(client.Connected && store.Load() != null, "Temporary outage preserves sign-in");
        try { await client.SignOut(default); } catch (FriendlyException) { }
        Check(!client.Connected && store.Load() == null, "Sign out clears local session even during outage");
    }
    store.Save(credential with { ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(-1) });
    Check(store.Load() == null, "Expired credential is not restored");
    File.WriteAllText(Path.Combine(root, "discord-session.bin"), "corrupt");
    Check(store.Load() == null, "Corrupt credential falls back to sign-in");
    await LauncherStatusTests.Run(Path.Combine(root, "status"));
}
finally { Directory.Delete(root, true); }

sealed class Stub(HttpStatusCode status) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.Headers.Authorization?.Parameter != "fixture-session-not-a-real-token") throw new Exception("Missing restored authorization");
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("""
            {"discordConnected":true,"serverJoined":true,"rockstarLinked":true,"rockstarName":"Cowboy","accessEnabled":true,"stage":"COMPLETE","errorCategory":null,"receiverAvailable":true}
            """, Encoding.UTF8, "application/json") });
    }
}

sealed class KeyStub(bool current) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        if (request.RequestUri?.AbsolutePath != "/api/v1/lobbies/key-status" || request.Method != HttpMethod.Post || request.Headers.Authorization?.Parameter != "fixture-session-not-a-real-token") throw new Exception("Invalid key check request");
        var payload = await request.Content!.ReadAsStringAsync(ct);
        if (!payload.Contains(new string('a', 64)) || !payload.Contains("communityId")) throw new Exception("Missing key check scope");
        return new(HttpStatusCode.OK) { Content = new StringContent(current ? "{\"current\":true}" : "{\"current\":false}", Encoding.UTF8, "application/json") };
    }
}
