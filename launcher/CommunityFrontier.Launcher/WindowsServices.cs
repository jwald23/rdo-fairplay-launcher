using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;
using CommunityFrontier.Core;

namespace CommunityFrontier.Launcher;

public sealed class WindowsInstallationDetector(InstallationDiscovery discovery) : IGameInstallationDetector
{
    public Task<IReadOnlyList<DetectedGameInstallation>> DetectAsync(CancellationToken token = default) => Task.Run<IReadOnlyList<DetectedGameInstallation>>(() =>
    {
        var found = new List<DetectedGameInstallation>();
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
            foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using var registry = RegistryKey.OpenBaseKey(hive, view);
                    using var steam = registry.OpenSubKey(@"SOFTWARE\Valve\Steam");
                    if ((steam?.GetValue("SteamPath") ?? steam?.GetValue("InstallPath")) is string root) found.AddRange(discovery.Steam(root));
                    using var rockstar = registry.OpenSubKey(@"SOFTWARE\Rockstar Games\Red Dead Redemption 2");
                    if (rockstar?.GetValue("InstallFolder") is string game) discovery.Add(found, game, GamePlatform.Rockstar, "Rockstar registry");
                    using var uninstall = registry.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    foreach (var name in uninstall?.GetSubKeyNames() ?? [])
                    {
                        using var item = uninstall!.OpenSubKey(name);
                        if (item?.GetValue("DisplayName") is string display && display.Contains("Red Dead", StringComparison.OrdinalIgnoreCase) && item.GetValue("InstallLocation") is string location)
                            discovery.Add(found, location, GamePlatform.Unknown, "Windows installation metadata");
                    }
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
            }
        found.AddRange(discovery.Epic(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests")));
        foreach (var process in Process.GetProcessesByName("RDR2"))
            using (process)
                try
                {
                    if (process.MainModule?.FileName is string exe) discovery.Add(found, Path.GetDirectoryName(exe)!, GamePlatform.Unknown, "Running Red Dead process");
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException) { }
        // Prefer store metadata over registry fallback entries.
        return found.OrderBy(g => g.Platform == GamePlatform.Unknown).DistinctBy(g => g.InstallationPath, StringComparer.OrdinalIgnoreCase).ToList();
    }, token);
    public Task<IReadOnlyList<DetectedGameInstallation>> ResolveAsync(string selection, CancellationToken token = default) => Task.Run(() => discovery.Resolve(selection, token), token);
    public Task<IReadOnlyList<DetectedGameInstallation>> SearchAsync(IProgress<string> progress, CancellationToken token) => Task.Run<IReadOnlyList<DetectedGameInstallation>>(() =>
    {
        var found = new List<DetectedGameInstallation>();
        foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType == DriveType.Fixed))
        {
            token.ThrowIfCancellationRequested();
            progress.Report($"Searching {drive.Name} drive…");
            foreach (var folder in new[] { "SteamLibrary", "Steam", "Games", "Epic Games", "Rockstar Games", @"Program Files\Rockstar Games", @"Program Files\Epic Games", @"Program Files (x86)\Steam" })
            {
                var path = Path.Combine(drive.RootDirectory.FullName, folder);
                if (Directory.Exists(path)) found.AddRange(discovery.Resolve(path, token));
            }
        }
        return found.DistinctBy(g => g.InstallationPath, StringComparer.OrdinalIgnoreCase).ToList();
    }, token);
}
public sealed class WindowsGameRunner : IGameRunner
{
    public void EnsureStopped()
    {
        var processes = Process.GetProcessesByName("RDR2");
        try { if (processes.Length > 0) throw new FriendlyException("Close Red Dead before changing play modes or restoring your game."); }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    public Task LaunchAsync(DetectedGameInstallation game, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        string launch;
        switch (game.Platform)
        {
            case GamePlatform.Steam:
                if (game.PlatformId is not ("1174180" or "1404210")) throw new FriendlyException("Find Red Dead again to reconnect its Steam installation.");
                launch = "steam://rungameid/" + game.PlatformId;
                break;
            case GamePlatform.Epic:
                if (string.IsNullOrWhiteSpace(game.PlatformId)) throw new FriendlyException("Open Epic Games and launch Red Dead there. Your selected mode is ready.");
                launch = "com.epicgames.launcher://apps/" + Uri.EscapeDataString(game.PlatformId) + "?action=launch&silent=true";
                break;
            case GamePlatform.Rockstar:
                launch = Path.Combine(game.InstallationPath, "PlayRDR2.exe");
                if (!File.Exists(launch)) throw new FriendlyException("Open Rockstar Games Launcher and launch Red Dead there. Your selected mode is ready.");
                break;
            default: throw new FriendlyException("Your selected mode is ready. Open your usual game launcher to play, or select your store in Advanced settings.");
        }
        Process.Start(new ProcessStartInfo(launch) { UseShellExecute = true, WorkingDirectory = game.InstallationPath });
        return Task.CompletedTask;
    }
}
public sealed record LauncherSettings
{
    public string ProductName { get; init; } = "RDO FairPlay";
    public string CommunityName { get; init; } = "RDO FairPlay";
    public string? DevelopmentLobbyIdentifier { get; init; }
    public string? BackendUrl { get; init; }
    public Guid? CommunityId { get; init; }
}
public sealed class LocalSettings(string root)
{
    public string Root { get; } = root;
    public DetectedGameInstallation? LoadGame()
    {
        try { return JsonSerializer.Deserialize<DetectedGameInstallation>(File.ReadAllText(Path.Combine(Root, "installation.json"))); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { return null; }
    }
    public void SaveGame(DetectedGameInstallation game)
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, "installation.json");
        var temporary = path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(game));
        File.Move(temporary, path, true);
    }
}
public sealed class JsonEventLog(string root) : IEventLog
{
    public void Write(string eventName, string outcome)
    {
        // Explicit allowlisted fields: no paths, configuration bytes, exception messages or credentials.
        try
        {
            var directory = Path.Combine(root, "Logs");
            Directory.CreateDirectory(directory);
            foreach (var old in Directory.GetFiles(directory, "*.jsonl").Where(f => File.GetLastWriteTimeUtc(f) < DateTime.UtcNow.AddDays(-14))) File.Delete(old);
            File.AppendAllText(Path.Combine(directory, DateTime.UtcNow.ToString("yyyy-MM-dd") + ".jsonl"),
                JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, eventName, outcome }) + Environment.NewLine);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* Logging cannot block recovery. */ }
    }
}
