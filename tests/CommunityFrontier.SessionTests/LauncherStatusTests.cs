using CommunityFrontier.Core;
using CommunityFrontier.Launcher;
using System.IO;

static class LauncherStatusTests
{
    public static async Task Run(string root)
    {
        void Check(bool value, string message) { if (!value) throw new Exception(message); Console.WriteLine("PASS " + message); }
        var client = new TestAccountClient(); var files = new TestFiles(); var runner = new TestRunner(); var validator = new TestValidator();
        var activity = new TestActivity();
        var vm = new MainViewModel(new(), new(root), new TestDetector(), validator, files, new(files, new StartupMetaFormatter(), runner, validator), new NullEventLog(), client, activity);
        Check(vm.DiscordActivityEnabled && activity.Enabled, "Launcher starts activity from its saved preference");
        vm.DiscordActivityEnabled = false;
        Check(!activity.Enabled && !new LocalSettings(root).LoadDiscordActivity(), "Settings toggle disables activity and persists the choice");
        vm.DiscordActivityEnabled = true;
        Check(activity.Enabled, "Settings toggle can restore activity");
        vm.SelectedInstallation = new(root, GamePlatform.Steam, Path.Combine(root, "RDR2.exe"), "fixture");
        await vm.RefreshAccountAsync();
        Check(vm.FairPlayAvailable && vm.RockstarStatus.Contains("__Rider__"), "Verified account retains underscores and unlocks Fair Play");
        var delayed = new TaskCompletionSource<AccountStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Read = ct => delayed.Task.WaitAsync(ct);
        var pending = vm.RefreshAccountAsync();
        Check(!vm.IsBusy && vm.FairPlayAvailable && vm.RockstarStatus.Contains("Verified"), "Background refresh preserves the verified display without global busy state");
        Check(vm.AccountRefreshLabel.Contains("Checking"), "Background refresh is visibly identified");
        vm.OriginalSettingsSelected = true;
        Check(vm.PlayCommand.CanExecute(null), "Original Settings stays available during background refresh");
        delayed.SetException(new System.Net.Http.HttpRequestException("fixture outage")); await pending;
        Check(!vm.FairPlayAvailable && vm.RockstarStatus == "Access check unavailable" && vm.AccountSummary.Contains("still saved"), "Outage is distinct from loss of verification and preserves sign-in");
        Check(vm.PlayCommand.CanExecute(null), "Original Settings stays available during account outage");
        client.Read = _ => Task.FromResult(TestAccountClient.Verified); await vm.RefreshAccountAsync(); vm.FairPlaySelected = true;
        client.Read = _ => Task.FromResult(TestAccountClient.Verified with { AccessEnabled = false, Stage = "REVIEW_PENDING" });
        vm.PlayCommand.Execute(null);
        for (var i = 0; vm.IsBusy && i < 100; i++) await Task.Delay(10);
        Check(client.Allocations == 0 && runner.Launches == 0 && files.Writes == 0 && vm.RockstarStatus == "Awaiting verification", "Play rechecks access and never allocates or writes a key after fresh denial");
        var closing = new TaskCompletionSource<AccountStatus>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.Read = ct => closing.Task.WaitAsync(ct); var duringClose = vm.RefreshAccountAsync();
        vm.Close(); await duringClose;
        Check(client.Disposed && !vm.IsBusy, "Closing cancels an account refresh without blocking shutdown");
        Check(activity.Disposed, "Closing the launcher clears its Discord activity service");
    }
    sealed class TestActivity : IDiscordActivity
    {
        public bool Enabled, Disposed;
        public void SetEnabled(bool enabled) => Enabled = enabled;
        public void Dispose() => Disposed = true;
    }
    sealed class TestAccountClient : IFairPlayClient
    {
        public static readonly AccountStatus Verified = new(true, true, true, "__Rider__", true, "COMPLETE", null, false);
        public Func<CancellationToken, Task<AccountStatus>> Read = _ => Task.FromResult(Verified);
        public bool Connected => !Disposed;
        public bool Disposed; public int Allocations;
        public Task<AccountStatus> GetAccountStatus(CancellationToken ct) => Read(ct);
        public Task<bool> IsCurrentKey(string fingerprint, CancellationToken ct) => Task.FromResult(true);
        public Task SignIn(CancellationToken ct) => Task.CompletedTask;
        public Task SignOut(CancellationToken ct) => Task.CompletedTask;
        public Task<string> Allocate(CancellationToken ct) { Allocations++; return Task.FromResult("fixture"); }
        public void Dispose() => Disposed = true;
    }
    sealed class TestValidator : IGameInstallationValidator { public bool IsValid(string path) => true; }
    sealed class TestDetector : IGameInstallationDetector
    {
        public Task<IReadOnlyList<DetectedGameInstallation>> DetectAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<DetectedGameInstallation>>([]);
        public Task<IReadOnlyList<DetectedGameInstallation>> ResolveAsync(string selection, CancellationToken token = default) => DetectAsync(token);
        public Task<IReadOnlyList<DetectedGameInstallation>> SearchAsync(IProgress<string> progress, CancellationToken token) => DetectAsync(token);
    }
    sealed class TestRunner : IGameRunner
    {
        public int Launches;
        public void EnsureStopped() { }
        public Task LaunchAsync(DetectedGameInstallation installation, CancellationToken token) { Launches++; return Task.CompletedTask; }
    }
    sealed class TestFiles : ILobbyConfigurationWriter
    {
        public int Writes;
        public Task ApplyAsync(string gamePath, byte[] configuration, PlayMode mode, CancellationToken token = default) { Writes++; return Task.CompletedTask; }
        public Task RestoreAsync(string gamePath, bool restoreExternal = false, CancellationToken token = default) => Task.CompletedTask;
        public Task RestoreAllAsync(CancellationToken token = default) => Task.CompletedTask;
        public Task<IReadOnlyList<ModificationManifest>> ReadStateAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<ModificationManifest>>([]);
        public Task<string> DiagnoseAsync(string gamePath, CancellationToken token = default) => Task.FromResult("Healthy fixture");
    }
}
