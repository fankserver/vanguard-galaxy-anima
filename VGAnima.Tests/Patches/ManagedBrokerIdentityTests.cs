using VGAnima.Patches;
using VGModAPI;
using Xunit;

namespace VGAnima.Tests.Patches;

public sealed class ManagedBrokerIdentityTests
{
    [Fact]
    public void SeedIdentityIsStableBoundedAndDoesNotEmbedUnsafeCharacters()
    {
        const string seed = "broker/Élodie\\station:42";
        var local = ManagedBrokerRosters.LocalId(seed);
        Assert.True(StoryContentId.IsValidSegment(local));
        Assert.Equal(47, local.Length);
        Assert.Equal(local, ManagedBrokerRosters.LocalId(seed));
        Assert.NotEqual(local, ManagedBrokerRosters.LocalId(seed + "next"));
    }
}
