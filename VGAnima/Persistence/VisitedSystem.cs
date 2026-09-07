using Newtonsoft.Json;

namespace VGAnima.Persistence;

/// <summary>Per-system visit tally persisted in the sidecar so the LLM can
/// surface "regional recognition" — brokers casually acknowledging the
/// player as a regular in the system ("you've been through here a few
/// times"). Written by <c>SystemVisitObserver</c> from witnessed API travel
/// arrivals; read by <c>RegionallyKnownBuilder</c> at context-build time.
///
/// <para>Keyed by <see cref="Guid"/> (the opaque native system identifier
/// carried by the observed location) rather than <see cref="Name"/> —
/// procedural name regeneration or localization could otherwise corrupt the
/// map. The display label is snapshotted alongside so the LLM gets a
/// render-ready string without a cross-lookup against live galaxy state; it
/// stays empty while no observed label was available.</para>
///
/// <para>Fields are all primitive-typed so
/// <see cref="Newtonsoft.Json.TypeNameHandling.Auto"/> doesn't emit
/// <c>$type</c> discriminators — no binder allowlist change required.</para></summary>
internal sealed record VisitedSystem(
    [property: JsonProperty("guid")]               string Guid,
    [property: JsonProperty("name")]               string Name,
    [property: JsonProperty("visitCount")]         int    VisitCount,
    [property: JsonProperty("firstVisitSeconds")]  double FirstVisitGameSeconds,
    [property: JsonProperty("lastVisitSeconds")]   double LastVisitGameSeconds);
