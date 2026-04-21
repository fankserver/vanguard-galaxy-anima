using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace VGAnima.Llm;

/// <summary>Validates the <c>mission</c> sub-object of a v2-mission LLM
/// response. Called by <see cref="ResponseValidator"/> after the dialogue
/// block passes. Rule ordering mirrors spec §5 exactly — first failure
/// raises <see cref="LlmValidationException"/>.
///
/// Cross-context inputs (<paramref name="atWar"/> and
/// <paramref name="reputation"/>) drive the enemy_faction coherence rule
/// (spec §3 — reject KillEnemies against a currently-friendly faction).
/// Callers (the orchestrator in Task 6) thread them through from the same
/// <see cref="LlmContext"/> that was sent to the LLM.</summary>
internal sealed class MissionBlockValidator
{
    private const int NameMaxLen           = 60;
    private const int DescriptionMaxLen    = 500;
    private const int CompletionTextMaxLen = 200;
    // Bumped from 80 → 120 after live testing: naturally-written objective
    // descriptions land at 84–100 chars regularly ("Travel to the hostile
    // signature and destroy all Corsair Syndicate ships in the area." = 84).
    // 120 matches the dialogue-line limit so there's one budget to remember.
    private const int ObjDescriptionMaxLen = 120;
    private const int ProtectTextMaxLen    = 120;

    private const int KillRequiredMin      = 1;
    private const int KillRequiredMax      = 5;
    private const int TriggerRequiredMin   = 1;
    private const int TriggerRequiredMax   = 3;
    private const int CollectRequiredMin   = 1;
    private const int CollectRequiredMax   = 50;

    // Internal so the context gatherer can surface these to the LLM prompt
    // (DRY — single source of truth for both validation and prompt clamps).
    //
    // Bounds mirror vanilla's procedural + SideMissions envelope so broker
    // rewards fit alongside vanilla's mission board. Evidence (decompile of
    // MissionGenerator.AddRewards + SideMissions, 2026-04-21):
    //   - Procedural base_value after per-objective multiplier: 20..50
    //     (MineOre=30, Courier=24, Kill/Escort/Clear=40, + 0.8..1.25 noise)
    //   - SideMissions story bases: 50 or 100 flat
    //   - Skilltree gate missions: 200 (out of scope for brokers)
    //   - Umbral/Conquest endgame: 100..20k (out of scope for brokers)
    // XP bases: Sqrt(difficulty) * 50 = 40..122 across Easy..Insane; SideMissions
    // uses 50 or 100 flat. Rep: procedural picks from {200,250,300,350};
    // SideMissions=400. Broker missions target the overlap.
    internal const int CreditsBaseMin       =  15;
    internal const int CreditsBaseMax       = 100;
    internal const int ExperienceBaseMin    =  30;
    internal const int ExperienceBaseMax    = 100;
    internal const int ReputationMin        = -500;
    internal const int ReputationMax        =  500;

    private static readonly HashSet<string> MissionKeys = new()
    {
        "name", "description", "completion_text",
        "source_faction", "steps", "rewards",
    };

    public LlmMissionBlock Parse(
        JToken mission,
        IReadOnlyList<string> atWar,
        IReadOnlyDictionary<string, int> reputation)
    {
        // Rule 1.
        if (mission == null || mission.Type != JTokenType.Object)
            throw new LlmValidationException(
                $"field `mission` must be a json object, got {(mission == null ? "null" : mission.Type.ToString())}");

        var obj = (JObject)mission;

        // Rule 2: strict field set.
        foreach (var prop in obj.Properties())
            if (!MissionKeys.Contains(prop.Name))
                throw new LlmValidationException(
                    $"unexpected field `mission.{prop.Name}` (v2-mission schema is strict)");
        foreach (var required in MissionKeys)
            if (!obj.ContainsKey(required))
                throw new LlmValidationException($"missing field `mission.{required}`");

        // Rule 3: string caps + ASCII.
        var name           = ReadString(obj, "mission.name",            NameMaxLen);
        var description    = ReadString(obj, "mission.description",     DescriptionMaxLen);
        var completionText = ReadString(obj, "mission.completion_text", CompletionTextMaxLen);

        // Rule 4: source_faction whitelist.
        var sourceFactionTok = obj["source_faction"]!;
        if (sourceFactionTok.Type != JTokenType.String)
            throw new LlmValidationException("field `mission.source_faction` must be a string");
        var sourceFaction = sourceFactionTok.Value<string>() ?? string.Empty;
        if (!FactionWhitelist.Contains(sourceFaction))
            throw new LlmValidationException(
                $"field `mission.source_faction` must be a whitelisted faction, got \"{sourceFaction}\"");

        // Rule 5: steps array, 1..3.
        var steps = ReadSteps(obj, atWar, reputation);

        // Rule 8: rewards array, 1..5.
        var rewards = ReadRewards(obj);

        // Rule 9: global coherence — at least one non-ProtectUnit objective.
        if (!AnyNonProtectObjective(steps))
            throw new LlmValidationException(
                "mission is degenerate: every objective is ProtectUnit (need at least one "
                + "KillEnemies / TriggerObjective / CollectItemTypes)");

        return new LlmMissionBlock(
            Name:           name,
            Description:    description,
            CompletionText: completionText,
            SourceFaction:  sourceFaction,
            Steps:          steps,
            Rewards:        rewards);
    }

    private static string ReadString(JObject obj, string path, int maxLen)
    {
        var tok = obj[PathTail(path)]!;
        if (tok.Type != JTokenType.String)
            throw new LlmValidationException($"field `{path}` must be a string, got {tok.Type}");
        var s = tok.Value<string>() ?? string.Empty;
        if (s.Trim().Length == 0)
            throw new LlmValidationException($"field `{path}` must be non-empty after trim");
        if (s.Length > maxLen)
            throw new LlmValidationException($"field `{path}` exceeds max length {maxLen} (got {s.Length})");
        for (var i = 0; i < s.Length; i++)
            if (s[i] >= 128)
                throw new LlmValidationException(
                    $"field `{path}` has non-ascii char U+{(int)s[i]:X4} at offset {i}");
        return s;
    }

    private static string PathTail(string path)
    {
        var dot = path.LastIndexOf('.');
        return dot < 0 ? path : path.Substring(dot + 1);
    }

    private IReadOnlyList<LlmMissionStep> ReadSteps(
        JObject obj,
        IReadOnlyList<string> atWar,
        IReadOnlyDictionary<string, int> reputation)
    {
        var stepsTok = obj["steps"]!;
        if (stepsTok.Type != JTokenType.Array)
            throw new LlmValidationException("field `mission.steps` must be an array");
        var stepsArr = (JArray)stepsTok;
        if (stepsArr.Count < 1 || stepsArr.Count > 3)
            throw new LlmValidationException(
                $"field `mission.steps` must have 1..3 elements, got {stepsArr.Count}");

        var steps = new List<LlmMissionStep>(stepsArr.Count);
        for (var i = 0; i < stepsArr.Count; i++)
        {
            var step = stepsArr[i];
            if (step.Type != JTokenType.Object)
                throw new LlmValidationException(
                    $"field `mission.steps[{i}]` must be an object");
            var stepObj = (JObject)step;
            foreach (var prop in stepObj.Properties())
                if (prop.Name != "objectives")
                    throw new LlmValidationException(
                        $"unexpected field `mission.steps[{i}].{prop.Name}`");
            if (!stepObj.ContainsKey("objectives"))
                throw new LlmValidationException(
                    $"missing field `mission.steps[{i}].objectives`");

            var objsTok = stepObj["objectives"]!;
            if (objsTok.Type != JTokenType.Array)
                throw new LlmValidationException(
                    $"field `mission.steps[{i}].objectives` must be an array");
            var objsArr = (JArray)objsTok;
            if (objsArr.Count < 1 || objsArr.Count > 2)
                throw new LlmValidationException(
                    $"field `mission.steps[{i}].objectives` must have 1..2 elements, got {objsArr.Count}");

            var objectives = new List<LlmObjective>(objsArr.Count);
            for (var j = 0; j < objsArr.Count; j++)
                objectives.Add(ParseObjective(objsArr[j], i, j, atWar, reputation));

            // Within a step, at most one combat objective. ClearPoi and
            // KillEnemies use parallel tracking (POI-bound vs faction-wide),
            // mixing them produces a "main mission done + loose stragglers"
            // shape that's confusing for players. The prompt discourages it;
            // this rule enforces it.
            var combatCount = objectives.Count(o => o is LlmClearPoi or LlmKillEnemies);
            if (combatCount > 1)
                throw new LlmValidationException(
                    $"field `mission.steps[{i}].objectives` has {combatCount} combat objectives; " +
                    $"at most one of ClearPoi or KillEnemies per step");

            steps.Add(new LlmMissionStep(objectives));
        }
        return steps;
    }

    private LlmObjective ParseObjective(
        JToken tok, int stepIdx, int objIdx,
        IReadOnlyList<string> atWar,
        IReadOnlyDictionary<string, int> reputation)
    {
        var path = $"mission.steps[{stepIdx}].objectives[{objIdx}]";
        if (tok.Type != JTokenType.Object)
            throw new LlmValidationException($"field `{path}` must be an object");
        var obj = (JObject)tok;

        var typeTok = obj["type"];
        if (typeTok == null || typeTok.Type != JTokenType.String)
            throw new LlmValidationException($"field `{path}.type` missing or not a string");
        var type = typeTok.Value<string>() ?? string.Empty;
        if (!ObjectiveTypeWhitelist.Contains(type))
            throw new LlmValidationException(
                $"field `{path}.type` must be a whitelisted objective type, got \"{type}\"");

        return type switch
        {
            "KillEnemies"      => ParseKillEnemies(obj, path, atWar, reputation),
            "ProtectUnit"      => ParseProtectUnit(obj, path),
            "TriggerObjective" => ParseTriggerObjective(obj, path),
            "CollectItemTypes" => ParseCollectItemTypes(obj, path),
            "ClearPoi"         => ParseClearPoi(obj, path, atWar, reputation),
            _                  => throw new LlmValidationException(
                                      $"unreachable: whitelist passed but switch missed \"{type}\""),
        };
    }

    private LlmObjective ParseKillEnemies(
        JObject obj, string path,
        IReadOnlyList<string> atWar,
        IReadOnlyDictionary<string, int> reputation)
    {
        RequireStrictKeys(obj, path, "type", "enemy_faction", "required_amount", "description");

        var faction = obj["enemy_faction"]!.Value<string>() ?? string.Empty;
        if (!FactionWhitelist.Contains(faction))
            throw new LlmValidationException(
                $"field `{path}.enemy_faction` must be a whitelisted faction, got \"{faction}\"");

        // Vanilla FactionData.IsEnemy: hostile iff at_war OR rep < -500.
        // Mirror that here — reject KillEnemies against anything not
        // sufficiently hostile (friendly OR neutral). Unknown rep defaults
        // to 0 (treated as neutral, not hostile).
        var isAtWar = atWar != null && atWar.Contains(faction);
        var rep     = 0;
        reputation?.TryGetValue(faction, out rep);
        if (!isAtWar && rep >= -500)
            throw new LlmValidationException(
                $"field `{path}.enemy_faction` is not hostile to player " +
                $"(rep={rep}, at_war=false); refusing kill mission");

        var required = ReadInt(obj, $"{path}.required_amount", KillRequiredMin, KillRequiredMax);
        var desc     = ReadObjectiveDescription(obj, $"{path}.description");
        return new LlmKillEnemies(faction, required, desc);
    }

    private LlmObjective ParseProtectUnit(JObject obj, string path)
    {
        RequireStrictKeys(obj, path, "type", "protect_text");
        var text = obj["protect_text"]!;
        if (text.Type != JTokenType.String)
            throw new LlmValidationException($"field `{path}.protect_text` must be a string");
        var s = text.Value<string>() ?? string.Empty;
        if (s.Trim().Length == 0)
            throw new LlmValidationException($"field `{path}.protect_text` must be non-empty");
        if (s.Length > ProtectTextMaxLen)
            throw new LlmValidationException(
                $"field `{path}.protect_text` exceeds max length {ProtectTextMaxLen}");
        for (var i = 0; i < s.Length; i++)
            if (s[i] >= 128)
                throw new LlmValidationException(
                    $"field `{path}.protect_text` has non-ascii char U+{(int)s[i]:X4}");
        return new LlmProtectUnit(s);
    }

    private LlmObjective ParseTriggerObjective(JObject obj, string path)
    {
        RequireStrictKeys(obj, path, "type", "trigger", "required_amount", "description");
        var trigTok = obj["trigger"]!;
        if (trigTok.Type != JTokenType.String)
            throw new LlmValidationException($"field `{path}.trigger` must be a string");
        var trigger = trigTok.Value<string>() ?? string.Empty;
        if (!TriggerWhitelist.Contains(trigger))
            throw new LlmValidationException(
                $"field `{path}.trigger` must be a whitelisted trigger, got \"{trigger}\"");
        var required = ReadInt(obj, $"{path}.required_amount", TriggerRequiredMin, TriggerRequiredMax);
        var desc     = ReadObjectiveDescription(obj, $"{path}.description");
        return new LlmTriggerObjective(trigger, required, desc);
    }

    private LlmObjective ParseCollectItemTypes(JObject obj, string path)
    {
        RequireStrictKeys(obj, path, "type", "item_category", "required_amount", "description");
        var catTok = obj["item_category"]!;
        if (catTok.Type != JTokenType.String)
            throw new LlmValidationException($"field `{path}.item_category` must be a string");
        var cat = catTok.Value<string>() ?? string.Empty;
        if (!ItemCategoryWhitelist.Contains(cat))
            throw new LlmValidationException(
                $"field `{path}.item_category` must be a whitelisted category, got \"{cat}\"");
        var required = ReadInt(obj, $"{path}.required_amount", CollectRequiredMin, CollectRequiredMax);
        var desc     = ReadObjectiveDescription(obj, $"{path}.description");
        return new LlmCollectItemTypes(cat, required, desc);
    }

    private LlmObjective ParseClearPoi(
        JObject obj, string path,
        IReadOnlyList<string> atWar,
        IReadOnlyDictionary<string, int> reputation)
    {
        RequireStrictKeys(obj, path, "type", "enemy_faction", "description");

        var faction = obj["enemy_faction"]!.Value<string>() ?? string.Empty;
        if (!FactionWhitelist.Contains(faction))
            throw new LlmValidationException(
                $"field `{path}.enemy_faction` must be a whitelisted faction, got \"{faction}\"");

        // Same hostility rule as KillEnemies — vanilla FactionData.IsEnemy:
        // hostile iff at_war OR rep < -500.
        var isAtWar = atWar != null && atWar.Contains(faction);
        var rep     = 0;
        reputation?.TryGetValue(faction, out rep);
        if (!isAtWar && rep >= -500)
            throw new LlmValidationException(
                $"field `{path}.enemy_faction` is not hostile to player " +
                $"(rep={rep}, at_war=false); refusing clear-POI mission");

        var desc = ReadObjectiveDescription(obj, $"{path}.description");
        return new LlmClearPoi(faction, desc);
    }

    private IReadOnlyList<LlmReward> ReadRewards(JObject obj)
    {
        var tok = obj["rewards"]!;
        if (tok.Type != JTokenType.Array)
            throw new LlmValidationException("field `mission.rewards` must be an array");
        var arr = (JArray)tok;
        if (arr.Count < 1 || arr.Count > 5)
            throw new LlmValidationException(
                $"field `mission.rewards` must have 1..5 elements, got {arr.Count}");

        var rewards = new List<LlmReward>(arr.Count);
        for (var i = 0; i < arr.Count; i++)
            rewards.Add(ParseReward(arr[i], i));
        return rewards;
    }

    private LlmReward ParseReward(JToken tok, int idx)
    {
        var path = $"mission.rewards[{idx}]";
        if (tok.Type != JTokenType.Object)
            throw new LlmValidationException($"field `{path}` must be an object");
        var obj = (JObject)tok;

        var typeTok = obj["type"];
        if (typeTok == null || typeTok.Type != JTokenType.String)
            throw new LlmValidationException($"field `{path}.type` missing or not a string");
        var type = typeTok.Value<string>() ?? string.Empty;
        if (!RewardTypeWhitelist.Contains(type))
            throw new LlmValidationException(
                $"field `{path}.type` must be a whitelisted reward type, got \"{type}\"");

        return type switch
        {
            "Credits"    => ParseCreditsReward(obj, path),
            "Experience" => ParseExperienceReward(obj, path),
            "Reputation" => ParseReputationReward(obj, path),
            _            => throw new LlmValidationException(
                                $"unreachable: whitelist passed but switch missed \"{type}\""),
        };
    }

    private LlmReward ParseCreditsReward(JObject obj, string path)
    {
        RequireStrictKeys(obj, path, "type", "base_value");
        var v = ReadInt(obj, $"{path}.base_value", CreditsBaseMin, CreditsBaseMax);
        return new LlmCreditsReward(v);
    }

    private LlmReward ParseExperienceReward(JObject obj, string path)
    {
        RequireStrictKeys(obj, path, "type", "base_value");
        var v = ReadInt(obj, $"{path}.base_value", ExperienceBaseMin, ExperienceBaseMax);
        return new LlmExperienceReward(v);
    }

    private LlmReward ParseReputationReward(JObject obj, string path)
    {
        RequireStrictKeys(obj, path, "type", "faction", "amount");
        var factionTok = obj["faction"]!;
        if (factionTok.Type != JTokenType.String)
            throw new LlmValidationException($"field `{path}.faction` must be a string");
        var faction = factionTok.Value<string>() ?? string.Empty;
        if (!FactionWhitelist.Contains(faction))
            throw new LlmValidationException(
                $"field `{path}.faction` must be a whitelisted faction, got \"{faction}\"");
        var amount = ReadInt(obj, $"{path}.amount", ReputationMin, ReputationMax);
        return new LlmReputationReward(faction, amount);
    }

    private static string ReadObjectiveDescription(JObject obj, string path)
    {
        var tok = obj["description"]!;
        if (tok.Type != JTokenType.String)
            throw new LlmValidationException($"field `{path}` must be a string");
        var s = tok.Value<string>() ?? string.Empty;
        if (s.Trim().Length == 0)
            throw new LlmValidationException($"field `{path}` must be non-empty");
        if (s.Length > ObjDescriptionMaxLen)
            throw new LlmValidationException(
                $"field `{path}` exceeds max length {ObjDescriptionMaxLen} (got {s.Length})");
        for (var i = 0; i < s.Length; i++)
            if (s[i] >= 128)
                throw new LlmValidationException(
                    $"field `{path}` has non-ascii char U+{(int)s[i]:X4}");
        return s;
    }

    private static int ReadInt(JObject obj, string path, int min, int max)
    {
        var tail = PathTail(path);
        var tok = obj[tail]!;
        if (tok.Type != JTokenType.Integer)
            throw new LlmValidationException($"field `{path}` must be an integer");
        var v = tok.Value<int>();
        if (v < min || v > max)
            throw new LlmValidationException($"field `{path}` must be in [{min}..{max}], got {v}");
        return v;
    }

    private static void RequireStrictKeys(JObject obj, string path, params string[] allowed)
    {
        var set = new HashSet<string>(allowed);
        foreach (var prop in obj.Properties())
            if (!set.Contains(prop.Name))
                throw new LlmValidationException(
                    $"unexpected field `{path}.{prop.Name}`");
        foreach (var key in allowed)
            if (!obj.ContainsKey(key))
                throw new LlmValidationException($"missing field `{path}.{key}`");
    }

    private static bool AnyNonProtectObjective(IReadOnlyList<LlmMissionStep> steps)
    {
        foreach (var step in steps)
            foreach (var obj in step.Objectives)
                if (obj is not LlmProtectUnit)
                    return true;
        return false;
    }
}
