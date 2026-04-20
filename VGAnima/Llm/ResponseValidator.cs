using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace VGAnima.Llm;

/// <summary>Strict parser + validator for v1 LLM output. Every rule from
/// spec §2 and §8 is enforced in order, with the first failure raising
/// <see cref="LlmValidationException"/> carrying a message that pinpoints
/// field + rule.
///
/// Rule order:
///   1. Raw content parses as JSON.
///   2. Root is a JSON object (not array, not scalar, not null).
///   3. <c>schema</c> field exists and equals <c>"vganima/story/v1"</c>.
///   4. Exactly the fields <c>{schema, pitch, check_in, payout}</c> are present.
///   5. Each of <c>pitch</c>/<c>check_in</c>/<c>payout</c> is an array of strings
///      within size bounds (3-5 / 1-2 / 2-4).
///   6. Each string: non-empty after trim, all chars less than 128, max 120 chars,
///      no leading or trailing whitespace.
///
/// Uses <see cref="Newtonsoft.Json.Linq.JToken"/> rather than System.Text.Json —
/// Unity 6000.2's Mono profile VTable-faults on STJ 8.x's Utf8JsonWriter.
/// Newtonsoft is bundled by the game, so no runtime dep shipping needed.</summary>
internal sealed class ResponseValidator
{
    public const string ExpectedSchema = "vganima/story/v1";

    private static readonly HashSet<string> AllowedKeys = new()
    {
        "schema", "pitch", "check_in", "payout",
    };

    public LlmStory Parse(string rawContent)
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

        // Rule 3: schema presence + value.
        if (!obj.TryGetValue("schema", out var schemaTok))
            throw new LlmValidationException("missing field `schema`");
        if (schemaTok.Type != JTokenType.String)
            throw new LlmValidationException("field `schema` must be a string");
        var schemaValue = schemaTok.Value<string>();
        if (schemaValue != ExpectedSchema)
            throw new LlmValidationException(
                $"field `schema` must equal \"{ExpectedSchema}\", got \"{schemaValue}\"");

        // Rule 4: exactly the allowed keys — no more, no less.
        foreach (var prop in obj.Properties())
        {
            if (!AllowedKeys.Contains(prop.Name))
                throw new LlmValidationException(
                    $"unexpected top-level field `{prop.Name}` (v1 schema is strict)");
        }

        if (!obj.TryGetValue("pitch", out var pitchTok))
            throw new LlmValidationException("missing field `pitch`");
        if (!obj.TryGetValue("check_in", out var checkInTok))
            throw new LlmValidationException("missing field `check_in`");
        if (!obj.TryGetValue("payout", out var payoutTok))
            throw new LlmValidationException("missing field `payout`");

        var pitch   = ReadStringArray("pitch",    pitchTok,   minCount: 3, maxCount: 5);
        var checkIn = ReadStringArray("check_in", checkInTok, minCount: 1, maxCount: 2);
        var payout  = ReadStringArray("payout",   payoutTok,  minCount: 2, maxCount: 4);

        return new LlmStory(pitch, checkIn, payout);
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
