using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using CommunityFrontier.Core;

namespace CommunityFrontier.Launcher;

public sealed record AccountStatus(bool DiscordConnected, bool ServerJoined, bool RockstarLinked, string? RockstarName, bool AccessEnabled, string Stage, string? ErrorCategory, bool ReceiverAvailable);

public sealed class FairPlayClient : IDisposable
{
    private readonly LauncherSettings options;
    private readonly SessionStore store;
    private readonly HttpClient http;
    private string? session;
    private DateTimeOffset sessionExpiry;
    public FairPlayClient(LauncherSettings options, string localRoot, HttpMessageHandler? handler = null)
    {
        this.options = options;
        store = new SessionStore(localRoot, options.BackendUrl, options.CommunityId);
        http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(20) };
        var saved = store.Load();
        session = saved?.Token; sessionExpiry = saved?.ExpiresAt ?? default;
    }
    public bool Connected => session != null && sessionExpiry > DateTimeOffset.UtcNow;
    public async Task<AccountStatus> GetAccountStatus(CancellationToken ct)
    {
        if (!Connected) throw new FriendlyException("Sign in with Discord to check your FairPlay account.");
        if (options.CommunityId is not Guid community) throw new FriendlyException("The FairPlay server has not been configured in this build yet.");
        using var request = Request(HttpMethod.Get, $"api/v1/communities/{community:D}/account-status");
        using var response = await http.SendAsync(request, ct); await Check(response);
        return await response.Content.ReadFromJsonAsync<AccountStatus>(ct) ?? throw new FriendlyException("Your account status is temporarily unavailable.");
    }
    private Uri Endpoint(string path)
    {
        if (!Uri.TryCreate(options.BackendUrl, UriKind.Absolute, out var root) || root.Scheme != "https" || root.UserInfo.Length > 0 || root.Query.Length > 0 || root.Fragment.Length > 0 || root.AbsolutePath != "/")
            throw new FriendlyException("Discord sign-in is not configured yet. The server address must be set by the FairPlay owner.");
        return new(root, path);
    }
    public async Task SignIn(CancellationToken ct)
    {
        var verifier = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var verifierHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(verifier)));
        using var start = await http.PostAsJsonAsync(Endpoint("auth/discord/start"), new { verifierHash }, ct);
        await Check(start);
        var flow = await start.Content.ReadFromJsonAsync<LoginFlow>(ct) ?? throw new FriendlyException("Sign-in could not start.");
        var browser = new Uri(flow.BrowserUrl);
        var expected = Endpoint("auth/discord/begin");
        if (browser.Scheme != expected.Scheme || browser.Authority != expected.Authority || browser.AbsolutePath != expected.AbsolutePath || browser.UserInfo.Length != 0)
            throw new FriendlyException("The server returned an invalid sign-in address.");
        Process.Start(new ProcessStartInfo(browser.AbsoluteUri) { UseShellExecute = true });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromMinutes(5));
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(3), timeout.Token);
            using var finish = await http.PostAsJsonAsync(Endpoint("auth/discord/finish"), new { flowId = flow.FlowId, verifier }, timeout.Token);
            if (finish.StatusCode == HttpStatusCode.Accepted) continue;
            await Check(finish);
            var result = await finish.Content.ReadFromJsonAsync<LoginSession>(timeout.Token) ?? throw new FriendlyException("Sign-in could not finish.");
            session = result.Token; sessionExpiry = result.ExpiresAt;
            try { store.Save(new(session, sessionExpiry)); }
            catch (Exception e) when (e is System.IO.IOException or UnauthorizedAccessException or CryptographicException)
            { throw new FriendlyException("Signed in for this session, but Windows could not securely remember your sign-in. You may need to sign in again after closing the launcher."); }
            return;
        }
    }
    public async Task SignOut(CancellationToken ct)
    {
        try { if (session != null) { using var request = Request(HttpMethod.Post, "auth/logout"); using var response = await http.SendAsync(request, ct); await Check(response); } }
        finally { session = null; sessionExpiry = default; store.Clear(); }
    }
    public async Task<string> Allocate(CancellationToken ct)
    {
        if (!Connected) throw new FriendlyException("Sign in with Discord first, then submit your Red Dead name for Support approval in the FairPlay Discord server.");
        if (options.CommunityId is not Guid community) throw new FriendlyException("The FairPlay community has not been configured yet.");
        using var request = Request(HttpMethod.Post, "api/v1/lobbies/allocate"); request.Content = JsonContent.Create(new { communityId = community });
        using var response = await http.SendAsync(request, ct); await Check(response);
        var result = await response.Content.ReadFromJsonAsync<Allocation>(ct) ?? throw new FriendlyException("The lobby is unavailable. Try again shortly.");
        if (result.LeaseExpiresAt <= DateTimeOffset.UtcNow || result.CredentialExpiresAt <= DateTimeOffset.UtcNow) throw new FriendlyException("Your lobby session expired. Press Play again.");
        return result.Identifier;
    }
    private HttpRequestMessage Request(HttpMethod method, string path)
    {
        var request = new HttpRequestMessage(method, Endpoint(path)); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session); return request;
    }
    private async Task Check(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode) return;
        if (response.StatusCode == HttpStatusCode.Unauthorized) { session = null; sessionExpiry = default; store.Clear(); throw new FriendlyException("Your Discord sign-in expired. Sign in again."); }
        if (response.StatusCode == HttpStatusCode.Forbidden) throw new FriendlyException("Fair Play access is not ready. Check your verification status in Discord, or contact support if your access is on hold.");
        if (response.StatusCode == HttpStatusCode.Conflict) throw new FriendlyException("No FairPlay lobby space is available. Try again shortly.");
        await Task.CompletedTask; // Do not echo backend response bodies or credentials into diagnostics.
        throw new FriendlyException("FairPlay could not finish that step. Try again shortly. Original Settings and restoration are still available.");
    }
    public void Dispose() { session = null; http.Dispose(); }
    private sealed record LoginFlow(Guid FlowId, string BrowserUrl);
    private sealed record LoginSession(string Token, DateTimeOffset ExpiresAt);
    private sealed record Allocation(string Identifier, DateTimeOffset LeaseExpiresAt, DateTimeOffset CredentialExpiresAt);
}
