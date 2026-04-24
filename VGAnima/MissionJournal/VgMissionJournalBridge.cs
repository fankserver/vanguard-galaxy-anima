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

    private readonly IMissionJournalQuery? _query;

    public bool IsAvailable => _query != null;

    /// <summary>Production constructor: reads
    /// <see cref="MissionJournalApi.Current"/> after confirming the
    /// plugin is loaded via <see cref="Chainloader.PluginInfos"/>.
    /// Both paths catch — xUnit's AppDomain doesn't have BepInEx
    /// initialized and accessing Chainloader there throws.</summary>
    public VgMissionJournalBridge()
    {
        try
        {
            var present = Chainloader.PluginInfos != null
                          && Chainloader.PluginInfos.ContainsKey(PluginGuid);
            _query = present ? MissionJournalApi.Current : null;
        }
        catch
        {
            _query = null;
        }
    }

    /// <summary>Test-only constructor — inject a fake
    /// <see cref="IMissionJournalQuery"/> to exercise the
    /// journal-builder path without a live BepInEx runtime. Null input
    /// is equivalent to "plugin absent."</summary>
    internal VgMissionJournalBridge(IMissionJournalQuery? query)
    {
        _query = query;
    }

    /// <summary>In-flight missions (accept with no terminal outcome yet).
    /// Used for the journal's active window — duplicate-avoidance when
    /// the LLM pitches new missions.</summary>
    public IReadOnlyList<MissionRecord> GetActiveMissions() =>
        _query?.GetActiveMissions() ?? Array.Empty<MissionRecord>();

    /// <summary>Missions sourced at the given station's system, within
    /// the time window. <paramref name="sinceGameSeconds"/> defaults to
    /// 0 (entire history); pass <c>currentGameSeconds - N*86400</c> for
    /// a last-N-days slice.</summary>
    public IReadOnlyList<MissionRecord> GetMissionsInSystem(
        string systemId, double sinceGameSeconds = 0.0)
    {
        if (string.IsNullOrEmpty(systemId)) return Array.Empty<MissionRecord>();
        return _query?.GetMissionsInSystem(systemId, sinceGameSeconds)
               ?? Array.Empty<MissionRecord>();
    }

    /// <summary>Missions with the given source-faction identifier, within
    /// the time window. Used for the journal's network window.</summary>
    public IReadOnlyList<MissionRecord> GetMissionsByFaction(
        string factionId, double sinceGameSeconds = 0.0)
    {
        if (string.IsNullOrEmpty(factionId)) return Array.Empty<MissionRecord>();
        return _query?.GetMissionsByFaction(factionId, sinceGameSeconds)
               ?? Array.Empty<MissionRecord>();
    }

    /// <summary>Missions sourced within <paramref name="maxJumps"/> of
    /// the pivot system. <paramref name="jumpDistance"/> is the graph
    /// closure — same <c>(sysA, sysB) → jumps</c> shape VGMissionJournal's
    /// own API takes.</summary>
    public IReadOnlyList<MissionRecord> GetMissionsWithinJumps(
        string pivotSystemId, int maxJumps,
        Func<string, string, int> jumpDistance,
        double sinceGameSeconds = 0.0)
    {
        if (string.IsNullOrEmpty(pivotSystemId)) return Array.Empty<MissionRecord>();
        return _query?.GetMissionsWithinJumps(pivotSystemId, maxJumps, jumpDistance, sinceGameSeconds)
               ?? Array.Empty<MissionRecord>();
    }
}
