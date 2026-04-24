using VGAnima.MissionJournal;
using Xunit;

namespace VGAnima.Tests.MissionJournal;

/// <summary>Bridge tests run in an environment where BepInEx
/// Chainloader hasn't discovered VGMissionJournal (the plugin is
/// installed into the game, not into xUnit's AppDomain). Every path
/// should take the "plugin absent" branch and return empty.
///
/// <para>Live-path behaviour (VGMissionJournal loaded, API returning
/// real data) is exercised by MJ-T5 manual E2E — xUnit can't boot
/// BepInEx.</para></summary>
public class VgMissionJournalBridgeTests
{
    [Fact]
    public void IsAvailable_FalseInTestEnv_WherePluginIsNotLoaded()
    {
        var bridge = new VgMissionJournalBridge();
        Assert.False(bridge.IsAvailable);
    }

    [Fact]
    public void GetActiveMissions_PluginAbsent_ReturnsEmpty()
    {
        var bridge = new VgMissionJournalBridge();
        Assert.Empty(bridge.GetActiveMissions());
    }

    [Fact]
    public void GetMissionsInSystem_PluginAbsent_ReturnsEmpty()
    {
        var bridge = new VgMissionJournalBridge();
        Assert.Empty(bridge.GetMissionsInSystem("sys-any"));
        Assert.Empty(bridge.GetMissionsInSystem("sys-any", sinceGameSeconds: 1000));
    }

    [Fact]
    public void GetMissionsInSystem_EmptySystemId_ReturnsEmpty()
    {
        var bridge = new VgMissionJournalBridge();
        Assert.Empty(bridge.GetMissionsInSystem(""));
    }

    [Fact]
    public void GetMissionsByFaction_PluginAbsent_ReturnsEmpty()
    {
        var bridge = new VgMissionJournalBridge();
        Assert.Empty(bridge.GetMissionsByFaction("BountyGuild"));
    }

    [Fact]
    public void GetMissionsWithinJumps_PluginAbsent_ReturnsEmpty()
    {
        var bridge = new VgMissionJournalBridge();
        Assert.Empty(bridge.GetMissionsWithinJumps(
            "sys-pivot", maxJumps: 3,
            jumpDistance: (_, _) => 1));
    }

    [Fact]
    public void GetMissionsWithinJumps_EmptyPivot_ReturnsEmpty()
    {
        var bridge = new VgMissionJournalBridge();
        Assert.Empty(bridge.GetMissionsWithinJumps(
            pivotSystemId: "", maxJumps: 3,
            jumpDistance: (_, _) => 1));
    }
}
