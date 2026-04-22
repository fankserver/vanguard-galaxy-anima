using Newtonsoft.Json;
using VGAnima.Llm;

namespace VGAnima.Persistence;

internal sealed record PersistedEntry(
    [property: JsonProperty("storyId")]      string StoryId,
    [property: JsonProperty("state")]        string State,
    [property: JsonProperty("missionBlock")] LlmMissionBlock MissionBlock,
    [property: JsonProperty("broker")]       PersistedBroker Broker,
    [property: JsonProperty("timestamps")]   PersistedTimestamps Timestamps);

// Only fields vanilla doesn't persist natively are stored here. The
// salesman's name, description, gender, and portrait all derive from
// the seed via vanilla's own regeneration — at dispatch / rehydration
// time we re-resolve them from the seed.
//
// The *DisplaySnapshot fields below are a journal-driven addition: we
// save a snapshot of the broker name / station name / system name at
// creation time so the completed-mission log (which builds after the
// broker is gone) has human-readable strings to put into broker
// dialogue without re-resolving from vanilla state. All three are
// nullable so pre-journal sidecars deserialize unchanged.
internal sealed record PersistedBroker(
    [property: JsonProperty("seed")]      string Seed,
    [property: JsonProperty("stationId")] string StationId,
    [property: JsonProperty("story")]     LlmStory Story,
    [property: JsonProperty("nameSnapshot",        NullValueHandling = NullValueHandling.Ignore)] string? NameSnapshot        = null,
    [property: JsonProperty("stationNameSnapshot", NullValueHandling = NullValueHandling.Ignore)] string? StationNameSnapshot = null,
    [property: JsonProperty("systemNameSnapshot",  NullValueHandling = NullValueHandling.Ignore)] string? SystemNameSnapshot  = null);

internal sealed record PersistedTimestamps(
    [property: JsonProperty("createdGameSeconds")]  double CreatedGameSeconds,
    [property: JsonProperty("createdRealUtc")]      string CreatedRealUtc,
    [property: JsonProperty("lastSeenGameSeconds")] double LastSeenGameSeconds,
    [property: JsonProperty("lastSeenRealUtc")]     string LastSeenRealUtc);

internal static class PersistedEntryStates
{
    public const string Offered  = "offered";
    public const string Accepted = "accepted";
}
