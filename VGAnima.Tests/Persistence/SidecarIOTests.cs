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

    [Fact]
    public void Write_ThenRead_RoundtripsSchema()
    {
        var path = Path.Combine(_tempDir, "slot0.vganima.json");
        var io = new SidecarIO(() => DateTime.UtcNow);
        var schema = new SidecarSchema(Version: 1, Entries: System.Array.Empty<PersistedEntry>());

        io.Write(path, schema);
        var result = io.Read(path);

        Assert.Equal(SidecarReadStatus.Loaded, result.Status);
        Assert.Equal(1, result.Schema!.Version);
        Assert.Empty(result.Schema.Entries);
    }

    [Fact]
    public void Write_IsAtomic_LeavesNoTempFileBehind()
    {
        var path = Path.Combine(_tempDir, "slot0.vganima.json");
        var io = new SidecarIO(() => DateTime.UtcNow);
        io.Write(path, new SidecarSchema(Version: 1, Entries: System.Array.Empty<PersistedEntry>()));

        Assert.True(File.Exists(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Write_OverwritesExistingFile()
    {
        var path = Path.Combine(_tempDir, "slot0.vganima.json");
        var io = new SidecarIO(() => DateTime.UtcNow);
        File.WriteAllText(path, "previous contents");

        io.Write(path, new SidecarSchema(Version: 1, Entries: System.Array.Empty<PersistedEntry>()));

        var contents = File.ReadAllText(path);
        Assert.Contains("\"version\":1", contents);
        Assert.DoesNotContain("previous", contents);
    }

    [Fact]
    public void SerializationBinder_RejectsTypesOutsideAllowlist()
    {
        // The binder in SidecarSchema.SerializerSettings must reject any
        // $type discriminator naming a type outside the allowlisted
        // LlmObjective / LlmReward subtypes. This is our defense against
        // a malicious sidecar (e.g., one that arrived via cloud-sync or
        // modpack distribution) trying to instantiate arbitrary types.
        var json = """
            {"version":1,"entries":[{
              "storyId":"x","state":"offered",
              "missionBlock":{"name":"x","description":"x","sourceFaction":"x","completionText":"x",
                "steps":[{"description":"x","objectives":[{"$type":"System.IO.FileInfo, System.IO.FileSystem","description":"x"}]}],
                "rewards":[]},
              "broker":{"seed":"x","stationId":"x","displayName":"x","isMale":true,"story":{"hook":["h"],"pitch":["p"],"rejection":["r"],"reward":["w"],"character":[]}},
              "timestamps":{"createdGameSeconds":0,"createdRealUtc":"z","lastSeenGameSeconds":0,"lastSeenRealUtc":"z"}
            }]}
            """;
        Assert.Throws<JsonSerializationException>(() =>
            JsonConvert.DeserializeObject<SidecarSchema>(json, SidecarSchema.SerializerSettings));
    }
}
