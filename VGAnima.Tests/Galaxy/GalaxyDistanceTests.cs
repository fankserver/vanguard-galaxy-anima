using System.Collections.Generic;
using VGAnima.Galaxy;
using Xunit;

namespace VGAnima.Tests.Galaxy;

public class GalaxyDistanceTests
{
    [Fact]
    public void JumpsBetween_SameSystem_ReturnsZero()
    {
        var graph = new Graph();
        graph.Connect("a", "b");

        Assert.Equal(0, GalaxyDistance.JumpsBetween("a", "a", graph.Adjacent));
    }

    [Fact]
    public void JumpsBetween_AdjacentSystems_ReturnsOne()
    {
        var graph = new Graph();
        graph.Connect("a", "b");

        Assert.Equal(1, GalaxyDistance.JumpsBetween("a", "b", graph.Adjacent));
        Assert.Equal(1, GalaxyDistance.JumpsBetween("b", "a", graph.Adjacent));
    }

    [Fact]
    public void JumpsBetween_Chain_ReturnsDepth()
    {
        //   a ─ b ─ c ─ d ─ e
        var graph = new Graph();
        graph.Connect("a", "b");
        graph.Connect("b", "c");
        graph.Connect("c", "d");
        graph.Connect("d", "e");

        Assert.Equal(2, GalaxyDistance.JumpsBetween("a", "c", graph.Adjacent));
        Assert.Equal(4, GalaxyDistance.JumpsBetween("a", "e", graph.Adjacent));
        Assert.Equal(4, GalaxyDistance.JumpsBetween("e", "a", graph.Adjacent));  // symmetric
    }

    [Fact]
    public void JumpsBetween_PrefersShortestPath_WhenMultiplePathsExist()
    {
        //         b
        //       ╱   ╲
        //      a     d
        //       ╲   ╱
        //         c
        // Short route a→b→d is 2; longer would be a→c→d = 2. Both 2. Pick 2.
        var graph = new Graph();
        graph.Connect("a", "b");
        graph.Connect("b", "d");
        graph.Connect("a", "c");
        graph.Connect("c", "d");

        Assert.Equal(2, GalaxyDistance.JumpsBetween("a", "d", graph.Adjacent));
    }

    [Fact]
    public void JumpsBetween_ShortcutEdge_PreferredOverLongPath()
    {
        //   a ─ b ─ c ─ d
        //    \─────────╱
        // Shortcut a→d directly = 1; long path a→b→c→d = 3. BFS must pick 1.
        var graph = new Graph();
        graph.Connect("a", "b");
        graph.Connect("b", "c");
        graph.Connect("c", "d");
        graph.Connect("a", "d");

        Assert.Equal(1, GalaxyDistance.JumpsBetween("a", "d", graph.Adjacent));
    }

    [Fact]
    public void JumpsBetween_DisconnectedGraph_ReturnsNegativeOne()
    {
        //   a ─ b       c ─ d  (two components)
        var graph = new Graph();
        graph.Connect("a", "b");
        graph.Connect("c", "d");

        Assert.Equal(-1, GalaxyDistance.JumpsBetween("a", "c", graph.Adjacent));
        Assert.Equal(-1, GalaxyDistance.JumpsBetween("d", "a", graph.Adjacent));
    }

    [Fact]
    public void JumpsBetween_UnknownFromGuid_ReturnsNegativeOne()
    {
        var graph = new Graph();
        graph.Connect("a", "b");

        Assert.Equal(-1, GalaxyDistance.JumpsBetween("ghost", "a", graph.Adjacent));
    }

    [Fact]
    public void JumpsBetween_UnknownToGuid_ReturnsNegativeOne()
    {
        var graph = new Graph();
        graph.Connect("a", "b");

        Assert.Equal(-1, GalaxyDistance.JumpsBetween("a", "ghost", graph.Adjacent));
    }

    [Fact]
    public void JumpsBetween_EmptyGuids_ReturnsNegativeOne()
    {
        var graph = new Graph();
        Assert.Equal(-1, GalaxyDistance.JumpsBetween("", "a", graph.Adjacent));
        Assert.Equal(-1, GalaxyDistance.JumpsBetween("a", "", graph.Adjacent));
    }

    [Fact]
    public void JumpsBetween_CycleDoesNotInfiniteLoop()
    {
        //   a ─ b
        //   │   │   (triangle)
        //   └─ c
        var graph = new Graph();
        graph.Connect("a", "b");
        graph.Connect("b", "c");
        graph.Connect("c", "a");

        Assert.Equal(1, GalaxyDistance.JumpsBetween("a", "b", graph.Adjacent));
        Assert.Equal(1, GalaxyDistance.JumpsBetween("a", "c", graph.Adjacent));
        Assert.Equal(1, GalaxyDistance.JumpsBetween("b", "c", graph.Adjacent));
    }

    /// <summary>Dict-backed graph keyed by guid. Symmetric: Connect(a, b)
    /// adds both directions so the test doesn't have to repeat itself.
    /// Mirrors how SystemMapData.GetAdjacentSystems behaves — every
    /// jumpgate in A to B has a reciprocal jumpgate in B to A.</summary>
    private sealed class Graph
    {
        private readonly Dictionary<string, List<string>> _adj = new();

        public void Connect(string a, string b)
        {
            if (!_adj.TryGetValue(a, out var la)) { la = new List<string>(); _adj[a] = la; }
            if (!_adj.TryGetValue(b, out var lb)) { lb = new List<string>(); _adj[b] = lb; }
            if (!la.Contains(b)) la.Add(b);
            if (!lb.Contains(a)) lb.Add(a);
        }

        public IEnumerable<string> Adjacent(string guid) =>
            _adj.TryGetValue(guid, out var list) ? list : System.Array.Empty<string>();
    }
}
