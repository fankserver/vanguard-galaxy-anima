using System;
using System.Collections.Generic;
using VGAnima.Llm;

namespace VGAnima.Pitch;

/// <summary>Pitch provider that replays LLM-authored dialogue from the broker's
/// <see cref="LlmStory"/>. State mapping:
/// <list type="bullet">
///   <item><see cref="BrokerState.Initial"/> → <see cref="LlmStory.Pitch"/></item>
///   <item><see cref="BrokerState.InProgress"/> → <see cref="LlmStory.CheckIn"/></item>
///   <item><see cref="BrokerState.ReadyToClaim"/> → <see cref="LlmStory.Payout"/></item>
///   <item><see cref="BrokerState.Done"/> → last element of <see cref="LlmStory.Payout"/></item>
/// </list>
///
/// Takes a <see cref="Func{T,TResult}"/> that looks up an <see cref="LlmStory"/>
/// by NPC name — a minimal seam so the provider stays unit-testable without
/// pulling the whole <see cref="Cache.ConversionRegistry{TKey,TValue}"/> into
/// scope. Production wiring (Task 8) passes a lambda that searches the registry
/// by name via <c>FindByValue</c>.
///
/// Null-story fallback: returns a 1-line placeholder when the lookup yields null.
/// This handles the rehydrated-broker window where the seed is known but the
/// async LLM call for this session hasn't landed yet. A broker in that window
/// will still open a dialogue rather than NRE; the placeholder text is bland by
/// design (the LLM-authored line is the only "good" one).</summary>
internal sealed class LlmPitchProvider : IPitchProvider
{
    private readonly Func<string, LlmStory?> _lookup;

    public LlmPitchProvider(Func<string, LlmStory?> lookup)
    {
        _lookup = lookup;
    }

    public PitchResult Pitch(PatronContext ctx) => PitchForState(ctx, BrokerState.Initial);

    public PitchResult PitchForState(PatronContext ctx, BrokerState state)
    {
        var story = _lookup(ctx.NpcName);
        if (story == null)
            return new PitchResult(new[] { FallbackLineFor(state) });

        IReadOnlyList<string> lines = state switch
        {
            BrokerState.Initial      => story.Pitch,
            BrokerState.InProgress   => story.CheckIn,
            BrokerState.ReadyToClaim => story.Payout,
            BrokerState.Done         => LastOrFallback(story.Payout, FallbackLineFor(state)),
            _                        => new[] { FallbackLineFor(state) },
        };

        if (lines.Count == 0)
            return new PitchResult(new[] { FallbackLineFor(state) });

        return new PitchResult(lines);
    }

    private static IReadOnlyList<string> LastOrFallback(IReadOnlyList<string> src, string fallback)
    {
        if (src.Count == 0) return new[] { fallback };
        return new[] { src[src.Count - 1] };
    }

    private static string FallbackLineFor(BrokerState state) => state switch
    {
        BrokerState.Initial      => "Captain. Give me a moment.",
        BrokerState.InProgress   => "Still on it, Captain?",
        BrokerState.ReadyToClaim => "Well done, Captain. Settling up.",
        BrokerState.Done         => "Safe flying, Captain.",
        _                        => "Captain.",
    };
}
