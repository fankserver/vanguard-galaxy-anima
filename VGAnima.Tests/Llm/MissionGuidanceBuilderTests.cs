using System.Collections.Generic;
using System.Linq;
using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Llm;

public class MissionGuidanceBuilderTests
{
    // ---- fixture ----

    /// <summary>Minimum-viable context with a single hostile faction so the
    /// `combat`/`escort` forbidden-rule doesn't fire by default. Individual
    /// tests mutate fields on the returned instance.</summary>
    private static LlmContext NewContext() => new()
    {
        Player = new LlmPlayerSection
        {
            Level = 1, Credits = 1000, Specialization = string.Empty,
            UnlockedTitles    = new List<string>(),
            ActiveMissionCount = 0, ActiveMissionCap = 20,
        },
        Fleet = new LlmFleetSection
        {
            PrimaryShip = new LlmShipSnapshot(
                Name: "X", Faction: "Player", Level: 1,
                HullPct: 100, ShieldPct: 100, CargoUsedPct: 0),
            StoredShips = new List<LlmStoredShipSnapshot>(),
            Crew        = new List<LlmCrewSnapshot>(),
        },
        CargoContents = new List<LlmCargoSnapshot>(),
        Location = new LlmLocationSection
        {
            CurrentStation = "Test", StationFaction = "Blue",
            StationFacilities = new List<string>(),
            CurrentSystem = "Sys", CurrentSector = "Sec", Quadrant = 1,
            ConnectedSystems = new List<LlmSystemSnapshot>(),
        },
        Factions = new Dictionary<string, LlmFactionEntry>
        {
            ["Marauders"] = new("Corsair Syndicate", "hostile", -6000),
            ["TradingGuild"] = new("Intertrade Network", "neutral", 0),
        },
        Missions = new LlmMissionsSection
        {
            ActiveStoryIds = new List<string>(),
            ArchiveRecent  = new List<string>(),
        },
        StoryArcsActive = new List<string>(),
        Waypoints       = new List<LlmWaypointSnapshot>(),
        Time   = new LlmTimeSection { ElapsedSeconds = 0, DayOfYear = 1 },
        Broker = new LlmBrokerSection { Name = "B", IsMale = true, Seed = "s", StationFaction = "Blue" },
    };

    private static double Weight(LlmMissionGuidance g, string archetype) =>
        g.ArchetypeWeights.TryGetValue(archetype, out var v) ? v : 0.0;

    // ---- tests ----

    [Fact]
    public void Build_OnlyHostileFactionSignal_CombatWinsAlone()
    {
        // NewContext has one hostile faction and nothing else. The
        // hostile-faction-exists rule contributes Weak (0.5) to combat, so
        // combat is the only non-zero raw weight. After normalization it
        // claims 100%.
        var g = MissionGuidanceBuilder.Build(NewContext());
        Assert.Equal(1.0, Weight(g, "combat"));
    }

    [Fact]
    public void Build_TotalNoSignals_DistributesEvenlyAcrossNonForbidden()
    {
        // No hostile factions + no other signals. combat/escort forbidden,
        // remaining three share evenly.
        var ctx = NewContext();
        ctx.Factions = new Dictionary<string, LlmFactionEntry>
        {
            ["TradingGuild"] = new("Intertrade Network", "friendly", 100),
        };
        var g = MissionGuidanceBuilder.Build(ctx);
        Assert.Equal(0.0, Weight(g, "combat"));
        Assert.Equal(0.0, Weight(g, "escort"));
        var expected = System.Math.Round(1.0 / 3.0, 2);
        Assert.Equal(expected, Weight(g, "gather"));
        Assert.Equal(expected, Weight(g, "salvage"));
        Assert.Equal(expected, Weight(g, "deliver"));
    }

    [Fact]
    public void Build_NoHostileFactions_ForbidsCombatAndEscort()
    {
        var ctx = NewContext();
        ctx.Factions = new Dictionary<string, LlmFactionEntry>
        {
            ["TradingGuild"] = new("Intertrade Network", "friendly", 100),
        };
        var g = MissionGuidanceBuilder.Build(ctx);

        Assert.Contains("combat", g.ForbiddenArchetypes);
        Assert.Contains("escort", g.ForbiddenArchetypes);
        Assert.Equal(0.0, Weight(g, "combat"));
        Assert.Equal(0.0, Weight(g, "escort"));
    }

    [Fact]
    public void Build_GatlingAmmoInCargo_RanksCombatFirst()
    {
        var ctx = NewContext();
        ctx.CargoContents = new List<LlmCargoSnapshot> { new("@GatlingAmmo", 600) };
        var g = MissionGuidanceBuilder.Build(ctx);

        var top = g.ArchetypeWeights.First().Key;
        Assert.Equal("combat", top);
    }

    [Fact]
    public void Build_PlasmaCellInCargo_GivesNoArchetypeSignal()
    {
        // Plasma cells are universal fuel — every ship burns them. NOT a
        // mining tool. Regression guard: a miner without other mining signals
        // should NOT be pushed toward gather just because they have fuel.
        var ctx = NewContext();
        ctx.CargoContents = new List<LlmCargoSnapshot> { new("@PlasmaCellName", 6) };
        var g = MissionGuidanceBuilder.Build(ctx);

        Assert.DoesNotContain(g.Rationale, r => r.Contains("PlasmaCell"));
    }

    [Fact]
    public void Build_PoiBeaconInCargo_GivesNoArchetypeSignal()
    {
        // POI beacons are waypoint bookmarks — used equally for combat /
        // exploration / salvage / mining. No archetype signal.
        var ctx = NewContext();
        ctx.CargoContents = new List<LlmCargoSnapshot> { new("@PoiBeaconName", 1) };
        var g = MissionGuidanceBuilder.Build(ctx);

        Assert.DoesNotContain(g.Rationale, r => r.Contains("PoiBeacon"));
    }

    [Fact]
    public void Build_MiningExplosivesInCargo_RanksGatherFirst()
    {
        // MiningExplosives IS unambiguously mining-specific — keep as a strong
        // gather signal.
        var ctx = NewContext();
        ctx.CargoContents = new List<LlmCargoSnapshot> { new("@MiningExplosives", 100) };
        var g = MissionGuidanceBuilder.Build(ctx);

        Assert.Equal("gather", g.ArchetypeWeights.First().Key);
    }

    [Fact]
    public void Build_SalvagePartsInCargo_RanksSalvageFirst()
    {
        var ctx = NewContext();
        ctx.CargoContents = new List<LlmCargoSnapshot> { new("@SalvageParts", 5) };
        var g = MissionGuidanceBuilder.Build(ctx);

        var top = g.ArchetypeWeights.First().Key;
        Assert.Equal("salvage", top);
    }

    [Fact]
    public void Build_TradeGoodsInCargo_RanksDeliverFirst()
    {
        var ctx = NewContext();
        ctx.CargoContents = new List<LlmCargoSnapshot> { new("@TradeGoods01", 10) };
        var g = MissionGuidanceBuilder.Build(ctx);

        var top = g.ArchetypeWeights.First().Key;
        Assert.Equal("deliver", top);
    }

    [Fact]
    public void Build_OffenseSpec_RanksCombatFirst()
    {
        var ctx = NewContext();
        ctx.Player.Specialization = "Offense";
        var g = MissionGuidanceBuilder.Build(ctx);

        Assert.Equal("combat", g.ArchetypeWeights.First().Key);
    }

    [Fact]
    public void Build_MiningSpec_RanksGatherFirst()
    {
        var ctx = NewContext();
        ctx.Player.Specialization = "Mining";
        var g = MissionGuidanceBuilder.Build(ctx);

        Assert.Equal("gather", g.ArchetypeWeights.First().Key);
    }

    [Fact]
    public void Build_EngineeringSpec_LeansGatherAndDeliver()
    {
        // Engineering is a production-chain spec (crafting/producing items
        // in-game). Same shape as Industrial: gather raw materials, deliver
        // finished products.
        var ctx = NewContext();
        ctx.Player.Specialization = "Engineering";
        var g = MissionGuidanceBuilder.Build(ctx);

        Assert.True(Weight(g, "gather")  > 0.0);
        Assert.True(Weight(g, "deliver") > 0.0);
        Assert.Equal(0.0, Weight(g, "escort"));   // was wrongly set in an earlier draft
    }

    [Fact]
    public void Build_LeadershipSpec_LeansCombatAndEscort()
    {
        var ctx = NewContext();
        ctx.Player.Specialization = "Leadership";
        var g = MissionGuidanceBuilder.Build(ctx);

        // Leadership contributes Weak (0.5) to both combat and escort. In the
        // NewContext fixture combat also gets +0.5 from hostile-factions-exist,
        // so combat totals 1.0 while escort totals 0.5 — combat ranks first.
        Assert.Equal("combat", g.ArchetypeWeights.First().Key);
        Assert.True(Weight(g, "escort") > 0.0);
    }

    [Fact]
    public void Build_DefenseSpec_LeansEscortOverCombat()
    {
        var ctx = NewContext();
        ctx.Player.Specialization = "Defense";
        var g = MissionGuidanceBuilder.Build(ctx);

        // Defense contributes Strong(2) to escort, Medium(1) to combat — escort
        // ranks higher than combat for a pure-Defense player.
        Assert.True(Weight(g, "escort") > Weight(g, "combat"),
            $"escort ({Weight(g, "escort")}) should outweigh combat ({Weight(g, "combat")}) for Defense");
    }

    [Fact]
    public void Build_NavyCaptainTitle_BoostsCombat()
    {
        var ctx = NewContext();
        ctx.Player.UnlockedTitles = new[] { "navycaptain" };
        var g = MissionGuidanceBuilder.Build(ctx);

        Assert.Equal("combat", g.ArchetypeWeights.First().Key);
    }

    [Fact]
    public void Build_MinerTitle_BoostsGather()
    {
        var ctx = NewContext();
        ctx.Player.UnlockedTitles = new[] { "miner" };
        var g = MissionGuidanceBuilder.Build(ctx);

        Assert.Equal("gather", g.ArchetypeWeights.First().Key);
    }

    [Fact]
    public void Build_ActiveBountyMission_BoostsCombat()
    {
        var ctx = NewContext();
        ctx.Missions.ActiveStoryIds = new[] { "SideMissionBounty" };
        var g = MissionGuidanceBuilder.Build(ctx);

        Assert.Equal("combat", g.ArchetypeWeights.First().Key);
    }

    [Fact]
    public void Build_ActiveMiningMission_BoostsGather()
    {
        var ctx = NewContext();
        ctx.Missions.ActiveStoryIds = new[] { "SkilltreeMissionMining2" };
        var g = MissionGuidanceBuilder.Build(ctx);

        Assert.Equal("gather", g.ArchetypeWeights.First().Key);
    }

    [Fact]
    public void Build_RefineryAndForge_BoostsGather()
    {
        var ctx = NewContext();
        ctx.Location.StationFacilities = new[] { "Bar", "Refinery", "Forge", "MissionBoard" };
        var g = MissionGuidanceBuilder.Build(ctx);

        Assert.Equal("gather", g.ArchetypeWeights.First().Key);
    }

    [Fact]
    public void Build_SalvageWorkshop_BoostsSalvage()
    {
        var ctx = NewContext();
        ctx.Location.StationFacilities = new[] { "Bar", "SalvageWorkshop" };
        var g = MissionGuidanceBuilder.Build(ctx);

        Assert.Equal("salvage", g.ArchetypeWeights.First().Key);
    }

    [Fact]
    public void Build_DamagedShip_BoostsCombat()
    {
        var ctx = NewContext();
        ctx.Fleet.PrimaryShip = new LlmShipSnapshot(
            Name: "Wreck", Faction: "Player", Level: 1,
            HullPct: 45, ShieldPct: 10, CargoUsedPct: 0);
        var g = MissionGuidanceBuilder.Build(ctx);

        Assert.Equal("combat", g.ArchetypeWeights.First().Key);
    }

    [Fact]
    public void Build_FullCargo_BoostsDeliver()
    {
        var ctx = NewContext();
        ctx.Fleet.PrimaryShip = new LlmShipSnapshot(
            Name: "Hauler", Faction: "Player", Level: 5,
            HullPct: 100, ShieldPct: 100, CargoUsedPct: 85);
        var g = MissionGuidanceBuilder.Build(ctx);

        // Full cargo alone is Medium(1) — competes against the even-fallback base;
        // confirm it wins outright.
        Assert.Equal("deliver", g.ArchetypeWeights.First().Key);
    }

    [Fact]
    public void Build_HostileNeighborSystem_BoostsCombatAndEscort()
    {
        var ctx = NewContext();
        ctx.Location.ConnectedSystems = new[]
        {
            new LlmSystemSnapshot("Kingston-9", "Marauders", 1),
        };
        var g = MissionGuidanceBuilder.Build(ctx);

        // Among the zero-signal archetypes (gather/salvage/deliver), combat and
        // escort got +1 each from the hostile-neighbor rule. Combat additionally
        // got +0.5 from the hostile-factions-exist rule. Combat should rank
        // above any archetype with zero signal.
        Assert.True(Weight(g, "combat") > Weight(g, "gather"));
        Assert.True(Weight(g, "escort") > Weight(g, "gather"));
    }

    [Fact]
    public void Build_OffenseWithAmmoAndNavyTitle_StronglyCombat()
    {
        // The exact scenario that failed in live testing (Glass Revelation
        // Array): specialization=Offense, @GatlingAmmo cargo, navycaptain title.
        // The builder must rank combat overwhelmingly.
        var ctx = NewContext();
        ctx.Player.Specialization = "Offense";
        ctx.Player.UnlockedTitles = new[] { "navycaptain" };
        ctx.CargoContents = new List<LlmCargoSnapshot>
        {
            new("@GatlingAmmo", 600), new("@PlasmaCellName", 1),
        };
        var g = MissionGuidanceBuilder.Build(ctx);

        var top = g.ArchetypeWeights.First().Key;
        Assert.Equal("combat", top);
        Assert.True(Weight(g, "combat") > 0.5,
            $"combat weight {Weight(g, "combat")} should dominate (>0.5)");
    }

    [Fact]
    public void Build_Rationale_ExplainsTopSignals()
    {
        var ctx = NewContext();
        ctx.Player.Specialization = "Offense";
        ctx.Player.UnlockedTitles = new[] { "navycaptain" };
        var g = MissionGuidanceBuilder.Build(ctx);

        Assert.Contains(g.Rationale, r => r.Contains("Offense"));
        Assert.Contains(g.Rationale, r => r.Contains("navycaptain"));
    }

    [Fact]
    public void Build_WeightsSumToOne()
    {
        var ctx = NewContext();
        ctx.Player.Specialization = "Mining";
        ctx.CargoContents = new List<LlmCargoSnapshot> { new("@OreCommon13", 25) };
        var g = MissionGuidanceBuilder.Build(ctx);

        var sum = g.ArchetypeWeights.Values.Sum();
        Assert.InRange(sum, 0.98, 1.02);   // tolerate 2-decimal rounding
    }

    [Fact]
    public void Build_ArchetypeWeights_AreRankedHighToLow()
    {
        var ctx = NewContext();
        ctx.Player.Specialization = "Mining";
        ctx.CargoContents = new List<LlmCargoSnapshot> { new("@PlasmaCellName", 1) };
        var g = MissionGuidanceBuilder.Build(ctx);

        var values = g.ArchetypeWeights.Values.ToList();
        for (var i = 1; i < values.Count; i++)
            Assert.True(values[i - 1] >= values[i],
                $"weights should rank high-to-low, but [{i-1}]={values[i-1]} < [{i}]={values[i]}");
    }
}
