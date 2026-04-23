using System;
using VGAnima.Llm;
using VGAnima.Persistence;

namespace VGAnima.Missions;

/// <summary>Scores a completed mission's "notability" on a 1-10 scale.
/// Higher-magnitude events propagate further in the journal context
/// windows — a Scaplord-tier warzone bust shows up in distant brokers'
/// <c>rumors</c> window (via the reach formula); a small ore-gather
/// barely scrapes past its neighbors.
///
/// <para>v1 formula is deliberately coarse:
/// <c>base = missionLevel / 2</c> (1..~20 → 1..~10),
/// <c>+1</c> if the mission was a multi-step shape,
/// <c>+1</c> for combat archetypes (fights get talked about more),
/// <c>+2</c> for defended-collect (rare + dramatic),
/// <c>-2</c> if the outcome was <c>abandoned</c> (nobody brags about walking),
/// <c>-1</c> if the outcome was <c>failed</c>.
/// Clamped to [1, 10].</para>
///
/// <para>v2 could incorporate reward magnitude, duration, enemy unit
/// counts, or story-flag gravity — but v1 ships with what's already in
/// the block.</para></summary>
internal static class MagnitudeScorer
{
    public static int Score(LlmMissionBlock block, string archetype, string outcome, int missionLevel)
    {
        var score = missionLevel / 2;

        // Multi-step implies more scope / narrative weight.
        if (block.Steps.Count > 1) score += 1;

        // Combat archetypes get talked about more than quiet hauls.
        if (archetype == MissionArchetypes.Combat) score += 1;
        if (archetype == MissionArchetypes.DefendedCollect) score += 2;

        // Outcome modifiers — abandoned/failed missions propagate
        // less because they don't generate the success chatter that
        // drives word-of-mouth.
        if (outcome == CompletedMissionOutcomes.Abandoned) score -= 2;
        if (outcome == CompletedMissionOutcomes.Failed)    score -= 1;

        return Math.Max(1, Math.Min(10, score));
    }
}
