using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Persistence;

public class PersistedBrokerRegistryTests
{
    [Fact]
    public void Add_ThenGet_ReturnsEntry()
    {
        var reg = new PersistedBrokerRegistry();
        var entry = MakeEntry("story-1", "seed-1");

        reg.Add(entry);

        Assert.Same(entry, reg.Get("story-1"));
    }

    [Fact]
    public void Get_Missing_ReturnsNull()
    {
        var reg = new PersistedBrokerRegistry();
        Assert.Null(reg.Get("nope"));
    }

    [Fact]
    public void FindBySeed_ReturnsEntryWithMatchingSeed()
    {
        var reg = new PersistedBrokerRegistry();
        var entry = MakeEntry("story-1", "seed-abc");
        reg.Add(entry);

        Assert.Same(entry, reg.FindBySeed("seed-abc"));
        Assert.Null(reg.FindBySeed("seed-other"));
    }

    [Fact]
    public void Remove_DropsEntryFromBothIndexes()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(MakeEntry("story-1", "seed-1"));

        reg.Remove("story-1");

        Assert.Null(reg.Get("story-1"));
        Assert.Null(reg.FindBySeed("seed-1"));
    }

    [Fact]
    public void Clear_WipesAllEntries()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(MakeEntry("story-1", "seed-1"));
        reg.Add(MakeEntry("story-2", "seed-2"));

        reg.Clear();

        Assert.Empty(reg.All());
    }

    [Fact]
    public void MarkAccepted_TransitionsStateFromOfferedToAccepted()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(MakeEntry("story-1", "seed-1", state: PersistedEntryStates.Offered));

        reg.MarkAccepted("story-1");

        Assert.Equal(PersistedEntryStates.Accepted, reg.Get("story-1")!.State);
    }

    [Fact]
    public void MarkAccepted_OnMissingStoryId_IsNoOp()
    {
        var reg = new PersistedBrokerRegistry();
        reg.MarkAccepted("nope");  // should not throw
        Assert.Empty(reg.All());
    }

    [Fact]
    public void BumpLastSeen_UpdatesTimestamps()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(MakeEntry("story-1", "seed-1"));

        reg.BumpLastSeen("story-1", gameSeconds: 99999.0, realUtc: "2026-04-22T00:00:00Z");

        var e = reg.Get("story-1")!;
        Assert.Equal(99999.0, e.Timestamps.LastSeenGameSeconds);
        Assert.Equal("2026-04-22T00:00:00Z", e.Timestamps.LastSeenRealUtc);
    }

    [Fact]
    public void LoadFrom_ReplacesAllEntries()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(MakeEntry("old-story", "old-seed"));

        reg.LoadFrom(new[] { MakeEntry("new-story", "new-seed") });

        Assert.Null(reg.Get("old-story"));
        Assert.NotNull(reg.Get("new-story"));
    }

    [Fact]
    public void Add_SameStoryId_Overwrites()
    {
        var reg = new PersistedBrokerRegistry();
        reg.Add(MakeEntry("story-1", "seed-old"));
        reg.Add(MakeEntry("story-1", "seed-new"));

        Assert.Equal("seed-new", reg.Get("story-1")!.Broker.Seed);
        Assert.Null(reg.FindBySeed("seed-old"));  // old seed index evicted
    }

    private static PersistedEntry MakeEntry(string storyId, string seed, string state = PersistedEntryStates.Offered)
    {
        return new PersistedEntry(
            StoryId: storyId,
            State: state,
            MissionBlock: null!,   // shape-only test; registry doesn't read MissionBlock
            Broker: new PersistedBroker(
                Seed: seed,
                StationId: "station-1",
                Story: null!),     // same rationale
            Timestamps: new PersistedTimestamps(
                CreatedGameSeconds: 0,
                CreatedRealUtc: "2026-04-21T00:00:00Z",
                LastSeenGameSeconds: 0,
                LastSeenRealUtc: "2026-04-21T00:00:00Z"));
    }
}
