using Newtonsoft.Json;

namespace VGAnima.Persistence;

/// <summary>Compact summary of a resolved VGAnima broker mission. Persisted
/// in the sidecar so the context builder can feed past-activity signals
/// into future broker prompts ("you've been hunting Corsairs in Zoran
/// lately").
///
/// <para>Written once per mission resolution (complete / fail / abandon)
/// by the lifecycle Harmony patches, then appended to the registry's
/// rolling log. Capped at <see cref="PersistedBrokerRegistry.MaxCompletedMissions"/>
/// entries — older ones drop off FIFO.</para>
///
/// <para>Fields are primitive-typed so TypeNameHandling.Auto does not emit
/// <c>$type</c> discriminators on them — no serialization-binder changes
/// required.</para></summary>
internal sealed record CompletedMissionRecord(
    [property: JsonProperty("storyId")]              string StoryId,
    [property: JsonProperty("brokerName")]           string BrokerName,
    [property: JsonProperty("stationId")]            string StationId,
    [property: JsonProperty("stationName")]          string StationName,
    [property: JsonProperty("sourceFaction")]        string SourceFaction,
    [property: JsonProperty("missionName")]          string MissionName,
    [property: JsonProperty("archetype")]            string Archetype,
    [property: JsonProperty("outcome")]              string Outcome,
    [property: JsonProperty("missionLevel")]         int    MissionLevel,
    [property: JsonProperty("systemName")]           string SystemName,
    [property: JsonProperty("magnitudeScore")]       int    MagnitudeScore,
    [property: JsonProperty("resolvedGameSeconds")]  double ResolvedGameSeconds,
    [property: JsonProperty("resolvedRealUtc")]      string ResolvedRealUtc);

internal static class CompletedMissionOutcomes
{
    public const string Completed  = "completed";
    public const string Failed     = "failed";
    public const string Abandoned  = "abandoned";
    // Used only by the journal's `active` window — marks entries that
    // are still in flight (state = offered or accepted) so the LLM can
    // distinguish them from resolved history when reading context.
    public const string InProgress = "in_progress";
}

internal static class MissionArchetypes
{
    public const string Combat           = "combat";
    public const string Gather           = "gather";
    public const string Salvage          = "salvage";
    public const string Deliver          = "deliver";
    public const string Escort           = "escort";
    public const string DefendedCollect  = "defended-collect";
    public const string Other            = "other";
}
