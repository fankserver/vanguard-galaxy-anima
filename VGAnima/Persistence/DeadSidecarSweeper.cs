using System.Collections.Generic;
using System.IO;

namespace VGAnima.Persistence;

/// <summary>Startup-time cleanup of sidecars whose vanilla save file no
/// longer exists. Prevents accumulation when users delete saves outside
/// the game. Ignores <c>.vganima.corrupt.*.json</c> quarantine files
/// (they carry forensic value).</summary>
internal static class DeadSidecarSweeper
{
    public static IReadOnlyList<string> Sweep(string saveDirectory)
    {
        if (!Directory.Exists(saveDirectory)) return System.Array.Empty<string>();

        var deleted = new List<string>();
        foreach (var sidecar in Directory.EnumerateFiles(saveDirectory, "*.vganima.json"))
        {
            if (!SidecarPathResolver.IsSidecar(sidecar)) continue;      // skips .corrupt.*.json
            var baseSave = SidecarPathResolver.BaseSavePathFrom(sidecar);
            if (File.Exists(baseSave)) continue;

            File.Delete(sidecar);
            deleted.Add(sidecar);
        }
        return deleted;
    }
}
