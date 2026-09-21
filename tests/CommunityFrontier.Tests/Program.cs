using System.Text;
using System.Text.Json;
using CommunityFrontier.Core;

// Dependency-free executable test suite. Fails the process on any regression.
var tests = new List<(string Name, Func<Task> Run)>();
void Test(string name, Func<Task> run) => tests.Add((name, run));
void Equal<T>(T expected, T actual) { if (!EqualityComparer<T>.Default.Equals(expected, actual)) throw new Exception($"Expected {expected}; got {actual}"); }
void Bytes(byte[] expected, byte[] actual) { if (!expected.SequenceEqual(actual)) throw new Exception("Byte content differs"); }
async Task Throws<T>(Func<Task> run) where T : Exception
{ try { await run(); } catch (T) { return; } throw new Exception($"Expected {typeof(T).Name}"); }

Test("Key fingerprint accepts only a complete canonical configuration", () =>
{
    var identifier = new string('a', 64); var formatter = new StartupMetaFormatter();
    Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(identifier))), StartupMetaFormatter.Fingerprint(formatter.Format(identifier)));
    Equal<string?>(null, StartupMetaFormatter.Fingerprint(Encoding.UTF8.GetBytes(identifier)));
    Equal<string?>(null, StartupMetaFormatter.Fingerprint(formatter.Format(identifier).Concat(new byte[] { 10 }).ToArray()));
    return Task.CompletedTask;
});
Test("Steam metadata finds a game on an additional library", () =>
{
    using var f = new Fixture();
    var steam = Path.Combine(f.Root, "Steam"); var library = Path.Combine(f.Root, "Other Drive", "Library");
    var game = f.MakeGame(Path.Combine(library, "steamapps", "common", "Red Dead Redemption 2"));
    Directory.CreateDirectory(Path.Combine(steam, "steamapps"));
    File.WriteAllText(Path.Combine(steam, "steamapps", "libraryfolders.vdf"), "\"libraryfolders\" { \"1\" { \"path\" \"" + library.Replace("\\", "\\\\") + "\" } }");
    File.WriteAllText(Path.Combine(library, "steamapps", "appmanifest_1174180.acf"), "\"AppState\" { \"appid\" \"1174180\" \"installdir\" \"Red Dead Redemption 2\" }");
    var result = f.Discovery.Steam(steam);
    Equal(1, result.Count); Equal(game, result[0].InstallationPath); Equal(GamePlatform.Steam, result[0].Platform); Equal("1174180", result[0].PlatformId);
    return Task.CompletedTask;
});
Test("Standalone Red Dead Online Steam manifest is recognized", () =>
{
    using var f = new Fixture();
    f.MakeGame(Path.Combine(f.Root, "steamapps", "common", "Red Dead Online"));
    File.WriteAllText(Path.Combine(f.Root, "steamapps", "appmanifest_1404210.acf"), "\"appid\" \"1404210\" \"installdir\" \"Red Dead Online\"");
    Equal("1404210", f.Discovery.Steam(f.Root).Single().PlatformId); return Task.CompletedTask;
});
Test("Steam traversal and wrong app ID are rejected", () =>
{
    using var f = new Fixture(); Directory.CreateDirectory(Path.Combine(f.Root, "steamapps"));
    var manifest = Path.Combine(f.Root, "steamapps", "appmanifest_1174180.acf");
    File.WriteAllText(manifest, "\"appid\" \"1174180\" \"installdir\" \"../outside\""); Equal(0, f.Discovery.Steam(f.Root).Count);
    File.WriteAllText(manifest, "\"appid\" \"42\" \"installdir\" \"Game\""); Equal(0, f.Discovery.Steam(f.Root).Count); return Task.CompletedTask;
});
Test("Epic manifest identifies game and store launch ID; corrupt manifests are isolated", () =>
{
    using var f = new Fixture();
    File.WriteAllText(Path.Combine(f.Root, "bad.item"), "not json");
    File.WriteAllText(Path.Combine(f.Root, "valid.item"), JsonSerializer.Serialize(new { InstallLocation = f.Game, AppName = "app", CatalogNamespace = "namespace", CatalogItemId = "catalog" }));
    var result = f.Discovery.Epic(f.Root); Equal(1, result.Count); Equal("namespace:catalog:app", result[0].PlatformId); return Task.CompletedTask;
});
Test("Invalid game path and fake text executable rejected", () =>
{
    using var f = new Fixture(); Equal(false, f.Validator.IsValid(f.Root));
    File.WriteAllText(Path.Combine(f.Game, "RDR2.exe"), "not a portable executable"); Equal(false, f.Validator.IsValid(f.Game)); return Task.CompletedTask;
});
Test("Manual library root, game folder and executable resolve", () =>
{
    using var f = new Fixture();
    var library = Path.Combine(f.Root, "Library");
    var game = f.MakeGame(Path.Combine(library, "steamapps", "common", "Red Dead Redemption 2"));
    foreach (var selection in new[] { library, game, Path.Combine(game, "RDR2.exe") })
        Equal(game, f.Discovery.Resolve(selection, CancellationToken.None).Single().InstallationPath);
    return Task.CompletedTask;
});
Test("Discovery cancellation stops bounded scan", async () =>
{
    using var f = new Fixture(); using var cancel = new CancellationTokenSource(); cancel.Cancel();
    await Throws<OperationCanceledException>(() => Task.Run(() => f.Discovery.Resolve(f.Root, cancel.Token)));
});
Test("Existing arbitrary bytes are backed up and restored exactly", async () =>
{
    using var f = new Fixture(); var original = new byte[] { 0, 255, 13, 10, 42, 128 }; File.WriteAllBytes(f.Target, original);
    await f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Solo);
    var state = (await f.Writer().ReadStateAsync()).Single(); Bytes(original, File.ReadAllBytes(state.BackupLocation!));
    await f.Writer().RestoreAsync(f.Game); Bytes(original, File.ReadAllBytes(f.Target)); Equal(0, (await f.Writer().ReadStateAsync()).Count);
});
Test("Launcher-created file removed and repeated offline restore is idempotent", async () =>
{
    using var f = new Fixture(); await f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Solo);
    await f.Writer().RestoreAllAsync(); await f.Writer().RestoreAllAsync(); Equal(false, File.Exists(f.Target));
});
Test("Unmanaged original is never deleted by Normal restoration", async () =>
{
    using var f = new Fixture(); File.WriteAllBytes(f.Target, [4, 5, 6]); await f.Writer().RestoreAsync(f.Game); Bytes([4, 5, 6], File.ReadAllBytes(f.Target));
});
Test("Mode switches preserve the first original, not the previous generated file", async () =>
{
    using var f = new Fixture(); File.WriteAllBytes(f.Target, [1, 2]);
    await f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Community);
    await f.Writer().ApplyAsync(f.Game, new StartupMetaFormatter().Format(StartupMetaFormatter.NewSoloIdentifier()), PlayMode.Solo);
    await f.Writer().RestoreAsync(f.Game); Bytes([1, 2], File.ReadAllBytes(f.Target));
});
Test("External modifications block apply and default restore; explicit restore preserves them", async () =>
{
    using var f = new Fixture(); File.WriteAllBytes(f.Target, [1, 2]); await f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Solo);
    var external = new byte[] { 9, 8, 7 }; File.WriteAllBytes(f.Target, external);
    await Throws<ExternalChangeException>(() => f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Solo));
    await Throws<ExternalChangeException>(() => f.Writer().RestoreAsync(f.Game)); Bytes(external, File.ReadAllBytes(f.Target));
    await f.Writer().RestoreAsync(f.Game, true); Bytes([1, 2], File.ReadAllBytes(f.Target));
    Equal(true, Directory.GetFiles(Path.Combine(f.State, "Backups"), "preserved-*.bin").Any(p => File.ReadAllBytes(p).SequenceEqual(external)));
});
Test("Missing or corrupt original backup fails closed", async () =>
{
    using var f = new Fixture(); File.WriteAllBytes(f.Target, [1, 2]); await f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Solo);
    var backup = (await f.Writer().ReadStateAsync()).Single().BackupLocation!; File.WriteAllBytes(backup, [8]);
    await Throws<FriendlyException>(() => f.Writer().RestoreAsync(f.Game)); Bytes(f.Payload, File.ReadAllBytes(f.Target));
    await Throws<FriendlyException>(() => f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Solo));
});
Test("External deletion with an original backup is detected before restoration", async () =>
{
    using var f = new Fixture(); File.WriteAllBytes(f.Target, [3, 4]);
    await f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Solo); File.Delete(f.Target);
    await Throws<ExternalChangeException>(() => f.Writer().RestoreAsync(f.Game)); Equal(false, File.Exists(f.Target));
    await f.Writer().RestoreAsync(f.Game, true); Bytes([3, 4], File.ReadAllBytes(f.Target));
});
Test("Apply refuses an unresolved interrupted operation", async () =>
{
    using var f = new Fixture();
    await Throws<SimulatedCrash>(() => f.Writer(point => { if (point == "apply-journal") throw new SimulatedCrash(); }).ApplyAsync(f.Game, f.Payload, PlayMode.Solo));
    await Throws<FriendlyException>(() => f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Community));
    Equal(false, File.Exists(f.Target)); await f.Writer().RestoreAllAsync();
});
Test("Damaged journal does not become an empty state", async () =>
{
    using var f = new Fixture(); await f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Solo);
    File.WriteAllText(Directory.GetFiles(Path.Combine(f.State, "State"), "*.json").Single(), "broken");
    await Throws<FriendlyException>(() => f.Writer().RestoreAsync(f.Game)); Bytes(f.Payload, File.ReadAllBytes(f.Target));
});
Test("Tampered journal target is refused", async () =>
{
    using var f = new Fixture(); await f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Solo);
    var state = (await f.Writer().ReadStateAsync()).Single();
    File.WriteAllText(Directory.GetFiles(Path.Combine(f.State, "State"), "*.json").Single(), JsonSerializer.Serialize(state with { OriginalPath = Path.Combine(f.Root, "unrelated.txt") }));
    await Throws<FriendlyException>(() => f.Writer().RestoreAsync(f.Game)); Bytes(f.Payload, File.ReadAllBytes(f.Target));
});
Test("Running game prevents apply and restoration", async () =>
{
    using var f = new Fixture(); f.Runner.Running = true;
    await Throws<FriendlyException>(() => f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Solo)); Equal(false, File.Exists(f.Target));
    f.Runner.Running = false; await f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Solo); f.Runner.Running = true;
    await Throws<FriendlyException>(() => f.Writer().RestoreAsync(f.Game)); Bytes(f.Payload, File.ReadAllBytes(f.Target));
});
Test("Concurrent writer lock prevents mutation", async () =>
{
    using var f = new Fixture(); Directory.CreateDirectory(f.State);
    using var file = new FileStream(Path.Combine(f.State, "operation.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    await Throws<FriendlyException>(() => f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Solo)); Equal(false, File.Exists(f.Target));
});
foreach (var originalExists in new[] { false, true })
    foreach (var checkpoint in new[] { "apply-journal", "apply-temp", "apply-replace", "apply-commit" })
        Test($"Crash recovery: {checkpoint}, original={originalExists}", async () =>
        {
            using var f = new Fixture(); if (originalExists) File.WriteAllBytes(f.Target, [21, 22, 23]);
            await Throws<SimulatedCrash>(() => f.Writer(point => { if (point == checkpoint) throw new SimulatedCrash(); }).ApplyAsync(f.Game, f.Payload, PlayMode.Solo));
            await f.Writer().RestoreAllAsync();
            if (originalExists) Bytes([21, 22, 23], File.ReadAllBytes(f.Target)); else Equal(false, File.Exists(f.Target));
            Equal(0, Directory.GetFiles(Path.GetDirectoryName(f.Target)!, ".community-frontier-*").Length);
        });
foreach (var originalExists in new[] { false, true })
    foreach (var checkpoint in new[] { "restore-journal", "restore-replace", "restore-verified" })
        Test($"Crash recovery: {checkpoint}, original={originalExists}", async () =>
        {
            using var f = new Fixture(); if (originalExists) File.WriteAllBytes(f.Target, [21, 22, 23]);
            await f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Solo);
            await Throws<SimulatedCrash>(() => f.Writer(point => { if (point == checkpoint) throw new SimulatedCrash(); }).RestoreAsync(f.Game));
            await f.Writer().RestoreAllAsync();
            if (originalExists) Bytes([21, 22, 23], File.ReadAllBytes(f.Target)); else Equal(false, File.Exists(f.Target));
        });
Test("Interrupted mode switch restores first original", async () =>
{
    using var f = new Fixture(); File.WriteAllBytes(f.Target, [1]); await f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Solo);
    await Throws<SimulatedCrash>(() => f.Writer(point => { if (point == "apply-journal") throw new SimulatedCrash(); }).ApplyAsync(f.Game, [8, 9], PlayMode.Community));
    await f.Writer().RestoreAllAsync(); Bytes([1], File.ReadAllBytes(f.Target));
});
Test("Late external edit between journal and replacement is preserved", async () =>
{
    using var f = new Fixture(); File.WriteAllBytes(f.Target, [1]);
    await Throws<ExternalChangeException>(() => f.Writer(point => { if (point == "apply-temp") File.WriteAllBytes(f.Target, [77]); }).ApplyAsync(f.Game, f.Payload, PlayMode.Solo));
    Bytes([77], File.ReadAllBytes(f.Target)); await Throws<ExternalChangeException>(() => f.Writer().RestoreAsync(f.Game));
    await f.Writer().RestoreAsync(f.Game, true); Bytes([1], File.ReadAllBytes(f.Target));
});
Test("Recovery works without executable or backend", async () =>
{
    using var f = new Fixture(); await f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Solo);
    File.Delete(Path.Combine(f.Game, "RDR2.exe")); await f.Writer().RestoreAllAsync(); Equal(false, File.Exists(f.Target));
});
Test("Multiple installations restore independently", async () =>
{
    using var f = new Fixture(); var second = f.MakeGame(Path.Combine(f.Root, "Second"));
    await f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Solo); await f.Writer().ApplyAsync(second, f.Payload, PlayMode.Solo);
    await f.Writer().RestoreAsync(f.Game); Equal(1, (await f.Writer().ReadStateAsync()).Count); await f.Writer().RestoreAllAsync(); Equal(0, (await f.Writer().ReadStateAsync()).Count);
});
Test("Solo identifiers are random and formatted deterministically", () =>
{
    var ids = Enumerable.Range(0, 1000).Select(_ => StartupMetaFormatter.NewSoloIdentifier()).ToHashSet(); Equal(1000, ids.Count);
    var formatter = new StartupMetaFormatter(); var id = ids.First(); var payload = formatter.Format(id);
    Bytes(payload, formatter.Format(id)); Equal(true, Encoding.UTF8.GetString(payload).EndsWith("</CDataFileMgr__ContentsOfDataFileXml>" + id));
    Equal(false, payload.Take(3).SequenceEqual(new byte[] { 239, 187, 191 })); return Task.CompletedTask;
});
Test("Formatter rejects markup and multiline identifiers", async () =>
{
    foreach (var id in new[] { "short", "abcdefghijklmnop\n", "<script>not-a-key</script>" })
        await Throws<FriendlyException>(() => Task.Run(() => new StartupMetaFormatter().Format(id)));
});
Test("Normal mode restores before launch without any backend dependency", async () =>
{
    using var f = new Fixture(); await f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Solo);
    f.Runner.BeforeLaunch = () => Equal(false, File.Exists(f.Target));
    var coordinator = new LaunchCoordinator(f.Writer(), new StartupMetaFormatter(), f.Runner, f.Validator);
    await coordinator.PlayAsync(f.Installation, PlayMode.Normal, null); Equal(1, f.Runner.Launches);
});
Test("Community without dev configuration cannot write or launch; Solo remains usable", async () =>
{
    using var f = new Fixture(); var coordinator = new LaunchCoordinator(f.Writer(), new StartupMetaFormatter(), f.Runner, f.Validator);
    await Throws<FriendlyException>(() => coordinator.PlayAsync(f.Installation, PlayMode.Community, null));
    Equal(false, File.Exists(f.Target)); Equal(0, f.Runner.Launches);
    await coordinator.PlayAsync(f.Installation, PlayMode.Solo, null); Equal(1, f.Runner.Launches);
});
Test("Launch failure still leaves an offline recovery path", async () =>
{
    using var f = new Fixture(); f.Runner.BeforeLaunch = () => throw new IOException("store unavailable");
    var coordinator = new LaunchCoordinator(f.Writer(), new StartupMetaFormatter(), f.Runner, f.Validator);
    await Throws<IOException>(() => coordinator.PlayAsync(f.Installation, PlayMode.Solo, null));
    await f.Writer().RestoreAllAsync(); Equal(false, File.Exists(f.Target));
});
Test("Structured log events contain no lobby identifier or payload", async () =>
{
    using var f = new Fixture(); await f.Writer().ApplyAsync(f.Game, f.Payload, PlayMode.Solo); await f.Writer().RestoreAsync(f.Game);
    Equal("configuration.apply:Solo|configuration.restore:success", string.Join('|', f.Log.Events));
});

var failures = 0;
foreach (var (name, run) in tests)
{
    try { await run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failures++; Console.WriteLine($"FAIL {name}: {e}"); }
}
Console.WriteLine($"{tests.Count - failures}/{tests.Count} tests passed.");
return failures == 0 ? 0 : 1;

sealed class SimulatedCrash : Exception;
sealed class TestRunner : IGameRunner
{
    public bool Running { get; set; }
    public int Launches { get; private set; }
    public Action? BeforeLaunch { get; set; }
    public void EnsureStopped() { if (Running) throw new FriendlyException("Game is running"); }
    public Task LaunchAsync(DetectedGameInstallation installation, CancellationToken token) { BeforeLaunch?.Invoke(); Launches++; return Task.CompletedTask; }
}
sealed class TestLog : IEventLog
{
    public List<string> Events { get; } = [];
    public void Write(string eventName, string outcome) => Events.Add(eventName + ":" + outcome);
}
sealed class Fixture : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "CommunityFrontierTests", Guid.NewGuid().ToString("N"));
    public string Game { get; }
    public string State => Path.Combine(Root, "LocalState");
    public string Target => Path.Combine(Game, "x64", "data", "startup.meta");
    public byte[] Payload { get; } = new StartupMetaFormatter().Format("development-fixture-identifier");
    public TestRunner Runner { get; } = new();
    public TestLog Log { get; } = new();
    public GameInstallationValidator Validator { get; } = new();
    public InstallationDiscovery Discovery => new(Validator);
    public DetectedGameInstallation Installation => new(Game, GamePlatform.Steam, Path.Combine(Game, "RDR2.exe"), "Fixture", PlatformId: "1174180");
    public Fixture() { Game = MakeGame(Path.Combine(Root, "Game")); }
    public LobbyConfigurationWriter Writer(Action<string>? checkpoint = null) => new(State, Runner, Log, checkpoint);
    public string MakeGame(string path)
    {
        Directory.CreateDirectory(Path.Combine(path, "x64", "data"));
        var bytes = new byte[128]; bytes[0] = 0x4d; bytes[1] = 0x5a; bytes[0x3c] = 64; bytes[64] = 0x50; bytes[65] = 0x45;
        File.WriteAllBytes(Path.Combine(path, "RDR2.exe"), bytes); return path;
    }
    public void Dispose()
    {
        var expected = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "CommunityFrontierTests")) + Path.DirectorySeparatorChar;
        if (!Path.GetFullPath(Root).StartsWith(expected, StringComparison.OrdinalIgnoreCase)) throw new Exception("Unsafe test cleanup path");
        Directory.Delete(Root, true);
    }
}
