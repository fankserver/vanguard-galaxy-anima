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
///       via seed prefix). <see cref="StationId"/> is the serializable string form
///       of the station reference, carried here so persistence
///       (<see cref="VGAnima.Persistence.PersistedBroker"/>) can roundtrip the
///       binding without a live <see cref="SpaceStation"/>.</item>
///     <item>The unique <c>storyId</c> for this broker's LLM-authored mission.
///       Minted by <see cref="VGAnima.Missions.LlmMissionAssigner"/> after the
///       LLM call validates and the Mission is built+registered; never rewritten.</item>
///     <item>The validated <see cref="LlmStory"/> — dialogue + optional
///       <see cref="LlmMissionBlock"/>. For v2-mission responses the Mission
///       block is already materialized as a live registered Mission; the block
///       is kept here only for diagnostics / future save serialization.</item>
///   </list>
/// Not persisted directly — lives in <see cref="ConversionRegistry"/> during a
/// session. The sidecar (<see cref="VGAnima.Persistence.SidecarIO"/>) carries
/// the durable copy.
/// <para>No <c>DisplayName</c> / <c>IsMale</c> fields: the salesman's vanilla
/// seed-derived identity is authoritative; VGAnima no longer overrides those
/// properties, so no in-session cache of them is needed.</para></summary>
internal sealed class ConversionRecord
{
    public IReadOnlyList<(string Speaker, string Text)> WarmedLines { get; }
    public SpaceStation Station { get; }
    public string StoryId { get; }
    public LlmStory? LlmStory { get; }
    public string StationId { get; }

    public ConversionRecord(
        IReadOnlyList<(string Speaker, string Text)> warmedLines,
        SpaceStation station,
        string storyId,
        LlmStory? llmStory,
        string stationId)
    {
        WarmedLines = warmedLines;
        Station = station;
        StoryId = storyId;
        LlmStory = llmStory;
        StationId = stationId;
    }
}
