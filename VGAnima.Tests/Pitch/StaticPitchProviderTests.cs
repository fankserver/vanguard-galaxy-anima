using VGAnima.Pitch;
using Xunit;

namespace VGAnima.Tests.Pitch;

public class StaticPitchProviderTests
{
    private static PatronContext Ctx(bool isMale = true) =>
        new("Test Broker", isMale, Station: null!, Mission: null!);

    [Fact]
    public void Pitch_EqualsInitialState()
    {
        var provider = new StaticPitchProvider();
        var direct = provider.Pitch(Ctx());
        var viaState = provider.PitchForState(Ctx(), BrokerState.Initial);
        Assert.Equal(direct.Lines, viaState.Lines);
    }

    [Fact]
    public void Initial_HasThreeToFiveLines()
    {
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), BrokerState.Initial);
        Assert.InRange(result.Lines.Count, 3, 5);
        foreach (var line in result.Lines) Assert.False(string.IsNullOrWhiteSpace(line));
    }

    [Fact]
    public void InProgress_HasOneOrTwoLines()
    {
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), BrokerState.InProgress);
        Assert.InRange(result.Lines.Count, 1, 2);
        foreach (var line in result.Lines) Assert.False(string.IsNullOrWhiteSpace(line));
    }

    [Fact]
    public void ReadyToClaim_HasThreeToFourLines()
    {
        // Must have enough lines for one of them to carry the CompleteMission
        // trigger without awkwardly short dialogue.
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), BrokerState.ReadyToClaim);
        Assert.InRange(result.Lines.Count, 3, 4);
        foreach (var line in result.Lines) Assert.False(string.IsNullOrWhiteSpace(line));
    }

    [Fact]
    public void Done_HasOneOrTwoLines()
    {
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), BrokerState.Done);
        Assert.InRange(result.Lines.Count, 1, 2);
        foreach (var line in result.Lines) Assert.False(string.IsNullOrWhiteSpace(line));
    }

    [Theory]
    [InlineData(BrokerState.Initial)]
    [InlineData(BrokerState.InProgress)]
    [InlineData(BrokerState.ReadyToClaim)]
    [InlineData(BrokerState.Done)]
    public void AllStates_UseAsciiOnlyPunctuation(BrokerState state)
    {
        // The game's pixel16 font renders non-ASCII as blank spaces. Enforce
        // ASCII-only so em-dashes / smart quotes don't slip back in.
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), state);
        foreach (var line in result.Lines)
            foreach (var ch in line)
                Assert.True(ch < 128,
                    $"Non-ASCII char U+{(int)ch:X4} '{ch}' in {state} line: \"{line}\"");
    }
}
