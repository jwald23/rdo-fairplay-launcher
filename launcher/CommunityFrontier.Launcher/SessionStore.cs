using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CommunityFrontier.Launcher;

public sealed record SavedSession(string Token, DateTimeOffset ExpiresAt);

// Windows protects the credential for the current OS user. Backend/community binding
// prevents a different launcher configuration from reusing the saved credential.
public sealed class SessionStore(string root, string? backend, Guid? community)
{
    private readonly string path = Path.Combine(root, "discord-session.bin");
    private readonly byte[] entropy = SHA256.HashData(Encoding.UTF8.GetBytes($"RDOFairPlay/session/v1/{backend}/{community}"));
    public SavedSession? Load()
    {
        try
        {
            if (!File.Exists(path)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(path), entropy, DataProtectionScope.CurrentUser);
            try
            {
                var saved = JsonSerializer.Deserialize<SavedSession>(plain);
                if (saved != null && !string.IsNullOrWhiteSpace(saved.Token) && saved.ExpiresAt > DateTimeOffset.UtcNow) return saved;
            }
            finally { CryptographicOperations.ZeroMemory(plain); }
            Clear();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or CryptographicException or JsonException) { }
        return null;
    }
    public void Save(SavedSession session)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var plain = JsonSerializer.SerializeToUtf8Bytes(session);
        try
        {
            var encrypted = ProtectedData.Protect(plain, entropy, DataProtectionScope.CurrentUser);
            var temporary = path + ".tmp";
            File.WriteAllBytes(temporary, encrypted);
            File.Move(temporary, path, true);
        }
        finally { CryptographicOperations.ZeroMemory(plain); }
    }
    public void Clear() => File.Delete(path);
}
