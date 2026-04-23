using Newtonsoft.Json;

namespace VGAnima.Persistence;

/// <summary>Top-level schema of a <c>&lt;save&gt;.vganima.json</c> sidecar.
/// Version 2 is the current shape (intent-based mission records). v1
/// sidecars — with <c>LlmKillEnemies</c> / <c>LlmClearPoi</c> /
/// <c>LlmCollectItemTypes</c> / <c>LlmProtectUnit</c> / <c>LlmTriggerObjective</c>
/// <c>$type</c> refs — are rejected here (version mismatch) or at the
/// binder (deleted types); both paths quarantine the file via
/// <see cref="SidecarIO"/> and let the game continue with an empty
/// VGAnima registry. In-flight v1 broker missions appear as orphans on
/// first v2 load; vanilla archives the zero-step
/// <see cref="VGAnima.Missions.PlaceholderMission"/> on the next tick.
///
/// <para>The <c>Entries</c> property is typed as <c>PersistedEntry[]</c>
/// rather than <c>IReadOnlyList&lt;PersistedEntry&gt;</c> so its declared
/// type matches its runtime type — under
/// <see cref="SerializerSettings"/>' <c>TypeNameHandling.Auto</c>, any
/// interface-typed collection would emit a top-level <c>$type</c>
/// discriminator on the <c>entries</c> field, which we don't want for the
/// schema's public shape.</para></summary>
internal sealed record SidecarSchema(
    [property: JsonProperty("version")] int Version,
    [property: JsonProperty("entries")] PersistedEntry[] Entries,
    // Additive v1 field that survives into v2 unchanged — completed
    // missions are archetype-string + metadata, no LLM-objective type
    // names, so the v1 → v2 break doesn't touch them.
    [property: JsonProperty("completed_missions", NullValueHandling = NullValueHandling.Ignore)]
    CompletedMissionRecord[]? CompletedMissions = null)
{
    // v1 → v2 bump: v1 sidecars have `$type` refs to deleted objective
    // types (LlmKillEnemies etc.). Version check in SidecarIO.Read
    // triggers `UnsupportedVersion` quarantine before the binder has
    // to reject those types. Either path ends in quarantine — clean
    // break, no migration, no in-flight broker survives.
    public const int CurrentVersion = 2;

    /// <summary>Shared Newtonsoft settings for read/write of sidecar JSON.
    /// <para><c>TypeNameHandling.Auto</c> is required because
    /// <see cref="VGAnima.Llm.LlmObjective"/> / <see cref="VGAnima.Llm.LlmReward"/>
    /// are abstract records — without a <c>$type</c> discriminator, the
    /// deserializer can't reinstantiate the concrete subtype when reading
    /// back a <see cref="PersistedEntry.MissionBlock"/>. These settings only
    /// apply where explicitly passed; LLM-facing paths in
    /// <c>HttpLlmClient</c> / <c>BarRefreshPatches</c> must never use them
    /// (the model's input JSON has to stay discriminator-free).</para>
    /// <para><see cref="SidecarIO"/> (Task 4) must use these settings so its
    /// output pairs with what this schema expects on read.</para>
    /// <para><b>Security note:</b> <see cref="TypeNameHandling.Auto"/>
    /// without a <see cref="Newtonsoft.Json.Serialization.ISerializationBinder"/>
    /// is a deserialization-gadget surface per Newtonsoft's own guidance.
    /// Sidecars are not guaranteed to be user-authored — they travel with
    /// saves (cloud-sync, share, modpack). A locked-down
    /// <c>ISerializationBinder</c> is added by <see cref="SidecarIO"/>
    /// (MP-T4) allowlisting exactly the persisted
    /// <see cref="VGAnima.Llm.LlmObjective"/> /
    /// <see cref="VGAnima.Llm.LlmReward"/> subtypes. Do not strip the
    /// binder when it lands — it's load-bearing.</para></summary>
    public static JsonSerializerSettings SerializerSettings { get; } = new()
    {
        TypeNameHandling = TypeNameHandling.Auto,
        SerializationBinder = VGAnimaSidecarSerializationBinder.Instance,
    };
}
