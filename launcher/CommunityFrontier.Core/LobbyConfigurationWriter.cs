using System.Security.Cryptography;
using System.Text.Json;

namespace CommunityFrontier.Core;

/// <summary>Write-ahead journal and byte-exact recovery. Never needs the backend.</summary>
public sealed class LobbyConfigurationWriter : ILobbyConfigurationWriter
{
    private readonly string stateRoot;
    private readonly IGameRunner runner;
    private readonly IEventLog log;
    private readonly Action<string>? checkpoint;
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    // checkpoint is an injectable fault boundary used by the recovery tests.
    public LobbyConfigurationWriter(string stateRoot, IGameRunner runner, IEventLog log, Action<string>? checkpoint = null)
    {
        this.stateRoot = PathSafety.Full(stateRoot);
        this.runner = runner;
        this.log = log;
        this.checkpoint = checkpoint;
    }
    private string StateDirectory => Path.Combine(stateRoot, "State");
    private string BackupDirectory => Path.Combine(stateRoot, "Backups");
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
    private static string? FileHash(string path) => File.Exists(path) ? Hash(File.ReadAllBytes(path)) : null;
    private string Journal(string root) => Path.Combine(StateDirectory, Hash(System.Text.Encoding.UTF8.GetBytes(PathSafety.Full(root).ToUpperInvariant())) + ".json");
    private FileStream Lock()
    {
        PathSafety.NoLinks(stateRoot);
        Directory.CreateDirectory(StateDirectory);
        Directory.CreateDirectory(BackupDirectory);
        PathSafety.NoLinks(StateDirectory);
        PathSafety.NoLinks(BackupDirectory);
        var path = Path.Combine(stateRoot, "operation.lock");
        PathSafety.NoLinks(path);
        try { return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new FriendlyException("Another launcher operation is in progress. Please wait and try again."); }
    }
    private static void DurableWrite(string path, byte[] bytes)
    {
        PathSafety.NoLinks(path);
        using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
        file.Write(bytes);
        file.Flush(true);
    }
    private void Save(ModificationManifest state)
    {
        var journal = Journal(state.GamePath);
        var temporary = journal + "." + Guid.NewGuid().ToString("N") + ".tmp";
        DurableWrite(temporary, JsonSerializer.SerializeToUtf8Bytes(state, Json));
        try
        {
            PathSafety.NoLinks(journal);
            if (File.Exists(journal)) File.Replace(temporary, journal, null);
            else File.Move(temporary, journal);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    private ModificationManifest? Read(string root)
    {
        var path = Journal(root);
        PathSafety.NoLinks(path);
        if (!File.Exists(path)) return null;
        ModificationManifest state;
        try { state = JsonSerializer.Deserialize<ModificationManifest>(File.ReadAllBytes(path), Json) ?? throw new JsonException(); }
        catch (JsonException) { throw new FriendlyException("The recovery record is damaged. Your game file was left unchanged. Keep the Backups folder and contact support."); }
        var expected = PathSafety.Target(root);
        if (state.SchemaVersion != 1 || !string.Equals(PathSafety.Full(state.GamePath), PathSafety.Full(root), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(state.OriginalPath, expected, StringComparison.OrdinalIgnoreCase))
            throw new FriendlyException("The recovery record does not match this installation. No game files were changed.");
        if (state.OriginalExisted && (state.BackupLocation is null || state.OriginalSha256 is null))
            throw new FriendlyException("The original backup record is incomplete. No game files were changed.");
        if (state.BackupLocation is not null)
        {
            if (!string.Equals(Path.GetDirectoryName(PathSafety.Full(state.BackupLocation)), BackupDirectory, StringComparison.OrdinalIgnoreCase))
                throw new FriendlyException("The backup location is invalid. No game files were changed.");
            PathSafety.NoLinks(state.BackupLocation);
        }
        foreach (var sidecar in new[] { state.TemporaryPath, state.DisplacedPath }.OfType<string>())
        {
            if (!string.Equals(Path.GetDirectoryName(sidecar), Path.GetDirectoryName(expected), StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(sidecar).StartsWith(".community-frontier-", StringComparison.Ordinal))
                throw new FriendlyException("The recovery record contains an invalid temporary path.");
            PathSafety.NoLinks(sidecar);
        }
        return state;
    }
    private void VerifyBackup(ModificationManifest state)
    {
        if (state.OriginalExisted && FileHash(state.BackupLocation!) != state.OriginalSha256)
            throw new FriendlyException("The original backup is missing or damaged. Nothing was overwritten. Keep the recovery folder for support.");
    }
    private string Preserve(string path)
    {
        var backup = Path.Combine(BackupDirectory, "preserved-" + Guid.NewGuid().ToString("N") + ".bin");
        DurableWrite(backup, File.ReadAllBytes(path));
        return backup;
    }
    private void Cleanup(ModificationManifest state)
    {
        // Recovery sidecars may contain an external writer's data. Preserve before removing.
        foreach (var path in new[] { state.TemporaryPath, state.DisplacedPath }.OfType<string>())
            if (File.Exists(path)) { Preserve(path); File.Delete(path); }
    }
    private static bool Known(string? current, ModificationManifest state) =>
        current == state.OriginalSha256 || (current is not null &&
        (current == state.GeneratedSha256 || current == state.PendingSha256));

    public Task ApplyAsync(string gamePath, byte[] configuration, PlayMode mode, CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        using var operation = Lock();
        runner.EnsureStopped();
        var root = PathSafety.Full(gamePath);
        var target = PathSafety.Target(root);
        var state = Read(root);
        if (state is not null && state.Operation != "Applied")
            throw new FriendlyException("An interrupted change needs recovery. Select Restore Normal Red Dead first.");
        var current = FileHash(target);
        if (state is not null && current != state.GeneratedSha256) throw new ExternalChangeException(target);
        if (configuration.Length is 0 or > 65536) throw new FriendlyException("The lobby configuration is invalid.");
        if (state is null)
        {
            string? backup = null;
            if (current is not null)
            {
                backup = Path.Combine(BackupDirectory, "original-" + Guid.NewGuid().ToString("N") + ".bin");
                DurableWrite(backup, File.ReadAllBytes(target));
                if (FileHash(backup) != current) throw new ExternalChangeException(target);
            }
            state = new ModificationManifest { GamePath = root, OriginalPath = target, OriginalExisted = current is not null,
                OriginalSha256 = current, BackupLocation = backup };
        }
        VerifyBackup(state);
        var id = Guid.NewGuid().ToString("N");
        state = state with { PendingSha256 = Hash(configuration), Operation = "Applying", Mode = mode,
            TemporaryPath = Path.Combine(Path.GetDirectoryName(target)!, ".community-frontier-" + id + ".tmp"),
            DisplacedPath = Path.Combine(Path.GetDirectoryName(target)!, ".community-frontier-" + id + ".displaced"), Timestamp = DateTimeOffset.UtcNow };
        Save(state); checkpoint?.Invoke("apply-journal");
        DurableWrite(state.TemporaryPath!, configuration); checkpoint?.Invoke("apply-temp");
        if (FileHash(state.TemporaryPath!) != state.PendingSha256) throw new IOException("Temporary file verification failed.");
        runner.EnsureStopped();
        PathSafety.NoLinks(target);
        if (FileHash(target) != current) throw new ExternalChangeException(target);
        if (current is null) File.Move(state.TemporaryPath!, target);
        else File.Replace(state.TemporaryPath!, target, state.DisplacedPath);
        checkpoint?.Invoke("apply-replace");
        // File.Replace retains the displaced file, even if an external rename raced the hash check.
        if (current is not null && FileHash(state.DisplacedPath!) != current)
            throw new ExternalChangeException(target);
        if (FileHash(target) != state.PendingSha256) throw new ExternalChangeException(target);
        Cleanup(state);
        state = state with { GeneratedSha256 = state.PendingSha256, PendingSha256 = null, Operation = "Applied", TemporaryPath = null, DisplacedPath = null };
        Save(state); checkpoint?.Invoke("apply-commit");
        log.Write("configuration.apply", mode.ToString());
    }, token);

    public Task RestoreAsync(string gamePath, bool restoreExternal = false, CancellationToken token = default) => Task.Run(() =>
    {
        token.ThrowIfCancellationRequested();
        using var operation = Lock();
        Restore(PathSafety.Full(gamePath), restoreExternal);
    }, token);

    private void Restore(string root, bool restoreExternal)
    {
        runner.EnsureStopped();
        var state = Read(root);
        if (state is null) return; // Never delete an unowned startup.meta.
        VerifyBackup(state);
        var target = state.OriginalPath;
        var current = FileHash(target);
        if (!Known(current, state))
        {
            if (!restoreExternal) throw new ExternalChangeException(target);
            if (current is not null) Preserve(target);
        }
        // Preserve displaced data before replacing journal pointers (including a concurrent external rename).
        Cleanup(state);
        var id = Guid.NewGuid().ToString("N");
        state = state with { Operation = "Restoring", TemporaryPath = Path.Combine(Path.GetDirectoryName(target)!, ".community-frontier-" + id + ".tmp"),
            DisplacedPath = Path.Combine(Path.GetDirectoryName(target)!, ".community-frontier-" + id + ".displaced") };
        Save(state); checkpoint?.Invoke("restore-journal");
        if (state.OriginalExisted) DurableWrite(state.TemporaryPath!, File.ReadAllBytes(state.BackupLocation!));
        runner.EnsureStopped();
        PathSafety.NoLinks(target);
        if (FileHash(target) != current) throw new ExternalChangeException(target);
        if (state.OriginalExisted)
        {
            if (current is null) File.Move(state.TemporaryPath!, target);
            else File.Replace(state.TemporaryPath!, target, state.DisplacedPath);
        }
        else if (current is not null) File.Move(target, state.DisplacedPath!);
        checkpoint?.Invoke("restore-replace");
        if (File.Exists(state.DisplacedPath) && FileHash(state.DisplacedPath!) != current) throw new ExternalChangeException(target);
        if (FileHash(target) != state.OriginalSha256) throw new ExternalChangeException(target);
        Cleanup(state);
        checkpoint?.Invoke("restore-verified");
        File.Delete(Journal(root));
        log.Write("configuration.restore", "success");
    }
    public Task<IReadOnlyList<ModificationManifest>> ReadStateAsync(CancellationToken token = default) => Task.Run<IReadOnlyList<ModificationManifest>>(() =>
    {
        using var operation = Lock();
        return ReadAll();
    }, token);
    private List<ModificationManifest> ReadAll()
    {
        var result = new List<ModificationManifest>();
        foreach (var path in Directory.GetFiles(StateDirectory, "*.json"))
        {
            PathSafety.NoLinks(path);
            ModificationManifest? raw;
            try { raw = JsonSerializer.Deserialize<ModificationManifest>(File.ReadAllBytes(path), Json); }
            catch (JsonException) { throw new FriendlyException("A recovery record is damaged. Keep the Backups folder and contact support."); }
            if (raw is null || !string.Equals(Journal(raw.GamePath), path, StringComparison.OrdinalIgnoreCase))
                throw new FriendlyException("An invalid recovery record needs attention. No game files were changed.");
            result.Add(Read(raw.GamePath)!);
        }
        return result;
    }
    public Task RestoreAllAsync(CancellationToken token = default) => Task.Run(() =>
    {
        using var operation = Lock();
        foreach (var state in ReadAll()) { token.ThrowIfCancellationRequested(); Restore(state.GamePath, false); }
    }, token);
    public Task<string> DiagnoseAsync(string gamePath, CancellationToken token = default) => Task.Run(() =>
    {
        using var operation = Lock();
        var state = Read(gamePath);
        if (state is null) return File.Exists(PathSafety.Target(gamePath)) ? "Unmanaged existing configuration; it will be preserved." : "Normal · no managed configuration";
        VerifyBackup(state);
        var current = FileHash(state.OriginalPath);
        return $"{state.Mode} · {state.Operation} · Backup verified · " + (Known(current, state) ? "File recognized" : "External change detected");
    }, token);
}
