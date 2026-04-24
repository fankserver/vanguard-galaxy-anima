using Newtonsoft.Json;

namespace VGAnima.Persistence;

/// <summary>Top-level schema of a <c>&lt;save&gt;.vganima.json</c> sidecar.
///
/// <para>Version history:
/// <list type="bullet">
///   <item><b>v1</b>: original objective-typed mission records. Hard cut —
///     v1 sidecars quarantine at the binder.</item>
///   <item><b>v2</b>: intent-based mission records. No longer upgradeable
///     in a single hop (v2 → v4 skips the v3 contract). Quarantines.</item>
///   <item><b>v3</b>: added <c>visited_systems</c> and
///     <c>completed_missions</c> (rolling log of resolved VGAnima
///     missions). Still upgradeable: the completed-missions field is
///     dropped on read.</item>
///   <item><b>v4</b> (current): drops <c>completed_missions</c>. Resolved
///     mission history now comes from the VGMissionJournal plugin via
///     <see cref="VGAnima.MissionJournal.VgMissionJournalBridge"/>;
///     VGAnima no longer maintains its own log. v3 → v4 upgrade is
///     lossy (completed-mission rows are discarded), but that log was
///     only a prompt-context signal — no gameplay state loss.</item>
/// </list></para>
///
/// <para>Array-typed collection properties keep their declared types
/// matching their runtime types so <see cref="TypeNameHandling.Auto"/>
/// doesn't emit a top-level <c>$type</c> discriminator on the field.</para></summary>
internal sealed record SidecarSchema(
    [property: JsonProperty("version")] int Version,
    [property: JsonProperty("entries")] PersistedEntry[] Entries,
    [property: JsonProperty("visited_systems", NullValueHandling = NullValueHandling.Ignore)]
    VisitedSystem[]? VisitedSystems = null)
{
    public const int CurrentVersion = 4;

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
