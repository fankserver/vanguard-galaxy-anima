using System.Collections.Generic;
using System.Linq;

namespace VGAnima.Llm;

/// <summary>Flattens an <see cref="IGameStateView"/> into an <see cref="LlmContext"/>
/// ready for JSON serialization. Enforces spec §4 truncation caps:
/// <list type="bullet">
///   <item><c>stored_ships</c> → first 10</item>
///   <item><c>crew</c> → first 10</item>
///   <item><c>connected_systems</c> → first 8 (2-jump radius)</item>
///   <item><c>archive_recent</c> → last 10 (most recent = end of list)</item>
///   <item><c>waypoints</c> → first 5</item>
/// </list>
/// <see cref="IGameStateView"/> already yields the "natural" lists; the
/// gatherer applies these additional caps on top before building the context.
/// Deterministic, side-effect-free, safe to call from the Unity main thread.</summary>
internal sealed class ContextGatherer
{
    /// <summary>Builds the LLM context for a given game state + broker.
    /// Optionally includes a per-broker <see cref="LlmJournalSection"/>
    /// when the caller supplies one — pre-built by
    /// <see cref="JournalContextBuilder"/> against the current
    /// <c>PersistedBrokerRegistry</c>. Passing <c>null</c> omits the
    /// <c>journal</c> key from the context JSON entirely (the LlmContext
    /// field uses NullValueHandling.Ignore).</summary>
    public LlmContext Gather(
        IGameStateView view,
        BrokerInfo broker,
        LlmJournalSection? journal = null,
        LlmBarEcosystemSection? barEcosystem = null,
        LlmPurchaseProfileSection? purchaseProfile = null,
        IReadOnlyList<AccessibleDestination>? accessibleDestinations = null)
    {
        var ctx = new LlmContext
        {
            Player = new LlmPlayerSection
            {
                Level             = view.PlayerLevel,
                Credits           = view.PlayerCredits,
                Specialization    = view.PlayerSpecialization,
                BountyRank        = view.BountyRank,
                PatrolRank        = view.PatrolRank,
                IndustryRank      = view.IndustryRank,
                MaxBountyLevel    = view.MaxBountyLevel,
                MaxPatrolLevel    = view.MaxPatrolLevel,
                MaxIndustryLevel  = view.MaxIndustryLevel,
                UnlockedTitles    = view.UnlockedTitles,
                ActiveMissionCount = view.ActiveMissionCount,
                ActiveMissionCap   = view.ActiveMissionCap,
            },
            Fleet = new LlmFleetSection
            {
                PrimaryShip = view.PrimaryShip,
                StoredShips = view.StoredShips.Take(10).ToList(),
                Crew        = view.Crew.Take(10).ToList(),
            },
            Location = new LlmLocationSection
            {
                CurrentStation    = view.CurrentStationName,
                StationFaction    = view.StationFaction,
                StationFacilities = view.StationFacilities,
                CurrentSystem     = view.CurrentSystemName,
                CurrentSector     = view.CurrentSectorName,
                Quadrant          = view.Quadrant,
                ConnectedSystems  = view.ConnectedSystems.Take(8).ToList(),
            },
            Factions   = BuildFactions(view.Reputation, view.AtWar),
            RewardClamps = new LlmRewardClampsSection
            {
                CreditsBaseValueMin    = MissionBlockValidator.CreditsBaseMin,
                CreditsBaseValueMax    = MissionBlockValidator.CreditsBaseMax,
                ExperienceBaseValueMin = MissionBlockValidator.ExperienceBaseMin,
                ExperienceBaseValueMax = MissionBlockValidator.ExperienceBaseMax,
                ReputationAmountMin    = MissionBlockValidator.ReputationMin,
                ReputationAmountMax    = MissionBlockValidator.ReputationMax,
            },
            Missions = new LlmMissionsSection
            {
                ActiveStoryIds       = view.ActiveStoryIds,
                // archive tail — most recent 10 entries.
                ArchiveRecent        = TakeLast(view.ArchiveRecent, 10),
                CurrentBountyLevel   = view.CurrentBountyLevel,
                CurrentPatrolLevel   = view.CurrentPatrolLevel,
                CurrentIndustryLevel = view.CurrentIndustryLevel,
            },
            StoryArcsActive = view.StoryArcsActive,
            Waypoints       = view.Waypoints.Take(5).ToList(),
            Time = new LlmTimeSection
            {
                ElapsedSeconds = view.ElapsedSeconds,
                // Game days ≈ 86400 elapsed-seconds per day (matches vanilla bar refresh cadence).
                DayOfYear      = ((int)(view.ElapsedSeconds / 86400.0) % 365) + 1,
            },
            Broker = new LlmBrokerSection
            {
                Name           = broker.Name,
                IsMale         = broker.IsMale,
                Seed           = broker.Seed,
                StationFaction = broker.StationFaction,
            },
            Journal      = journal,
            // Null-unless-populated: LlmContext.BarEcosystem carries
            // NullValueHandling.Ignore so an empty bar (no salesmen,
            // or callers that don't pass it) produces no field in the
            // serialized JSON.
            BarEcosystem = (barEcosystem is { OtherSalesmenHere: { Count: > 0 } })
                            ? barEcosystem : null,
            PurchaseProfile = purchaseProfile,
            // Omit when empty — pocket systems with no reachable stations
            // should not emit a section the LLM can't act on anyway.
            AccessibleDestinations =
                (accessibleDestinations is { Count: > 0 }) ? accessibleDestinations : null,
        };
        // Final pass: derive mission guidance from the fully-populated context.
        // Must run last — reads every section.
        ctx.MissionGuidance = MissionGuidanceBuilder.Build(ctx);
        // station_condition depends on MissionGuidance (combat-forbidden
        // check), so it runs AFTER the guidance builder. Cheap — one
        // boolean + one linq + one count.
        ctx.Location.StationCondition = StationConditionInferrer.Infer(ctx);
        return ctx;
    }

    /// <summary>Builds the per-faction snapshot. Relation bands mirror
    /// <c>FactionData.IsEnemy</c> (<c>rep &lt; -500 OR at_war</c> = hostile)
    /// plus a friendly/neutral split at rep=0. Keys are all known
    /// identifiers that appear either in <paramref name="reputation"/> or
    /// <paramref name="atWar"/> — we don't synthesize unseen factions into
    /// the dict.</summary>
    private static IReadOnlyDictionary<string, LlmFactionEntry> BuildFactions(
        IReadOnlyDictionary<string, int> reputation,
        IReadOnlyList<string> atWar)
    {
        var atWarSet = new HashSet<string>(atWar);
        var result   = new Dictionary<string, LlmFactionEntry>(reputation.Count + atWar.Count);

        foreach (var kv in reputation)
        {
            result[kv.Key] = BuildEntry(kv.Key, kv.Value, atWarSet.Contains(kv.Key));
        }
        foreach (var f in atWar)
        {
            // at-war faction with no rep entry — record as hostile regardless.
            if (!result.ContainsKey(f))
                result[f] = BuildEntry(f, 0, isAtWar: true);
        }
        return result;
    }

    private static LlmFactionEntry BuildEntry(string identifier, int rep, bool isAtWar)
    {
        string relation;
        if (isAtWar || rep < -500) relation = "hostile";
        else if (rep > 0)          relation = "friendly";
        else                       relation = "neutral";
        return new LlmFactionEntry(FactionDisplayNames.Lookup(identifier), relation, rep);
    }

    private static IReadOnlyList<T> TakeLast<T>(IReadOnlyList<T> source, int n)
    {
        // netstandard2.1 lacks LINQ's TakeLast on IReadOnlyList<T> generically,
        // and Enumerable.TakeLast requires IEnumerable<T>. Open-code to avoid
        // allocating twice.
        if (source.Count <= n) return source;
        var list = new List<T>(n);
        for (var i = source.Count - n; i < source.Count; i++) list.Add(source[i]);
        return list;
    }
}
