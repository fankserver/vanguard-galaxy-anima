using System.Linq;
using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Llm;

/// <summary>Covers the pure <see cref="AccessibleDestinationsBuilder.Rank"/>
/// path. The vanilla-reading <c>Build</c> method touches Unity types and
/// isn't unit-testable here — its logic is thin (enumerate + call Rank),
/// so ranking is the interesting surface.</summary>
public class AccessibleDestinationsBuilderTests
{
    private static AccessibleDestination MakeCandidate(
        string name, int jumps, bool sameFaction = false, string? factionId = null)
    {
        return new AccessibleDestination(
            ShortId:             string.Empty,
            StationName:         name,
            SystemName:          "Sys",
            FactionIdentifier:   factionId ?? (sameFaction ? "SalvageGuild" : "Gold"),
            FactionDisplayName:  sameFaction ? "Steel Vultures" : "Luminate Combine",
            JumpsAway:           jumps,
            SameFactionAsBroker: sameFaction,
            Guid:                $"guid_{name}");
    }

    [Fact]
    public void Rank_OrdersByJumpsAwayAscending()
    {
        var result = AccessibleDestinationsBuilder.Rank(new[]
        {
            MakeCandidate("Far", 1),
            MakeCandidate("Near", 0),
        }, maxCount: 8);

        Assert.Equal(2, result.Count);
        Assert.Equal("Near", result[0].StationName);
        Assert.Equal("Far",  result[1].StationName);
    }

    [Fact]
    public void Rank_WithinSameJumps_PrefersSameFactionAsBroker()
    {
        // Same-faction broker → station of same faction outranks a
        // cross-faction station at the same jump distance. Narrative nudge.
        var result = AccessibleDestinationsBuilder.Rank(new[]
        {
            MakeCandidate("CrossFaction", 1, sameFaction: false),
            MakeCandidate("OwnFaction",   1, sameFaction: true),
        }, maxCount: 8);

        Assert.Equal("OwnFaction",   result[0].StationName);
        Assert.Equal("CrossFaction", result[1].StationName);
    }

    [Fact]
    public void Rank_WithinSameJumpsAndFaction_OrdersAlphabetically()
    {
        var result = AccessibleDestinationsBuilder.Rank(new[]
        {
            MakeCandidate("Bravo", 0),
            MakeCandidate("Alpha", 0),
            MakeCandidate("Charlie", 0),
        }, maxCount: 8);

        Assert.Equal("Alpha",   result[0].StationName);
        Assert.Equal("Bravo",   result[1].StationName);
        Assert.Equal("Charlie", result[2].StationName);
    }

    [Fact]
    public void Rank_CombinedCriteria_AllThreeInteract()
    {
        // jump wins over faction-match; faction-match wins over name.
        var result = AccessibleDestinationsBuilder.Rank(new[]
        {
            MakeCandidate("FarSameFaction",  1, sameFaction: true),
            MakeCandidate("NearCrossZeta",   0, sameFaction: false),
            MakeCandidate("NearSameBeta",    0, sameFaction: true),
            MakeCandidate("NearSameAlpha",   0, sameFaction: true),
        }, maxCount: 8);

        // 0-jump same-faction stations first, alphabetical among them.
        Assert.Equal("NearSameAlpha",  result[0].StationName);
        Assert.Equal("NearSameBeta",   result[1].StationName);
        // Then 0-jump cross-faction.
        Assert.Equal("NearCrossZeta",  result[2].StationName);
        // Then 1-jump entries regardless of faction-match.
        Assert.Equal("FarSameFaction", result[3].StationName);
    }

    [Fact]
    public void Rank_CapsAtMaxCount()
    {
        var candidates = Enumerable.Range(0, 20).Select(i => MakeCandidate($"S{i:D2}", 0));
        var result = AccessibleDestinationsBuilder.Rank(candidates, maxCount: 6);
        Assert.Equal(6, result.Count);
    }

    [Fact]
    public void Rank_AssignsSequentialShortIds_Dest0IsTopPick()
    {
        var result = AccessibleDestinationsBuilder.Rank(new[]
        {
            MakeCandidate("Alpha",   0),
            MakeCandidate("Bravo",   0),
            MakeCandidate("Charlie", 0),
        }, maxCount: 8);

        // dest_0 is the top-ranked entry regardless of input order — we
        // rely on this in the validator ("the LLM's first intuition
        // should be to pick dest_0 for the 'most obvious' target").
        Assert.Equal("dest_0", result[0].ShortId);
        Assert.Equal("dest_1", result[1].ShortId);
        Assert.Equal("dest_2", result[2].ShortId);
    }

    [Fact]
    public void Rank_PreservesGuidThroughRanking()
    {
        var result = AccessibleDestinationsBuilder.Rank(new[]
        {
            MakeCandidate("Alpha", 0),
        }, maxCount: 8);

        // Factory resolves ShortId → Guid via the list; if this broke,
        // TravelToPOI would get a wrong / empty targetPOI.
        Assert.Equal("guid_Alpha", result[0].Guid);
    }

    [Fact]
    public void Rank_Empty_ReturnsEmpty()
    {
        var result = AccessibleDestinationsBuilder.Rank(
            System.Array.Empty<AccessibleDestination>(), maxCount: 8);
        Assert.Empty(result);
    }

    [Fact]
    public void Rank_MaxCountZero_ReturnsEmpty()
    {
        // Defensive — `deliver_to_station` intent should be inapplicable
        // when no destinations are reachable at all (e.g. isolated pocket
        // system). Builder should handle the zero-cap degenerate case
        // without throwing.
        var result = AccessibleDestinationsBuilder.Rank(new[]
        {
            MakeCandidate("Alpha", 0),
        }, maxCount: 0);
        Assert.Empty(result);
    }
}
