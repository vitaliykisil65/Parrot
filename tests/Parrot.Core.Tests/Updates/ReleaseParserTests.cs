using Parrot.Core.Updates;

namespace Parrot.Core.Tests.Updates;

public class ReleaseParserTests
{
    // Trimmed from a real api.github.com/repos/{owner}/{repo}/releases/latest response.
    private const string Latest = """
        {
          "html_url": "https://github.com/owner/parrot/releases/tag/v0.2.0",
          "tag_name": "v0.2.0",
          "name": "Parrot 0.2.0",
          "draft": false,
          "prerelease": false,
          "published_at": "2026-09-21T10:00:00Z",
          "assets": [
            {
              "name": "checksums.txt",
              "size": 120,
              "browser_download_url": "https://github.com/owner/parrot/releases/download/v0.2.0/checksums.txt"
            },
            {
              "name": "Parrot-Setup-0.2.0.exe",
              "size": 45123456,
              "digest": "sha256:ABCDEF0123456789",
              "browser_download_url": "https://github.com/owner/parrot/releases/download/v0.2.0/Parrot-Setup-0.2.0.exe"
            }
          ]
        }
        """;

    [Fact]
    public void Reads_the_version_and_the_installer_asset()
    {
        var release = ReleaseParser.Parse(Latest);

        Assert.NotNull(release);
        Assert.Equal(new Version(0, 2, 0), release.Version);
        Assert.Equal("v0.2.0", release.Tag);
        Assert.Equal("https://github.com/owner/parrot/releases/tag/v0.2.0", release.PageUrl);
        Assert.EndsWith("/Parrot-Setup-0.2.0.exe", release.InstallerUrl);
        Assert.Equal(45123456, release.InstallerSize);
        Assert.Equal("abcdef0123456789", release.Sha256);
        Assert.Equal(new DateTimeOffset(2026, 9, 21, 10, 0, 0, TimeSpan.Zero), release.PublishedAt);
    }

    [Fact]
    public void A_release_without_an_installer_is_ignored()
    {
        var json = Latest.Replace("Parrot-Setup-0.2.0.exe", "Parrot-0.2.0.zip");
        Assert.Null(ReleaseParser.Parse(json));
    }

    [Theory]
    [InlineData("\"draft\": false", "\"draft\": true")]
    [InlineData("\"prerelease\": false", "\"prerelease\": true")]
    public void Drafts_and_prereleases_are_ignored(string from, string to)
    {
        Assert.Null(ReleaseParser.Parse(Latest.Replace(from, to)));
    }

    [Fact]
    public void A_missing_digest_leaves_the_hash_empty()
    {
        var json = Latest.Replace("\"digest\": \"sha256:ABCDEF0123456789\",", "");
        Assert.Null(ReleaseParser.Parse(json)!.Sha256);
    }

    [Theory]
    [InlineData("v1.2.3", 1, 2, 3)]
    [InlineData("1.2.3", 1, 2, 3)]
    [InlineData("V0.10", 0, 10, 0)]
    [InlineData("2", 2, 0, 0)]
    [InlineData("v1.2.3-beta.1", 1, 2, 3)]
    [InlineData("1.2.3.4", 1, 2, 3)]
    public void Tags_parse_into_three_part_versions(string tag, int major, int minor, int build)
    {
        Assert.Equal(new Version(major, minor, build), ReleaseParser.ParseVersion(tag));
    }

    [Theory]
    [InlineData("latest")]
    [InlineData("")]
    public void Tags_that_are_not_versions_parse_to_null(string tag)
    {
        Assert.Null(ReleaseParser.ParseVersion(tag));
    }
}
