using System.Collections.Generic;
using Source.MissionSystem;
using VGAnima.Missions;
using VGAnima.Patches;
using Xunit;

namespace VGAnima.Tests.Missions;

public class VanillaSideMissionAssignerTests
{
    private sealed class FakePlayerView : IGamePlayerView
    {
        public HashSet<string> Archive { get; } = new();
        public HashSet<string> Available { get; } = new()
        {
            "SideMissionPatrol", "SideMissionBounty", "SideMissionFastLane"
        };

        public bool IsArchived(string storyId) => Archive.Contains(storyId);
        public Mission? GetActive(string storyId) => null;
    }

    /// <summary>Test-only assigner that lets us inject an IsAvailable predicate
    /// so we don't need the real StoryMission registry (the real one is populated
    /// by a Unity-bound static ctor — out of reach in unit tests).</summary>
    private static VanillaSideMissionAssigner Build(FakePlayerView view) =>
        new VanillaSideMissionAssigner(id => view.Available.Contains(id));

    [Fact]
    public void Assign_ReturnsOneOfTheThreeWhitelistedIds()
    {
        var view = new FakePlayerView();
        var assigner = Build(view);
        var empty = new HashSet<string>();

        var id = assigner.Assign("seed-alpha", empty, view);

        Assert.NotNull(id);
        Assert.Contains(id, new[]
        {
            "SideMissionPatrol", "SideMissionBounty", "SideMissionFastLane"
        });
    }

    [Fact]
    public void Assign_IsDeterministic_SameSeedYieldsSameId()
    {
        var view = new FakePlayerView();
        var assigner = Build(view);
        var empty = new HashSet<string>();

        var first  = assigner.Assign("seed-gamma", empty, view);
        var second = assigner.Assign("seed-gamma", empty, view);
        var third  = assigner.Assign("seed-gamma", empty, view);

        Assert.Equal(first, second);
        Assert.Equal(second, third);
    }

    [Fact]
    public void Assign_ArchivedCandidate_IsSkipped()
    {
        var view = new FakePlayerView();
        var assigner = Build(view);
        var empty = new HashSet<string>();

        // Archive whichever one seed-beta would otherwise prefer first.
        var preferred = assigner.Assign("seed-beta", empty, view)!;
        view.Archive.Add(preferred);

        var fallback = assigner.Assign("seed-beta", empty, view);

        Assert.NotNull(fallback);
        Assert.NotEqual(preferred, fallback);
    }

    [Fact]
    public void Assign_UnavailableCandidate_IsSkipped()
    {
        var view = new FakePlayerView();
        var assigner = Build(view);
        var empty = new HashSet<string>();

        var preferred = assigner.Assign("seed-delta", empty, view)!;
        view.Available.Remove(preferred);  // make it unavailable

        var fallback = assigner.Assign("seed-delta", empty, view);

        Assert.NotNull(fallback);
        Assert.NotEqual(preferred, fallback);
    }

    [Fact]
    public void Assign_AlreadyAssignedInBar_IsSkipped()
    {
        var view = new FakePlayerView();
        var assigner = Build(view);

        var preferred = assigner.Assign("seed-epsilon", new HashSet<string>(), view)!;
        var assigned  = new HashSet<string> { preferred };

        var fallback = assigner.Assign("seed-epsilon", assigned, view);

        Assert.NotNull(fallback);
        Assert.NotEqual(preferred, fallback);
    }

    [Fact]
    public void Assign_AllCandidatesFilteredOut_ReturnsNull()
    {
        var view = new FakePlayerView();
        view.Archive.Add("SideMissionPatrol");
        view.Archive.Add("SideMissionBounty");
        view.Archive.Add("SideMissionFastLane");
        var assigner = Build(view);

        var id = assigner.Assign("seed-zeta", new HashSet<string>(), view);

        Assert.Null(id);
    }

    [Fact]
    public void Assign_DifferentSeedsExploreDifferentStartingIndices()
    {
        // Smoke check that the hash-mod-3 rotation isn't degenerate. With 3
        // candidates, we expect to see at least 2 distinct first-choices across
        // a modest sample of seed strings.
        var view = new FakePlayerView();
        var assigner = Build(view);
        var empty = new HashSet<string>();

        var results = new HashSet<string>();
        foreach (var seed in new[]
        {
            "seed-00", "seed-01", "seed-02", "seed-03", "seed-04",
            "seed-05", "seed-06", "seed-07", "seed-08", "seed-09",
        })
        {
            results.Add(assigner.Assign(seed, empty, view)!);
        }

        Assert.True(results.Count >= 2,
            $"Expected >=2 distinct starting choices across 10 seeds, got {results.Count}");
    }
}
