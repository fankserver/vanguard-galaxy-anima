using System;
using HarmonyLib;
using BepInEx.Logging;
using LightJson;
using Source.Util;
using VGAnima.Persistence;

namespace VGAnima.Patches;

/// <summary>Harmony postfix on <see cref="SaveGame.Store"/>. After vanilla
/// successfully writes a save to
/// <c>{SaveGame.SavesPath}/{saveName}.save</c>, we flush the current
/// in-memory <see cref="PersistedBrokerRegistry"/> to the paired sidecar
/// at <c>{SaveGame.SavesPath}/{saveName}.save.vganima.json</c>.
///
/// <para>Postfix (not prefix) ensures the sidecar only commits if vanilla's
/// own save succeeded — avoids orphan sidecars referencing saves that
/// never got written. Atomic write (tmp + rename) via
/// <see cref="SidecarIO"/>. Every exception is swallowed and logged:
/// persistence failures must never poison vanilla's save success.</para>
///
/// <para>Wiring: <see cref="Plugin"/> assigns <see cref="Registry"/>,
/// <see cref="Io"/>, <see cref="Log"/> during <c>Awake</c>. When null,
/// the postfix is a no-op.</para></summary>
[HarmonyPatch(typeof(SaveGame), nameof(SaveGame.Store))]
internal static class SaveWritePatch
{
    public static PersistedBrokerRegistry? Registry;
    public static SidecarIO? Io;
    public static ManualLogSource? Log;

    /// <summary>Remembered so <c>Plugin</c>'s <c>ApplicationQuit</c> flush
    /// safety-net knows which slot the session was last attached to
    /// (spec §5 "Safety net"). Set on every successful flush.</summary>
    public static string? LastKnownSavePath;

    [HarmonyPostfix]
    private static void Postfix(JsonObject data, string saveName)
    {
        if (Registry is null || Io is null) return;

        try
        {
            var savePath    = SaveGame.SavesPath + "/" + saveName + ".save";
            var sidecarPath = SidecarPathResolver.From(savePath);
            var entries     = System.Linq.Enumerable.ToArray(Registry.All());
            var completed   = System.Linq.Enumerable.ToArray(Registry.CompletedMissions);
            // Emit null when the journal is empty so the field disappears
            // from disk entirely — keeps pre-journal sidecar shapes
            // byte-identical for users who never resolve a mission.
            var schema      = new SidecarSchema(
                Version:           SidecarSchema.CurrentVersion,
                Entries:           entries,
                CompletedMissions: completed.Length == 0 ? null : completed);
            Io.Write(sidecarPath, schema);
            LastKnownSavePath = savePath;
            Log?.LogInfo(
                $"Flushed {entries.Length} broker entr{(entries.Length == 1 ? "y" : "ies")} + " +
                $"{completed.Length} completed to {sidecarPath}");
        }
        catch (Exception e)
        {
            Log?.LogError($"Sidecar flush failed for save `{saveName}`: {e}");
            // Never rethrow: vanilla's save already succeeded; persistence
            // is best-effort. Next save attempt will try again.
        }
    }
}
