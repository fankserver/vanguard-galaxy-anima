using System.Collections.Generic;
using Source.MissionSystem;
using VGAnima.Patches;
using VGAnima.Pitch;
using Xunit;

namespace VGAnima.Tests.Patches;

public class BrokerStateDetectorTests
{
    /// <summary>Hand-rolled fake — lets us drive archive membership and the
    /// active-mission map without constructing a real GamePlayer.</summary>
    private sealed class FakePlayerView : IGamePlayerView
    {
        public HashSet<string> Archive { get; } = new();
        public Dictionary<string, Mission?> Active { get; } = new();

        public bool IsArchived(string storyId) => Archive.Contains(storyId);
        public Mission? GetActive(string storyId) =>
            Active.TryGetValue(storyId, out var m) ? m : null;
    }

    [Fact]
    public void Detect_Archived_ReturnsDone()
    {
        var view = new FakePlayerView();
        view.Archive.Add("SideMissionPatrol");

        var state = BrokerStateDetector.Detect("SideMissionPatrol", view);

        Assert.Equal(BrokerState.Done, state);
    }

    [Fact]
    public void Detect_NotArchivedAndNoActive_ReturnsInitial()
    {
        var view = new FakePlayerView();
        // Archive is empty; no active mission registered.

        var state = BrokerStateDetector.Detect("SideMissionPatrol", view);

        Assert.Equal(BrokerState.Initial, state);
    }

    [Fact]
    public void Detect_ActiveNotReady_ReturnsInProgress()
    {
        // We can't easily construct a real Mission in tests (Mission's internals
        // touch Unity). Instead we use FakeMission, a trivial subclass that
        // overrides CanClaimRewards — sufficient because BrokerStateDetector only
        // reads that one method on the mission.
        var view = new FakePlayerView();
        view.Active["SideMissionPatrol"] = new FakeMission(canClaimRewards: false);

        var state = BrokerStateDetector.Detect("SideMissionPatrol", view);

        Assert.Equal(BrokerState.InProgress, state);
    }

    [Fact]
    public void Detect_ActiveAndReady_ReturnsReadyToClaim()
    {
        var view = new FakePlayerView();
        view.Active["SideMissionPatrol"] = new FakeMission(canClaimRewards: true);

        var state = BrokerStateDetector.Detect("SideMissionPatrol", view);

        Assert.Equal(BrokerState.ReadyToClaim, state);
    }

    /// <summary>Mission subclass that ignores the usual turnIn / isComplete
    /// gating and just returns a fixed answer. Test-only.</summary>
    private sealed class FakeMission : Mission
    {
        private readonly bool _canClaim;
        public FakeMission(bool canClaimRewards) { _canClaim = canClaimRewards; }
        public override bool CanClaimRewards() => _canClaim;
    }
}
