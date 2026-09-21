using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityFrontier.Core;

namespace CommunityFrontier.Launcher;

public partial class App : Application
{
    private Mutex? instance;
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var uninstallCheck = e.Args.Contains("--uninstall-check");
        instance = new Mutex(true, e.Args.Contains("--render-preview") ? "Local\\CommunityFrontier.Launcher.Preview" : "Local\\CommunityFrontier.Launcher", out var first);
        if (!first) { if (!uninstallCheck) MessageBox.Show("RDO FairPlay is already open."); Shutdown(uninstallCheck ? 1 : 0); return; }
        try
        {
            // Explicit constructor injection. The UI depends on interfaces; no service locator or static writer.
            var settingsPath = Path.Combine(AppContext.BaseDirectory, "launcher.settings.json");
            var options = File.Exists(settingsPath) ? JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(settingsPath)) ?? new() : new LauncherSettings();
            var local = new LocalSettings(e.Args.Contains("--render-preview") ? Path.GetFullPath("artifacts/ui-fixtures") : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CommunityFrontier"));
            var runner = new WindowsGameRunner();
            var log = new JsonEventLog(local.Root);
            var validator = new GameInstallationValidator();
            var detector = new WindowsInstallationDetector(new InstallationDiscovery(validator));
            var writer = new LobbyConfigurationWriter(local.Root, runner, log);
            if (uninstallCheck || e.Args.Contains("--emergency-restore") || e.Args.Contains("--prepare-uninstall"))
            {
                await writer.RestoreAllAsync();
                if (!uninstallCheck) MessageBox.Show("All recorded installations have been restored to their pre-launcher configuration. Backups have been kept.", options.ProductName);
                Shutdown(0); return;
            }
            var vm = new MainViewModel(options, local, detector, validator, writer,
                new LaunchCoordinator(writer, new StartupMetaFormatter(), runner, validator), log);
            var window = new MainWindow { DataContext = vm, Title = options.ProductName };
            MainWindow = window;
            window.Show();
            if (e.Args.Contains("--render-preview"))
            {
                // Noninteractive visual QA: no discovery, game launch, or game-file mutation.
                if (Descendants(window).OfType<RadioButton>().Count() != 2)
                    throw new InvalidOperationException("The launcher must expose exactly two play modes.");
                var logo = Descendants(window).OfType<Image>().Single(i => System.Windows.Automation.AutomationProperties.GetName(i) == "RDO FairPlay logo");
                if (logo.Source is not BitmapSource { PixelWidth: > 0 })
                    throw new InvalidOperationException("The embedded FairPlay logo did not load.");
                if (!vm.FairPlaySelected)
                    throw new InvalidOperationException("Fair Play must be selected on startup.");
                if (!Descendants(window).OfType<Button>().Any(b => Equals(b.Content, "Sign in with Discord")))
                    throw new InvalidOperationException("Discord sign-in must be visible.");
                if (!Descendants(window).OfType<Button>().Any(b => Equals(b.Content, "Open Discord · Verify")) ||
                    !Descendants(window).OfType<Button>().Any(b => Equals(b.Content, "Refresh status")) || vm.DiscordIcon != "✕" || vm.RockstarIcon != "✕")
                    throw new InvalidOperationException("Account status and verification actions must be visible and signed out by default.");
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                if (!Descendants(window).OfType<Button>().Any(b => Equals(b.Content, "Verification required")))
                    throw new InvalidOperationException("Play-mode binding failed.");
                vm.SelectedInstallation = new DetectedGameInstallation(Path.GetFullPath("artifacts/ui-fixtures/game"), GamePlatform.Steam, Path.GetFullPath("artifacts/ui-fixtures/game/RDR2.exe"), "UI fixture");
                if (vm.PlayCommand.CanExecute(null)) throw new InvalidOperationException("An installation must not unlock unverified Fair Play.");
                vm.SelectedInstallation = null;
                vm.OriginalSettingsSelected = true;
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                var playButton = Descendants(window).OfType<Button>().Single(b => Equals(b.Content, "Play with Original Settings"));
                if (playButton.IsEnabled || vm.PlayCommand.CanExecute(null))
                    throw new InvalidOperationException("Play must be disabled until an installation is selected.");
                vm.SelectedInstallation = new DetectedGameInstallation(Path.GetFullPath("artifacts/ui-fixtures/game"), GamePlatform.Steam, Path.GetFullPath("artifacts/ui-fixtures/game/RDR2.exe"), "UI fixture");
                if (!vm.PlayCommand.CanExecute(null)) throw new InvalidOperationException("Original Settings must work without verification when a game is selected.");
                vm.SelectedInstallation = null;
                vm.FairPlaySelected = true;
                if (!vm.OriginalSettingsSelected || vm.FairPlayAvailable)
                    throw new InvalidOperationException("Unverified users must not select locked Fair Play.");
                var lockedMode = Descendants(window).OfType<RadioButton>().Single(r => System.Windows.Automation.AutomationProperties.GetName(r).StartsWith("Fair Play"));
                if (lockedMode.IsEnabled) throw new InvalidOperationException("Fair Play radio must be visibly disabled until verified.");
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                if (e.Args.Contains("--small")) { window.Width = window.MinWidth; window.Height = window.MinHeight; }
                if (e.Args.Contains("--details"))
                {
                    if (!vm.SettingsOpen) vm.ToggleSettingsCommand.Execute(null);
                    foreach (var expander in Descendants(window).OfType<Expander>().ToList()) expander.IsExpanded = true;
                    window.UpdateLayout();
                    foreach (var expander in Descendants(window).OfType<Expander>().ToList()) expander.IsExpanded = true;
                }
                window.UpdateLayout();
                if (e.Args.Contains("--details"))
                {
                    Descendants(window).OfType<ScrollViewer>().First().ScrollToBottom();
                    window.UpdateLayout();
                }
                var content = (FrameworkElement)window.Content;
                var bitmap = new RenderTargetBitmap((int)content.ActualWidth, (int)content.ActualHeight, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(window);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                Directory.CreateDirectory("artifacts");
                var suffix = e.Args.Contains("--details") ? "-details" : e.Args.Contains("--small") ? "-small" : "";
                using (var output = File.Create($"artifacts/launcher-preview{suffix}.png")) encoder.Save(output);
                Shutdown(); return;
            }
            await vm.InitializeAsync();
        }
        catch (Exception error)
        {
            if (uninstallCheck) { Shutdown(1); return; }
            if (e.Args.Contains("--render-preview"))
            {
                Directory.CreateDirectory("artifacts");
                File.WriteAllText("artifacts/preview-error.txt", error.ToString());
                Shutdown(1); return;
            }
            MessageBox.Show(error is FriendlyException or ExternalChangeException ? error.Message : "The launcher could not start or complete recovery. Keep your local Backups folder and check that your game drive is connected.", "RDO FairPlay");
            Shutdown(1);
        }
    }
    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
    protected override void OnExit(ExitEventArgs e) { instance?.Dispose(); base.OnExit(e); }
}
