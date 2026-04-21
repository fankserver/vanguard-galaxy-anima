using System;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Persistence;

public class SidecarPathResolverTests
{
    [Fact]
    public void From_AppendsVganimaJsonSuffix()
    {
        var result = SidecarPathResolver.From("/home/user/saves/slot0.save");
        Assert.Equal("/home/user/saves/slot0.save.vganima.json", result);
    }

    [Fact]
    public void IsSidecar_ReturnsTrueForSidecarPaths()
    {
        Assert.True(SidecarPathResolver.IsSidecar("/x/y.save.vganima.json"));
        Assert.False(SidecarPathResolver.IsSidecar("/x/y.save"));
        Assert.False(SidecarPathResolver.IsSidecar("/x/y.vganima.corrupt.20260421120000.json"));
    }

    [Fact]
    public void BaseSavePathFrom_StripsVganimaSuffix()
    {
        Assert.Equal("/x/y.save", SidecarPathResolver.BaseSavePathFrom("/x/y.save.vganima.json"));
    }

    [Fact]
    public void QuarantineName_AppendsTimestampInUtc()
    {
        var when = new DateTime(2026, 04, 21, 12, 00, 00, DateTimeKind.Utc);
        var result = SidecarPathResolver.QuarantineName("/x/y.save.vganima.json", when);
        Assert.Equal("/x/y.save.vganima.corrupt.20260421120000.json", result);
    }
}
