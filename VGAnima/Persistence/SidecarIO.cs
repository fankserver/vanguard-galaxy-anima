using System;
using System.IO;
using Newtonsoft.Json;

namespace VGAnima.Persistence;

internal enum SidecarReadStatus { Loaded, MissingFile, Corrupted, UnsupportedVersion }

internal sealed record SidecarReadResult(
    SidecarReadStatus Status,
    SidecarSchema? Schema,
    string? QuarantinedTo);

/// <summary>Reads and writes the sidecar JSON file. Writes are atomic
/// (tmp + rename); reads quarantine corrupt or future-version files so
/// the game can proceed with an empty registry.</summary>
internal sealed class SidecarIO
{
    private readonly Func<DateTime> _utcNow;

    public SidecarIO(Func<DateTime> utcNow)
    {
        _utcNow = utcNow;
    }

    public SidecarReadResult Read(string sidecarPath)
    {
        if (!File.Exists(sidecarPath))
            return new SidecarReadResult(SidecarReadStatus.MissingFile, null, null);

        string raw;
        try { raw = File.ReadAllText(sidecarPath); }
        catch (IOException) { return new SidecarReadResult(SidecarReadStatus.MissingFile, null, null); }

        SidecarSchema? schema;
        try { schema = JsonConvert.DeserializeObject<SidecarSchema>(raw, SidecarSchema.SerializerSettings); }
        catch (JsonException) { return Quarantine(sidecarPath, SidecarReadStatus.Corrupted); }

        if (schema is null) return Quarantine(sidecarPath, SidecarReadStatus.Corrupted);

        // Version acceptance policy: the current version loads as-is.
        // One version back (additive schema change only) loads via an
        // in-memory upgrade that re-stamps the version field so downstream
        // code sees the current shape. Anything older or newer
        // quarantines. v2 → v3 is the one live upgrade path: v3 added
        // `visited_systems` — a v2 sidecar has no such field, so it
        // deserializes with `VisitedSystems = null` and we just bump the
        // version number. The file gets rewritten as v3 on the next save.
        if (schema.Version == SidecarSchema.CurrentVersion)
            return new SidecarReadResult(SidecarReadStatus.Loaded, schema, null);
        if (schema.Version == SidecarSchema.CurrentVersion - 1)
            return new SidecarReadResult(
                SidecarReadStatus.Loaded,
                schema with { Version = SidecarSchema.CurrentVersion },
                null);
        return Quarantine(sidecarPath, SidecarReadStatus.UnsupportedVersion);
    }

    public void Write(string sidecarPath, SidecarSchema schema)
    {
        var tmp = sidecarPath + ".tmp";
        var json = JsonConvert.SerializeObject(schema, SidecarSchema.SerializerSettings);
        File.WriteAllText(tmp, json);
        if (File.Exists(sidecarPath)) File.Delete(sidecarPath);
        File.Move(tmp, sidecarPath);
    }

    private SidecarReadResult Quarantine(string sidecarPath, SidecarReadStatus status)
    {
        var quarantinePath = SidecarPathResolver.QuarantineName(sidecarPath, _utcNow());
        File.Move(sidecarPath, quarantinePath);
        return new SidecarReadResult(status, null, quarantinePath);
    }
}
