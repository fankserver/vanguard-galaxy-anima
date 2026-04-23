using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Llm;

/// <summary>
/// Round-trip guard for <see cref="LlmContext"/>'s JSON shape. Catches
/// <see cref="JsonPropertyAttribute"/> typos and accidental C# property
/// renames that would silently drop fields from the LLM prompt.
///
/// Builds a fully-populated context, serializes it, and asserts every
/// spec §4 top-level key is present in the JSON. Deeper structural
/// equivalence is out of scope — that's too brittle against future
/// additions. The sentinel here is: if any top-level section disappears
/// or is mis-named, this test breaks.
/// </summary>
public class LlmContextJsonTests
{
    [Fact]
    public void Serializes_AllTopLevelKeysPresent()
    {
        var ctx = BuildMinimal();
        var json = JsonConvert.SerializeObject(ctx);
        var root = JObject.Parse(json);

        // Every v1-spec top-level key must survive the roundtrip.
        foreach (var key in new[]
        {
            "player", "fleet", "location",
            "factions", "reward_clamps", "mission_guidance",
            "missions", "story_arcs_active", "waypoints", "time", "broker",
        })
        {
            Assert.True(root.ContainsKey(key), $"missing top-level key `{key}` in serialized LlmContext");
        }
    }

    [Fact]
    public void Serializes_RewardClampsSectionKeysPresent()
    {
        var ctx = BuildMinimal();
        var clamps = (JObject)JObject.Parse(JsonConvert.SerializeObject(ctx))["reward_clamps"]!;

        foreach (var key in new[]
        {
            "credits_base_value_min", "credits_base_value_max",
            "experience_base_value_min", "experience_base_value_max",
            "reputation_amount_min", "reputation_amount_max",
        })
        {
            Assert.True(clamps.ContainsKey(key), $"missing reward_clamps.{key}");
        }
    }

    [Fact]
    public void Serializes_FactionEntryShape()
    {
        var ctx = BuildMinimal();
        ctx.Factions = new Dictionary<string, LlmFactionEntry>
        {
            ["Marauders"] = new("Corsair Syndicate", "hostile", -11485),
        };
        var entry = (JObject)((JObject)JObject.Parse(JsonConvert.SerializeObject(ctx))["factions"]!)["Marauders"]!;

        foreach (var key in new[] { "display_name", "relation", "reputation" })
            Assert.True(entry.ContainsKey(key), $"missing factions.Marauders.{key}");
        Assert.Equal("Corsair Syndicate", (string)entry["display_name"]!);
        Assert.Equal("hostile",           (string)entry["relation"]!);
        Assert.Equal(-11485,              (int)entry["reputation"]!);
    }

    [Fact]
    public void Serializes_PlayerSectionKeysPresent()
    {
        var ctx = BuildMinimal();
        var player = (JObject)JObject.Parse(JsonConvert.SerializeObject(ctx))["player"]!;

        foreach (var key in new[]
        {
            "level", "specialization",
            "bounty_rank", "patrol_rank", "industry_rank",
            "max_bounty_level", "max_patrol_level", "max_industry_level",
            "unlocked_titles", "active_mission_count", "active_mission_cap",
        })
        {
            Assert.True(player.ContainsKey(key), $"missing player.{key}");
        }
    }

    [Fact]
    public void Serializes_LocationSectionKeysPresent()
    {
        var ctx = BuildMinimal();
        var loc = (JObject)JObject.Parse(JsonConvert.SerializeObject(ctx))["location"]!;

        foreach (var key in new[]
        {
            "current_station", "station_faction", "station_facilities",
            "current_system", "current_sector", "quadrant", "connected_systems",
        })
        {
            Assert.True(loc.ContainsKey(key), $"missing location.{key}");
        }
    }

    [Fact]
    public void Serializes_BrokerSectionKeysPresent()
    {
        var ctx = BuildMinimal();
        var broker = (JObject)JObject.Parse(JsonConvert.SerializeObject(ctx))["broker"]!;

        foreach (var key in new[] { "name", "is_male", "seed", "station_faction" })
        {
            Assert.True(broker.ContainsKey(key), $"missing broker.{key}");
        }
    }

    [Fact]
    public void Serializes_MissionsSectionKeysPresent()
    {
        var ctx = BuildMinimal();
        var missions = (JObject)JObject.Parse(JsonConvert.SerializeObject(ctx))["missions"]!;

        foreach (var key in new[]
        {
            "active_story_ids", "archive_recent",
            "current_bounty_level", "current_patrol_level", "current_industry_level",
        })
        {
            Assert.True(missions.ContainsKey(key), $"missing missions.{key}");
        }
    }

    private static LlmContext BuildMinimal()
    {
        return new LlmContext
        {
            Player = new LlmPlayerSection
            {
                Level = 1,
                Specialization = "Unspecified",
                BountyRank = 0, PatrolRank = 0, IndustryRank = 0,
                MaxBountyLevel = 0, MaxPatrolLevel = 0, MaxIndustryLevel = 0,
                UnlockedTitles = new List<string>(),
                ActiveMissionCount = 0, ActiveMissionCap = 20,
            },
            Fleet = new LlmFleetSection
            {
                PrimaryShip = null,
                StoredShips = new List<LlmStoredShipSnapshot>(),
                Crew = new List<LlmCrewSnapshot>(),
            },
            Location = new LlmLocationSection
            {
                CurrentStation = "test",
                StationFaction = "test",
                StationFacilities = new List<string>(),
                CurrentSystem = "test",
                CurrentSector = "test",
                Quadrant = 1,
                ConnectedSystems = new List<LlmSystemSnapshot>(),
            },
            Factions = new Dictionary<string, LlmFactionEntry>(),
            RewardClamps = new LlmRewardClampsSection
            {
                CreditsBaseValueMin = 15, CreditsBaseValueMax = 100,
                ExperienceBaseValueMin = 30, ExperienceBaseValueMax = 100,
                ReputationAmountMin = -500, ReputationAmountMax = 500,
            },
            MissionGuidance = new LlmMissionGuidance
            {
                ArchetypeWeights    = new Dictionary<string, double>(),
                ForbiddenArchetypes = new List<string>(),
                Rationale           = new List<string>(),
            },
            Missions = new LlmMissionsSection
            {
                ActiveStoryIds = new List<string>(),
                ArchiveRecent = new List<string>(),
                CurrentBountyLevel = null,
                CurrentPatrolLevel = null,
                CurrentIndustryLevel = null,
            },
            StoryArcsActive = new List<string>(),
            Waypoints = new List<LlmWaypointSnapshot>(),
            Time = new LlmTimeSection { ElapsedSeconds = 0, DayOfYear = 1 },
            Broker = new LlmBrokerSection
            {
                Name = "test", IsMale = true, Seed = "test", StationFaction = "test",
            },
        };
    }
}
