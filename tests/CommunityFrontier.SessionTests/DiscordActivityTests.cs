using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using CommunityFrontier.Core;
using CommunityFrontier.Launcher;
using DiscordRPC;

static class DiscordActivityTests
{
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception(message);
        Console.WriteLine("PASS " + message);
    }

    public static async Task Run(string root)
    {
        var preferences = new LocalSettings(root);
        Check(preferences.LoadDiscordActivity(), "New installation enables Discord activity");
        preferences.SaveDiscordActivity(false);
        Check(!new LocalSettings(root).LoadDiscordActivity(), "Discord activity opt-out survives restart");
        preferences.SaveDiscordActivity(true);
        Check(new LocalSettings(root).LoadDiscordActivity(), "Discord activity can be re-enabled persistently");
        File.WriteAllText(Path.Combine(root, "discord-activity.json"), "corrupt");
        Check(!preferences.LoadDiscordActivity(), "Unreadable activity preference does not silently share activity");

        var clients = new List<FakeClient>();
        using var activity = new DiscordActivity(new NullEventLog(), () => { var client = new FakeClient(); clients.Add(client); return client; });
        activity.SetEnabled(false);
        Check(clients.Count == 0, "Disabled activity never opens a Discord connection");
        activity.SetEnabled(true); activity.SetEnabled(true);
        Check(clients.Count == 1 && clients[0].Initialized, "Repeated enable uses one connection");
        activity.SetEnabled(false);
        Check(clients[0].Disposed, "Turning activity off disposes its connection");
        activity.SetEnabled(true); activity.Dispose(); activity.SetEnabled(true);
        Check(clients.Count == 2 && clients[1].Disposed, "Activity can restart but cannot reopen after launcher exit");
        var failed = new FakeClient { FailInitialization = true };
        using var unavailable = new DiscordActivity(new NullEventLog(), () => failed);
        unavailable.SetEnabled(true);
        Check(failed.Disposed, "Failed initialization releases the client without interrupting the launcher");
        using var missing = new DiscordActivity(new NullEventLog(), () => throw new IOException("fixture"));
        missing.SetEnabled(true); missing.SetEnabled(false);
        Check(true, "Unavailable Discord cannot fail launcher startup or shutdown");

        // Reserve the last supported pipe exclusively; fail rather than connect to an existing Discord instance.
        const int pipeNumber = 9;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var token = deadline.Token;
        await using var pipe = new NamedPipeServerStream($"discord-ipc-{pipeNumber}", PipeDirection.InOut, 1,
            PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.FirstPipeInstance);
        using var real = new DiscordActivity(new NullEventLog(), () => new DiscordActivityRpcClient(pipeNumber));
        real.SetEnabled(true);
        await Accept(pipe, token);
        var command = await Read(pipe, token);
        var payload = command.GetProperty("args").GetProperty("activity");
        Check(command.GetProperty("cmd").GetString() == "SET_ACTIVITY" && payload.GetProperty("state").GetString() == "Launcher open",
            "Desktop IPC receives truthful launcher activity");
        Check(payload.GetProperty("assets").GetProperty("large_image").GetString() == "logo", "Desktop IPC receives the uploaded logo asset key");
        var buttons = payload.GetProperty("buttons");
        Check(buttons.GetArrayLength() == 2 && buttons[0].GetProperty("url").GetString() == "https://rdofairplay.com" &&
            buttons[1].GetProperty("url").GetString() == MainViewModel.DiscordInvite, "Desktop IPC receives both public project links");
        Check(!payload.TryGetProperty("party", out var party) || party.ValueKind == JsonValueKind.Null, "Activity has no invented party");
        Check(!payload.TryGetProperty("secrets", out var secrets) || secrets.ValueKind == JsonValueKind.Null, "Activity shares no join secret or lobby key");
        pipe.Disconnect();
        await Accept(pipe, token);
        command = await Read(pipe, token);
        Check(command.GetProperty("args").GetProperty("activity").GetProperty("assets").GetProperty("large_image").GetString() == "logo",
            "Activity reconnects and restores its card after Discord restarts");
        await Send(pipe, new { cmd = "SET_ACTIVITY", nonce = command.GetProperty("nonce").GetString(), data = payload }, token);
        var stopping = Task.Run(() => real.SetEnabled(false));
        var clear = await Read(pipe, token);
        while (clear.GetProperty("args").GetProperty("activity").ValueKind != JsonValueKind.Null)
        {
            // State synchronization may still have a duplicate activity frame in flight.
            await Send(pipe, new { cmd = "SET_ACTIVITY", nonce = clear.GetProperty("nonce").GetString(), data = clear.GetProperty("args").GetProperty("activity") }, token);
            clear = await Read(pipe, token);
        }
        Check(clear.GetProperty("cmd").GetString() == "SET_ACTIVITY" && clear.GetProperty("args").GetProperty("activity").ValueKind == JsonValueKind.Null,
            "Disabling activity sends an explicit clear over Discord IPC");
        await Send(pipe, new { cmd = "SET_ACTIVITY", nonce = clear.GetProperty("nonce").GetString(), data = (object?)null }, token);
        await stopping.WaitAsync(token);
    }

    private static async Task Accept(NamedPipeServerStream pipe, CancellationToken token)
    {
        await pipe.WaitForConnectionAsync(token);
        var handshake = await Read(pipe, token);
        Check(handshake.GetProperty("client_id").GetString() == DiscordActivity.ApplicationId, "IPC handshake uses the FairPlay application ID");
        await Send(pipe, new { cmd = "DISPATCH", evt = "READY", data = new { v = 1,
            user = new { id = "100000000000000000", username = "fixture", discriminator = "0001", avatar = "fixture" },
            config = new { cdn_host = "cdn.discordapp.com", api_endpoint = "//discord.com/api", environment = "production" } } }, token);
    }

    private static async Task<JsonElement> Read(Stream pipe, CancellationToken token)
    {
        var header = new byte[8]; await pipe.ReadExactlyAsync(header, token);
        var length = BitConverter.ToInt32(header, 4);
        if (length is < 0 or > 65536) throw new IOException("Unexpected IPC frame length");
        var bytes = new byte[length]; await pipe.ReadExactlyAsync(bytes, token);
        using var json = JsonDocument.Parse(bytes); return json.RootElement.Clone();
    }

    private static async Task Send(Stream pipe, object value, CancellationToken token)
    {
        var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value));
        var frame = new byte[8 + bytes.Length]; BitConverter.GetBytes(1).CopyTo(frame, 0); BitConverter.GetBytes(bytes.Length).CopyTo(frame, 4);
        bytes.CopyTo(frame, 8);
        await pipe.WriteAsync(frame, token); await pipe.FlushAsync(token);
    }

    private sealed class FakeClient : IDiscordActivityClient
    {
        public bool Initialized, Disposed, FailInitialization;
        public bool Initialize() { if (FailInitialization) throw new IOException("fixture"); return Initialized = true; }
        public void SetPresence(RichPresence presence) { }
        public void Dispose() => Disposed = true;
    }
}
