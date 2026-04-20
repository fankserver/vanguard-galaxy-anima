using System.Collections.Generic;
using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Llm;

public class ContextGathererTests
{
    private sealed class FakeGameStateView : IGameStateView
    {
        public int PlayerLevel { get; set; } = 15;
        public long PlayerCredits { get; set; } = 250000;
        public string PlayerSpecialization { get; set; } = "Engineering";
        public int BountyRank { get; set; } = 3;
        public int PatrolRank { get; set; } = 0;
        public int IndustryRank { get; set; } = 1;
        public int MaxBountyLevel { get; set; } = 8;
        public int MaxPatrolLevel { get; set; } = 4;
        public int MaxIndustryLevel { get; set; } = 5;
        public IReadOnlyList<string> UnlockedTitles { get; set; } = new[] { "Rookie" };
        public int ActiveMissionCount { get; set; } = 5;
        public int ActiveMissionCap { get; set; } = 20;

        public LlmShipSnapshot? PrimaryShip { get; set; } = new(
            Name: "Vanguard-X", Faction: "Player", Level: 14,
            HullPct: 85, ShieldPct: 100, CargoUsedPct: 60);
        public IReadOnlyList<LlmStoredShipSnapshot> StoredShips { get; set; } = new List<LlmStoredShipSnapshot>();
        public IReadOnlyList<LlmCrewSnapshot> Crew { get; set; } = new List<LlmCrewSnapshot>();
        public IReadOnlyList<LlmCargoSnapshot> CargoContents { get; set; } = new List<LlmCargoSnapshot>();

        public string CurrentStationName { get; set; } = "Spire XIV";
        public string StationFaction { get; set; } = "TradingGuild";
        public IReadOnlyList<string> StationFacilities { get; set; } = new[] { "Bar", "Shipyard" };
        public string CurrentSystemName { get; set; } = "Kepler-442";
        public string CurrentSectorName { get; set; } = "Outer Rim";
        public int Quadrant { get; set; } = 1;
        public IReadOnlyList<LlmSystemSnapshot> ConnectedSystems { get; set; } = new List<LlmSystemSnapshot>();

        public IReadOnlyDictionary<string, int> Reputation { get; set; } = new Dictionary<string, int>();
        public IReadOnlyList<string> AtWar { get; set; } = new List<string>();

        public IReadOnlyList<string> ActiveStoryIds { get; set; } = new List<string>();
        public IReadOnlyList<string> ArchiveRecent { get; set; } = new List<string>();
        public int? CurrentBountyLevel { get; set; }
        public int? CurrentPatrolLevel { get; set; }
        public int? CurrentIndustryLevel { get; set; }

        public IReadOnlyList<string> StoryArcsActive { get; set; } = new[] { "Sandbox" };
        public IReadOnlyList<LlmWaypointSnapshot> Waypoints { get; set; } = new List<LlmWaypointSnapshot>();
        public double ElapsedSeconds { get; set; } = 18420;
    }

    private static BrokerInfo Broker() => new(
        Name: "Shawn Jenkins", IsMale: true,
        Seed: "vganima-broker-abc-0", StationFaction: "TradingGuild");

    [Fact]
    public void Gather_Produces_PlayerSection()
    {
        var view = new FakeGameStateView();
        var ctx = new ContextGatherer().Gather(view, Broker());

        Assert.Equal(15, ctx.Player.Level);
        Assert.Equal(250000, ctx.Player.Credits);
        Assert.Equal("Engineering", ctx.Player.Specialization);
        Assert.Equal(3, ctx.Player.BountyRank);
        Assert.Equal(8, ctx.Player.MaxBountyLevel);
        Assert.Equal(5, ctx.Player.ActiveMissionCount);
        Assert.Equal(20, ctx.Player.ActiveMissionCap);
        Assert.Single(ctx.Player.UnlockedTitles);
    }

    [Fact]
    public void Gather_Produces_Fleet_WithPrimaryShip()
    {
        var view = new FakeGameStateView();
        var ctx = new ContextGatherer().Gather(view, Broker());

        Assert.NotNull(ctx.Fleet.PrimaryShip);
        Assert.Equal("Vanguard-X", ctx.Fleet.PrimaryShip!.Name);
        Assert.Equal(85, ctx.Fleet.PrimaryShip.HullPct);
    }

    [Fact]
    public void Gather_Caps_StoredShipsAt10()
    {
        var view = new FakeGameStateView();
        var ships = new List<LlmStoredShipSnapshot>();
        for (var i = 0; i < 15; i++)
            ships.Add(new LlmStoredShipSnapshot($"Ship{i}", 10, "Player", "cargo"));
        view.StoredShips = ships;

        var ctx = new ContextGatherer().Gather(view, Broker());

        Assert.Equal(10, ctx.Fleet.StoredShips.Count);
        Assert.Equal("Ship0", ctx.Fleet.StoredShips[0].Name);
        Assert.Equal("Ship9", ctx.Fleet.StoredShips[9].Name);
    }

    [Fact]
    public void Gather_Caps_CrewAt10()
    {
        var view = new FakeGameStateView();
        var crew = new List<LlmCrewSnapshot>();
        for (var i = 0; i < 15; i++)
            crew.Add(new LlmCrewSnapshot($"Crew{i}", "gunner"));
        view.Crew = crew;

        var ctx = new ContextGatherer().Gather(view, Broker());

        Assert.Equal(10, ctx.Fleet.Crew.Count);
    }

    [Fact]
    public void Gather_Caps_ConnectedSystemsAt8()
    {
        var view = new FakeGameStateView();
        var systems = new List<LlmSystemSnapshot>();
        for (var i = 0; i < 12; i++)
            systems.Add(new LlmSystemSnapshot($"Sys{i}", i == 0 ? null : "MiningGuild", (i / 4) + 1));
        view.ConnectedSystems = systems;

        var ctx = new ContextGatherer().Gather(view, Broker());

        Assert.Equal(8, ctx.Location.ConnectedSystems.Count);
    }

    [Fact]
    public void Gather_Caps_ArchiveRecentAt10()
    {
        var view = new FakeGameStateView();
        var archive = new List<string>();
        for (var i = 0; i < 20; i++) archive.Add($"mission-{i}");
        view.ArchiveRecent = archive;

        var ctx = new ContextGatherer().Gather(view, Broker());

        Assert.Equal(10, ctx.Missions.ArchiveRecent.Count);
    }

    [Fact]
    public void Gather_Caps_WaypointsAt5()
    {
        var view = new FakeGameStateView();
        var waypoints = new List<LlmWaypointSnapshot>();
        for (var i = 0; i < 8; i++)
            waypoints.Add(new LlmWaypointSnapshot($"poi-{i}", i));
        view.Waypoints = waypoints;

        var ctx = new ContextGatherer().Gather(view, Broker());

        Assert.Equal(5, ctx.Waypoints.Count);
    }

    [Fact]
    public void Gather_Produces_BrokerSection_FromBrokerInfo()
    {
        var view = new FakeGameStateView();
        var ctx = new ContextGatherer().Gather(view, Broker());

        Assert.Equal("Shawn Jenkins", ctx.Broker.Name);
        Assert.True(ctx.Broker.IsMale);
        Assert.Equal("vganima-broker-abc-0", ctx.Broker.Seed);
        Assert.Equal("TradingGuild", ctx.Broker.StationFaction);
    }

    [Fact]
    public void Gather_Produces_LocationSection()
    {
        var view = new FakeGameStateView();
        var ctx = new ContextGatherer().Gather(view, Broker());

        Assert.Equal("Spire XIV", ctx.Location.CurrentStation);
        Assert.Equal("TradingGuild", ctx.Location.StationFaction);
        Assert.Equal("Kepler-442", ctx.Location.CurrentSystem);
        Assert.Equal("Outer Rim", ctx.Location.CurrentSector);
        Assert.Equal(1, ctx.Location.Quadrant);
        Assert.Contains("Bar", ctx.Location.StationFacilities);
    }

    [Fact]
    public void Gather_Produces_TimeSection()
    {
        var view = new FakeGameStateView { ElapsedSeconds = 86400 * 3 + 7200 };
        var ctx = new ContextGatherer().Gather(view, Broker());

        Assert.Equal(86400 * 3 + 7200, ctx.Time.ElapsedSeconds);
        // DayOfYear is literal: elapsed / 86400 mod 365 + 1. 3 full days → 4.
        Assert.Equal(4, ctx.Time.DayOfYear);
    }
}
