using System;
using System.Collections.Generic;
using System.Linq;
using Behaviour.Item;
using Behaviour.Item.Builder;
using Source.Galaxy;
using Source.Galaxy.POI;
using Source.Item;
using Source.MissionSystem;
using Source.MissionSystem.Objectives;
using Source.MissionSystem.Rewards;
using Source.Util;
using VGAnima.Llm;
// Disambiguate between the reward type and the registry type sharing the
// short name "StoryMission".
using StoryMissionRegistry = Source.MissionSystem.StoryMission;
// `Reputation` is ambiguous — both `Source.MissionSystem.Objectives` and
// `Source.MissionSystem.Rewards` define a type by that name. Alias the
// reward variant (the one we construct below).
using ReputationReward = Source.MissionSystem.Rewards.Reputation;
// `Item` is ambiguous — there's a `Source.MissionSystem.Objectives.Item`
// AND a `Source.MissionSystem.Rewards.Item`. Alias the reward variant.
using ItemReward = Source.MissionSystem.Rewards.Item;
// `Mining` is ambiguous — `Source.Galaxy.POI.Mining` (the asteroid field
// POI) vs `Source.MissionSystem.Objectives.Mining` (the quantity-counting
// collect objective). Alias the objective variant; POIs keep the FQN.
using MiningObjective = Source.MissionSystem.Objectives.Mining;
// `Salvage` has three candidates: Source.Galaxy.POI.Salvage,
// Source.Simulation.TravelEvents.Salvage, and the objective variant.
// Alias the objective.
using SalvageObjective = Source.MissionSystem.Objectives.Salvage;

namespace VGAnima.Missions;

/// <summary>Translates a validated <see cref="LlmMissionBlock"/> into a live
/// <see cref="Mission"/> instance. Called on the Unity main thread from the
/// factory delegate the plugin registers via <c>StoryMission.Add</c> at
/// broker-injection time.
///
/// <para>v2 is intent-dispatched: each <see cref="LlmMissionStep"/> carries
/// one <see cref="LlmIntent"/>, which this factory expands into a concrete
/// <see cref="MissionStep"/> (with one or two vanilla objectives plus an
/// optional POI). The LLM never sees vanilla objective classes or POI
/// constructors — all of that is owned here. Mirrors vanilla's own
/// <c>MissionGenerator</c> subclasses (<c>BountyHunt</c>, <c>HelpMiner</c>,
/// <c>SalvageWreck</c>, etc.) — each takes a narrative shape and emits the
/// matching objective + POI combo.</para></summary>
internal static class MissionFactoryFromJson
{
    /// <summary>Builds a live <see cref="Mission"/> from a validated block.</summary>
    /// <param name="missionLevel">Area level the reward math anchors to —
    /// read from <c>brokerStation.level</c> in production. Matches vanilla's
    /// <c>MissionGenerator</c> path, not <c>SideMissions</c> (which uses
    /// player level). Area level activates the XP over-level penalty in
    /// <c>GameMath.GetExperienceRewardValue</c> that zeros out XP for
    /// players &gt;3 levels above the station.</param>
    /// <param name="brokerStation">May be null in unit tests — production
    /// always passes the SpaceStation the broker was injected at.</param>
    /// <param name="accessibleDestinations">Must be non-null whenever the
    /// block contains a <see cref="DeliverToStationIntent"/> or
    /// <see cref="HaulGoodsIntent"/>. The factory resolves each
    /// <c>DestinationShortId</c> → the station's live GUID via this list.
    /// Safe to pass empty in tests that don't exercise destination intents.</param>
    public static Mission Build(
        LlmMissionBlock block,
        int missionLevel,
        SpaceStation? brokerStation,
        string brokerSeed,
        IReadOnlyList<AccessibleDestination>? accessibleDestinations = null)
    {
        var destinations = accessibleDestinations ?? System.Array.Empty<AccessibleDestination>();

        var mission = new Mission
        {
            name            = block.Name,
            description     = block.Description,
            completionText  = block.CompletionText,
            sourceFaction   = Faction.Get(block.SourceFaction),
            sourcePoi       = brokerStation,
            turnIn          = brokerStation,
            trackedOnHud    = true,
            difficulty      = MissionDifficulty.Story,
            iconName        = "Combat",
            canBeIdled      = false,
            // Area-anchored, not player-anchored. dynamicLevel=true would
            // make Mission.level return GamePlayer.current.level on every
            // access; we want the station's level so damage/loot stay put.
            dynamicLevel    = false,
            storyId         = BuildStoryId(brokerStation, brokerSeed),
        };

        foreach (var stepBlock in block.Steps)
        {
            var step = new MissionStep();
            if (brokerStation != null) step.system = brokerStation.system;
            BuildIntent(stepBlock.Intent, step, brokerStation, mission.sourceFaction,
                        missionLevel, destinations);
            mission.steps.Add(step);
        }

        foreach (var rewardBlock in block.Rewards)
        {
            var reward = BuildReward(rewardBlock, missionLevel, brokerStation);
            if (reward is null) continue;
            mission.rewards.Add(reward);
            LogRewardResolution(rewardBlock, reward, missionLevel);
        }

        return mission;
    }

    /// <summary>Emits a LogDebug line showing the LLM's base_value input and
    /// the resolved amount GameMath produced, plus the missionLevel fed
    /// into the formula. Gives the operator the exact numbers to
    /// spot-check anomalies (e.g. XP=1 when player is &gt;3 levels above
    /// station is vanilla's over-level penalty, not a bug).</summary>
    private static void LogRewardResolution(LlmReward input, MissionReward output, int missionLevel)
    {
        try
        {
            string line = input switch
            {
                LlmCreditsReward c when output is Credits cr =>
                    $"Reward[Credits]: base_value={c.BaseValue} missionLevel={missionLevel} → amount={cr.amount}",
                LlmExperienceReward e when output is Experience ex =>
                    $"Reward[Experience]: base_value={e.BaseValue} missionLevel={missionLevel} → amount={ex.amount}",
                LlmReputationReward r when output is ReputationReward rp =>
                    $"Reward[Reputation]: faction={r.Faction} amount={rp.amount} (no scaling)",
                LlmItemReward i when output is ItemReward ir =>
                    $"Reward[Item]: kind={i.Kind} → itemIdentifier={ir.item?.itemBuilder?.identifier ?? "?"} " +
                    $"displayName=\"{ir.item?.displayName ?? "?"}\"",
                _ => $"Reward[?]: input={input.GetType().Name} output={output.GetType().Name}",
            };
            VGAnima.Plugin.Log?.LogDebug(line);
        }
        catch
        {
            // Logging must never throw. Plugin.Log may be null under unit
            // tests (Plugin isn't constructed outside BepInEx).
        }
    }

    /// <summary>Deterministic-per-broker but always-unique storyId:
    /// <c>vganima_llm_{station.guid}_{brokerSeed}_{guid}</c>. The trailing
    /// Guid.NewGuid avoids collisions across re-rolls. Station may be null
    /// in tests.</summary>
    private static string BuildStoryId(SpaceStation? station, string brokerSeed)
    {
        var stationPart = station?.guid ?? "nullstation";
        var tail        = Guid.NewGuid().ToString("N");
        return $"vganima_llm_{stationPart}_{brokerSeed}_{tail}";
    }

    /// <summary>Dispatches on intent type and mutates the step in place
    /// (appends objectives, may set <see cref="MissionStep.dynamicPointOfInterest"/>
    /// for POI-bearing intents). Each intent method owns its vanilla
    /// plumbing — POIs, payloads, guards — and attaches the result to the
    /// step.</summary>
    private static void BuildIntent(
        LlmIntent intent, MissionStep step, SpaceStation? brokerStation,
        Faction sourceFaction, int missionLevel,
        IReadOnlyList<AccessibleDestination> destinations)
    {
        switch (intent)
        {
            case ClearCombatSiteIntent ccs:
                BuildClearCombatSite(ccs, step, brokerStation, missionLevel);
                break;
            case GatherOreIntent go:
                BuildGather(go.RequiredAmount, ItemCategory.Ore, step, brokerStation,
                            sourceFaction, guardsFaction: null, missionLevel);
                break;
            case GatherSalvageIntent gs:
                BuildGather(gs.RequiredAmount, ItemCategory.Salvage, step, brokerStation,
                            sourceFaction, guardsFaction: null, missionLevel);
                break;
            case DefendedGatherOreIntent dgo:
                BuildGather(dgo.RequiredAmount, ItemCategory.Ore, step, brokerStation,
                            sourceFaction, guardsFaction: Faction.Get(dgo.GuardsFaction),
                            missionLevel);
                break;
            case DefendedGatherSalvageIntent dgs:
                BuildGather(dgs.RequiredAmount, ItemCategory.Salvage, step, brokerStation,
                            sourceFaction, guardsFaction: Faction.Get(dgs.GuardsFaction),
                            missionLevel);
                break;
            case DeliverToStationIntent dts:
                BuildDeliverToStation(dts, step, destinations);
                break;
            case HaulGoodsIntent hg:
                BuildHaulGoods(hg, step, destinations);
                break;
            default:
                throw new InvalidOperationException(
                    $"unknown validated intent {intent.GetType().Name}");
        }
    }

    // Ship-size pointsScale presets. `CreateUnitPayload`'s pointsScale
    // multiplies the POI's pointsValue (max(34, level*4)) to form each
    // unit's strength budget. Pinning minUnits==maxUnits to a fixed
    // count per call gives us explicit "N small ships" / "1 big ship"
    // composition without guessing at random rolls. Mixing sizes in one
    // wave = two CreateUnitPayload calls concatenated.
    private const float SmallScale  = 0.4f;
    private const float MediumScale = 0.9f;
    private const float BigScale    = 1.8f;

    /// <summary>Mirrors vanilla <c>BountyHunt.GenerateMission</c>: spawn a
    /// Combat POI with enemy guards in the broker's system, pin it to the
    /// step, emit a <c>KillEnemies</c> objective whose requiredAmount =
    /// the spawn's totalUnitCount. Player clears the zone; step
    /// auto-completes.
    ///
    /// <para>If <see cref="ClearCombatSiteIntent.Flavor"/> is set, the
    /// plugin composes a flavor-specific fleet (scouting / outpost /
    /// lair) with initial spawn + fast + slow reinforcement waves. The
    /// reinforcement timing exploits vanilla's ship-speed physics —
    /// smaller ships reach the player faster than larger ones — so
    /// "fast" waves are small scouts and "slow" waves are capital-class
    /// responders.</para></summary>
    private static void BuildClearCombatSite(
        ClearCombatSiteIntent intent, MissionStep step, SpaceStation? brokerStation,
        int missionLevel)
    {
        if (brokerStation == null)
            throw new InvalidOperationException(
                "clear_combat_site requires a broker station to spawn the Combat POI into");

        var enemyFaction = Faction.Get(intent.EnemyFaction);
        var combat       = brokerStation.system.AddCombat(enemyFaction);

        switch (intent.Flavor)
        {
            case CombatFlavorWhitelist.Scouting:         SpawnScoutingFleet(combat, enemyFaction);         break;
            case CombatFlavorWhitelist.Outpost:          SpawnOutpostFleet (combat, enemyFaction);         break;
            case CombatFlavorWhitelist.Lair:             SpawnLairFleet    (combat, enemyFaction);         break;
            case CombatFlavorWhitelist.Raid:             SpawnRaidFleet    (combat, enemyFaction);         break;
            case CombatFlavorWhitelist.CorneredRemnants: SpawnCorneredRemnantsFleet(combat, enemyFaction); break;
            case CombatFlavorWhitelist.Swarm:            SpawnSwarmFleet   (combat, enemyFaction);         break;
            default:                                     SpawnBalancedFleet(combat, enemyFaction, missionLevel); break;
        }

        step.dynamicPointOfInterest = combat;
        step.objectives.Add(new KillEnemies
        {
            enemyFaction   = enemyFaction,
            requiredAmount = combat.totalUnitCount,
        });
    }

    /// <summary>Default composition when the LLM didn't pick a flavor.
    /// 3-5 ships initial + 2-3 ship reinforcement wave at 25s. Matches
    /// the pre-flavor behavior so old missions feel unchanged.</summary>
    private static void SpawnBalancedFleet(MapPointOfInterest combat, Faction enemy, int missionLevel)
    {
        var scale = Math.Clamp(1.5f + missionLevel * 0.1f, 1.5f, 3f);
        combat.AddGuards(combat.CreateUnitPayload(
            pointsScale: scale, gType: GameplayType.Combat, f: enemy,
            minUnits: 3, maxUnits: 5));
        combat.AddTriggeredSpawn(
            combat.CreateUnitPayload(
                pointsScale: 1f, gType: GameplayType.Combat, f: enemy,
                minUnits: 2, maxUnits: 3),
            spawnDelay: 25f);
    }

    /// <summary>"We spotted some scouts." Initial 2 small + 1 medium = a
    /// patrol that noticed the player. Fast wave (15s) 2 small = the
    /// patrol called home. Slow wave (45s) 1 big = heavier reaction
    /// force. Engagement stays lethal but feels like a sequence of
    /// escalating sightings.</summary>
    private static void SpawnScoutingFleet(MapPointOfInterest combat, Faction enemy)
    {
        combat.AddGuards(combat.CreateUnitPayload(
            pointsScale: SmallScale, gType: GameplayType.Combat, f: enemy,
            minUnits: 2, maxUnits: 2));
        combat.AddGuards(combat.CreateUnitPayload(
            pointsScale: MediumScale, gType: GameplayType.Combat, f: enemy,
            minUnits: 1, maxUnits: 1));

        combat.AddTriggeredSpawn(
            combat.CreateUnitPayload(
                pointsScale: SmallScale, gType: GameplayType.Combat, f: enemy,
                minUnits: 2, maxUnits: 2),
            spawnDelay: 15f);
        combat.AddTriggeredSpawn(
            combat.CreateUnitPayload(
                pointsScale: BigScale, gType: GameplayType.Combat, f: enemy,
                minUnits: 1, maxUnits: 1),
            spawnDelay: 45f);
    }

    /// <summary>"They're fortifying the position." Initial 1 big + 4 small
    /// = command ship + escorts (a dug-in garrison). Fast wave (20s)
    /// 4 small = outer-perimeter patrols closing in. Slow wave (60s)
    /// 2 big = HQ response. Pulls engagement out — the player has to
    /// commit to clearing the zone.</summary>
    private static void SpawnOutpostFleet(MapPointOfInterest combat, Faction enemy)
    {
        combat.AddGuards(combat.CreateUnitPayload(
            pointsScale: BigScale, gType: GameplayType.Combat, f: enemy,
            minUnits: 1, maxUnits: 1));
        combat.AddGuards(combat.CreateUnitPayload(
            pointsScale: SmallScale, gType: GameplayType.Combat, f: enemy,
            minUnits: 4, maxUnits: 4));

        combat.AddTriggeredSpawn(
            combat.CreateUnitPayload(
                pointsScale: SmallScale, gType: GameplayType.Combat, f: enemy,
                minUnits: 4, maxUnits: 4),
            spawnDelay: 20f);
        combat.AddTriggeredSpawn(
            combat.CreateUnitPayload(
                pointsScale: BigScale, gType: GameplayType.Combat, f: enemy,
                minUnits: 2, maxUnits: 2),
            spawnDelay: 60f);
    }

    /// <summary>"A hidden Corsair lair." Initial 3 big = heavy up front,
    /// the trap was set. Fast wave (15s) 3 small = scouts posted at
    /// the perimeter racing back. Slow wave (45s) 2 big = reserves from
    /// a secondary hideout. Most dangerous flavor — player starts at
    /// the deepest end.</summary>
    private static void SpawnLairFleet(MapPointOfInterest combat, Faction enemy)
    {
        combat.AddGuards(combat.CreateUnitPayload(
            pointsScale: BigScale, gType: GameplayType.Combat, f: enemy,
            minUnits: 3, maxUnits: 3));

        combat.AddTriggeredSpawn(
            combat.CreateUnitPayload(
                pointsScale: SmallScale, gType: GameplayType.Combat, f: enemy,
                minUnits: 3, maxUnits: 3),
            spawnDelay: 15f);
        combat.AddTriggeredSpawn(
            combat.CreateUnitPayload(
                pointsScale: BigScale, gType: GameplayType.Combat, f: enemy,
                minUnits: 2, maxUnits: 2),
            spawnDelay: 45f);
    }

    /// <summary>"A Marauder warband hunting the lane." Uniform mobile
    /// pack, no command hierarchy. Initial 5 medium = the pack itself.
    /// Fast wave (+15s) 3 small = tail of the pack catching up. NO slow
    /// wave — they're nomadic, nobody to call. Shorter engagement than
    /// the static-position flavors; fleet is cohesive equals rather
    /// than commander+escorts.</summary>
    private static void SpawnRaidFleet(MapPointOfInterest combat, Faction enemy)
    {
        combat.AddGuards(combat.CreateUnitPayload(
            pointsScale: MediumScale, gType: GameplayType.Combat, f: enemy,
            minUnits: 5, maxUnits: 5));

        combat.AddTriggeredSpawn(
            combat.CreateUnitPayload(
                pointsScale: SmallScale, gType: GameplayType.Combat, f: enemy,
                minUnits: 3, maxUnits: 3),
            spawnDelay: 15f);
    }

    /// <summary>"They're cornered — finish them." Everyone they have is
    /// already here; no reinforcements possible. t=0 2 big (staggered
    /// to avoid wall-of-fire instakill). +5s 4 small = rest of their
    /// garrison. NO further waves — the "remnants" in the name is
    /// load-bearing: there's nobody left to summon. Peak threat is
    /// front-loaded; engagement trends down as the player attrits.</summary>
    private static void SpawnCorneredRemnantsFleet(MapPointOfInterest combat, Faction enemy)
    {
        // Big ships spawn immediately — as guards, they're present on
        // player arrival.
        combat.AddGuards(combat.CreateUnitPayload(
            pointsScale: BigScale, gType: GameplayType.Combat, f: enemy,
            minUnits: 2, maxUnits: 2));

        // Small ships follow 5s after arrival via triggered spawn —
        // the "stagger" qwen flagged as necessary to avoid a single
        // overwhelming wave. Narratively: the small escorts scramble
        // out to the big ships after the player arrives.
        combat.AddTriggeredSpawn(
            combat.CreateUnitPayload(
                pointsScale: SmallScale, gType: GameplayType.Combat, f: enemy,
                minUnits: 4, maxUnits: 4),
            spawnDelay: 5f);
    }

    /// <summary>"A drone swarm" / "disposable Fanatic zealots." Quantity
    /// over quality — no big ships, just a cloud of cheap threats.
    /// Initial 5 small. Fast wave (+15s) 3 small. Capped at 8 total
    /// per qwen's note about Unity pathfinding stutter on large mob
    /// counts. Individually trivial; dangerous in aggregate.</summary>
    private static void SpawnSwarmFleet(MapPointOfInterest combat, Faction enemy)
    {
        combat.AddGuards(combat.CreateUnitPayload(
            pointsScale: SmallScale, gType: GameplayType.Combat, f: enemy,
            minUnits: 5, maxUnits: 5));

        combat.AddTriggeredSpawn(
            combat.CreateUnitPayload(
                pointsScale: SmallScale, gType: GameplayType.Combat, f: enemy,
                minUnits: 3, maxUnits: 3),
            spawnDelay: 15f);
    }

    /// <summary>Unified builder for gather_ore / gather_salvage and their
    /// defended variants. Spawns the correct POI flavor (asteroid field vs
    /// derelict fleet), optionally attaches hostile guards, emits a
    /// quantity-counting gather objective for the requested amount.
    ///
    /// <para>Why <c>Mining</c> not <c>CollectItemTypes</c>: vanilla's
    /// <c>CollectItemTypes</c> is a diversity counter (HashSet of distinct
    /// identifiers), so "15 salvage" = 15 different types, which a single
    /// wreck typically can't supply. <c>Mining</c> counts
    /// <c>tractorableItemData.itemAmount</c> per pickup — proper quantity
    /// semantics. Class name is a vanilla misnomer: it works for any
    /// ItemCategory, not just ore.</para>
    ///
    /// <para>Salvage uses the <see cref="SalvageObjective"/> subclass
    /// (<c>Salvage : Mining</c>). Inherits all tracking behavior; only
    /// overrides display text and <c>LoadoutCanRetrieveItem</c>. Emitting
    /// the right class means VGMissionJournal records VGAnima-authored
    /// salvage as <c>Type="Salvage"</c> — same shape as vanilla's own
    /// <c>SalvageWreck</c> missions — so downstream consumers don't have
    /// to peek at <c>itemCategory</c> to tell salvage from ore.</para></summary>
    private static void BuildGather(
        int requiredAmount, ItemCategory category, MissionStep step,
        SpaceStation? brokerStation, Faction sourceFaction,
        Faction? guardsFaction, int missionLevel)
    {
        if (brokerStation == null)
            throw new InvalidOperationException(
                "gather intents require a broker station to spawn the resource POI into");

        MapPointOfInterest poi = category switch
        {
            ItemCategory.Ore     => brokerStation.system.AddMiningPoi(sourceFaction),
            ItemCategory.Salvage => brokerStation.system.AddDerelictFleetPoi(sourceFaction),
            _ => throw new InvalidOperationException(
                     $"BuildGather: unexpected category {category}"),
        };
        step.dynamicPointOfInterest = poi;

        if (guardsFaction != null)
        {
            // Same pointsScale curve as BuildClearCombatSite (matches
            // each unit's strength to the station level), but with a
            // lower minUnits — a defended gather site is a "fight
            // your way in to work" shape, not a standalone combat zone,
            // so 2-4 guards read as "defenders" rather than a fleet.
            var payloadMultiplier = Math.Clamp(1.5f + missionLevel * 0.1f, 1.5f, 3f);
            poi.AddGuards(poi.CreateUnitPayload(
                pointsScale: payloadMultiplier,
                gType:       GameplayType.Combat,
                f:           guardsFaction,
                minUnits:    2,
                maxUnits:    4));
            poi.dangerLevel = "@MapPOIDangerPirates";
        }

        step.objectives.Add(category == ItemCategory.Salvage
            ? (MissionObjective)new SalvageObjective
            {
                itemCategory   = category,
                requiredAmount = requiredAmount,
            }
            : new MiningObjective
            {
                itemCategory   = category,
                requiredAmount = requiredAmount,
            });
    }

    /// <summary>Single <see cref="TravelToPOI"/> pointing at a specific
    /// station GUID. Completes when the player docks there (vanilla tracks
    /// <c>lastVisitedTime</c> on the POI).</summary>
    private static void BuildDeliverToStation(
        DeliverToStationIntent intent, MissionStep step,
        IReadOnlyList<AccessibleDestination> destinations)
    {
        var dest = ResolveDestination(intent.DestinationShortId, destinations);
        step.objectives.Add(new TravelToPOI { targetPOI = dest.Guid });
    }

    /// <summary>Two objectives in one step:
    /// <see cref="MiningObjective"/>(TradeGoods) for the haul, then
    /// <see cref="TravelToPOI"/> for the drop-off. MissionStep's
    /// <c>requireAllObjectives = true</c> default means both must complete —
    /// player can't drop off without the cargo and can't complete without
    /// docking at the target.</summary>
    private static void BuildHaulGoods(
        HaulGoodsIntent intent, MissionStep step,
        IReadOnlyList<AccessibleDestination> destinations)
    {
        var dest = ResolveDestination(intent.DestinationShortId, destinations);
        step.objectives.Add(new MiningObjective
        {
            itemCategory   = ItemCategory.TradeGoods,
            requiredAmount = intent.RequiredAmount,
        });
        step.objectives.Add(new TravelToPOI { targetPOI = dest.Guid });
    }

    /// <summary>Look up the destination record by short-id. Validator has
    /// already guaranteed the id is present, so a missing entry here means
    /// an internal contract violation (factory called with a different
    /// destinations list than the validator saw) — surface as an
    /// <see cref="InvalidOperationException"/>.</summary>
    private static AccessibleDestination ResolveDestination(
        string shortId, IReadOnlyList<AccessibleDestination> destinations)
    {
        var dest = destinations.FirstOrDefault(d => d.ShortId == shortId);
        if (dest == null)
            throw new InvalidOperationException(
                $"factory contract violation: destination `{shortId}` not in the provided list " +
                $"(valid ids: {string.Join(", ", destinations.Select(d => d.ShortId))})");
        return dest;
    }

    private static MissionReward? BuildReward(
        LlmReward block, int missionLevel, SpaceStation? brokerStation)
    {
        switch (block)
        {
            case LlmCreditsReward c:
                return new Credits
                {
                    amount = GameMath.GetCreditsValue(c.BaseValue, missionLevel),
                };

            case LlmExperienceReward e:
                return new Experience
                {
                    amount = GameMath.GetExperienceRewardValue(e.BaseValue, missionLevel),
                };

            case LlmReputationReward r:
                return new ReputationReward
                {
                    faction = Faction.Get(r.Faction),
                    amount  = r.Amount,
                };

            case LlmItemReward i:
                return BuildItemReward(i, brokerStation);

            default:
                throw new InvalidOperationException(
                    $"unknown validated reward type {block.GetType().Name}");
        }
    }

    /// <summary>Converts an LLM item-reward descriptor into vanilla's
    /// <see cref="ItemReward"/>. All three kinds anchor to the broker
    /// station's system — claims point at asteroid fields or derelict
    /// fleets in a specific <see cref="SystemMapData"/>. Returns null when
    /// <paramref name="brokerStation"/> is unavailable (unit tests) —
    /// caller skips the reward rather than crashing.</summary>
    private static MissionReward? BuildItemReward(LlmItemReward block, SpaceStation? brokerStation)
    {
        var system = brokerStation?.system;
        if (system == null) return null;

        var builder = ItemBuilder.Get(block.Kind);
        if (builder == null)
            throw new InvalidOperationException(
                $"item-reward builder `{block.Kind}` not found in vanilla ItemBuilder registry");

        InventoryItemType item = block.Kind switch
        {
            ItemRewardKindWhitelist.MiningClaim         => builder.CreateMiningClaim(system),
            ItemRewardKindWhitelist.SalvageClaim        => builder.CreateSalvageClaim(system),
            ItemRewardKindWhitelist.MaterialMiningClaim => builder.CreateMaterialMiningClaim(system),
            _ => throw new InvalidOperationException(
                    $"item-reward kind `{block.Kind}` passed validator but has no factory case"),
        };
        return new ItemReward { item = item, amount = 1 };
    }

    // Suppress the "unused alias" warning — the using is kept to document
    // that this class DOES NOT call StoryMission.Add itself; the caller
    // wires the factory into the registry.
    private static readonly Type _keepRegistryAlias = typeof(StoryMissionRegistry);
}
