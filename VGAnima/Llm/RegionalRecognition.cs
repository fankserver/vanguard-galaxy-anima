using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>The single production decision behind the <c>regionally_known</c>
/// prompt window: whether the live context contributes regional-recognition
/// entries at all, and which ones.
///
/// <para>It exists so that decision has exactly one implementation. The bar
/// context builder calls it, and nothing else may re-derive the gate: a copy of
/// "recording is live, so build from the registry" elsewhere could drift from
/// this one and describe the player on evidence this build no longer maintains.
/// </para>
///
/// <para>It reads the live state itself — <see cref="Plugin.VisitHistoryRecording"/>,
/// <see cref="Plugin.PersistedRegistry"/>, <see cref="Plugin.MissionJournalBridge"/>
/// and <see cref="Plugin.Clock"/> — so no caller can supply a substitute flag or
/// a substitute visit history. Null means the key is omitted entirely: either
/// nothing is live, or visit recording is unavailable/stopped and the preserved
/// counts must not be pitched as current, or no system clears
/// <see cref="RegionallyKnownBuilder.MinVisitsThreshold"/>.</para></summary>
internal static class RegionalRecognition
{
    /// <summary>Entries for the current live context, or null when
    /// <c>regionally_known</c> must be omitted. Sampled at the call: a later stop
    /// does not retract a snapshot already handed to an in-flight pitch.</summary>
    internal static IReadOnlyList<LlmRegionallyKnownEntry>? ForCurrentContext()
    {
        if (Plugin.Instance is not { } plugin) return null;
        if (plugin.PersistedRegistry is not { } registry || !plugin.VisitHistoryRecording) return null;
        return RegionallyKnownBuilder.Build(
            registry.VisitedSystems,
            plugin.MissionJournalBridge,
            currentGameSeconds: plugin.Clock.GameSeconds);
    }
}
