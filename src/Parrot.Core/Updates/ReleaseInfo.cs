namespace Parrot.Core.Updates;

/// <summary>A published version of Parrot and the installer that delivers it.</summary>
public sealed record ReleaseInfo(
    Version Version,
    string Tag,
    string PageUrl,
    string InstallerUrl,
    long InstallerSize,
    string? Sha256,
    DateTimeOffset? PublishedAt);
