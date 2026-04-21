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
///     <item>The unique <c>storyId</c> for this broker's LLM-authored mission.
///       Minted by <see cref="VGAnima.Missions.LlmMissionAssigner"/> after the
///       LLM call validates and the Mission is built+registered; never rewritten.
///       Legacy v1 records (with fixed <c>vganima_test_jobsite_survey</c>) still
///       load via <see cref="VGAnima.Missions.TestStoryMissions"/>.</item>
///     <item>The validated <see cref="LlmStory"/> — dialogue + optional
///       <see cref="LlmMissionBlock"/>. For v2-mission responses the Mission
///       block is already materialized as a live registered Mission; the block
///       is kept here only for diagnostics / future save serialization.</item>
///   </list>
/// Not persisted — derived on bar open from the seed prefix; LlmStory dies
/// with the record (eviction or departure) and is re-synthesised on the next
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
