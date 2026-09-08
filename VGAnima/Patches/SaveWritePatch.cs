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
/// <para>This legacy postfix is not proof of vanilla write success:
/// Store can catch errors internally. Only the sidecar's own write is
/// atomic (tmp + rename) via <see cref="SidecarIO"/>. It is not an
/// exact-snapshot API save-data owner. Every exception is swallowed and logged:
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
    internal static Func<bool>? CanWrite;

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
            if (CanWrite?.Invoke() == false) return;
            var savePath    = SaveGame.SavesPath + "/" + saveName + ".save";
            var sidecarPath = SidecarPathResolver.From(savePath);
            var entries     = System.Linq.Enumerable.ToArray(Registry.All());
            var visited     = System.Linq.Enumerable.ToArray(Registry.VisitedSystems.Values);
            // Emit null when the visited map is empty so the field stays
            // out of disk on early-game sidecars.
            var schema      = new SidecarSchema(
                Version:        SidecarSchema.CurrentVersion,
                Entries:        entries,
                VisitedSystems: visited.Length == 0 ? null : visited,
                BarReservations: Registry.BarReservations.Count == 0 ? null : System.Linq.Enumerable.ToArray(Registry.BarReservations));
            Io.Write(sidecarPath, schema);
            LastKnownSavePath = savePath;
            Log?.LogInfo(
                $"Flushed {entries.Length} broker entr{(entries.Length == 1 ? "y" : "ies")} + " +
                $"{visited.Length} visited to {sidecarPath}");
        }
        catch (Exception e)
        {
            Log?.LogError($"Sidecar flush failed for save `{saveName}`: {e}");
            // Never poison the vanilla call; this sidecar remains best-effort.
        }
    }
}
