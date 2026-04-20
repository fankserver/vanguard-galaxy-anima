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
            "player", "fleet", "cargo_contents", "location", "reputation",
            "at_war", "missions", "story_arcs_active", "waypoints", "time", "broker",
        })
        {
            Assert.True(root.ContainsKey(key), $"missing top-level key `{key}` in serialized LlmContext");
        }
    }

    [Fact]
    public void Serializes_PlayerSectionKeysPresent()
    {
        var ctx = BuildMinimal();
        var player = (JObject)JObject.Parse(JsonConvert.SerializeObject(ctx))["player"]!;

        foreach (var key in new[]
        {
            "level", "credits", "specialization",
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
                Credits = 0,
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
            CargoContents = new List<LlmCargoSnapshot>(),
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
            Reputation = new Dictionary<string, int>(),
            AtWar = new List<string>(),
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
