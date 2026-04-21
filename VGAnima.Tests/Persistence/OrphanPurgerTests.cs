using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Persistence;

public class OrphanPurgerTests
{
    [Fact]
    public void Purge_RemovesAcceptedEntriesMissingFromMissionLists()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(Accepted("keep-story", "keep-seed", station: "any-station"));
        reg.Add(Accepted("orphan-story", "orphan-seed", station: "any-station"));

        OrphanPurger.Purge(
            reg,
            activeStoryIds: new[] { "keep-story" },
            archivedStoryIds: System.Array.Empty<string>(),
            knownPatronSeeds: System.Array.Empty<string>(),
            currentStationId: "any-station");

        Assert.NotNull(reg.Get("keep-story"));
        Assert.Null(reg.Get("orphan-story"));
    }

    [Fact]
    public void Purge_KeepsAcceptedEntriesInArchive()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(Accepted("archived-story", "archived-seed", station: "any"));

        OrphanPurger.Purge(
            reg,
            activeStoryIds: System.Array.Empty<string>(),
            archivedStoryIds: new[] { "archived-story" },
            knownPatronSeeds: System.Array.Empty<string>(),
            currentStationId: "any");

        Assert.NotNull(reg.Get("archived-story"));
    }

    [Fact]
    public void Purge_RemovesOfferedEntriesAtCurrentStationWithUnknownSeeds()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(Offered("alive-story", "alive-seed", station: "station-a"));
        reg.Add(Offered("dead-story",  "dead-seed",  station: "station-a"));

        OrphanPurger.Purge(
            reg,
            activeStoryIds: System.Array.Empty<string>(),
            archivedStoryIds: System.Array.Empty<string>(),
            knownPatronSeeds: new[] { "alive-seed" },
            currentStationId: "station-a");

        Assert.NotNull(reg.Get("alive-story"));
        Assert.Null(reg.Get("dead-story"));
    }

    // Regression for a live-E2E bug: the first bar refresh after load
    // would purge offered entries whose home station wasn't the one that
    // happened to refresh first. Fix: offered-purge scope respects
    // stationId. Entries for other stations survive.
    [Fact]
    public void Purge_OfferedEntriesAtOtherStations_AreKept()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(Offered("home-station-story", "home-seed", station: "home-station"));
        reg.Add(Offered("other-station-story", "other-seed", station: "other-station"));

        OrphanPurger.Purge(
            reg,
            activeStoryIds: System.Array.Empty<string>(),
            archivedStoryIds: System.Array.Empty<string>(),
            knownPatronSeeds: System.Array.Empty<string>(),  // empty: no patrons at home-station
            currentStationId: "home-station");

        // home-station entry purged (we verified its bar).
        Assert.Null(reg.Get("home-station-story"));
        // other-station entry kept — we can't verify from home-station's bar.
        Assert.NotNull(reg.Get("other-station-story"));
    }

    [Fact]
    public void Purge_NullCurrentStation_SkipsOfferedPurge()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(Offered("story", "seed", station: "x"));

        OrphanPurger.Purge(
            reg,
            activeStoryIds: System.Array.Empty<string>(),
            archivedStoryIds: System.Array.Empty<string>(),
            knownPatronSeeds: System.Array.Empty<string>(),
            currentStationId: null);

        Assert.NotNull(reg.Get("story"));
    }

    [Fact]
    public void Purge_ReportsDroppedIds()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(Accepted("orphan-a", "seed-a", station: "s"));
        reg.Add(Offered("orphan-b", "seed-b", station: "s"));

        var dropped = OrphanPurger.Purge(
            reg,
            activeStoryIds: System.Array.Empty<string>(),
            archivedStoryIds: System.Array.Empty<string>(),
            knownPatronSeeds: System.Array.Empty<string>(),
            currentStationId: "s");

        Assert.Equal(2, dropped.Count);
        Assert.Contains("orphan-a", dropped);
        Assert.Contains("orphan-b", dropped);
    }

    [Fact]
    public void Purge_EmptyRegistry_ReturnsEmptyDropped()
    {
        var reg = new PersistedBrokerRegistry();
        var dropped = OrphanPurger.Purge(
            reg,
            activeStoryIds: System.Array.Empty<string>(),
            archivedStoryIds: System.Array.Empty<string>(),
            knownPatronSeeds: System.Array.Empty<string>(),
            currentStationId: "x");
        Assert.Empty(dropped);
    }

    private static PersistedEntry Accepted(string storyId, string seed, string station)
        => Make(storyId, seed, station, PersistedEntryStates.Accepted);
    private static PersistedEntry Offered(string storyId, string seed, string station)
        => Make(storyId, seed, station, PersistedEntryStates.Offered);

    private static PersistedEntry Make(string storyId, string seed, string station, string state) =>
        new(storyId, state, null!,
            new PersistedBroker(seed, station, null!),
            new PersistedTimestamps(0, "2026-04-21T00:00:00Z", 0, "2026-04-21T00:00:00Z"));
}
