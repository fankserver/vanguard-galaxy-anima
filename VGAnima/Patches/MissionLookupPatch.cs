using System;
using HarmonyLib;
using LightJson;
using Source.MissionSystem;
using VGAnima.Missions;
using VGAnima.Persistence;

namespace VGAnima.Patches;

/// <summary>Harmony prefix on <see cref="Mission.FromJson"/> intercepts
/// the string-path dispatch (<c>StoryMission.Get(player, storyId)</c>)
/// for <c>vganima_llm_*</c> storyIds that our in-memory registry
/// doesn't cover. Returns a <see cref="PlaceholderMission"/> and skips
/// the original — prevents vanilla from throwing
/// <see cref="System.Collections.Generic.KeyNotFoundException"/> on
/// missing / stale sidecar entries.
///
/// <para>Vanilla contract (from decomp): <c>Mission.FromJson(JsonValue)</c>
/// checks <c>data.IsString</c>; on true it dispatches to
/// <c>StoryMission.Get(GamePlayer.current, data)</c>, which indexes a
/// private dict and throws on an unknown key. Our prefix runs BEFORE
/// that dispatch and substitutes a placeholder when our registry has
/// no record for the id.</para>
///
/// <para>Registry wiring: <see cref="Plugin"/> assigns
/// <see cref="Registry"/> during <c>Awake</c> once the persistence
/// subsystem lands. Until then (and in tests), <see cref="Registry"/>
/// is <c>null</c> and the prefix is a no-op — vanilla proceeds
/// unchanged. Only <c>vganima_llm_*</c> ids are inspected, so vanilla
/// story missions and tutorials never hit our path.</para></summary>
[HarmonyPatch(typeof(Mission), nameof(Mission.FromJson))]
internal static class MissionLookupPatch
{
    /// <summary>Stable prefix for VGAnima LLM-authored storyIds. Must
    /// match the prefix emitted by <c>LlmMissionAssigner.Assign</c>.</summary>
    internal const string LlmStoryIdPrefix = "vganima_llm_";

    /// <summary>In-memory persisted-broker registry. Plugin.Awake sets
    /// this after loading the sidecar. Null until the persistence epic
    /// is fully wired; the prefix treats null as "vanilla handles".</summary>
    public static PersistedBrokerRegistry? Registry;

    [HarmonyPrefix]
    // Harmony003 fires a false positive on `data.IsString` / `string id = data;`
    // below — the analyzer reads struct-parameter property access as an
    // assignment. `JsonValue` is a struct with readonly fields and only
    // exposes pure getters / implicit conversions, so no mutation occurs.
#pragma warning disable Harmony003
    private static bool Prefix(JsonValue data, ref Mission __result)
    {
        // Non-string dispatch: vanilla deserializes a full Mission object
        // — not our concern.
        if (!data.IsString) return true;

        string id = data;
        if (string.IsNullOrEmpty(id)) return true;

        // Not a VGAnima-authored id: let vanilla's StoryMission.Get handle
        // it (including throwing for truly-unknown vanilla ids — that's
        // vanilla's invariant, not our problem to paper over).
        if (!id.StartsWith(LlmStoryIdPrefix, StringComparison.Ordinal))
            return true;

        // Registry unwired or present — fall through to vanilla so
        // StoryMission.Get can use whatever factory is registered.
        // `Registry?.Get(id)` returns null when registry is null OR when
        // the id isn't known to us.
        if (Registry is null) return true;
        if (Registry.Get(id) is not null) return true;

        // Registry miss for a VGAnima-prefixed id: vanilla's
        // StoryMission.Get would throw KeyNotFoundException. Substitute
        // a placeholder and skip the original.
        __result = PlaceholderMission.Build(id);
        return false;
    }
#pragma warning restore Harmony003
}
