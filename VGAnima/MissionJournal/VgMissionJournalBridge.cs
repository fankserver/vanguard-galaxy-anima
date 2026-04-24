using System;
using System.Collections.Generic;
using BepInEx.Bootstrap;
using VGMissionJournal.Api;
using VGMissionJournal.Logging;

namespace VGAnima.MissionJournal;

/// <summary>Typed soft-dep on VGMissionJournal. When the plugin isn't
/// installed, <see cref="IsAvailable"/> is false and every query method
/// returns an empty list — callers don't need to check the flag unless
/// they want to log or short-circuit.
///
/// <para>Why typed rather than reflection: field renames on
/// <see cref="MissionRecord"/> become compile-time errors instead of
/// runtime silent drift. Trade-off: <see cref="VGMissionJournal.dll"/>
/// must be on the build path (symlinked via <c>make link-missionjournal</c>),
/// but it's <c>Private=false</c> in the csproj so it doesn't ship with
/// VGAnima — BepInEx loads it separately.</para>
///
/// <para>The <see cref="Chainloader.PluginInfos"/> presence check is
/// deliberate: if we only relied on <see cref="MissionJournalApi.Current"/>
/// null-check, the CLR would resolve <see cref="MissionJournalApi"/>
/// at JIT time and fail to load VGAnima entirely when the assembly is
/// missing. Gating the first type reference behind a plugin-presence
/// check pushes type resolution past the guard.</para></summary>
internal sealed class VgMissionJournalBridge
{
    private const string PluginGuid = "vgmissionjournal";

    public bool IsAvailable { get; }

    public VgMissionJournalBridge()
    {
        // Guard: accessing Chainloader.PluginInfos triggers BepInEx
        // ConfigFile static initialization which crashes in xUnit's
        // AppDomain (no BepInEx runtime). Treat any access failure as
        // "plugin absent" — a reasonable posture since the only time
        // Chainloader throws is when BepInEx itself isn't around.
        try
        {
            IsAvailable = Chainloader.PluginInfos != null
                          && Chainloader.PluginInfos.ContainsKey(PluginGuid);
        }
        catch
        {
            IsAvailable = false;
        }
    }

    /// <summary>In-flight missions (accept with no terminal outcome yet).
    /// Used for the journal's active window — duplicate-avoidance when
    /// the LLM pitches new missions.</summary>
    public IReadOnlyList<MissionRecord> GetActiveMissions()
    {
        if (!IsAvailable) return Array.Empty<MissionRecord>();
        return GetActiveMissionsInner();
    }

    /// <summary>Missions sourced at the given station's system, within
    /// the time window. <paramref name="sinceGameSeconds"/> defaults to
    /// 0 (entire history); pass <c>currentGameSeconds - N*86400</c> for
    /// a last-N-days slice. Returns empty when
    /// <see cref="IsAvailable"/> is false.</summary>
    public IReadOnlyList<MissionRecord> GetMissionsInSystem(
        string systemId, double sinceGameSeconds = 0.0)
    {
        if (!IsAvailable) return Array.Empty<MissionRecord>();
        if (string.IsNullOrEmpty(systemId)) return Array.Empty<MissionRecord>();
        return GetMissionsInSystemInner(systemId, sinceGameSeconds);
    }

    /// <summary>Missions with the given source-faction identifier, within
    /// the time window. Used for the journal's network window.</summary>
    public IReadOnlyList<MissionRecord> GetMissionsByFaction(
        string factionId, double sinceGameSeconds = 0.0)
    {
        if (!IsAvailable) return Array.Empty<MissionRecord>();
        if (string.IsNullOrEmpty(factionId)) return Array.Empty<MissionRecord>();
        return GetMissionsByFactionInner(factionId, sinceGameSeconds);
    }

    /// <summary>Missions sourced within <paramref name="maxJumps"/> of
    /// the pivot system. <paramref name="jumpDistance"/> is the graph
    /// closure — same shape VGAnima already uses for
    /// <see cref="Galaxy.GalaxyDistance.JumpsBetween"/>.</summary>
    public IReadOnlyList<MissionRecord> GetMissionsWithinJumps(
        string pivotSystemId, int maxJumps,
        Func<string, string, int> jumpDistance,
        double sinceGameSeconds = 0.0)
    {
        if (!IsAvailable) return Array.Empty<MissionRecord>();
        if (string.IsNullOrEmpty(pivotSystemId)) return Array.Empty<MissionRecord>();
        return GetMissionsWithinJumpsInner(pivotSystemId, maxJumps, jumpDistance, sinceGameSeconds);
    }

    // ---- Plugin-present paths, split so the JIT doesn't resolve the
    // VGMissionJournal types until IsAvailable is true. Naming convention:
    // *Inner() functions contain every reference to VGMissionJournal types;
    // the public wrappers above contain no such references in their body,
    // only in their signature (loaded lazily).

    private IReadOnlyList<MissionRecord> GetActiveMissionsInner()
    {
        var api = MissionJournalApi.Current;
        return api?.GetActiveMissions() ?? Array.Empty<MissionRecord>();
    }

    private IReadOnlyList<MissionRecord> GetMissionsInSystemInner(
        string systemId, double sinceGS)
    {
        var api = MissionJournalApi.Current;
        return api?.GetMissionsInSystem(systemId, sinceGS) ?? Array.Empty<MissionRecord>();
    }

    private IReadOnlyList<MissionRecord> GetMissionsByFactionInner(
        string factionId, double sinceGS)
    {
        var api = MissionJournalApi.Current;
        return api?.GetMissionsByFaction(factionId, sinceGS) ?? Array.Empty<MissionRecord>();
    }

    private IReadOnlyList<MissionRecord> GetMissionsWithinJumpsInner(
        string pivotSystemId, int maxJumps,
        Func<string, string, int> jumpDistance, double sinceGS)
    {
        var api = MissionJournalApi.Current;
        return api?.GetMissionsWithinJumps(
            pivotSystemId, maxJumps, jumpDistance, sinceGS)
            ?? Array.Empty<MissionRecord>();
    }
}
