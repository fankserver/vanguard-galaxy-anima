using System.Collections.Generic;
using Source.Galaxy.POI;
using VGAnima.Llm;

namespace VGAnima.Cache;

/// <summary>Broker-level bookkeeping for one converted Salesman:
///   <list type="bullet">
///     <item>Warmed TTS lines for every pitch variant across all broker states
///       — dropped from VGTTS cache when the broker departs.</item>
///     <item>The source <see cref="SpaceStation"/> the broker was injected at
///       (used for rolloff eviction, departure cleanup, and broker identification
///       via seed prefix).</item>
///     <item>The single vanilla <c>storyId</c> this broker offers — one of
///       <c>SideMissionPatrol</c> / <c>SideMissionBounty</c> / <c>SideMissionFastLane</c>
///       OR <c>vganima_test_jobsite_survey</c>. One broker owns exactly one mission
///       (Option A contract).</item>
///     <item>Optional <see cref="LlmStory"/> — the validated LLM-authored dialogue
///       (v1: pitch / check_in / payout strings). Null for rehydrated brokers until
///       a fresh LLM call lands, and null on test paths that don't run the LLM.
///       Non-null on the happy broker-injection path once Task 8 lands.</item>
///   </list>
/// Not persisted — derived on bar open from the seed prefix + assigner; LlmStory
/// dies with the record (eviction or departure) and is re-synthesised on the next
/// injection of the same seed.</summary>
internal sealed class ConversionRecord
{
    public IReadOnlyList<(string Speaker, string Text)> WarmedLines { get; }
    public SpaceStation Station { get; }
    public string StoryId { get; }
    public LlmStory? LlmStory { get; }

    public ConversionRecord(
        IReadOnlyList<(string Speaker, string Text)> warmedLines,
        SpaceStation station,
        string storyId,
        LlmStory? llmStory = null)
    {
        WarmedLines = warmedLines;
        Station = station;
        StoryId = storyId;
        LlmStory = llmStory;
    }
}
