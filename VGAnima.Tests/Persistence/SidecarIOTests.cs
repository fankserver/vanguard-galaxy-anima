using System;
using System.IO;
using Newtonsoft.Json;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Persistence;

public class SidecarIOTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"vganima-test-{Guid.NewGuid()}");

    public SidecarIOTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Read_MissingFile_ReturnsMissingStatus()
    {
        var io = new SidecarIO(() => DateTime.UtcNow);
        var result = io.Read(Path.Combine(_tempDir, "no-such.vganima.json"));
        Assert.Equal(SidecarReadStatus.MissingFile, result.Status);
        Assert.Null(result.Schema);
    }

    [Fact]
    public void Read_CorruptedJson_QuarantinesFileAndReturnsCorruptedStatus()
    {
        var path = Path.Combine(_tempDir, "bad.vganima.json");
        File.WriteAllText(path, "{ this is not valid json");
        var io = new SidecarIO(() => new DateTime(2026, 04, 21, 12, 00, 00, DateTimeKind.Utc));

        var result = io.Read(path);

        Assert.Equal(SidecarReadStatus.Corrupted, result.Status);
        Assert.Null(result.Schema);
        Assert.False(File.Exists(path));
        Assert.True(File.Exists(Path.Combine(_tempDir, "bad.vganima.corrupt.20260421120000.json")));
    }

    [Fact]
    public void Read_UnsupportedVersion_QuarantinesFileAndReturnsUnsupportedStatus()
    {
        var path = Path.Combine(_tempDir, "futuristic.vganima.json");
        File.WriteAllText(path, "{\"version\":99,\"entries\":[]}");
        var io = new SidecarIO(() => new DateTime(2026, 04, 21, 12, 00, 00, DateTimeKind.Utc));

        var result = io.Read(path);

        Assert.Equal(SidecarReadStatus.UnsupportedVersion, result.Status);
        Assert.False(File.Exists(path));
    }

    // Anything older than one-back quarantines too — v1 stays a hard cut.
    // v1 sidecars have $type refs to deleted objective types; even if
    // someone hand-edited the version number, the binder would still
    // reject the payload.
    [Fact]
    public void Read_MuchOlderVersion_Quarantines()
    {
        var path = Path.Combine(_tempDir, "v1.vganima.json");
        File.WriteAllText(path, "{\"version\":1,\"entries\":[]}");
        var io = new SidecarIO(() => new DateTime(2026, 04, 21, 12, 00, 00, DateTimeKind.Utc));

        var result = io.Read(path);

        Assert.Equal(SidecarReadStatus.UnsupportedVersion, result.Status);
        Assert.False(File.Exists(path));
    }

    // v3 added the optional `visited_systems` field; v2 sidecars deserialize
    // cleanly (the field reads as null) and should upgrade in memory without
    // quarantine. Downstream consumers expect Version == CurrentVersion on
    // the returned schema; a mismatched version number would confuse the
    // SaveWritePatch flush that immediately follows.
    [Fact]
    public void Read_PreviousVersion_UpgradesInMemoryToCurrentVersion()
    {
        var path = Path.Combine(_tempDir, "v2.vganima.json");
        File.WriteAllText(path, $"{{\"version\":{SidecarSchema.CurrentVersion - 1},\"entries\":[]}}");
        var io = new SidecarIO(() => new DateTime(2026, 04, 21, 12, 00, 00, DateTimeKind.Utc));

        var result = io.Read(path);

        Assert.Equal(SidecarReadStatus.Loaded, result.Status);
        Assert.NotNull(result.Schema);
        Assert.Equal(SidecarSchema.CurrentVersion, result.Schema!.Version);
        Assert.Null(result.Schema.VisitedSystems);
        // File stays untouched — no quarantine, no rewrite. The rewrite
        // happens on the next SaveWritePatch flush, not during Read.
        Assert.True(File.Exists(path));
    }

    // The upgrade accepts existing v2 payloads (entries + completed
    // missions) intact — the version bump is metadata-only.
    [Fact]
    public void Read_PreviousVersion_PreservesExistingFields()
    {
        var path = Path.Combine(_tempDir, "v2-populated.vganima.json");
        File.WriteAllText(path, $$"""
            {
              "version": {{SidecarSchema.CurrentVersion - 1}},
              "entries": [],
              "completed_missions": [{
                "storyId": "vganima.llm.test",
                "brokerName": "Test Broker",
                "stationId": "station-x",
                "stationName": "Testing Hub",
                "sourceFaction": "TradingGuild",
                "missionName": "Legacy Contract",
                "archetype": "deliver",
                "outcome": "completed",
                "missionLevel": 7,
                "systemName": "Zoran",
                "magnitudeScore": 4,
                "resolvedGameSeconds": 1234.5,
                "resolvedRealUtc": "2026-04-21T10:00:00Z"
              }]
            }
            """);
        var io = new SidecarIO(() => DateTime.UtcNow);

        var result = io.Read(path);

        Assert.Equal(SidecarReadStatus.Loaded, result.Status);
        Assert.Equal(SidecarSchema.CurrentVersion, result.Schema!.Version);
        Assert.NotNull(result.Schema.CompletedMissions);
        Assert.Single(result.Schema.CompletedMissions!);
        Assert.Equal("Legacy Contract", result.Schema.CompletedMissions![0].MissionName);
    }

    [Fact]
    public void Write_ThenRead_RoundtripsSchema()
    {
        var path = Path.Combine(_tempDir, "slot0.vganima.json");
        var io = new SidecarIO(() => DateTime.UtcNow);
        var schema = new SidecarSchema(Version: SidecarSchema.CurrentVersion, Entries: System.Array.Empty<PersistedEntry>());

        io.Write(path, schema);
        var result = io.Read(path);

        Assert.Equal(SidecarReadStatus.Loaded, result.Status);
        Assert.Equal(SidecarSchema.CurrentVersion, result.Schema!.Version);
        Assert.Empty(result.Schema.Entries);
    }

    [Fact]
    public void Write_IsAtomic_LeavesNoTempFileBehind()
    {
        var path = Path.Combine(_tempDir, "slot0.vganima.json");
        var io = new SidecarIO(() => DateTime.UtcNow);
        io.Write(path, new SidecarSchema(Version: SidecarSchema.CurrentVersion, Entries: System.Array.Empty<PersistedEntry>()));

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Write_OverwritesExistingFile()
    {
        var path = Path.Combine(_tempDir, "slot0.vganima.json");
        var io = new SidecarIO(() => DateTime.UtcNow);
        File.WriteAllText(path, "previous contents");

        io.Write(path, new SidecarSchema(Version: SidecarSchema.CurrentVersion, Entries: System.Array.Empty<PersistedEntry>()));

        var contents = File.ReadAllText(path);
        Assert.Contains($"\"version\":{SidecarSchema.CurrentVersion}", contents);
        Assert.DoesNotContain("previous", contents);
    }

    [Fact]
    public void SerializationBinder_RejectsTypesOutsideAllowlist()
    {
        // The binder in SidecarSchema.SerializerSettings must reject any
        // $type discriminator naming a type outside the allowlisted
        // LlmIntent / LlmReward subtypes. Defense against a malicious
        // sidecar (cloud-sync, modpack distribution) trying to instantiate
        // arbitrary gadget types.
        //
        // Smoking-gun case: $type naming System.IO.FileInfo — a real
        // Newtonsoft gadget-chain target. Binder must refuse.
        var json = """
            {"version":1,"entries":[{
              "storyId":"x","state":"offered",
              "missionBlock":{"name":"x","description":"x","sourceFaction":"x","completionText":"x",
                "steps":[{"intent":{"$type":"System.IO.FileInfo, System.IO.FileSystem","description":"x"}}],
                "rewards":[]},
              "broker":{"seed":"x","stationId":"x","displayName":"x","isMale":true,"story":{"hook":["h"],"pitch":["p"],"rejection":["r"],"reward":["w"],"character":[]}},
              "timestamps":{"createdGameSeconds":0,"createdRealUtc":"z","lastSeenGameSeconds":0,"lastSeenRealUtc":"z"}
            }]}
            """;
        Assert.Throws<JsonSerializationException>(() =>
            JsonConvert.DeserializeObject<SidecarSchema>(json, SidecarSchema.SerializerSettings));
    }
}
