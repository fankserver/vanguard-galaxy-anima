using System;

namespace VGAnima.Persistence;

/// <summary>Pure-function helpers for deriving sidecar paths from vanilla
/// save paths. No I/O. No state. Vanilla save files are
/// <c>{SaveGame.SavesPath}/{saveName}.save</c> (GZip-compressed JSON); the
/// paired VGAnima sidecar is <c>{same-path}.vganima.json</c>. Quarantine
/// files get a timestamp suffix and live in the same directory.</summary>
internal static class SidecarPathResolver
{
    private const string Suffix = ".vganima.json";

    public static string From(string vanillaSavePath) => vanillaSavePath + Suffix;

    public static bool IsSidecar(string path) =>
        path.EndsWith(Suffix, StringComparison.Ordinal)
        && !path.Contains(".vganima.corrupt.");

    public static string BaseSavePathFrom(string sidecarPath) =>
        sidecarPath.EndsWith(Suffix, StringComparison.Ordinal)
            ? sidecarPath.Substring(0, sidecarPath.Length - Suffix.Length)
            : sidecarPath;

    public static string QuarantineName(string sidecarPath, DateTime utcNow)
    {
        var stamp = utcNow.ToString("yyyyMMddHHmmss");
        var withoutSuffix = sidecarPath.Substring(0, sidecarPath.Length - Suffix.Length);
        return $"{withoutSuffix}.vganima.corrupt.{stamp}.json";
    }
}
