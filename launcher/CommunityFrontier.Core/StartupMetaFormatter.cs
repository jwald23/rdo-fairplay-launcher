using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CommunityFrontier.Core;

public sealed partial class StartupMetaFormatter : ILobbyConfigurationFormatter
{
    private static readonly string Template = LoadTemplate();
    private static string LoadTemplate()
    {
        using var stream = typeof(StartupMetaFormatter).Assembly.GetManifestResourceStream("CommunityFrontier.Core.Resources.startup.template")!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd().Replace("\r\n", "\n");
    }
    public static string NewSoloIdentifier() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    public byte[] Format(string identifier)
    {
        if (!IdentifierPattern().IsMatch(identifier))
            throw new FriendlyException("Use a development lobby identifier of 16–128 letters, numbers, underscores or hyphens.");
        // The community format appends the identifier after the XML root. Do not XML-serialize it.
        return Encoding.UTF8.GetBytes(Template + identifier);
    }
    [GeneratedRegex("\\A[A-Za-z0-9_-]{16,128}\\z", RegexOptions.CultureInvariant)]
    private static partial Regex IdentifierPattern();
}
