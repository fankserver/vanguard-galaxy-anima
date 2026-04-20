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
    public void Initial_ContainsBoardReference()
    {
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), BrokerState.Initial);
        Assert.True(result.Lines.Count >= 3, $"Expected >=3 lines, got {result.Lines.Count}");
        Assert.Contains(result.Lines, l => l.ToLowerInvariant().Contains("board"));
    }

    [Fact]
    public void Waiting_ReferencesBoard()
    {
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), BrokerState.Waiting);
        Assert.NotEmpty(result.Lines);
        Assert.Contains(result.Lines, l => l.ToLowerInvariant().Contains("board"));
    }

    [Fact]
    public void InProgress_AsksAboutProgress()
    {
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), BrokerState.InProgress);
        Assert.NotEmpty(result.Lines);
    }

    [Fact]
    public void ReadyToClaim_PromptsReport()
    {
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), BrokerState.ReadyToClaim);
        Assert.NotEmpty(result.Lines);
        Assert.Contains(result.Lines, l => l.ToLowerInvariant().Contains("board"));
    }

    [Fact]
    public void Done_Thanks()
    {
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), BrokerState.Done);
        Assert.NotEmpty(result.Lines);
        Assert.Contains(result.Lines, l => l.ToLowerInvariant().Contains("thank"));
    }

    [Theory]
    [InlineData(BrokerState.Initial)]
    [InlineData(BrokerState.Waiting)]
    [InlineData(BrokerState.InProgress)]
    [InlineData(BrokerState.ReadyToClaim)]
    [InlineData(BrokerState.Done)]
    internal void AllStates_UseAsciiOnlyPunctuation(BrokerState state)
    {
        var provider = new StaticPitchProvider();
        var result = provider.PitchForState(Ctx(), state);
        foreach (var line in result.Lines)
            foreach (var ch in line)
                Assert.True(ch < 128,
                    $"Non-ASCII char U+{(int)ch:X4} '{ch}' in {state} line: \"{line}\"");
    }
}
