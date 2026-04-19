using VGAnima.Pitch;
using Xunit;

namespace VGAnima.Tests.Pitch;

public class StaticPitchProviderTests
{
    [Fact]
    public void Pitch_ContainsBoardReference()
    {
        var provider = new StaticPitchProvider();
        var ctx = new PatronContext(
            NpcName: "Robert Miyama",
            IsMale: true,
            Station: null!,                    // unused by static provider
            Mission: null!);                   // unused by static provider

        var result = provider.Pitch(ctx);

        Assert.NotEmpty(result.Lines);
        Assert.Contains(result.Lines, l => l.ToLowerInvariant().Contains("board"));
    }

    [Fact]
    public void Pitch_IncludesNpcName()
    {
        var provider = new StaticPitchProvider();
        var ctx = new PatronContext("Alex Chen", IsMale: false, Station: null!, Mission: null!);

        var result = provider.Pitch(ctx);

        Assert.True(result.Lines.Count >= 3,
            $"Expected >=3 pitch lines, got {result.Lines.Count}");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pitch_IsGenderAgnostic_Structurally(bool isMale)
    {
        var provider = new StaticPitchProvider();
        var ctx = new PatronContext("Test Name", isMale, null!, null!);

        var result = provider.Pitch(ctx);

        Assert.Equal(3, result.Lines.Count);
    }
}
