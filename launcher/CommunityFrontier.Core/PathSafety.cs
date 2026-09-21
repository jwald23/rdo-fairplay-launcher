namespace CommunityFrontier.Core;

public static class PathSafety
{
    public static string Full(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    public static void NoLinks(string path)
    {
        var current = Path.GetFullPath(path);
        while (!string.IsNullOrEmpty(current))
        {
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new FriendlyException("This location uses a linked folder or file. Select a direct, local game installation.");
            current = Path.GetDirectoryName(current);
        }
    }
    public static string Target(string root)
    {
        root = Full(root);
        if (root.StartsWith("\\\\", StringComparison.Ordinal)) throw new FriendlyException("Select a game installed on a local drive.");
        var path = Path.Combine(root, "x64", "data", "startup.meta");
        NoLinks(path);
        if (!Directory.Exists(Path.GetDirectoryName(path))) throw new FriendlyException("The game data folder is missing. Find Red Dead again, or reconnect its drive.");
        return path;
    }
}
public sealed class GameInstallationValidator : IGameInstallationValidator
{
    public bool IsValid(string path)
    {
        try
        {
            PathSafety.Target(path);
            var exe = Path.Combine(path, "RDR2.exe");
            PathSafety.NoLinks(exe);
            using var stream = File.OpenRead(exe);
            // A PE signature check rejects empty/text files, without reading or hashing the executable body.
            using var reader = new BinaryReader(stream);
            if (stream.Length < 64 || reader.ReadUInt16() != 0x5A4D) return false;
            stream.Position = 0x3c;
            var offset = reader.ReadInt32();
            if (offset < 64 || offset > stream.Length - 4) return false;
            stream.Position = offset;
            return reader.ReadUInt32() == 0x00004550;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or FriendlyException or NotSupportedException) { return false; }
    }
}
