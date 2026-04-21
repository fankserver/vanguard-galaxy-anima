using Newtonsoft.Json;

namespace VGAnima.Persistence;

/// <summary>Top-level schema of a <c>&lt;save&gt;.vganima.json</c> sidecar.
/// Version 1 is the only shape the current build understands; unknown
/// versions are quarantined by <see cref="SidecarIO"/>.
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
    [property: JsonProperty("entries")] PersistedEntry[] Entries)
{
    public const int CurrentVersion = 1;

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
