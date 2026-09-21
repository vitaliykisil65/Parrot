namespace Parrot.Core.Updates;

public static class UpdateCheck
{
    /// <summary>True when <paramref name="release"/> is newer than what is running.</summary>
    public static bool IsNewer(Version current, ReleaseInfo release) =>
        Normalize(release.Version) > Normalize(current);

    /// <summary>
    /// True when the release deserves the sidebar card: newer, and not a version the user
    /// already waved away. A later release brings the card back.
    /// </summary>
    public static bool ShouldOffer(Version current, ReleaseInfo release, string? dismissedVersion) =>
        IsNewer(current, release)
        && (dismissedVersion is null
            || ReleaseParser.ParseVersion(dismissedVersion) is not { } dismissed
            || Normalize(release.Version) > dismissed);

    /// <summary>Assembly versions carry a revision part (0.1.0.0); releases do not.</summary>
    private static Version Normalize(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));
}
