using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace VGAnima.Llm;

/// <summary>Validates the <c>mission</c> sub-object of an LLM response and
/// produces a typed <see cref="LlmMissionBlock"/>. Called by
/// <see cref="ResponseValidator"/> after the dialogue block passes. First
/// failure raises <see cref="LlmValidationException"/> with a specific
/// error message.
///
/// <para>v2 is intent-based: each step carries ONE intent from the closed
/// <see cref="IntentWhitelist"/>, the validator checks the intent-specific
/// field set, and the factory owns all mechanical translation. This
/// structurally eliminates the v1 bug class where LLM-authored
/// <c>KillEnemies</c> shipped without a POI — intents ARE the POI shape;
/// they can't be emitted without it.</para>
///
/// <para>Cross-context inputs thread through:
/// <list type="bullet">
///   <item><paramref name="atWar"/> + <paramref name="reputation"/> —
///     hostility check for enemy_faction / guards_faction. Mirrors
///     vanilla <c>FactionData.IsEnemy</c>: hostile iff at_war OR rep &lt; -500.</item>
///   <item><paramref name="forbiddenArchetypes"/> — mechanical enforcement
///     of <c>mission_guidance.forbidden_archetypes</c>. An intent is
///     rejected if any of its archetypes (see
///     <see cref="IntentWhitelist.Archetypes"/>) is forbidden.</item>
///   <item><paramref name="accessibleDestinations"/> — whitelist of
///     destination ids the LLM may reference. Intents that need a
///     destination (<c>deliver_to_station</c> / <c>haul_goods</c>) must
///     pick from this set; anything else is rejected.</item>
/// </list></para></summary>
internal sealed class MissionBlockValidator
{
    internal const int NameMaxLen           = 60;
    internal const int DescriptionMaxLen    = 500;
    internal const int CompletionTextMaxLen = 200;
    internal const int ObjDescriptionMaxLen = 120;

    internal const int NameSoftMaxLen           = NameMaxLen           * 9 / 10;  // 54
    internal const int DescriptionSoftMaxLen    = DescriptionMaxLen    * 9 / 10;  // 450
    internal const int CompletionTextSoftMaxLen = CompletionTextMaxLen * 9 / 10;  // 180
    internal const int ObjDescriptionSoftMaxLen = ObjDescriptionMaxLen * 9 / 10;  // 108

    private const int GatherRequiredMin   = 1;
    private const int GatherRequiredMax   = 50;
    private const int HaulRequiredMin     = 1;
    // Trade goods are bulkier than ore/salvage — cap at 20 to match
    // vanilla's EscortMissionItem0 (20 units, decomp line 46575).
    private const int HaulRequiredMax     = 20;

    // Reward clamps — unchanged from v1. Bounds mirror vanilla's
    // procedural + SideMissions envelope so broker rewards fit alongside
    // vanilla's mission board.
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
        IReadOnlyDictionary<string, int> reputation,
        IReadOnlyList<string>? forbiddenArchetypes = null,
        IReadOnlyList<AccessibleDestination>? accessibleDestinations = null)
    {
        if (mission == null || mission.Type != JTokenType.Object)
            throw new LlmValidationException(
                $"field `mission` must be a json object, got {(mission == null ? "null" : mission.Type.ToString())}");

        var obj = (JObject)mission;

        foreach (var prop in obj.Properties())
            if (!MissionKeys.Contains(prop.Name))
                throw new LlmValidationException(
                    $"unexpected field `mission.{prop.Name}` (schema is strict)");
        foreach (var required in MissionKeys)
            if (!obj.ContainsKey(required))
                throw new LlmValidationException($"missing field `mission.{required}`");

        var name           = ReadString(obj, "mission.name",            NameMaxLen);
        var description    = ReadString(obj, "mission.description",     DescriptionMaxLen);
        var completionText = ReadString(obj, "mission.completion_text", CompletionTextMaxLen);

        var sourceFactionTok = obj["source_faction"]!;
        if (sourceFactionTok.Type != JTokenType.String)
            throw new LlmValidationException("field `mission.source_faction` must be a string");
        var sourceFaction = sourceFactionTok.Value<string>() ?? string.Empty;
        if (!FactionWhitelist.Contains(sourceFaction))
            throw new LlmValidationException(
                $"field `mission.source_faction` must be a whitelisted faction, got \"{sourceFaction}\"");

        var forbidden    = forbiddenArchetypes ?? System.Array.Empty<string>();
        var destinations = accessibleDestinations ?? System.Array.Empty<AccessibleDestination>();

        var steps   = ReadSteps(obj, atWar, reputation, forbidden, destinations);
        var rewards = ReadRewards(obj);

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
        IReadOnlyDictionary<string, int> reputation,
        IReadOnlyList<string> forbidden,
        IReadOnlyList<AccessibleDestination> destinations)
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
            var intent = ParseIntent((JObject)step, i, atWar, reputation, forbidden, destinations);
            steps.Add(new LlmMissionStep(intent));
        }
        return steps;
    }

    private LlmIntent ParseIntent(
        JObject obj, int stepIdx,
        IReadOnlyList<string> atWar,
        IReadOnlyDictionary<string, int> reputation,
        IReadOnlyList<string> forbidden,
        IReadOnlyList<AccessibleDestination> destinations)
    {
        var path = $"mission.steps[{stepIdx}]";

        var intentTok = obj["intent"];
        if (intentTok == null || intentTok.Type != JTokenType.String)
            throw new LlmValidationException(
                $"field `{path}.intent` missing or not a string");
        var intent = intentTok.Value<string>() ?? string.Empty;
        if (!IntentWhitelist.Contains(intent))
            throw new LlmValidationException(
                $"field `{path}.intent` must be a whitelisted intent, got \"{intent}\". " +
                $"Valid intents: {string.Join(", ", IntentWhitelist.All)}");

        // Mechanical enforcement of forbidden_archetypes. An intent is
        // blocked if ANY of its archetypes (composite intents carry
        // multiple) appears in the forbidden list. This is the single
        // backstop against prompt-attention drift on context-impossible
        // intents (e.g. combat forbidden because no faction is hostile).
        foreach (var arch in IntentWhitelist.Archetypes(intent))
            if (forbidden.Contains(arch))
                throw new LlmValidationException(
                    $"field `{path}.intent=\"{intent}\"` requires archetype `{arch}` " +
                    $"but it is in mission_guidance.forbidden_archetypes");

        return intent switch
        {
            IntentWhitelist.ClearCombatSite       => ParseClearCombatSite(obj, path, atWar, reputation),
            IntentWhitelist.GatherOre             => ParseGatherOre(obj, path),
            IntentWhitelist.GatherSalvage         => ParseGatherSalvage(obj, path),
            IntentWhitelist.DefendedGatherOre     => ParseDefendedGatherOre(obj, path, atWar, reputation),
            IntentWhitelist.DefendedGatherSalvage => ParseDefendedGatherSalvage(obj, path, atWar, reputation),
            IntentWhitelist.DeliverToStation      => ParseDeliverToStation(obj, path, destinations),
            IntentWhitelist.HaulGoods             => ParseHaulGoods(obj, path, destinations),
            _ => throw new LlmValidationException(
                     $"unreachable: whitelist passed but switch missed \"{intent}\""),
        };
    }

    private LlmIntent ParseClearCombatSite(
        JObject obj, string path,
        IReadOnlyList<string> atWar,
        IReadOnlyDictionary<string, int> reputation)
    {
        RequireStrictKeys(obj, path, "intent", "enemy_faction", "description");
        var faction = ReadHostileFaction(obj, $"{path}.enemy_faction", atWar, reputation);
        var desc    = ReadObjectiveDescription(obj, $"{path}.description");
        return new ClearCombatSiteIntent(faction, desc);
    }

    private LlmIntent ParseGatherOre(JObject obj, string path)
    {
        RequireStrictKeys(obj, path, "intent", "required_amount", "description");
        var amount = ReadInt(obj, $"{path}.required_amount", GatherRequiredMin, GatherRequiredMax);
        var desc   = ReadObjectiveDescription(obj, $"{path}.description");
        return new GatherOreIntent(amount, desc);
    }

    private LlmIntent ParseGatherSalvage(JObject obj, string path)
    {
        RequireStrictKeys(obj, path, "intent", "required_amount", "description");
        var amount = ReadInt(obj, $"{path}.required_amount", GatherRequiredMin, GatherRequiredMax);
        var desc   = ReadObjectiveDescription(obj, $"{path}.description");
        return new GatherSalvageIntent(amount, desc);
    }

    private LlmIntent ParseDefendedGatherOre(
        JObject obj, string path,
        IReadOnlyList<string> atWar,
        IReadOnlyDictionary<string, int> reputation)
    {
        RequireStrictKeys(obj, path, "intent", "required_amount", "guards_faction", "description");
        var amount  = ReadInt(obj, $"{path}.required_amount", GatherRequiredMin, GatherRequiredMax);
        var guards  = ReadHostileFaction(obj, $"{path}.guards_faction", atWar, reputation);
        var desc    = ReadObjectiveDescription(obj, $"{path}.description");
        return new DefendedGatherOreIntent(amount, guards, desc);
    }

    private LlmIntent ParseDefendedGatherSalvage(
        JObject obj, string path,
        IReadOnlyList<string> atWar,
        IReadOnlyDictionary<string, int> reputation)
    {
        RequireStrictKeys(obj, path, "intent", "required_amount", "guards_faction", "description");
        var amount  = ReadInt(obj, $"{path}.required_amount", GatherRequiredMin, GatherRequiredMax);
        var guards  = ReadHostileFaction(obj, $"{path}.guards_faction", atWar, reputation);
        var desc    = ReadObjectiveDescription(obj, $"{path}.description");
        return new DefendedGatherSalvageIntent(amount, guards, desc);
    }

    private LlmIntent ParseDeliverToStation(
        JObject obj, string path,
        IReadOnlyList<AccessibleDestination> destinations)
    {
        RequireStrictKeys(obj, path, "intent", "destination_id", "description");
        var destId = ReadDestinationId(obj, $"{path}.destination_id", destinations);
        var desc   = ReadObjectiveDescription(obj, $"{path}.description");
        return new DeliverToStationIntent(destId, desc);
    }

    private LlmIntent ParseHaulGoods(
        JObject obj, string path,
        IReadOnlyList<AccessibleDestination> destinations)
    {
        RequireStrictKeys(obj, path, "intent", "required_amount", "destination_id", "description");
        var amount = ReadInt(obj, $"{path}.required_amount", HaulRequiredMin, HaulRequiredMax);
        var destId = ReadDestinationId(obj, $"{path}.destination_id", destinations);
        var desc   = ReadObjectiveDescription(obj, $"{path}.description");
        return new HaulGoodsIntent(amount, destId, desc);
    }

    private static string ReadHostileFaction(
        JObject obj, string path,
        IReadOnlyList<string> atWar,
        IReadOnlyDictionary<string, int> reputation)
    {
        var tok = obj[PathTail(path)]!;
        if (tok.Type != JTokenType.String)
            throw new LlmValidationException($"field `{path}` must be a string");
        var faction = tok.Value<string>() ?? string.Empty;
        if (!FactionWhitelist.Contains(faction))
            throw new LlmValidationException(
                $"field `{path}` must be a whitelisted faction, got \"{faction}\"");

        // Mirror vanilla FactionData.IsEnemy: hostile iff at_war OR rep < -500.
        var isAtWar = atWar != null && atWar.Contains(faction);
        var rep     = 0;
        reputation?.TryGetValue(faction, out rep);
        if (!isAtWar && rep >= -500)
            throw new LlmValidationException(
                $"field `{path}` is not hostile to player " +
                $"(rep={rep}, at_war=false); refusing combat mission against a non-enemy");
        return faction;
    }

    private static string ReadDestinationId(
        JObject obj, string path,
        IReadOnlyList<AccessibleDestination> destinations)
    {
        var tok = obj[PathTail(path)]!;
        if (tok.Type != JTokenType.String)
            throw new LlmValidationException($"field `{path}` must be a string");
        var id = tok.Value<string>() ?? string.Empty;
        if (destinations.Count == 0)
            throw new LlmValidationException(
                $"field `{path}` was provided but context.accessible_destinations is empty; " +
                $"deliver/haul intents require at least one reachable station");
        if (!destinations.Any(d => d.ShortId == id))
            throw new LlmValidationException(
                $"field `{path}` = \"{id}\" is not in context.accessible_destinations " +
                $"(valid ids: {string.Join(", ", destinations.Select(d => d.ShortId))})");
        return id;
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
            "Item"       => ParseItemReward(obj, path),
            _            => throw new LlmValidationException(
                                $"unreachable: whitelist passed but switch missed \"{type}\""),
        };
    }

    private LlmReward ParseItemReward(JObject obj, string path)
    {
        RequireStrictKeys(obj, path, "type", "kind");
        var kindTok = obj["kind"]!;
        if (kindTok.Type != JTokenType.String)
            throw new LlmValidationException($"field `{path}.kind` must be a string");
        var kind = kindTok.Value<string>() ?? string.Empty;
        if (!ItemRewardKindWhitelist.Contains(kind))
            throw new LlmValidationException(
                $"field `{path}.kind` must be a whitelisted item-reward kind, got \"{kind}\"");
        return new LlmItemReward(kind);
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
}
