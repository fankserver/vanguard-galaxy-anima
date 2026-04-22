using System;
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

namespace VGAnima.Missions;

/// <summary>Translates a validated <see cref="LlmMissionBlock"/> into a live
/// <see cref="Mission"/> instance. Called on the Unity main thread from the
/// factory delegate the plugin registers via <c>StoryMission.Add</c> at
/// broker-injection time.
///
/// Faction identifiers and item categories are resolved via vanilla APIs
/// (<c>Faction.Get</c>, <c>Enum.Parse&lt;ItemCategory&gt;</c>). The Task 2
/// whitelists have already filtered the values, so these calls always
/// succeed under normal operation — any failure here (e.g. game updated the
/// faction registry) surfaces as an exception caught by the caller's
/// try/catch in <see cref="VGAnima.Patches.BarRefreshPatches"/>.
///
/// Spec §6.</summary>
internal static class MissionFactoryFromJson
{
    /// <summary>Builds the Mission from a validated block. All decomp-typed
    /// touches happen here; tests call this directly (skipping the caller's
    /// <c>StoryMission.Add</c> path) and assert on the returned Mission's
    /// field values.</summary>
    /// <param name="missionLevel">Area level the reward math anchors to —
    /// read from <c>brokerStation.level</c> in production. Matches vanilla's
    /// <c>MissionGenerator</c> path (procedural board missions), not
    /// <c>SideMissions</c> (story loops using player level). Passing area
    /// level activates the built-in XP over-level penalty in
    /// <c>GameMath.GetExperienceRewardValue</c>, which zeros out XP for
    /// players &gt;3 levels above the station.</param>
    /// <param name="brokerStation">May be null in unit tests — production
    /// always passes the SpaceStation the broker was injected at.</param>
    public static Mission Build(
        LlmMissionBlock block,
        int missionLevel,
        SpaceStation? brokerStation,
        string brokerSeed)
    {
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
            // Area-anchored, not player-anchored. dynamicLevel=true would make
            // Mission.level return GamePlayer.current.level on every access;
            // we want the station's level so damage/loot/etc. also stay put.
            dynamicLevel    = false,
            storyId         = BuildStoryId(brokerStation, brokerSeed),
        };

        foreach (var stepBlock in block.Steps)
        {
            var step = new MissionStep();
            if (brokerStation != null) step.system = brokerStation.system;
            foreach (var objBlock in stepBlock.Objectives)
                step.objectives.Add(BuildObjective(
                    objBlock, step, brokerStation, missionLevel,
                    sourceFaction: mission.sourceFaction));
            mission.steps.Add(step);
        }

        foreach (var rewardBlock in block.Rewards)
        {
            var reward = BuildReward(rewardBlock, missionLevel, brokerStation);
            // BuildReward returns null if an item reward couldn't be
            // constructed (e.g. null station in tests). Skip null instead
            // of crashing — the rest of the mission stays valid.
            if (reward is null) continue;
            mission.rewards.Add(reward);
            LogRewardResolution(rewardBlock, reward, missionLevel);
        }

        return mission;
    }

    /// <summary>Emits a LogDebug line showing the LLM's base_value input and
    /// the resolved amount GameMath produced, plus the missionLevel fed into
    /// the formula. Gives the operator the exact numbers to spot-check
    /// anomalies (e.g. 16k XP on a "level 1" mission → actual missionLevel
    /// was not 1).</summary>
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
            // Logging must never throw — swallowed. Under unit tests Plugin.Log
            // may be null (Plugin isn't constructed outside BepInEx runtime).
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

    /// <summary>Builds a vanilla <see cref="MissionObjective"/> from the
    /// LLM block. Some cases mutate <paramref name="step"/> as a side effect
    /// — specifically <see cref="LlmClearPoi"/> spawns a <c>Combat</c> POI
    /// in the broker-station's system and pins it to the step via
    /// <c>dynamicPointOfInterest</c>. That's the vanilla pattern
    /// (<see href="BountyHunt.GenerateMission"/>) for "fly to a spot on the
    /// map and clear it" missions.</summary>
    private static MissionObjective BuildObjective(
        LlmObjective block, MissionStep step, SpaceStation? brokerStation, int missionLevel,
        Faction sourceFaction)
    {
        switch (block)
        {
            case LlmKillEnemies k:
                return new KillEnemies
                {
                    enemyFaction   = Faction.Get(k.EnemyFaction),
                    requiredAmount = k.RequiredAmount,
                    // KillEnemies has no `description` field — vanilla composes
                    // statusText from a translation key. We surface the LLM's
                    // description at mission.description level instead (already
                    // copied above). Drop k.Description here intentionally.
                };

            case LlmProtectUnit p:
                return new ProtectUnit
                {
                    protectText    = p.ProtectText,
                    requiredAmount = 1,  // ProtectUnit inherits from TriggerObjective;
                                         // default requiredAmount is 1.
                };

            case LlmTriggerObjective t:
                return new TriggerObjective
                {
                    trigger        = Enum.Parse<MissionTrigger>(t.Trigger),
                    requiredAmount = t.RequiredAmount,
                    description    = t.Description,
                };

            case LlmCollectItemTypes c:
                return BuildCollectItemTypes(c, step, brokerStation, sourceFaction, missionLevel);

            case LlmClearPoi cp:
                return BuildClearPoi(cp, step, brokerStation, missionLevel);

            default:
                throw new InvalidOperationException(
                    $"unknown validated objective type {block.GetType().Name}");
        }
    }

    /// <summary>Mirrors vanilla <c>BountyHunt.GenerateMission</c>: spawns a
    /// <c>Combat</c> POI in the broker-station's system, seeds it with a
    /// combat-ship payload, pins it to the step, and returns a
    /// <c>KillEnemies</c> whose <c>requiredAmount</c> is the spawn's
    /// <c>totalUnitCount</c>. The player sees a new icon on the system map,
    /// flies there, and the step auto-completes when the area is clear.</summary>
    /// <summary>Builds a <see cref="CollectItemTypes"/> objective. For
    /// categories with a natural POI home (Ore → Mining field, Salvage →
    /// derelict fleet), also spawns the corresponding POI in the broker's
    /// system and pins it to the step so the player gets a map waypoint.
    /// RefinedProduct and TradeGoods don't get a POI — those are acquired
    /// through refineries / traders, not spawned locations.
    ///
    /// <para>The POI type is derived from <see cref="LlmCollectItemTypes.ItemCategory"/>
    /// rather than let the LLM pick — we've been applying "LLM for
    /// narrative, code for mechanics" consistently (see ClearPoi +
    /// MissionGuidance). One mapping table, one place to extend when
    /// vanilla adds new POI flavors. The <see cref="ItemCategoryWhitelist"/>
    /// already filters out categories without a POI home.</para>
    ///
    /// <para>Faction for the spawned POI is the <b>broker's declared employer</b>
    /// — vanilla's <c>mission.sourceFaction</c>, which the LLM emitted as
    /// <c>source_faction</c>. NOT the broker's physical station faction:
    /// a SalvageGuild broker is sometimes sitting in a MiningGuild bar,
    /// and their salvage claim should read "SalvageGuild" even then.
    /// The station just happens to be where they're pitching work; the
    /// mission's "who owns this site" question is answered by who's
    /// paying.</para>
    ///
    /// <para>Mining POI hazards default to 0 (no anomalies); Salvage POI
    /// hazard chance defaults to vanilla's 0.5.</para></summary>
    private static MissionObjective BuildCollectItemTypes(
        LlmCollectItemTypes block, MissionStep step, SpaceStation? brokerStation,
        Faction sourceFaction, int missionLevel)
    {
        var category = Enum.Parse<ItemCategory>(block.ItemCategory);

        if (brokerStation != null)
        {
            MapPointOfInterest? poi = null;
            switch (category)
            {
                case ItemCategory.Ore:
                    // Spawn an asteroid field the player can go mine. Default
                    // hazard level (0) — adversarial spawns come from vanilla's
                    // own `pirateChance` logic if enabled; keep it conservative
                    // for broker missions so "go mine X ore" doesn't secretly
                    // become a combat encounter unless the LLM explicitly
                    // requested defenders via `guards_faction` below.
                    poi = brokerStation.system.AddMiningPoi(sourceFaction);
                    step.dynamicPointOfInterest = poi;
                    break;
                case ItemCategory.Salvage:
                    // Spawn a derelict-fleet debris field. Default ship
                    // template ("AncientWreck") + vanilla's 0.5 hazard chance.
                    // Each wreck carries its own item/scrap contents scaled
                    // to the system level.
                    poi = brokerStation.system.AddDerelictFleetPoi(sourceFaction);
                    step.dynamicPointOfInterest = poi;
                    break;
                // RefinedProduct / TradeGoods: no POI — player acquires these
                // through normal trade, not by flying to a spawn. Falls
                // through with no dynamicPointOfInterest set.
            }

            // Defended site — attach combat units to the spawned POI.
            // Mirrors vanilla `SalvageWreck.GenerateMission` on Hard+ and
            // `AddMiningPoi(pirateChance: true)`: one POI, one step, guards
            // inside. The validator rejects guards_faction on categories
            // without a POI (RefinedProduct / TradeGoods), and the Ore/
            // Salvage branches above are the only ones that set `poi`,
            // so `poi != null` is sufficient to know we can attach guards.
            if (poi != null && block.GuardsFaction != null)
            {
                var guardsFaction = Faction.Get(block.GuardsFaction);
                // Same scaling curve as BuildClearPoi so defended-gather
                // encounters match the narrative weight of dedicated
                // combat sites.
                var payloadMultiplier = Math.Clamp(2f + missionLevel * 0.2f, 2f, 5f);
                poi.AddGuards(poi.CreateUnitPayload(payloadMultiplier, GameplayType.Combat, guardsFaction));
                // Tooltip hint on the map POI — matches vanilla's pattern
                // (SalvageWreck sets dangerLevel to one of several hazard
                // strings depending on difficulty).
                poi.dangerLevel = "@MapPOIDangerPirates";
            }
        }

        return new CollectItemTypes
        {
            itemCategory   = category,
            requiredAmount = block.RequiredAmount,
        };
    }

    private static MissionObjective BuildClearPoi(
        LlmClearPoi block, MissionStep step, SpaceStation? brokerStation, int missionLevel)
    {
        if (brokerStation == null)
            throw new InvalidOperationException(
                "ClearPoi requires a broker station to spawn the Combat POI into");

        var enemyFaction = Faction.Get(block.EnemyFaction);
        var combat       = brokerStation.system.AddCombat(enemyFaction);

        // Scale guard payload with mission level so a "clear the zone"
        // objective actually plays like a real engagement. A single-unit
        // spawn (the pre-fix behavior from `CreateUnitPayload(1f, ...)`)
        // melts in seconds and doesn't match the narrative weight of a
        // dedicated Combat POI on the system map.
        //
        //   L1   →  2.2  (≈2 units)
        //   L5   →  3.0  (≈3 units)
        //   L10  →  4.0  (≈4 units)
        //   L15+ →  5.0  (capped; ≈5 units)
        //
        // The clamp caps high-level stations so ClearPoi doesn't become a
        // war. Vanilla's own spawn logic then scales each unit's ship
        // level on top of this count.
        var payloadMultiplier = Math.Clamp(2f + missionLevel * 0.2f, 2f, 5f);
        combat.AddGuards(combat.CreateUnitPayload(payloadMultiplier, GameplayType.Combat));
        step.dynamicPointOfInterest = combat;

        return new KillEnemies
        {
            enemyFaction   = enemyFaction,
            requiredAmount = combat.totalUnitCount,
        };
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
    /// <see cref="ItemReward"/>. All three v1 kinds anchor to the broker
    /// station's system — mining claims / salvage claims are
    /// system-local by construction (they point at asteroid fields or
    /// derelict fleets in a specific <see cref="SystemMapData"/>).
    /// Returns null when <paramref name="brokerStation"/> is unavailable
    /// (unit tests) — caller skips the reward rather than crashing.</summary>
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
    // the intent that this class DOES NOT call StoryMission.Add itself; the
    // caller wires the factory into the registry.
    private static readonly Type _keepRegistryAlias = typeof(StoryMissionRegistry);
}
