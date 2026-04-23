using Newtonsoft.Json;

namespace VGAnima.Persistence;

/// <summary>Top-level schema of a <c>&lt;save&gt;.vganima.json</c> sidecar.
/// Version 3 is the current shape: intent-based mission records plus the
/// visited-systems map that drives the "regionally known" signal.
///
/// <para>Version history:
/// <list type="bullet">
///   <item><b>v1</b>: original objective-typed mission records
///     (<c>LlmKillEnemies</c> / <c>LlmClearPoi</c> / <c>LlmCollectItemTypes</c>
///     / <c>LlmProtectUnit</c> / <c>LlmTriggerObjective</c>). <b>Hard cut</b> —
///     v1 sidecars quarantine at the binder because the types no longer exist.</item>
///   <item><b>v2</b>: intent-based mission records. Still readable — see
///     <see cref="SidecarIO.Read"/>, which upgrades v2 to v3 in memory by
///     defaulting <see cref="VisitedSystems"/> to null. The upgraded
///     schema is written back as v3 on the next save.</item>
///   <item><b>v3</b>: adds <see cref="VisitedSystems"/>. Purely additive —
///     no behavior change for existing broker / journal state.</item>
/// </list></para>
///
/// <para>The array-typed collection properties (<c>Entries</c>,
/// <c>CompletedMissions</c>, <c>VisitedSystems</c>) keep their declared
/// types matching their runtime types so
/// <see cref="SerializerSettings"/>' <see cref="Newtonsoft.Json.TypeNameHandling.Auto"/>
/// doesn't emit a top-level <c>$type</c> discriminator on the field. An
/// interface-typed collection (<c>IReadOnlyList&lt;T&gt;</c>) would bloat
/// the wire shape for no benefit.</para></summary>
internal sealed record SidecarSchema(
    [property: JsonProperty("version")] int Version,
    [property: JsonProperty("entries")] PersistedEntry[] Entries,
    // Additive v1 field that survives into v2/v3 unchanged — completed
    // missions are archetype-string + metadata, no LLM-objective type
    // names, so the v1 → v2 break doesn't touch them.
    [property: JsonProperty("completed_missions", NullValueHandling = NullValueHandling.Ignore)]
    CompletedMissionRecord[]? CompletedMissions = null,
    // Added in v3. Primitive-typed records only, so no binder allowlist
    // changes required. Null/empty for players who haven't traveled yet
    // or for freshly-upgraded v2 sidecars.
    [property: JsonProperty("visited_systems", NullValueHandling = NullValueHandling.Ignore)]
    VisitedSystem[]? VisitedSystems = null)
{
    // v2 → v3 bump: added VisitedSystems. Additive-only; old data
    // roundtrips. SidecarIO.Read accepts v2 and upgrades in memory.
    public const int CurrentVersion = 3;

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
