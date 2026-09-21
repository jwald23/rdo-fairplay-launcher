using System.Text.Json;
using System.Text.RegularExpressions;

namespace CommunityFrontier.Core;

/// <summary>Metadata readers shared by live discovery and disk fixtures.</summary>
public sealed partial class InstallationDiscovery(IGameInstallationValidator validator)
{
    public IReadOnlyList<DetectedGameInstallation> Steam(string steamRoot)
    {
        var found = new List<DetectedGameInstallation>();
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steamRoot };
        foreach (var vdf in new[] { Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf"), Path.Combine(steamRoot, "config", "libraryfolders.vdf") })
            if (File.Exists(vdf))
                foreach (Match pair in Pairs().Matches(File.ReadAllText(vdf)))
                    if (pair.Groups[1].Value == "path" || int.TryParse(pair.Groups[1].Value, out _))
                    {
                        var value = Unescape(pair.Groups[2].Value);
                        if (Path.IsPathFullyQualified(value)) libraries.Add(value);
                    }
        foreach (var library in libraries)
            foreach (var appId in new[] { "1174180", "1404210" })
            {
                var manifest = Path.Combine(library, "steamapps", $"appmanifest_{appId}.acf");
                if (!File.Exists(manifest)) continue;
                var values = Pairs().Matches(File.ReadAllText(manifest)).Cast<Match>()
                    .GroupBy(m => m.Groups[1].Value).ToDictionary(g => g.Key, g => Unescape(g.Last().Groups[2].Value));
                if (!values.TryGetValue("appid", out var actual) || actual != appId || !values.TryGetValue("installdir", out var folder)) continue;
                if (folder.IndexOfAny(['/', '\\', ':']) >= 0 || folder is "." or "..") continue;
                var path = Path.Combine(library, "steamapps", "common", folder);
                Add(found, path, GamePlatform.Steam, "Steam library manifest", appId);
            }
        return found;
    }
    public IReadOnlyList<DetectedGameInstallation> Epic(string manifestsDirectory)
    {
        var result = new List<DetectedGameInstallation>();
        if (!Directory.Exists(manifestsDirectory)) return result;
        foreach (var manifest in Directory.GetFiles(manifestsDirectory, "*.item"))
        {
            try
            {
                using var json = JsonDocument.Parse(File.ReadAllText(manifest));
                var item = json.RootElement;
                if (!item.TryGetProperty("InstallLocation", out var location)) continue;
                var root = location.GetString();
                if (root is null || !validator.IsValid(root)) continue;
                string? id = null;
                if (item.TryGetProperty("AppName", out var app))
                {
                    id = app.GetString();
                    if (item.TryGetProperty("CatalogNamespace", out var ns) && item.TryGetProperty("CatalogItemId", out var catalog))
                        id = $"{ns.GetString()}:{catalog.GetString()}:{id}";
                }
                Add(result, root, GamePlatform.Epic, "Epic installation manifest", id);
            }
            catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or InvalidOperationException) { /* Isolate corrupt manifests. */ }
        }
        return result;
    }
    public IReadOnlyList<DetectedGameInstallation> Resolve(string selection, CancellationToken token)
    {
        if (File.Exists(selection)) selection = Path.GetDirectoryName(selection)!;
        selection = PathSafety.Full(selection);
        var steam = Steam(selection);
        var result = new List<DetectedGameInstallation>(steam);
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((selection, 0));
        var visited = 0;
        while (queue.Count > 0 && visited++ < 2500)
        {
            token.ThrowIfCancellationRequested();
            var (path, depth) = queue.Dequeue();
            try
            {
                PathSafety.NoLinks(path);
                if (validator.IsValid(path))
                {
                    if (!result.Any(r => string.Equals(r.InstallationPath, path, StringComparison.OrdinalIgnoreCase)))
                    {
                        var common = Directory.GetParent(path);
                        var apps = common?.Parent;
                        var library = apps?.Parent;
                        var match = library is null ? null : Steam(library.FullName).FirstOrDefault(g => string.Equals(g.InstallationPath, path, StringComparison.OrdinalIgnoreCase));
                        result.Add(match ?? new(path, GamePlatform.Unknown, Path.Combine(path, "RDR2.exe"), "Selected game folder", "Validated"));
                    }
                    continue;
                }
                if (depth >= 4) continue;
                foreach (var child in Directory.GetDirectories(path))
                {
                    if ((File.GetAttributes(child) & (FileAttributes.ReparsePoint | FileAttributes.Hidden | FileAttributes.System)) != 0) continue;
                    if (Path.GetFileName(child) is "Users" or "Windows" or "Documents" or "AppData" or "$Recycle.Bin") continue;
                    queue.Enqueue((child, depth + 1));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FriendlyException) { }
        }
        return result.DistinctBy(g => g.InstallationPath, StringComparer.OrdinalIgnoreCase).ToList();
    }
    public void Add(List<DetectedGameInstallation> list, string path, GamePlatform platform, string method, string? id = null)
    {
        if (validator.IsValid(path)) list.Add(new(PathSafety.Full(path), platform, Path.Combine(PathSafety.Full(path), "RDR2.exe"), method, "High", id));
    }
    private static string Unescape(string value) => value.Replace("\\\\", "\\").Replace("\\\"", "\"");
    [GeneratedRegex("\"([^\"]+)\"\\s*\"((?:\\\\.|[^\"\\\\])*)\"", RegexOptions.CultureInvariant)]
    private static partial Regex Pairs();
}
