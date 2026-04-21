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
// the seed via vanilla's own regeneration, so we don't duplicate them.
internal sealed record PersistedBroker(
    [property: JsonProperty("seed")]      string Seed,
    [property: JsonProperty("stationId")] string StationId,
    [property: JsonProperty("story")]     LlmStory Story);

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
