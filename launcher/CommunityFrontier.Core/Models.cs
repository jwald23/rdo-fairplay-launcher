namespace CommunityFrontier.Core;

public enum PlayMode { Community, Solo, Normal }
public enum GamePlatform { Unknown, Steam, Epic, Rockstar }
public sealed record DetectedGameInstallation(string InstallationPath, GamePlatform Platform,
    string ExecutablePath, string DetectionMethod, string Confidence = "High", string? PlatformId = null)
{
    public string ValidationResult => "RDR2 executable and game data verified";
    public string FriendlyName => $"Red Dead Redemption 2 · {Platform} · {Path.GetPathRoot(InstallationPath)}";
}
public interface IGameInstallationValidator { bool IsValid(string path); }
public interface IGameInstallationDetector
{
    Task<IReadOnlyList<DetectedGameInstallation>> DetectAsync(CancellationToken token = default);
    Task<IReadOnlyList<DetectedGameInstallation>> ResolveAsync(string selection, CancellationToken token = default);
    Task<IReadOnlyList<DetectedGameInstallation>> SearchAsync(IProgress<string> progress, CancellationToken token);
}
public interface IGameRunner
{
    void EnsureStopped();
    Task LaunchAsync(DetectedGameInstallation installation, CancellationToken token);
}
public interface ILobbyConfigurationFormatter { byte[] Format(string identifier); }
public interface ILobbyConfigurationWriter
{
    Task ApplyAsync(string gamePath, byte[] configuration, PlayMode mode, CancellationToken token = default);
    Task RestoreAsync(string gamePath, bool restoreExternal = false, CancellationToken token = default);
    Task RestoreAllAsync(CancellationToken token = default);
    Task<IReadOnlyList<ModificationManifest>> ReadStateAsync(CancellationToken token = default);
    Task<string> DiagnoseAsync(string gamePath, CancellationToken token = default);
}
public interface IEventLog { void Write(string eventName, string outcome); }
public sealed class NullEventLog : IEventLog { public void Write(string eventName, string outcome) { } }
public sealed class FriendlyException(string message) : Exception(message);
public sealed class ExternalChangeException(string path) : IOException("Another program changed your lobby file. Your current file has been preserved. Choose how to restore it.")
{ public string FilePath { get; } = path; }
public sealed record ModificationManifest
{
    public int SchemaVersion { get; init; } = 1;
    public required string GamePath { get; init; }
    public required string OriginalPath { get; init; }
    public bool OriginalExisted { get; init; }
    public string? OriginalSha256 { get; init; }
    public string? BackupLocation { get; init; }
    public string? GeneratedSha256 { get; init; }
    public string? PendingSha256 { get; init; }
    public string? TemporaryPath { get; init; }
    public string? DisplacedPath { get; init; }
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string LauncherVersion { get; init; } = "0.1.0";
    public string Operation { get; init; } = "Prepared";
    public PlayMode Mode { get; init; }
}
