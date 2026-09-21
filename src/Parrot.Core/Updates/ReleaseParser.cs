using System.Text.Json;

namespace Parrot.Core.Updates;

/// <summary>
/// Reads the GitHub "latest release" response. Anything that is not a finished release
/// carrying a Parrot installer yields null: better to offer nothing than a broken update.
/// </summary>
public static class ReleaseParser
{
    public const string InstallerPrefix = "Parrot-Setup-";

    public static ReleaseInfo? Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object
            || Bool(root, "draft") || Bool(root, "prerelease")
            || String(root, "tag_name") is not { } tag
            || ParseVersion(tag) is not { } version
            || !root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var asset in assets.EnumerateArray())
        {
            var name = String(asset, "name");
            var url = String(asset, "browser_download_url");

            if (name is null || url is null
                || !name.StartsWith(InstallerPrefix, StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                continue;

            var size = asset.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var bytes) ? bytes : 0;

            return new ReleaseInfo(
                version,
                tag,
                String(root, "html_url") ?? url,
                url,
                size,
                Sha256(String(asset, "digest")),
                DateTimeOffset.TryParse(String(root, "published_at"), out var published) ? published : null);
        }

        return null;
    }

    /// <summary>"v1.2.3" or "1.2.3" → 1.2.3; the build and revision parts are always dropped.</summary>
    public static Version? ParseVersion(string text)
    {
        var trimmed = text.Trim().TrimStart('v', 'V');
        var core = trimmed.Split('-', '+')[0];

        if (!Version.TryParse(core.Contains('.') ? core : core + ".0", out var parsed))
            return null;

        return new Version(parsed.Major, parsed.Minor, Math.Max(parsed.Build, 0));
    }

    /// <summary>GitHub reports asset digests as "sha256:&lt;hex&gt;".</summary>
    private static string? Sha256(string? digest)
    {
        const string prefix = "sha256:";
        return digest is not null && digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && digest.Length > prefix.Length
            ? digest[prefix.Length..].ToLowerInvariant()
            : null;
    }

    private static string? String(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static bool Bool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
