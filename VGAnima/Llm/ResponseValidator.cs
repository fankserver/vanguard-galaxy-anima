using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VGAnima.Llm;

/// <summary>Strict parser + validator for LLM output. Dispatches on the
/// <c>schema</c> field: <c>vganima/story/v1</c> produces a dialogue-only
/// <see cref="LlmStory"/>, <c>vganima/mission/v1</c> additionally parses
/// the <c>mission</c> sub-object via <see cref="MissionBlockValidator"/>.
///
/// The dialogue rules (pitch / check_in / payout sizes + ASCII) are identical
/// across schemas. The v2 schema adds exactly one required top-level field
/// (<c>mission</c>) to the strict keyset.
///
/// Cross-context hostility inputs (<c>atWar</c>, <c>reputation</c>) are
/// optional — null means "skip the enemy_faction-is-friendly check". Unit
/// tests without a full context pass null; the orchestrator in
/// <see cref="VGAnima.Patches.BarRefreshPatches"/> threads real values in.
///
/// Uses <see cref="Newtonsoft.Json.Linq.JToken"/> (game-provided Newtonsoft)
/// — see comment in the v1 header for why System.Text.Json is off-limits on
/// Unity 6000.2's Mono profile.</summary>
internal sealed class ResponseValidator
{
    public const string ExpectedSchemaV1 = "vganima/story/v1";
    public const string ExpectedSchemaV2 = "vganima/mission/v1";

    private static readonly HashSet<string> DialogueKeys = new()
    {
        "schema", "pitch", "check_in", "payout",
    };

    private static readonly HashSet<string> MissionKeys = new()
    {
        "schema", "pitch", "check_in", "payout", "mission",
    };

    private readonly MissionBlockValidator _missionValidator = new();

    /// <summary>Parse with no cross-context — hostile-faction rule in the
    /// mission-block validator is skipped. Used by tests and for the v1
    /// dialogue-only path.</summary>
    public LlmStory Parse(string rawContent)
        => Parse(rawContent, atWar: null, reputation: null);

    public LlmStory Parse(
        string rawContent,
        IReadOnlyList<string>? atWar,
        IReadOnlyDictionary<string, int>? reputation)
    {
        JToken root;
        try
        {
            root = JToken.Parse(rawContent);
        }
        catch (JsonException ex)
        {
            throw new LlmValidationException(
                $"content is not valid json ({ex.Message})", ex);
        }

        if (root.Type != JTokenType.Object)
            throw new LlmValidationException(
                $"root must be a json object, got {root.Type}");

        var obj = (JObject)root;

        if (!obj.TryGetValue("schema", out var schemaTok))
            throw new LlmValidationException("missing field `schema`");
        if (schemaTok.Type != JTokenType.String)
            throw new LlmValidationException("field `schema` must be a string");

        var schemaValue = schemaTok.Value<string>();
        var expectedKeys = schemaValue switch
        {
            ExpectedSchemaV1 => DialogueKeys,
            ExpectedSchemaV2 => MissionKeys,
            _                => throw new LlmValidationException(
                                    $"field `schema` must be \"{ExpectedSchemaV1}\" "
                                    + $"or \"{ExpectedSchemaV2}\", got \"{schemaValue}\""),
        };

        foreach (var prop in obj.Properties())
            if (!expectedKeys.Contains(prop.Name))
                throw new LlmValidationException(
                    $"unexpected top-level field `{prop.Name}` (schema is strict)");

        if (!obj.TryGetValue("pitch",    out var pitchTok))    throw new LlmValidationException("missing field `pitch`");
        if (!obj.TryGetValue("check_in", out var checkInTok))  throw new LlmValidationException("missing field `check_in`");
        if (!obj.TryGetValue("payout",   out var payoutTok))   throw new LlmValidationException("missing field `payout`");

        var pitch   = ReadStringArray("pitch",    pitchTok,   minCount: 3, maxCount: 5);
        var checkIn = ReadStringArray("check_in", checkInTok, minCount: 1, maxCount: 2);
        var payout  = ReadStringArray("payout",   payoutTok,  minCount: 2, maxCount: 4);

        LlmMissionBlock? mission = null;
        if (schemaValue == ExpectedSchemaV2)
        {
            if (!obj.TryGetValue("mission", out var missionTok))
                throw new LlmValidationException("missing field `mission`");
            mission = _missionValidator.Parse(
                missionTok,
                atWar      ?? Array.Empty<string>(),
                reputation ?? new Dictionary<string, int>());
        }

        return new LlmStory(pitch, checkIn, payout, mission);
    }

    private static IReadOnlyList<string> ReadStringArray(string fieldName, JToken token, int minCount, int maxCount)
    {
        if (token.Type != JTokenType.Array)
            throw new LlmValidationException(
                $"field `{fieldName}` must be an array, got {token.Type}");

        var arr = (JArray)token;
        var count = arr.Count;
        if (count < minCount || count > maxCount)
            throw new LlmValidationException(
                $"field `{fieldName}` must have {minCount}..{maxCount} elements, got {count}");

        var list = new List<string>(count);
        for (var index = 0; index < count; index++)
        {
            var el = arr[index];
            if (el.Type != JTokenType.String)
                throw new LlmValidationException(
                    $"field `{fieldName}`[{index}] must be a string, got {el.Type}");

            var s = el.Value<string>() ?? string.Empty;
            ValidateString(fieldName, index, s);
            list.Add(s);
        }
        return list;
    }

    private static void ValidateString(string fieldName, int index, string s)
    {
        if (s.Trim().Length == 0)
            throw new LlmValidationException(
                $"field `{fieldName}`[{index}] must be non-empty after trim");

        if (s.Length > 120)
            throw new LlmValidationException(
                $"field `{fieldName}`[{index}] exceeds max length 120 (got {s.Length})");

        if (s.Length != s.Trim().Length)
            throw new LlmValidationException(
                $"field `{fieldName}`[{index}] must not have leading/trailing whitespace");

        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] >= 128)
                throw new LlmValidationException(
                    $"field `{fieldName}`[{index}] has non-ascii char U+{(int)s[i]:X4} at offset {i} (rule: ascii-only)");
        }
    }
}
