using System;
using System.IO;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Persistence;

public class DeadSidecarSweeperTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"vganima-sweep-{Guid.NewGuid()}");

    public DeadSidecarSweeperTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void Sweep_DeletesSidecarsWithNoPairedSaveFile()
    {
        var orphan = Path.Combine(_tempDir, "deleted-save.save.vganima.json");
        var paired = Path.Combine(_tempDir, "alive-save.save.vganima.json");
        File.WriteAllText(orphan, "{}");
        File.WriteAllText(paired, "{}");
        File.WriteAllText(Path.Combine(_tempDir, "alive-save.save"), "vanilla save");

        var deleted = DeadSidecarSweeper.Sweep(_tempDir);

        Assert.False(File.Exists(orphan));
        Assert.True(File.Exists(paired));
        Assert.Single(deleted);
        Assert.Contains(orphan, deleted);
    }

    [Fact]
    public void Sweep_IgnoresQuarantinedFiles()
    {
        var quarantined = Path.Combine(_tempDir, "slot0.save.vganima.corrupt.20260421000000.json");
        File.WriteAllText(quarantined, "{}");

        var deleted = DeadSidecarSweeper.Sweep(_tempDir);

        Assert.True(File.Exists(quarantined));
        Assert.Empty(deleted);
    }

    [Fact]
    public void Sweep_MissingDirectory_ReturnsEmpty()
    {
        var deleted = DeadSidecarSweeper.Sweep(Path.Combine(_tempDir, "does-not-exist"));
        Assert.Empty(deleted);
    }
}
