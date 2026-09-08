using Newtonsoft.Json;

namespace VGAnima.Persistence;

/// <summary>Top-level schema of a <c>&lt;save&gt;.vganima.json</c> sidecar.
///
/// <para>Version 5 stores narrative definitions, visit tallies, and pending managed-bar
/// cleanup identities. Versions 3 and 4 are accepted with empty cleanup metadata;
/// the obsolete completed-mission log in version 3 is ignored. Versions 1 and 2,
/// and versions newer than this reader, are not supported. Resolved mission history
/// comes from VGMissionJournal rather than this sidecar.</para>
///
/// <para>Array-typed collection properties keep their declared types
/// matching their runtime types so <see cref="TypeNameHandling.Auto"/>
/// doesn't emit a top-level <c>$type</c> discriminator on the field.</para></summary>
internal sealed record SidecarSchema(
    [property: JsonProperty("version")] int Version,
    [property: JsonProperty("entries")] PersistedEntry[] Entries,
    [property: JsonProperty("visited_systems", NullValueHandling = NullValueHandling.Ignore)]
    VisitedSystem[]? VisitedSystems = null,
    [property: JsonProperty("barReservations", NullValueHandling = NullValueHandling.Ignore)] BrokerReservation[]? BarReservations = null)
{
    public const int CurrentVersion = 5;

    /// <summary>Shared Newtonsoft settings for read/write of sidecar JSON.
    /// <see cref="TypeNameHandling.Auto"/> is required because
    /// <see cref="VGAnima.Llm.LlmObjective"/> / <see cref="VGAnima.Llm.LlmReward"/>
    /// are abstract records. The locked-down
    /// <see cref="VGAnimaSidecarSerializationBinder"/> allowlists exactly
    /// the persisted subtypes — do not strip it.</summary>
    public static JsonSerializerSettings SerializerSettings { get; } = new()
    {
        TypeNameHandling = TypeNameHandling.Auto,
        SerializationBinder = VGAnimaSidecarSerializationBinder.Instance,
    };
}
