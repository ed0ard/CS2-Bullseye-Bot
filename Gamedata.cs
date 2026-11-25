using System.Text.Json;
using System.Text.RegularExpressions;

namespace BotAimImprover;

internal sealed class Gamedata(JsonElement entries, string platform)
{
    internal string Platform { get; } = platform;

    internal static Gamedata Load(string path)
    {
        string platform = OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsLinux() ? "linux"
            : throw new PlatformNotSupportedException("Only Windows and Linux gamedata are supported.");
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream, new JsonDocumentOptions { AllowDuplicateProperties = false });
        return new Gamedata(document.RootElement.Clone(), platform);
    }

    internal string GetSignature(string key)
    {
        var signatures = entries.GetProperty(key).GetProperty("signatures");
        if (signatures.GetProperty("library").GetString() != "server")
            throw new InvalidDataException($"{key}: expected a server signature.");

        string signature = signatures.GetProperty(Platform).GetString()?.Trim() ?? "";
        if (!Regex.IsMatch(signature, @"\A(?:[0-9A-Fa-f]{2}|\?)(?: (?:[0-9A-Fa-f]{2}|\?))*\z")
            || !signature.Any(char.IsAsciiHexDigit))
            throw new InvalidDataException($"Invalid signature {key}/{Platform}.");
        return signature;
    }
}
