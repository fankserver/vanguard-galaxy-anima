using System.Collections.Generic;
using VGAnima.Llm;
using VGAnima.Pitch;
using Xunit;

namespace VGAnima.Tests.Pitch;

public class LlmPitchProviderTests
{
    private static PatronContext Ctx(string name) =>
        new(name, IsMale: true, Station: null!, Mission: null!);

    private static LlmStory Story() => new(
        Pitch:   new[] { "pitch-1", "pitch-2", "pitch-3" },
        CheckIn: new[] { "checkin-1" },
        Payout:  new[] { "payout-1", "payout-2", "payout-3" });

    [Fact]
    public void Initial_ReturnsPitchLines()
    {
        var story = Story();
        var provider = new LlmPitchProvider(name => story);

        var result = provider.PitchForState(Ctx("Broker"), BrokerState.Initial);

        Assert.Equal(story.Pitch, result.Lines);
    }

    [Fact]
    public void InProgress_ReturnsCheckInLines()
    {
        var story = Story();
        var provider = new LlmPitchProvider(name => story);

        var result = provider.PitchForState(Ctx("Broker"), BrokerState.InProgress);

        Assert.Equal(story.CheckIn, result.Lines);
    }

    [Fact]
    public void ReadyToClaim_ReturnsPayoutLines()
    {
        var story = Story();
        var provider = new LlmPitchProvider(name => story);

        var result = provider.PitchForState(Ctx("Broker"), BrokerState.ReadyToClaim);

        Assert.Equal(story.Payout, result.Lines);
    }

    [Fact]
    public void Done_ReturnsLastPayoutLineOnly()
    {
        var story = Story();
        var provider = new LlmPitchProvider(name => story);

        var result = provider.PitchForState(Ctx("Broker"), BrokerState.Done);

        Assert.Single(result.Lines);
        Assert.Equal("payout-3", result.Lines[0]);
    }

    [Fact]
    public void Pitch_IsShortcutFor_Initial()
    {
        var story = Story();
        var provider = new LlmPitchProvider(name => story);

        var direct = provider.Pitch(Ctx("Broker"));
        var viaState = provider.PitchForState(Ctx("Broker"), BrokerState.Initial);

        Assert.Equal(direct.Lines, viaState.Lines);
    }

    [Fact]
    public void PassesNpcName_ToLookup()
    {
        string? capturedName = null;
        var provider = new LlmPitchProvider(name =>
        {
            capturedName = name;
            return Story();
        });

        provider.PitchForState(Ctx("Shawn Jenkins"), BrokerState.Initial);

        Assert.Equal("Shawn Jenkins", capturedName);
    }

    [Fact]
    public void NullStory_FallsBackToPlaceholder_Initial()
    {
        // Rehydrated broker — LLM call still in flight, lookup returns null.
        var provider = new LlmPitchProvider(name => null);

        var result = provider.PitchForState(Ctx("Broker"), BrokerState.Initial);

        Assert.Single(result.Lines);
        Assert.False(string.IsNullOrWhiteSpace(result.Lines[0]));
    }

    [Fact]
    public void NullStory_FallsBackToPlaceholder_InProgress()
    {
        var provider = new LlmPitchProvider(name => null);

        var result = provider.PitchForState(Ctx("Broker"), BrokerState.InProgress);

        Assert.Single(result.Lines);
    }

    [Fact]
    public void NullStory_FallsBackToPlaceholder_ReadyToClaim()
    {
        var provider = new LlmPitchProvider(name => null);

        var result = provider.PitchForState(Ctx("Broker"), BrokerState.ReadyToClaim);

        Assert.Single(result.Lines);
    }

    [Fact]
    public void NullStory_FallsBackToPlaceholder_Done()
    {
        var provider = new LlmPitchProvider(name => null);

        var result = provider.PitchForState(Ctx("Broker"), BrokerState.Done);

        Assert.Single(result.Lines);
    }

    [Fact]
    public void EmptyPayout_Done_FallsBackToPlaceholder()
    {
        // Defensive: if validation ever admits an empty payout (it doesn't
        // today — ResponseValidator enforces min=2), the provider still
        // returns *something* for Done rather than crashing.
        var story = new LlmStory(
            Pitch:   new[] { "a", "b", "c" },
            CheckIn: new[] { "d" },
            Payout:  new List<string>());
        var provider = new LlmPitchProvider(name => story);

        var result = provider.PitchForState(Ctx("Broker"), BrokerState.Done);

        Assert.Single(result.Lines);
    }
}
