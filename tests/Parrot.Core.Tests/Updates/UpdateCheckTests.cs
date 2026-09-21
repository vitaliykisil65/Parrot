using Parrot.Core.Updates;

namespace Parrot.Core.Tests.Updates;

public class UpdateCheckTests
{
    private static ReleaseInfo Release(string version) =>
        new(ReleaseParser.ParseVersion(version)!, "v" + version, "page", "url", 1, null, null);

    [Theory]
    [InlineData("0.1.0.0", "0.1.1", true)]
    [InlineData("0.1.9", "0.1.10", true)]
    [InlineData("0.1.10", "0.1.9", false)]
    [InlineData("0.2.0.0", "0.2.0", false)]
    [InlineData("1.0.0", "0.9.9", false)]
    public void Compares_versions_numerically(string current, string release, bool newer)
    {
        Assert.Equal(newer, UpdateCheck.IsNewer(Version.Parse(current), Release(release)));
    }

    [Fact]
    public void A_dismissed_version_is_not_offered_again()
    {
        Assert.False(UpdateCheck.ShouldOffer(new Version(0, 1, 0), Release("0.2.0"), "0.2.0"));
    }

    [Fact]
    public void A_release_newer_than_the_dismissed_one_is_offered()
    {
        Assert.True(UpdateCheck.ShouldOffer(new Version(0, 1, 0), Release("0.2.1"), "0.2.0"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("garbage")]
    public void Without_a_usable_dismissed_version_a_newer_release_is_offered(string? dismissed)
    {
        Assert.True(UpdateCheck.ShouldOffer(new Version(0, 1, 0), Release("0.2.0"), dismissed));
    }
}
