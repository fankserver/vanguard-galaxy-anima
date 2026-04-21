using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Serialization;
using VGAnima.Llm;

namespace VGAnima.Persistence;

/// <summary>Allowlisting binder for <see cref="Newtonsoft.Json.TypeNameHandling.Auto"/>
/// used by <see cref="SidecarSchema.SerializerSettings"/>. Only the persisted
/// <see cref="LlmObjective"/> and <see cref="LlmReward"/> subtypes can be
/// reinstantiated on read; anything else throws.
///
/// <para>Load-bearing: sidecars travel with saves (cloud-sync, share,
/// modpack distribution) so input isn't guaranteed user-authored. Without
/// this binder, a crafted JSON could instantiate arbitrary types via
/// Newtonsoft's well-known gadget chains.</para></summary>
internal sealed class VGAnimaSidecarSerializationBinder : ISerializationBinder
{
    public static readonly VGAnimaSidecarSerializationBinder Instance = new();

    // Allowlisted persisted subtypes plus the collection shapes Newtonsoft
    // emits `$type` for whenever a declared type is an interface (e.g.
    // IReadOnlyList<T>) but the runtime type is a concrete collection.
    // BOTH array shapes (when a fixture builds with `new[] {...}`) AND
    // List<T> shapes (when JsonConvert deserializes a JSON array into an
    // IReadOnlyList<T> field — its default is List<T>) must be allowed:
    // production sidecars carry LlmMissionBlocks that came through the LLM
    // response path (List<T>), tests may build fixtures with arrays. None
    // of these container shapes are dangerous on their own — their
    // elements recursively bind through this same allowlist.
    // If a new LlmObjective / LlmReward subtype is added, extend this
    // list in the same commit that adds the subtype.
    private static readonly Type[] AllowedTypes = new[]
    {
        // Concrete objective / reward subtypes (the whole point of TypeNameHandling.Auto).
        typeof(LlmKillEnemies),
        typeof(LlmProtectUnit),
        typeof(LlmTriggerObjective),
        typeof(LlmCollectItemTypes),
        typeof(LlmClearPoi),
        typeof(LlmCreditsReward),
        typeof(LlmExperienceReward),
        typeof(LlmReputationReward),
        // Collection shapes — array variants (test fixtures built with `new[]`).
        typeof(LlmMissionStep[]),
        typeof(LlmObjective[]),
        typeof(LlmReward[]),
        typeof(string[]),
        // Collection shapes — List<T> variants (production JSON deserialization
        // of `IReadOnlyList<T>` fields defaults to `List<T>`). This is what
        // real LLM-derived LlmMissionBlocks carry; without these four, the
        // binder rejects its own output on round-trip.
        typeof(List<LlmMissionStep>),
        typeof(List<LlmObjective>),
        typeof(List<LlmReward>),
        typeof(List<string>),
    };

    // Pre-built lookup keyed by every name-form Newtonsoft might ask for.
    // For a generic like List<LlmMissionStep>, Type.FullName is the long
    // form (includes Version/Culture/PublicKeyToken on the type arg),
    // but Newtonsoft under TypeNameAssemblyFormatHandling.Simple (the
    // default) passes the short form — type arg assembly name only, no
    // version. We allowlist both so the binder matches on either.
    private static readonly Dictionary<string, Type> AllowedByName = BuildLookup();

    private static Dictionary<string, Type> BuildLookup()
    {
        var lookup = new Dictionary<string, Type>(StringComparer.Ordinal);
        foreach (var t in AllowedTypes)
        {
            if (t.FullName is string fn) lookup[fn] = t;
            var simple = SimpleTypeName(t);
            lookup[simple] = t;
        }
        return lookup;
    }

    /// <summary>Mirrors the string form Newtonsoft emits/consumes under
    /// <c>TypeNameAssemblyFormatHandling.Simple</c>. Non-generic types:
    /// <see cref="Type.FullName"/>. Generic types: outer full name followed
    /// by <c>[[argFullName, argAssemblySimpleName], ...]</c> per arg, with
    /// no version/culture/publickeytoken anywhere.</summary>
    private static string SimpleTypeName(Type t)
    {
        if (!t.IsGenericType) return t.FullName!;
        var def = t.GetGenericTypeDefinition();
        var args = t.GetGenericArguments();
        var sb = new StringBuilder();
        sb.Append(def.FullName).Append('[');
        for (var i = 0; i < args.Length; i++)
        {
            if (i > 0) sb.Append(',');
            var a = args[i];
            sb.Append('[').Append(SimpleTypeName(a))
              .Append(", ").Append(a.Assembly.GetName().Name).Append(']');
        }
        sb.Append(']');
        return sb.ToString();
    }

    public Type BindToType(string? assemblyName, string typeName)
    {
        if (AllowedByName.TryGetValue(typeName, out var t)) return t;
        throw new Newtonsoft.Json.JsonSerializationException(
            $"Refused to bind to disallowed type `{typeName}` — not in sidecar allowlist");
    }

    public void BindToName(Type serializedType, out string? assemblyName, out string? typeName)
    {
        assemblyName = null;
        typeName = serializedType.FullName;
    }
}
