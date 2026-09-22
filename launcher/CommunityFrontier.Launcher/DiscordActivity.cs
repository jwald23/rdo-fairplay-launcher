using CommunityFrontier.Core;
using DiscordRPC;

namespace CommunityFrontier.Launcher;

public interface IDiscordActivity : IDisposable
{
    void SetEnabled(bool enabled);
}

public interface IDiscordActivityClient : IDisposable
{
    bool Initialize();
    void SetPresence(RichPresence presence);
}

/// <summary>Optional local Discord IPC; never receives account details or lobby keys.</summary>
public sealed class DiscordActivity(IEventLog log, Func<IDiscordActivityClient>? createClient = null) : IDiscordActivity
{
    public const string ApplicationId = "1551312894396862556";
    private IDiscordActivityClient? client;
    private bool disposed;

    public static RichPresence CreatePresence() => new()
    {
        Details = "Private lobbies for Red Dead Online",
        State = "Launcher open",
        Assets = new Assets { LargeImageKey = "logo", LargeImageText = "RDO FairPlay" },
        Buttons = [
            new DiscordRPC.Button { Label = "Get FairPlay", Url = "https://rdofairplay.com" },
            new DiscordRPC.Button { Label = "Join Discord", Url = MainViewModel.DiscordInvite }
        ]
    };

    public void SetEnabled(bool enabled)
    {
        if (disposed) return;
        if (!enabled) { Stop(); return; }
        if (client is not null) return;
        try
        {
            client = createClient?.Invoke() ?? new DiscordActivityRpcClient();
            // Queue before connecting; the library restores this after Discord reconnects.
            client.SetPresence(CreatePresence());
            if (!client.Initialize()) Stop();
        }
        catch (Exception)
        {
            // Activity is cosmetic. Discord failures must never interrupt play or recovery.
            Stop();
            log.Write("discord_activity", "unavailable");
        }
    }

    private void Stop()
    {
        var previous = client;
        client = null;
        try { previous?.Dispose(); }
        catch (Exception) { log.Write("discord_activity", "close_failed"); }
    }

    public void Dispose() { if (disposed) return; disposed = true; Stop(); }

}

public sealed class DiscordActivityRpcClient(int pipe = -1) : IDiscordActivityClient
{
    // Reconnecting must resend the same card even when its text has not changed.
    private readonly DiscordRpcClient rpc = new(DiscordActivity.ApplicationId, pipe) { ShutdownOnly = true, SkipIdenticalPresence = false };
    public bool Initialize() => rpc.Initialize();
    public void SetPresence(RichPresence presence) => rpc.SetPresence(presence);
    public void Dispose()
    {
        if (rpc.IsDisposed) return;
        try
        {
            if (!rpc.IsInitialized || rpc.CurrentUser is null) return;
            var cleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            void Acknowledge(object sender, DiscordRPC.Message.PresenceMessage message)
            { if (message.Presence is null) cleared.TrySetResult(); }
            rpc.OnPresenceUpdate += Acknowledge;
            try
            {
                // Explicitly clear before shutdown: disposing alone can close before the queued clear is sent.
                rpc.ClearPresence();
                cleared.Task.Wait(TimeSpan.FromSeconds(2));
            }
            finally { rpc.OnPresenceUpdate -= Acknowledge; }
        }
        finally { rpc.Dispose(); }
    }
}
