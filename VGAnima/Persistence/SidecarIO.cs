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
        if (schema.Version != SidecarSchema.CurrentVersion)
            return Quarantine(sidecarPath, SidecarReadStatus.UnsupportedVersion);

        return new SidecarReadResult(SidecarReadStatus.Loaded, schema, null);
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
