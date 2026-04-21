using System.Collections.Generic;
using System.Linq;

namespace VGAnima.Llm;

/// <summary>Computes a ranked archetype recommendation + rationale from a
/// populated <see cref="LlmContext"/>. Runs last in <see cref="ContextGatherer"/>
/// — reads every other section and produces the final
/// <see cref="LlmMissionGuidance"/>.
///
/// Weighting model: each signal contributes a raw weight to one archetype.
/// Archetype raw weights are summed, forbidden archetypes are zeroed, then the
/// whole vector is normalized to sum 1.0. A rationale line is appended for
/// every signal that fired so the decision is self-documenting in logs.
///
/// Signal strengths (weights): VeryStrong=3, Strong=2, Medium=1, Weak=0.5.
/// Values hardcoded; tune via source edit + rebuild.
///
/// Archetype set (exactly five; map to objective types in the prompt):
///   combat  → ClearPoi (preferred) or KillEnemies
///   gather  → CollectItemTypes (Ore / RefinedProduct)
///   salvage → CollectItemTypes (Salvage / Junk)
///   deliver → TriggerObjective (travel) or CollectItemTypes (TradeGoods)
///   escort  → ProtectUnit + TriggerObjective travel</summary>
internal static class MissionGuidanceBuilder
{
    public const string Combat  = "combat";
    public const string Gather  = "gather";
    public const string Salvage = "salvage";
    public const string Deliver = "deliver";
    public const string Escort  = "escort";

    private const double VeryStrong = 3.0;
    private const double Strong     = 2.0;
    private const double Medium     = 1.0;
    private const double Weak       = 0.5;

    public static LlmMissionGuidance Build(LlmContext ctx)
    {
        var raw = new Dictionary<string, double>
        {
            [Combat]  = 0.0,
            [Gather]  = 0.0,
            [Salvage] = 0.0,
            [Deliver] = 0.0,
            [Escort]  = 0.0,
        };
        var rationale = new List<string>();

        ApplyHardpointSignals    (ctx, raw, rationale);
        ApplySpecializationSignals(ctx, raw, rationale);
        ApplyTitleSignals        (ctx, raw, rationale);
        ApplyLadderSignals       (ctx, raw, rationale);
        ApplyActiveMissionSignals(ctx, raw, rationale);
        ApplyFacilitySignals     (ctx, raw, rationale);
        ApplyShipStateSignals    (ctx, raw, rationale);
        ApplyFactionSignals      (ctx, raw, rationale);

        var forbidden = DetermineForbidden(ctx, rationale);
        foreach (var f in forbidden) raw[f] = 0.0;

        var normalized = Normalize(raw, forbidden);

        // Rank high-to-low so JSON preservation of insertion order gives the
        // LLM a top-ranked entry at the head of the dict.
        var ranked = normalized
            .OrderByDescending(kv => kv.Value)
            .ThenBy(kv => kv.Key)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

        return new LlmMissionGuidance
        {
            ArchetypeWeights    = ranked,
            ForbiddenArchetypes = forbidden,
            Rationale           = rationale,
        };
    }

    // ---- signal functions ----

    /// <summary>Combat/mining/salvage capability signal read from the primary
    /// ship's currently-mounted hardpoints via the game's own
    /// <c>SpaceShipData.HasLoadout</c> check. This is the authoritative "is
    /// this ship equipped to do X right now" signal — honest even when a
    /// nominally-combat hull has mining tools mounted (or vice versa).
    /// Replaces the old cargo-ammo heuristic, which was unreliable (ammo
    /// stockpiling, empty-after-fight, trading ammo as freight).</summary>
    private static void ApplyHardpointSignals(LlmContext ctx, Dictionary<string, double> raw, List<string> rationale)
    {
        var ship = ctx.Fleet?.PrimaryShip;
        if (ship == null) return;
        if (ship.HasCombatLoadout)
        {
            raw[Combat] += VeryStrong;
            rationale.Add("primary ship has combat hardpoints mounted → combat");
        }
        if (ship.HasMiningLoadout)
        {
            raw[Gather] += VeryStrong;
            rationale.Add("primary ship has mining hardpoints mounted → gather");
        }
        if (ship.HasSalvageLoadout)
        {
            raw[Salvage] += VeryStrong;
            rationale.Add("primary ship has salvage hardpoints mounted → salvage");
        }
    }

    private static void ApplySpecializationSignals(LlmContext ctx, Dictionary<string, double> raw, List<string> rationale)
    {
        var spec = ctx.Player?.Specialization ?? string.Empty;
        switch (spec)
        {
            case "Mining":
                raw[Gather] += Strong;
                rationale.Add("specialization=Mining → gather");
                break;
            case "Salvaging":
                raw[Salvage] += Strong;
                rationale.Add("specialization=Salvaging → salvage");
                break;
            case "Offense":
            case "Drones":
                raw[Combat] += Strong;
                rationale.Add($"specialization={spec} → combat");
                break;
            case "Defense":
                raw[Combat] += Medium;
                raw[Escort] += Strong;
                rationale.Add("specialization=Defense → escort (strong) / combat (medium)");
                break;
            case "Industrial":
                raw[Gather]  += Medium;
                raw[Deliver] += Medium;
                rationale.Add("specialization=Industrial → gather + deliver");
                break;
            case "Economy":
                raw[Deliver] += Strong;
                rationale.Add("specialization=Economy → deliver");
                break;
            case "Engineering":
                // Crafting / production role — gathers raw materials to refine
                // into finished goods, then ships the output. Same archetype
                // shape as Industrial (both are production-chain specs).
                raw[Gather]  += Medium;
                raw[Deliver] += Medium;
                rationale.Add("specialization=Engineering → gather + deliver (production chain)");
                break;
            case "Leadership":
                // Captain-class generalist — fleet command leans toward combat
                // and escort; generic enough that the lean is weak.
                raw[Combat] += Weak;
                raw[Escort] += Weak;
                rationale.Add("specialization=Leadership → combat + escort (weak)");
                break;
        }
    }

    private static void ApplyTitleSignals(LlmContext ctx, Dictionary<string, double> raw, List<string> rationale)
    {
        if (ctx.Player?.UnlockedTitles == null) return;
        foreach (var title in ctx.Player.UnlockedTitles)
        {
            switch (title)
            {
                case "navycaptain":
                case "bountyhunter":
                case "soldier":
                    raw[Combat] += Strong;
                    rationale.Add($"title {title} → combat");
                    break;
                case "miner":
                    raw[Gather] += Strong;
                    rationale.Add("title miner → gather");
                    break;
                case "merchant":
                case "trader":
                    raw[Deliver] += Strong;
                    rationale.Add($"title {title} → deliver");
                    break;
                // Unknown titles: no signal. Easy to extend later.
            }
        }
    }

    private static void ApplyLadderSignals(LlmContext ctx, Dictionary<string, double> raw, List<string> rationale)
    {
        var p = ctx.Player;
        if (p == null) return;
        if (p.BountyRank > 0)
        {
            raw[Combat] += Strong;
            rationale.Add($"bounty_rank={p.BountyRank} → combat");
        }
        if (p.PatrolRank > 0)
        {
            raw[Combat] += Strong;
            rationale.Add($"patrol_rank={p.PatrolRank} → combat");
        }
        if (p.IndustryRank > 0)
        {
            raw[Gather]  += Medium;
            raw[Deliver] += Medium;
            rationale.Add($"industry_rank={p.IndustryRank} → gather + deliver");
        }
        if (p.MaxBountyLevel > 0 && p.BountyRank == 0)
        {
            raw[Combat] += Weak;
            rationale.Add($"max_bounty_level={p.MaxBountyLevel} (history) → combat (weak)");
        }
    }

    private static void ApplyActiveMissionSignals(LlmContext ctx, Dictionary<string, double> raw, List<string> rationale)
    {
        if (ctx.Missions?.ActiveStoryIds == null) return;
        foreach (var id in ctx.Missions.ActiveStoryIds)
        {
            var lower = id.ToLowerInvariant();
            if (lower.Contains("bounty") || lower.Contains("patrol") ||
                lower.Contains("combat") || lower.Contains("defense"))
            {
                raw[Combat] += Strong;
                rationale.Add($"active mission {id} → combat");
            }
            else if (lower.Contains("mining") || lower.Contains("industrial"))
            {
                raw[Gather] += Strong;
                rationale.Add($"active mission {id} → gather");
            }
            else if (lower.Contains("salvage"))
            {
                raw[Salvage] += Strong;
                rationale.Add($"active mission {id} → salvage");
            }
        }
    }

    private static void ApplyFacilitySignals(LlmContext ctx, Dictionary<string, double> raw, List<string> rationale)
    {
        var facilities = ctx.Location?.StationFacilities;
        if (facilities == null) return;
        var set = new HashSet<string>(facilities);
        if (set.Contains("Refinery") && set.Contains("Forge"))
        {
            raw[Gather] += Strong;
            rationale.Add("station facilities Refinery+Forge → gather");
        }
        if (set.Contains("SalvageWorkshop"))
        {
            raw[Salvage] += Strong;
            rationale.Add("station facility SalvageWorkshop → salvage");
        }
        if (set.Contains("Shipyard"))
        {
            raw[Deliver] += Weak;
            rationale.Add("station facility Shipyard → deliver (weak)");
        }
    }

    private static void ApplyShipStateSignals(LlmContext ctx, Dictionary<string, double> raw, List<string> rationale)
    {
        var ship = ctx.Fleet?.PrimaryShip;
        if (ship == null) return;
        if (ship.HullPct < 70 || ship.ShieldPct < 50)
        {
            raw[Combat] += Medium;
            rationale.Add($"ship damaged (hull={ship.HullPct}%, shield={ship.ShieldPct}%) → combat (recent action)");
        }
        if (ship.CargoUsedPct >= 60)
        {
            raw[Deliver] += Medium;
            rationale.Add($"cargo_used_pct={ship.CargoUsedPct} (mostly full) → deliver");
        }
    }

    private static void ApplyFactionSignals(LlmContext ctx, Dictionary<string, double> raw, List<string> rationale)
    {
        if (ctx.Factions == null) return;
        var hostile = ctx.Factions.Count(kv => kv.Value.Relation == "hostile");
        if (hostile > 0)
        {
            raw[Combat] += Weak;
            rationale.Add($"{hostile} hostile faction(s) available → combat enabled (weak)");
        }

        // Hostile-neighbor signal: any connected system owned by a hostile faction.
        var neighbors = ctx.Location?.ConnectedSystems;
        if (neighbors != null)
        {
            foreach (var sys in neighbors)
            {
                if (sys.Faction != null &&
                    ctx.Factions.TryGetValue(sys.Faction, out var entry) &&
                    entry.Relation == "hostile")
                {
                    raw[Combat] += Medium;
                    raw[Escort] += Medium;
                    rationale.Add(
                        $"connected system {sys.Name} owned by hostile {sys.Faction} → combat + escort");
                    break;   // one nudge is enough; don't double-dip across all neighbors
                }
            }
        }
    }

    private static IReadOnlyList<string> DetermineForbidden(LlmContext ctx, List<string> rationale)
    {
        var forbidden = new List<string>();
        if (ctx.Factions == null || !ctx.Factions.Any(kv => kv.Value.Relation == "hostile"))
        {
            forbidden.Add(Combat);
            forbidden.Add(Escort);
            rationale.Add("no hostile factions → combat and escort forbidden");
        }
        return forbidden;
    }

    private static IReadOnlyDictionary<string, double> Normalize(
        IReadOnlyDictionary<string, double> raw, IReadOnlyList<string> forbidden)
    {
        var total = raw.Values.Sum();
        var result = new Dictionary<string, double>(raw.Count);
        if (total <= 0)
        {
            // No signals fired — distribute evenly across the non-forbidden set.
            // A forbidden archetype stays at 0 even in the fallback.
            var forbiddenSet = new HashSet<string>(forbidden);
            var allowedCount = raw.Keys.Count(k => !forbiddenSet.Contains(k));
            if (allowedCount == 0)
            {
                foreach (var key in raw.Keys) result[key] = 0.0;
                return result;
            }
            var share = Round2(1.0 / allowedCount);
            foreach (var key in raw.Keys)
                result[key] = forbiddenSet.Contains(key) ? 0.0 : share;
            return result;
        }
        foreach (var kv in raw) result[kv.Key] = Round2(kv.Value / total);
        return result;
    }

    private static double Round2(double v) => System.Math.Round(v, 2);

    // Historical note: cargo-item classification was tried and removed
    // 2026-04-21. A broker NPC can't see into a player's hold any more than
    // they can see a bank balance; the inferred "mining tool" / "ore output"
    // / "trade goods" signals were narratively wrong AND technically fragile
    // (pattern-matching on @-prefixed display-name strings with no
    // canonical prefix convention — only @OreCommon* was evidence-backed).
    // Capability signals (hardpoints, specialization, titles, active
    // missions, station facilities) carry the archetype decision honestly.
    // Future: a proper station-interaction history (refinery use, workshop
    // use, trade-terminal use) would be observable to a broker — out of
    // scope for this milestone.
}
