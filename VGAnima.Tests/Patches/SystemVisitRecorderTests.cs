using VGAnima.Patches;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Patches;

public class SystemVisitRecorderTests
{
    [Fact]
    public void FirstTransition_FromEmptyLatch_RecordsVisit()
    {
        var reg = new PersistedBrokerRegistry();

        var newLatch = SystemVisitRecorder.RecordVisitIfTransitioned(
            reg, currentLatchedGuid: "",
            arrivingGuid: "sys-a", arrivingName: "Alpha",
            gameSeconds: 100.0);

        Assert.Equal("sys-a", newLatch);
        Assert.Single(reg.VisitedSystems);
        Assert.Equal(1, reg.VisitedSystems["sys-a"].VisitCount);
    }

    [Fact]
    public void RepeatedCall_WithSameGuid_DoesNotDoubleCount()
    {
        // Defensive: if vanilla ever calls JumpToSystem twice for the
        // same logical travel (e.g. mid-coroutine target rewrite — decomp
        // line 107537 tutorial→sandbox hack), the latch prevents the
        // second firing from inflating the count.
        var reg = new PersistedBrokerRegistry();

        var latchAfterFirst = SystemVisitRecorder.RecordVisitIfTransitioned(
            reg, "", "sys-a", "Alpha", 100.0);
        var latchAfterSecond = SystemVisitRecorder.RecordVisitIfTransitioned(
            reg, latchAfterFirst, "sys-a", "Alpha", 200.0);

        Assert.Equal("sys-a", latchAfterSecond);
        Assert.Equal(1, reg.VisitedSystems["sys-a"].VisitCount);
        // LastVisitGameSeconds also not bumped — we don't record the
        // transition at all when the guid is unchanged.
        Assert.Equal(100.0, reg.VisitedSystems["sys-a"].LastVisitGameSeconds);
    }

    [Fact]
    public void DifferentGuid_IncrementsAndUpdatesLatch()
    {
        var reg = new PersistedBrokerRegistry();

        var latchA = SystemVisitRecorder.RecordVisitIfTransitioned(
            reg, "", "sys-a", "Alpha", 100.0);
        var latchB = SystemVisitRecorder.RecordVisitIfTransitioned(
            reg, latchA, "sys-b", "Beta", 200.0);

        Assert.Equal("sys-b", latchB);
        Assert.Equal(1, reg.VisitedSystems["sys-a"].VisitCount);
        Assert.Equal(1, reg.VisitedSystems["sys-b"].VisitCount);
    }

    [Fact]
    public void ReturnTrip_AfterAnotherSystem_IncrementsOriginalCount()
    {
        // Visit A → B → A: A should have visitCount 2, B has 1.
        // This is the usual "player hauls cargo round-trip" shape.
        var reg = new PersistedBrokerRegistry();
        var latch = "";

        latch = SystemVisitRecorder.RecordVisitIfTransitioned(reg, latch, "sys-a", "Alpha", 100.0);
        latch = SystemVisitRecorder.RecordVisitIfTransitioned(reg, latch, "sys-b", "Beta",  200.0);
        latch = SystemVisitRecorder.RecordVisitIfTransitioned(reg, latch, "sys-a", "Alpha", 300.0);

        Assert.Equal(2, reg.VisitedSystems["sys-a"].VisitCount);
        Assert.Equal(1, reg.VisitedSystems["sys-b"].VisitCount);
        Assert.Equal(100.0, reg.VisitedSystems["sys-a"].FirstVisitGameSeconds);
        Assert.Equal(300.0, reg.VisitedSystems["sys-a"].LastVisitGameSeconds);
    }

    [Fact]
    public void EmptyArrivingGuid_ReturnsUnchangedLatchAndSkipsRecord()
    {
        // Defensive: if vanilla hands us a system with a null / empty
        // guid (shouldn't happen but cheap to guard), we'd rather drop
        // the visit than store an unkeyed entry.
        var reg = new PersistedBrokerRegistry();

        var latch = SystemVisitRecorder.RecordVisitIfTransitioned(
            reg, "sys-a", "", "Anonymous", 100.0);

        Assert.Equal("sys-a", latch);
        Assert.Empty(reg.VisitedSystems);
    }

    [Fact]
    public void NameChange_OnRevisit_PropagatesToVisitedSystem()
    {
        // Mirrors PersistedBrokerRegistry's name-update contract — a
        // system whose display name changes mid-save gets the latest
        // name written on every revisit.
        var reg = new PersistedBrokerRegistry();
        var latch = "";

        latch = SystemVisitRecorder.RecordVisitIfTransitioned(reg, latch, "sys-a", "Alpha",       100.0);
        latch = SystemVisitRecorder.RecordVisitIfTransitioned(reg, latch, "sys-b", "Beta",        200.0);
        latch = SystemVisitRecorder.RecordVisitIfTransitioned(reg, latch, "sys-a", "Alpha Prime", 300.0);

        Assert.Equal("Alpha Prime", reg.VisitedSystems["sys-a"].Name);
    }
}
