using System;
using System.Collections.Generic;
using System.Linq;
using VGAnima.MissionJournal;
using VGAnima.Persistence;
using MissionRecord = VGAnima.MissionJournal.JournalRecord;

namespace VGAnima.Llm;

/// <summary>Filters VGMissionJournal's mission records through an
/// NPC-perspective lens: each broker only sees what they would
/// plausibly know given their station, faction, the event's magnitude,
/// the distance from where it happened, its age, and the player's
/// galactic fame. Four windows compose the view:
/// <list type="bullet">
///   <item><c>local</c> — recent events at THIS station. Bar gossip.
///   Always visible, highest fidelity.</item>
///   <item><c>network</c> — resolved events within reach via the
///   faction's internal network (same source-faction as the broker,
///   close enough for the reach formula to pass).</item>
///   <item><c>rumors</c> — distant hearsay that reached the broker
///   despite being out-of-network — either different faction, or same
///   faction far enough away that only magnitude pushed it through.</item>
///   <item><c>active</c> — in-flight offered/accepted VGAnima missions
///   the broker plausibly knows about. Duplicate-avoidance signal.</item>
/// </list>
///
/// <para>Reach is governed by <see cref="MagnitudeReachFormula"/>.
/// Resolved windows are sourced from <see cref="VgMissionJournalBridge"/>;
/// the active window stays on <see cref="PersistedBrokerRegistry"/>
/// because Offered-but-not-yet-accepted VGAnima missions don't reach
/// VGMissionJournal until accept.</para>
///
/// <para>Dedup across resolved windows uses storyId when populated and
/// <see cref="MissionRecord.MissionInstanceId"/> as fallback — vanilla
/// generator missions have empty StoryId so StoryId alone would collapse
/// many distinct records into one dedup bucket.</para></summary>
internal static class JournalContextBuilder
{
    public const int LocalWindowSize   = 5;
    public const int NetworkWindowSize = 5;
    public const int RumorsWindowSize  = 3;
    /// <summary>Cap on the in-flight window. In-flight entry count is
    /// naturally bounded (brokers are transient), so the cap is mostly
    /// defensive.</summary>
    public const int ActiveWindowSize  = 8;

    /// <summary>Jump-range cap for the bridge prefilter. The reach
    /// formula's worst-case "maximum mag-10 famous-player" could
    /// theoretically reach anywhere — but in a 15-jump radius we cover
    /// the effective galaxy. Tighter prefilters reduce the per-broker
    /// record scan without changing the visible result in practice.</summary>
    public const int MaxReachJumps = 15;

    /// <summary>Age cap for the bridge prefilter. Older resolved
    /// missions would fail the reach formula's age penalty anyway
    /// (+3 above 30 days pushes most records out); 90 days is the
    /// point beyond which only famous high-magnitude combat would
    /// still surface, and those are rare enough to be acceptable
    /// losses.</summary>
    public const double MaxRecordAgeDays = 90.0;

    /// <summary>Builds a filtered view of the journal for the broker.
    ///
    /// <param name="bridge">Source of resolved-mission records. When
    /// VGMissionJournal is absent the bridge returns empty lists and
    /// all three resolved windows end up empty.</param>
    /// <param name="brokerStationId">The broker's current station guid
    /// — used for the local-window station match.</param>
    /// <param name="brokerSystemId">The broker's current system guid
    /// — pivot for the jump-distance math.</param>
    /// <param name="factionIdentifier">The broker's faction identifier
    /// — drives same-faction bonus and network/rumors split.</param>
    /// <param name="jumpDistance">System→system jump graph closure. Same
    /// <c>(fromSys, toSys) → jumps</c> shape VGMissionJournal's proximity
    /// API takes.</param>
    /// <param name="currentGameSeconds">Used by the age penalty. Also
    /// drives the 90-day bridge prefilter.</param>
    /// <param name="playerFame">Max of bounty / patrol / industry ranks.
    /// Feeds <see cref="MagnitudeReachFormula.FameBonus"/>.</param>
    /// <param name="inFlight">VGAnima persisted offered/accepted entries
    /// for the active window.</param></summary>
    public static LlmJournalSection Build(
        VgMissionJournalBridge bridge,
        string brokerStationId,
        string brokerSystemId,
        string factionIdentifier,
        Func<string, string, int> jumpDistance,
        double currentGameSeconds = 0,
        int playerFame = 0,
        IReadOnlyCollection<PersistedEntry>? inFlight = null)
    {
        var sinceGameSeconds = currentGameSeconds > MaxRecordAgeDays * 86400.0
            ? currentGameSeconds - MaxRecordAgeDays * 86400.0
            : 0.0;

        // Single proximity query prefilters by system-distance + age; we
        // still apply the full reach formula per record to classify.
        var records = string.IsNullOrEmpty(brokerSystemId)
            ? Array.Empty<MissionRecord>()
            : bridge.GetMissionsWithinJumps(
                brokerSystemId, MaxReachJumps, jumpDistance, sinceGameSeconds);

        // Resolved-only, newest-first by terminal time.
        var resolved = records
            .Where(r => !r.IsActive && r.Outcome != null)
            .OrderByDescending(r => r.TerminalAtGameSeconds ?? 0.0)
            .ToList();

        // Distance cache — same source system resolves to the same jump
        // count, so memoize across the three window passes.
        var distanceCache = new Dictionary<string, int>(StringComparer.Ordinal);
        int JumpsFor(string fromSystemId)
        {
            if (string.IsNullOrEmpty(fromSystemId)) return int.MaxValue;
            if (distanceCache.TryGetValue(fromSystemId, out var cached)) return cached;
            var computed = jumpDistance(fromSystemId, brokerSystemId);
            if (computed < 0) computed = int.MaxValue;
            distanceCache[fromSystemId] = computed;
            return computed;
        }

        // ---- Local ----
        var local = new List<LlmJournalEntry>();
        foreach (var r in resolved)
        {
            if (local.Count >= LocalWindowSize) break;
            if (r.SourceStationId == brokerStationId)
                local.Add(ToSnapshot(r, jumpsFromHere: 0));
        }
        var seen = new HashSet<string>(local.Select(DedupKey), StringComparer.Ordinal);

        // ---- Network ----
        var network = new List<LlmJournalEntry>();
        foreach (var r in resolved)
        {
            if (network.Count >= NetworkWindowSize) break;
            if (string.IsNullOrEmpty(r.SourceSystemId))        continue;
            if (seen.Contains(DedupKey(r)))                    continue;
            if (r.SourceStationId == brokerStationId)          continue;
            if (r.SourceFaction   != factionIdentifier)        continue;

            var jumpsAway = JumpsFor(r.SourceSystemId!);
            if (jumpsAway == int.MaxValue) continue;

            var ageDays  = Math.Max(0,
                (currentGameSeconds - (r.TerminalAtGameSeconds ?? 0.0)) / 86400.0);
            var mag      = r.Magnitude;
            var required = MagnitudeReachFormula.RequiredMagnitude(
                jumpsAway, ageDays, sameFaction: true, fame: playerFame);
            var included = mag >= required;

            LogReachDecision(r, mag, jumpsAway, ageDays, required, included, "network");

            if (included)
            {
                network.Add(ToSnapshot(r, jumpsFromHere: jumpsAway));
                seen.Add(DedupKey(r));
            }
        }

        // ---- Rumors ----
        var rumors = new List<LlmJournalEntry>();
        foreach (var r in resolved)
        {
            if (rumors.Count >= RumorsWindowSize) break;
            if (string.IsNullOrEmpty(r.SourceSystemId))        continue;
            if (seen.Contains(DedupKey(r)))                    continue;
            if (r.SourceStationId == brokerStationId)          continue;

            var jumpsAway = JumpsFor(r.SourceSystemId!);
            if (jumpsAway == int.MaxValue) continue;

            var ageDays     = Math.Max(0,
                (currentGameSeconds - (r.TerminalAtGameSeconds ?? 0.0)) / 86400.0);
            var sameFaction = r.SourceFaction == factionIdentifier;
            var mag         = r.Magnitude;
            var required    = MagnitudeReachFormula.RequiredMagnitude(
                jumpsAway, ageDays, sameFaction: sameFaction, fame: playerFame);
            var included    = mag >= required;

            LogReachDecision(r, mag, jumpsAway, ageDays, required, included, "rumors");

            if (included)
            {
                rumors.Add(ToSnapshot(r, jumpsFromHere: jumpsAway));
                seen.Add(DedupKey(r));
            }
        }

        var active = BuildActiveWindow(inFlight, brokerStationId, factionIdentifier);

        return new LlmJournalSection
        {
            Local   = local,
            Network = network,
            Rumors  = rumors,
            Active  = active,
        };
    }

    /// <summary>Stable per-record key for cross-window dedup. StoryId
    /// when populated (authored story missions), MissionInstanceId
    /// otherwise (generator missions — vanilla leaves StoryId empty).</summary>
    private static string DedupKey(MissionRecord r) =>
        string.IsNullOrEmpty(r.StoryId) ? r.MissionInstanceId : r.StoryId;

    private static string DedupKey(LlmJournalEntry e) => e.StoryId;

    /// <summary>Projects in-flight persisted entries (state = offered or
    /// accepted) into the active window, filtered by same-station OR
    /// same-faction. Magnitude is recomputed on the fly since in-flight
    /// entries don't carry a resolved outcome yet.
    ///
    /// <para>The active window intentionally does NOT use the reach
    /// formula — we want the broker aware of any offered mission in
    /// their orbit regardless of magnitude, so duplicate-avoidance is
    /// robust for trivial hauls too.</para></summary>
    private static IReadOnlyList<LlmJournalEntry> BuildActiveWindow(
        IReadOnlyCollection<PersistedEntry>? inFlight,
        string stationId,
        string factionIdentifier)
    {
        if (inFlight is null || inFlight.Count == 0)
            return Array.Empty<LlmJournalEntry>();

        var ranked = new List<(int prio, PersistedEntry entry)>();
        foreach (var e in inFlight)
        {
            int prio;
            if (e.Broker.StationId == stationId)                            prio = 2;
            else if (e.MissionBlock.SourceFaction == factionIdentifier)     prio = 1;
            else                                                             continue;
            ranked.Add((prio, e));
        }
        ranked.Sort((a, b) =>
        {
            if (a.prio != b.prio) return b.prio.CompareTo(a.prio);
            return b.entry.Timestamps.CreatedGameSeconds
                    .CompareTo(a.entry.Timestamps.CreatedGameSeconds);
        });

        return ranked
            .Take(ActiveWindowSize)
            .Select(x => InFlightSnapshot(x.entry))
            .ToList();
    }

    /// <summary>Record → snapshot. Uses StoryId-or-InstanceId so the
    /// LLM has a non-empty reference string per entry (empty storyIds
    /// confuse the prompt's "don't offer an id you've already used" rule).</summary>
    private static LlmJournalEntry ToSnapshot(MissionRecord r, int jumpsFromHere) =>
        new(
            StoryId:             DedupKey(r),
            MissionName:         r.MissionName ?? "",
            Objectives:          r.ObjectiveTags,
            Outcome:             r.Outcome ?? "unknown",
            SourceFaction:       r.SourceFaction ?? "",
            StationName:         r.SourceStationName ?? "",
            SystemName:          r.SourceSystemName ?? "",
            ResolvedGameSeconds: r.TerminalAtGameSeconds ?? 0.0,
            Magnitude:           r.Magnitude,
            JumpsFromHere:       jumpsFromHere);

    private static void LogReachDecision(
        MissionRecord r, int mag, int jumpsAway, double ageDays,
        int required, bool included, string window)
    {
        Plugin.Log?.LogDebug(
            $"Reach[{window}]: '{r.MissionName}' ({r.MissionSubclass} {r.SourceFaction}) " +
            $"mag={mag} jumps={jumpsAway} age={ageDays:F1}d " +
            $"req={required} {(included ? "IN" : "OUT")}");
    }

    private static LlmJournalEntry InFlightSnapshot(PersistedEntry e)
    {
        var block      = e.MissionBlock;
        var objectives = InFlightObjectives(block);
        // Active-window magnitude is only a display signal — the reach
        // formula doesn't gate in-flight entries (broker awareness of
        // offered jobs is unconditional). 5 is a neutral mid-band
        // value that doesn't skew the LLM's relative weighting.
        const int magnitude = 5;
        return new LlmJournalEntry(
            StoryId:             e.StoryId,
            MissionName:         block.Name,
            Objectives:          objectives,
            Outcome:             CompletedMissionOutcomes.InProgress,
            SourceFaction:       block.SourceFaction,
            StationName:         e.Broker.StationNameSnapshot ?? "a station",
            SystemName:          e.Broker.SystemNameSnapshot  ?? string.Empty,
            ResolvedGameSeconds: e.Timestamps.CreatedGameSeconds,
            Magnitude:           magnitude,
            JumpsFromHere:       0);
    }

    /// <summary>Map a pitched LlmMissionBlock's intents to the same
    /// objective-tag vocabulary <see cref="MissionRecordArchetype.ObjectiveTags"/>
    /// emits for historical records. Keeps the shape identical across
    /// resolved and in-flight journal entries so the LLM reads one rule.</summary>
    private static IReadOnlyList<string> InFlightObjectives(LlmMissionBlock block)
    {
        var tags = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in block.Steps)
        {
            switch (step.Intent)
            {
                case ClearCombatSiteIntent:
                    tags.Add(MissionRecordArchetype.TagKillEnemies);
                    break;
                case GatherOreIntent:
                    tags.Add(MissionRecordArchetype.TagMineOre);
                    break;
                case GatherSalvageIntent:
                    tags.Add(MissionRecordArchetype.TagCollectSalvage);
                    break;
                case DefendedGatherOreIntent:
                    tags.Add(MissionRecordArchetype.TagKillEnemies);
                    tags.Add(MissionRecordArchetype.TagMineOre);
                    break;
                case DefendedGatherSalvageIntent:
                    tags.Add(MissionRecordArchetype.TagKillEnemies);
                    tags.Add(MissionRecordArchetype.TagCollectSalvage);
                    break;
                case HaulGoodsIntent:
                    tags.Add(MissionRecordArchetype.TagHaulGoods);
                    break;
                case DeliverToStationIntent:
                    tags.Add(MissionRecordArchetype.TagTravel);
                    break;
            }
        }
        if (tags.Count == 0) return Array.Empty<string>();
        var arr = new string[tags.Count];
        tags.CopyTo(arr);
        Array.Sort(arr, StringComparer.Ordinal);
        return arr;
    }
}
