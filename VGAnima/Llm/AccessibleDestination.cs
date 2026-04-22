using Newtonsoft.Json;

namespace VGAnima.Llm;

/// <summary>One destination the LLM may choose for intents that need a
/// target station (<c>deliver_to_station</c> / <c>haul_goods</c>).
///
/// <para>Identified by <see cref="ShortId"/> — a short opaque label like
/// <c>dest_3</c>. LLMs can misspell GUIDs; a short sequential id is cheap
/// to copy faithfully. The mapping back to the real vanilla station
/// <see cref="Guid"/> lives on the built list and is resolved at factory
/// time so the LLM never has to touch a UUID.</para>
///
/// <para>Built by <see cref="AccessibleDestinationsBuilder"/> from the
/// broker's station outward one jumpgate hop. Same-faction destinations
/// are ranked above cross-faction ones within the same jump distance
/// (LLM nudge, not hard restriction — it may still pick a rival-faction
/// target narratively).</para></summary>
internal sealed record AccessibleDestination(
    [property: JsonProperty("id")]             string ShortId,
    [property: JsonProperty("station_name")]   string StationName,
    [property: JsonProperty("system_name")]    string SystemName,
    [property: JsonProperty("faction")]        string FactionIdentifier,
    [property: JsonProperty("faction_display_name")] string FactionDisplayName,
    [property: JsonProperty("jumps_away")]     int    JumpsAway,
    [property: JsonProperty("same_faction_as_broker")] bool SameFactionAsBroker,
    // Not serialized — vanilla station GUID. The factory uses this when
    // building TravelToPOI.targetPOI; the LLM never sees it.
    [property: JsonIgnore] string Guid);
