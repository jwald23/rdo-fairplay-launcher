using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using CommunityFrontier.Core;

namespace CommunityFrontier.Launcher;

public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly LauncherSettings options;
    private readonly LocalSettings local;
    private readonly IGameInstallationDetector detector;
    private readonly IGameInstallationValidator validator;
    private readonly ILobbyConfigurationWriter writer;
    private readonly LaunchCoordinator coordinator;
    private readonly IEventLog log;
    private readonly IFairPlayClient online;
    private readonly CancellationTokenSource lifetime = new();
    private bool closed, accountCheckFailed;
    private int sessionRevision;
    private Task? accountRefresh;
    private bool diagnosticsOpen;
    public bool DiagnosticsOpen { get => diagnosticsOpen; set { diagnosticsOpen = value; Changed(); } }
    public string VersionLabel => "Version " + (typeof(MainViewModel).Assembly.GetName().Version?.ToString(3) ?? "unknown");
    public ICommand ReleasesCommand { get; }
    public bool DiscordConnected => online.Connected;
    public string AccountRefreshLabel => checkingAccount ? "Checking access…" : accountCheckFailed ? "Unable to confirm access — try Refresh status." : lastAccountCheck == default ? "Access has not been checked yet." : $"Access checked {lastAccountCheck.ToLocalTime():h:mm tt}";
    public string FairPlayAccessibleName => "Fair Play — " + (FairPlayAvailable ? "verified and available" : accountCheckFailed ? "access unavailable" : checkingAccount ? "checking access" : "requires verification");
    public string ModeGuidance => FairPlayAvailable ? "Choose Fair Play for the private lobby, or Original Settings for your usual setup." : !online.Connected ? "Sign in for Fair Play. You can use Original Settings without verification." : accountCheckFailed ? "We cannot confirm Fair Play access right now. Original Settings remains available." : VerificationPending ? "Awaiting verification. Original Settings still lets you play online normally." : "Fair Play stays locked until verified. Original Settings lets you play online normally.";
    public string PrimaryAccountLabel => !online.Connected ? "Sign in with Discord" : accountCheckFailed ? "Retry access check" : !FairPlayAvailable ? "Open Discord · Verify" : "Open FairPlay Discord";
    public ICommand PrimaryAccountCommand => !online.Connected ? DiscordCommand : accountCheckFailed ? RefreshAccountCommand : JoinDiscordCommand;
    private AccountStatus? account;
    private bool checkingAccount;
    private string keyStatus = "Installed key · Not checked";
    private string keyColor = "#C8BBA5";
    private DateTimeOffset? lastKeyCheck;
    private int keyRevision;
    public string KeyStatus { get => keyStatus; private set { keyStatus = value; Changed(); } }
    public string KeyColor { get => keyColor; private set { keyColor = value; Changed(); } }
    public string KeyDetail => (lastKeyCheck is null ? "" : $"Last confirmed {lastKeyCheck.Value.ToLocalTime():h:mm tt}. ") + "Checks the installed file, not a running game session.";
    private void SetKey(string text, string color = "#C8BBA5") { KeyStatus = "Installed key · " + text; KeyColor = color; Changed(nameof(KeyDetail)); }
    private async Task RefreshKey()
    {
        var game = selected;
        var revision = ++keyRevision;
        void Update(string text, string color = "#C8BBA5") { if (!closed && revision == keyRevision && selected == game) SetKey(text, color); }
        try
        {
            if (game is null) { Update("Select your game"); return; }
            var state = (await writer.ReadStateAsync()).SingleOrDefault(s => string.Equals(s.GamePath, game.InstallationPath, StringComparison.OrdinalIgnoreCase));
            if (state is null || state.Mode != PlayMode.Community) { Update("Not applied"); return; }
            if (state.Operation != "Applied") { Update("Recovery needed", "#E37C72"); return; }
            var path = PathSafety.Target(game.InstallationPath);
            if (new FileInfo(path).Length > 1024 * 1024) { Update("File changed", "#E37C72"); return; }
            var bytes = await File.ReadAllBytesAsync(path, lifetime.Token);
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));
            var fingerprint = StartupMetaFormatter.Fingerprint(bytes);
            if (!string.Equals(hash, state.GeneratedSha256, StringComparison.OrdinalIgnoreCase) || fingerprint is null) { Update("File changed", "#E37C72"); return; }
            if (!online.Connected) { Update("Sign in to check"); return; }
            if (accountCheckFailed || account is null) { Update("? Unable to check", "#D4B288"); return; }
            if (!FairPlayAvailable) { Update("Verification required to check"); return; }
            var current = await online.IsCurrentKey(fingerprint, lifetime.Token);
            if (closed || selected != game || revision != keyRevision) return;
            var latest = await File.ReadAllBytesAsync(path, lifetime.Token);
            if (!System.Security.Cryptography.SHA256.HashData(latest).AsSpan().SequenceEqual(System.Security.Cryptography.SHA256.HashData(bytes))) { Update("File changed", "#E37C72"); return; }
            lastKeyCheck = DateTimeOffset.Now;
            Update(current ? "✓ Current" : "! Outdated — close Red Dead, then launch Fair Play", current ? "#82BD88" : "#D4B288");
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Net.Http.HttpRequestException or OperationCanceledException or FriendlyException or System.Text.Json.JsonException)
        { if (!closed && selected == game) Update("? Unable to check", "#D4B288"); }
    }
    private DateTimeOffset lastAccountCheck;
    public const string DiscordInvite = "https://discord.gg/Mp6skUnf2b";
    private CancellationTokenSource? login;
    private readonly List<UiCommand> commands = [];
    private CancellationTokenSource? search;
    private bool busy;
    private string status = "Find your game to get started.";
    private string diagnostics = "Choose Check everything for a local diagnostic report.";
    private DetectedGameInstallation? selected;
    private PlayMode mode = PlayMode.Community;
    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<DetectedGameInstallation> Installations { get; } = [];
    public Array Platforms => Enum.GetValues<GamePlatform>();
    public string ProductName => options.ProductName;
    private bool settingsOpen;
    public bool SettingsOpen { get => settingsOpen; private set { settingsOpen = value; Changed(); } }
    public ICommand ToggleSettingsCommand { get; }
    public bool FairPlayAvailable => online.Connected && !accountCheckFailed && account?.AccessEnabled == true;
    public string FairPlayBadge => FairPlayAvailable ? "VERIFIED" : accountCheckFailed ? "UNAVAILABLE" : checkingAccount ? "CHECKING" : "LOCKED";
    public string FairPlayDescription => "Join the FairPlay private lobby with your verified account.";
    public string DiscordStatus => online.Connected ? "Discord linked" : "Discord not linked";
    public string DiscordIcon => online.Connected ? "✓" : "✕";
    public string DiscordColor => online.Connected ? "#82BD88" : "#E37C72";
    public string ServerIcon => !online.Connected ? "✕" : account is null ? "?" : account.ServerJoined ? "✓" : "✕";
    public string ServerColor => !online.Connected ? "#E37C72" : account is null ? "#C8BBA5" : account.ServerJoined ? "#82BD88" : "#E37C72";
    public string ServerStatus => checkingAccount && account is null ? "Checking Discord membership…" : online.Connected && account is null ? "Server status unavailable" : account?.ServerJoined == true && online.Connected ? "FairPlay Discord joined" : "Join the FairPlay Discord";
    private bool VerificationPending => account?.Stage is "REVIEW_PENDING" or "ROLE_SYNC_PENDING";
    public string RockstarIcon => !online.Connected ? "✕" : account is null ? "?" : account.AccessEnabled ? "✓" : VerificationPending ? "…" : "✕";
    public string RockstarColor => accountCheckFailed ? "#D4B288" : !online.Connected ? "#E37C72" : account is null ? "#C8BBA5" : account.AccessEnabled ? "#82BD88" : VerificationPending ? "#C79947" : "#E37C72";
    public string RockstarStatus => accountCheckFailed ? "Access check unavailable" : checkingAccount && account is null ? "Checking verification…" : !online.Connected ? "Sign in to get started" : account is null ? "Verification status unavailable" : account.AccessEnabled ? $"Verified · {account.RockstarName}" : account.ErrorCategory == "ACCESS_HOLD" ? "Fair Play access on hold" : VerificationPending ? "Awaiting verification" : "Verification needed";
    public string AccountSummary => accountCheckFailed ? "We could not confirm your current access. Try Refresh status. Your Discord sign-in is still saved." : checkingAccount && account is null ? "Restoring your sign-in and checking your current FairPlay access." : !online.Connected ? "Sign in, join our Discord, then submit your Red Dead Online username." : account is null ? "Could not check your account. Refresh to try again." : account.ErrorCategory == "ACCESS_HOLD" ? "Fair Play access is on hold. Contact Support in Discord." : !account.ServerJoined ? "Join our Discord to continue." : account.ErrorCategory == "RULES_PENDING" ? "Accept the server rules in Discord to continue." : account.AccessEnabled ? "Ready for Fair Play." : account.Stage == "REVIEW_PENDING" ? "Support is reviewing your Red Dead name. Fair Play unlocks once your Verified role is granted." : account.Stage == "ROLE_SYNC_PENDING" ? "Support approved your name. Awaiting your Verified role before Fair Play unlocks." : account.Stage is "REJECTED" or "CHANGES_REQUESTED" ? "Support reviewed your request. Check Status in Discord for their note and submit a corrected name." : "Use Verify My Account in Discord to submit your Red Dead name for Support approval.";
    public ICommand JoinDiscordCommand { get; }
    public ICommand RefreshAccountCommand { get; }
    public string DiscordButtonLabel => online.Connected ? "Sign out" : "Sign in with Discord";
    public ICommand DiscordCommand { get; }
    public bool IsBusy { get => busy; private set { busy = value; Changed(); Changed(nameof(IsIdle)); RefreshCommands(); } }
    public bool IsIdle => !IsBusy;
    public string Status { get => status; private set { status = value; Changed(); } }
    public string Diagnostics { get => diagnostics; private set { diagnostics = value; Changed(); } }
    public string GameStatus => selected is null ? "Game not selected" : "Ready to play · " + selected.Platform;
    public string InstallationPrompt => Installations.Count > 1 ? "We found more than one installation. Choose the one you play." : "Your Red Dead installation";
    public DetectedGameInstallation? SelectedInstallation
    {
        get => selected;
        set
        {
            selected = value; keyRevision++; lastKeyCheck = null; SetKey(value is null ? "Select your game" : "Refresh to check");
            if (value is not null)
            {
                try { local.SaveGame(value); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Status = "Your game is ready, but its location could not be remembered."; }
            }
            Changed(); Changed(nameof(GameStatus)); Changed(nameof(SelectedPlatform)); RefreshCommands();
        }
    }
    public GamePlatform SelectedPlatform
    {
        get => selected?.Platform ?? GamePlatform.Unknown;
        set
        {
            if (selected is null || selected.Platform == value) return;
            var replacement = selected with { Platform = value, PlatformId = value == GamePlatform.Steam ? "1174180" : null };
            var index = Installations.IndexOf(selected);
            if (index >= 0) Installations[index] = replacement;
            SelectedInstallation = replacement;
        }
    }
    public bool FairPlaySelected { get => mode == PlayMode.Community; set { if (value && FairPlayAvailable) SetMode(PlayMode.Community); } }
    public bool OriginalSettingsSelected { get => mode == PlayMode.Normal; set { if (value) SetMode(PlayMode.Normal); } }
    public string SelectedModeName => mode == PlayMode.Community ? "Fair Play" : "Original Settings";
    public string PlayLabel => mode == PlayMode.Normal ? "Play with Original Settings" : FairPlayAvailable ? "Play Fair Play" : accountCheckFailed ? "Access unavailable" : checkingAccount ? "Checking access…" : !online.Connected ? "Sign in for Fair Play" : "Verification required";
    private void SetMode(PlayMode value)
    {
        mode = value;
        RefreshCommands();
        foreach (var property in new[] { nameof(FairPlaySelected), nameof(OriginalSettingsSelected), nameof(SelectedModeName), nameof(PlayLabel) }) Changed(property);
    }
    public ICommand PlayCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand EmergencyCommand { get; }
    public ICommand UninstallCommand { get; }
    public ICommand DetectCommand { get; }
    public ICommand BrowseCommand { get; }
    public ICommand BrowseExeCommand { get; }
    public ICommand SearchCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand DiagnoseCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand OpenFolderCommand { get; }
    public ICommand OpenSteamCommand { get; }
    public ICommand OpenEpicCommand { get; }
    public ICommand HelpCommand { get; }

    public MainViewModel(LauncherSettings options, LocalSettings local, IGameInstallationDetector detector,
        IGameInstallationValidator validator, ILobbyConfigurationWriter writer, LaunchCoordinator coordinator, IEventLog log, IFairPlayClient? client = null)
    {
        this.options = options; this.local = local; this.detector = detector; this.validator = validator;
        this.writer = writer; this.coordinator = coordinator; this.log = log;
        online = client ?? new FairPlayClient(options, local.Root);
        checkingAccount = online.Connected;
        ToggleSettingsCommand = new UiCommand(() => { SettingsOpen = !SettingsOpen; return Task.CompletedTask; }, () => true);
        JoinDiscordCommand = Command(() => { Open(DiscordInvite); return Task.CompletedTask; });
        var refresh = new UiCommand(RefreshAccount, () => !busy && !checkingAccount);
        commands.Add(refresh); RefreshAccountCommand = refresh;
        ReleasesCommand = new UiCommand(() => { Open("https://github.com/jwald23/rdo-fairplay-launcher/releases/latest"); return Task.CompletedTask; }, () => true);
        DiscordCommand = Command(async () =>
        {
            try
            {
                sessionRevision++;
                if (online.Connected) { await online.SignOut(default); account = null; accountCheckFailed = false; SetKey("Sign in to check"); Status = "Signed out of Discord."; }
                else
                {
                    using var cancellation = new CancellationTokenSource(); login = cancellation; RefreshCommands();
                    Status = "Finish signing in with Discord in your browser. You can cancel below.";
                    await online.SignIn(cancellation.Token);
                    if (accountRefresh is { IsCompleted: false }) await accountRefresh;
                    await RefreshAccount();
                    Status = AccountSummary;
                }
            }
            finally { login = null; NotifyAccount(); RefreshCommands(); }
        });
        PlayCommand = Command(async () =>
        {
            Status = "Getting Red Dead ready…";
            if (mode == PlayMode.Community)
            {
                await RefreshAccount();
                if (!FairPlayAvailable)
                {
                    Status = AccountSummary + " You can still play online using Original Settings.";
                    return;
                }
            }
            var identifier = mode == PlayMode.Community ? await online.Allocate(default) : null;
            await coordinator.PlayAsync(selected!, mode, identifier);
            await RefreshKey();
            Status = "Your game launcher is opening. Choose Online in Red Dead to start playing.";
        }, true, () => mode != PlayMode.Community || FairPlayAvailable);
        RestoreCommand = Command(async () => { await writer.RestoreAsync(selected!.InstallationPath); Restored(); }, true);
        EmergencyCommand = Command(async () => { await writer.RestoreAllAsync(); Restored(); });
        UninstallCommand = Command(async () =>
        {
            await writer.RestoreAllAsync(); Restored();
            Status += " You can now remove the launcher. Keep your Backups folder until you have checked the game.";
        });
        DetectCommand = Command(Detect);
        BrowseCommand = Command(async () =>
        {
            var picker = new OpenFolderDialog { Title = "Choose your Red Dead folder, Steam library, or Games folder" };
            if (picker.ShowDialog() == true) SetFound(await detector.ResolveAsync(picker.FolderName));
        });
        BrowseExeCommand = Command(async () =>
        {
            var picker = new OpenFileDialog { Title = "Choose Red Dead", Filter = "Red Dead Redemption 2|RDR2.exe" };
            if (picker.ShowDialog() == true) SetFound(await detector.ResolveAsync(picker.FileName));
        });
        SearchCommand = Command(async () =>
        {
            using var cancellation = new CancellationTokenSource(); search = cancellation; RefreshCommands();
            try { SetFound(await detector.SearchAsync(new Progress<string>(message => Status = message), cancellation.Token)); }
            finally { search = null; RefreshCommands(); }
        });
        var cancel = new UiCommand(() => { search?.Cancel(); login?.Cancel(); return Task.CompletedTask; }, () => search is not null || login is not null);
        commands.Add(cancel); CancelCommand = cancel;
        DiagnoseCommand = Command(Diagnose);
        CopyCommand = Command(async () => { await Diagnose(); Clipboard.SetText(Diagnostics); Status = "Diagnostic report copied. It contains your game path; review it before sharing."; });
        OpenFolderCommand = Command(() => { Open(selected!.InstallationPath); return Task.CompletedTask; }, true);
        OpenSteamCommand = Command(() => { Open("steam://open/games"); return Task.CompletedTask; });
        OpenEpicCommand = Command(() => { Open("com.epicgames.launcher://library"); return Task.CompletedTask; });
        HelpCommand = Command(() =>
        {
            Status = "In Steam: Library → Red Dead → Manage → Browse local files. In Epic: Library → Red Dead → Manage → installation folder. In Rockstar: Settings → My installed games → Red Dead. Then choose Find my game, or retry automatic detection.";
            return Task.CompletedTask;
        });
    }
    private Task RefreshAccount() => closed ? Task.CompletedTask : accountRefresh is { IsCompleted: false } ? accountRefresh : accountRefresh = RefreshAccountCore();
    public Task RefreshAccountAsync() => RefreshAccount();
    private async Task RefreshAccountCore()
    {
        var revision = sessionRevision;
        checkingAccount = online.Connected; NotifyAccount();
        try
        {
            var result = online.Connected ? await online.GetAccountStatus(lifetime.Token) : null;
            if (!closed && revision == sessionRevision) { account = online.Connected ? result : null; accountCheckFailed = false; lastAccountCheck = DateTimeOffset.UtcNow; }
        }
        catch (Exception e) when (e is System.Net.Http.HttpRequestException or OperationCanceledException or FriendlyException or System.Text.Json.JsonException)
        { if (!closed && revision == sessionRevision) { accountCheckFailed = online.Connected; if (!online.Connected) account = null; } }
        finally { checkingAccount = false; if (!closed) NotifyAccount(); }
        if (!closed) await RefreshKey();
    }
    public Task RefreshAccountOnReturnAsync()
    {
        if (IsBusy || checkingAccount || closed) return Task.CompletedTask;
        if (!online.Connected) { account = null; accountCheckFailed = false; SetKey("Sign in to check"); NotifyAccount(); return Task.CompletedTask; }
        return DateTimeOffset.UtcNow - lastAccountCheck >= TimeSpan.FromSeconds(10) ? RefreshAccount() : Task.CompletedTask;
    }
    public void Close() { closed = true; lifetime.Cancel(); online.Dispose(); }
    private void NotifyAccount()
    {
        foreach (var name in new[] { nameof(DiscordStatus), nameof(DiscordButtonLabel), nameof(DiscordIcon), nameof(DiscordColor), nameof(ServerIcon), nameof(ServerColor), nameof(ServerStatus), nameof(RockstarIcon), nameof(RockstarColor), nameof(RockstarStatus), nameof(AccountSummary), nameof(FairPlayAvailable), nameof(FairPlayBadge), nameof(PlayLabel), nameof(ModeGuidance), nameof(AccountRefreshLabel), nameof(DiscordConnected), nameof(PrimaryAccountLabel), nameof(PrimaryAccountCommand), nameof(FairPlayAccessibleName) }) Changed(name);
        RefreshCommands();
    }
    public async Task InitializeAsync()
    {
      await Run(async () =>
      {
        await Detect();
        var states = await writer.ReadStateAsync();
        if (states.Any(s => s.Operation != "Applied"))
        {
            foreach (var interrupted in states.Where(s => s.Operation != "Applied"))
                await writer.RestoreAsync(interrupted.GamePath);
            Status = "Recovered an interrupted operation. Your original game configuration has been restored.";
        }
        else if (states.Count > 0) Status = "A private configuration is still active. Choose Play to use your selected mode, or Restore original settings.";
      });
      if (!closed) await RefreshAccount();
    }
    private async Task Detect()
    {
        Status = "Finding Red Dead in Steam, Epic Games and Rockstar…";
        var found = (await detector.DetectAsync()).ToList();
        var remembered = local.LoadGame();
        if (remembered is not null && validator.IsValid(remembered.InstallationPath) && !found.Any(g => string.Equals(g.InstallationPath, remembered.InstallationPath, StringComparison.OrdinalIgnoreCase)))
            found.Add(remembered);
        SetFound(found, remembered?.InstallationPath);
    }
    private void SetFound(IReadOnlyList<DetectedGameInstallation> found, string? preferred = null)
    {
        SelectedInstallation = null; Installations.Clear();
        foreach (var game in found) Installations.Add(game);
        SelectedInstallation = preferred is null ? null : found.FirstOrDefault(g => string.Equals(g.InstallationPath, preferred, StringComparison.OrdinalIgnoreCase));
        if (found.Count == 1) SelectedInstallation = found[0];
        Status = found.Count switch
        {
            0 => "We haven’t found Red Dead yet. Use Find game or Settings & help to choose its location.",
            1 => "Your game is ready.",
            _ => "Multiple installations found. Choose your game in Settings & help."
        };
        Changed(nameof(InstallationPrompt));
    }
    private async Task Diagnose()
    {
        var states = await writer.ReadStateAsync();
        var health = selected is null ? "No installation selected" : await writer.DiagnoseAsync(selected.InstallationPath);
        Diagnostics = $"{ProductName} {VersionLabel}\nGame: {selected?.InstallationPath ?? "Not found"}\nPlatform: {selected?.Platform}\nValidation: {(selected is not null && validator.IsValid(selected.InstallationPath) ? "Passed" : "Not ready")}\nDetection: {selected?.DetectionMethod}\nConfiguration: {health}\nSelected mode: {SelectedModeName}\nRecovery records: {states.Count}\nDiscord: {(online.Connected ? "Connected" : "Signed out")}\nBackend: {(options.BackendUrl is null ? "Not configured" : "Configured; live status checked when playing")}\nLocal recovery: {local.Root}\n";
        DiagnosticsOpen = true;
        Status = "Local checks finished. Results are shown below.";
    }
    private UiCommand Command(Func<Task> action, bool needsGame = false, Func<bool>? allowed = null)
    {
        var command = new UiCommand(() => Run(action), () => !busy && (!needsGame || selected is not null) && (allowed?.Invoke() ?? true));
        commands.Add(command); return command;
    }
    private async Task Run(Func<Task> action)
    {
        if (busy) return;
        IsBusy = true;
        try { await action(); }
        catch (OperationCanceledException) { Status = "Canceled or timed out. You can try again."; }
        catch (ExternalChangeException e)
        {
            Status = e.Message;
            if (ConflictDialog.Show(e.FilePath))
            {
                try { await writer.RestoreAsync(Directory.GetParent(Path.GetDirectoryName(e.FilePath)!)!.Parent!.FullName, true); Restored(); }
                catch (Exception restoreError) { ShowError(restoreError); }
            }
        }
        catch (Exception e) { ShowError(e); }
        finally { IsBusy = false; NotifyAccount(); }
    }
    private void ShowError(Exception e)
    {
        log.Write("launcher.operation", e.GetType().Name);
        Status = e switch
        {
            FriendlyException => e.Message,
            UnauthorizedAccessException => "Windows denied access. Check the game folder’s permissions and try again. Your backups are kept.",
            IOException => "A file is busy or the drive is unavailable. Close Red Dead, reconnect the drive, and choose Restore original settings.",
            _ => "We couldn’t finish that step. Your backups are kept. Try Check everything or Emergency restore."
        };
    }
    private void Restored() { lastKeyCheck = null; SetKey("Not applied"); SetMode(PlayMode.Normal); Status = "Red Dead has been restored to its pre-launcher configuration. Any file that existed before this launcher is preserved."; }
    private static void Open(string path) => Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
    private void Changed([CallerMemberName] string? property = null) => PropertyChanged?.Invoke(this, new(property));
    private void RefreshCommands() { foreach (var command in commands) command.Refresh(); }
}
public sealed class UiCommand(Func<Task> action, Func<bool> canExecute) : ICommand
{
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => canExecute();
    public async void Execute(object? parameter) { if (canExecute()) await action(); }
    public void Refresh() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
internal static class ConflictDialog
{
    public static bool Show(string file)
    {
        var restore = false;
        var window = new Window { Title = "Keep your file safe", Width = 540, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Owner = Application.Current.MainWindow, ResizeMode = ResizeMode.NoResize, Background = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(23, 22, 21)) };
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Another program changed your lobby file. Preserve current file leaves everything as it is. Restore original saves an extra copy of the current file before restoring your pre-launcher configuration.", Margin = new Thickness(0, 0, 0, 20) });
        var preserve = new Button { Content = "Preserve current file", IsCancel = true, IsDefault = true };
        preserve.Click += (_, _) => window.Close(); panel.Children.Add(preserve);
        var original = new Button { Content = "Restore original" };
        original.Click += (_, _) => { restore = true; window.Close(); }; panel.Children.Add(original);
        var location = new Button { Content = "Show file location" };
        location.Click += (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(Path.GetDirectoryName(file)!) { UseShellExecute = true }); }
            catch (Exception e) when (e is IOException or System.ComponentModel.Win32Exception) { MessageBox.Show("The game folder is unavailable. Reconnect its drive."); }
        };
        panel.Children.Add(location); window.Content = panel; window.ShowDialog(); return restore;
    }
}
