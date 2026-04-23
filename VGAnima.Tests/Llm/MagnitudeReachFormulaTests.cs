using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Llm;

public class MagnitudeReachFormulaTests
{
    // ========= BaseMagnitudeForDistance =========
    // Tuning rule: gossip always travels to adjacent systems (base = 0
    // for ≤ 2 jumps). Past that, the bar climbs in three bands.

    [Theory]
    [InlineData(0, 0)]   // same system (handled elsewhere but defensive)
    [InlineData(1, 0)]   // adjacent
    [InlineData(2, 0)]   // 2 jumps — still "neighbor"
    [InlineData(3, 2)]   // band change
    [InlineData(5, 2)]
    [InlineData(6, 4)]
    [InlineData(10, 4)]
    [InlineData(11, 6)]
    [InlineData(50, 6)]
    [InlineData(int.MaxValue, 6)]
    public void BaseMagnitudeForDistance_Bands(int jumps, int expected)
    {
        Assert.Equal(expected, MagnitudeReachFormula.BaseMagnitudeForDistance(jumps));
    }

    // ========= AgePenalty =========
    // Monotone non-decreasing — an event can only get harder to hear
    // over time, never easier.

    [Theory]
    [InlineData(0.0,  0)]
    [InlineData(0.5,  0)]
    [InlineData(1.0,  1)]   // 1 day onward = +1
    [InlineData(6.9,  1)]
    [InlineData(7.0,  2)]   // 7 days onward = +2
    [InlineData(29.9, 2)]
    [InlineData(30.0, 3)]   // 30 days onward = +3
    [InlineData(365.0, 3)]
    public void AgePenalty_Bands(double ageDays, int expected)
    {
        Assert.Equal(expected, MagnitudeReachFormula.AgePenalty(ageDays));
    }

    // ========= FameBonus =========
    // Tiers: 5/10/15. Generalized across all archetypes (product
    // decision 2026-04-23: "A CEO is more known than the facility
    // manager" — not coupled to specialty).

    [Theory]
    [InlineData(0, 0)]
    [InlineData(4, 0)]
    [InlineData(5, 1)]
    [InlineData(9, 1)]
    [InlineData(10, 2)]
    [InlineData(14, 2)]
    [InlineData(15, 3)]
    [InlineData(100, 3)]
    public void FameBonus_Tiers(int fame, int expected)
    {
        Assert.Equal(expected, MagnitudeReachFormula.FameBonus(fame));
    }

    // ========= Reaches (composite) =========

    [Fact]
    public void Reaches_GossipTravelsToNeighbor_EvenAtMagnitudeOne()
    {
        // Base 0 at ≤2 jumps, no age, no fame, no faction bonus.
        // Magnitude 1 ≥ required 0 → reaches.
        Assert.True(MagnitudeReachFormula.Reaches(
            magnitude: 1, jumpsAway: 1, ageDays: 0,
            sameFaction: false, fame: 0));
    }

    [Fact]
    public void Reaches_TrivialFarEvent_DoesNotReach()
    {
        // 8 jumps = base 4. Mag 1 < 4 → no.
        Assert.False(MagnitudeReachFormula.Reaches(
            magnitude: 1, jumpsAway: 8, ageDays: 0,
            sameFaction: false, fame: 0));
    }

    [Fact]
    public void Reaches_SameFactionBonus_HelpsMiddleDistance()
    {
        // 5 jumps = base 2. Mag 2 exactly at threshold (no bonus).
        // With same-faction bonus, required becomes 1 → mag 1 also reaches.
        Assert.True(MagnitudeReachFormula.Reaches(
            magnitude: 1, jumpsAway: 5, ageDays: 0,
            sameFaction: true, fame: 0));
        Assert.False(MagnitudeReachFormula.Reaches(
            magnitude: 1, jumpsAway: 5, ageDays: 0,
            sameFaction: false, fame: 0));
    }

    [Fact]
    public void Reaches_AgePenalty_BreaksBoundaryEvent()
    {
        // 5 jumps, mag 2, same-faction: required = 2 - 1 = 1. Mag-2 passes.
        // With 30-day age penalty: required = 2 + 3 - 1 = 4. Mag-2 fails.
        Assert.True(MagnitudeReachFormula.Reaches(
            2, jumpsAway: 5, ageDays: 0,
            sameFaction: true, fame: 0));
        Assert.False(MagnitudeReachFormula.Reaches(
            2, jumpsAway: 5, ageDays: 30,
            sameFaction: true, fame: 0));
    }

    [Fact]
    public void Reaches_HighFame_PushesTrivialEventsFarther()
    {
        // Fame 15 (bonus -3). At 8 jumps (base 4), required = 1. Mag-1 reaches.
        Assert.True(MagnitudeReachFormula.Reaches(
            magnitude: 1, jumpsAway: 8, ageDays: 0,
            sameFaction: false, fame: 15));
    }

    [Fact]
    public void Reaches_EdgeOfGalaxy_MaxMagnitude()
    {
        // 20 jumps = base 6. Magnitude-10 mission with no bonuses still
        // clears (10 ≥ 6). The "famous deed" story still travels galaxy-wide.
        Assert.True(MagnitudeReachFormula.Reaches(
            magnitude: 10, jumpsAway: 20, ageDays: 0,
            sameFaction: false, fame: 0));
    }

    [Fact]
    public void Reaches_AllPenaltiesStacked_HardLimit()
    {
        // 12 jumps (6) + 35-day age (3) - no bonuses = required 9.
        // Only magnitude-9 or -10 missions reach. Magnitude-8 fails.
        Assert.False(MagnitudeReachFormula.Reaches(
            magnitude: 8, jumpsAway: 12, ageDays: 35,
            sameFaction: false, fame: 0));
        Assert.True(MagnitudeReachFormula.Reaches(
            magnitude: 9, jumpsAway: 12, ageDays: 35,
            sameFaction: false, fame: 0));
    }
}
