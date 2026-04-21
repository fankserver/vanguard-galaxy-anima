using System.Linq;
using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Llm;

public class ResponseValidatorTests
{
    private const string ValidPayload = @"{
        ""schema"": ""vganima/story/v1"",
        ""pitch"":    [""Line 1."", ""Line 2."", ""Line 3.""],
        ""check_in"": [""Still out there?""],
        ""payout"":   [""Well done."", ""Here's your pay.""]
    }";

    [Fact]
    public void Parse_HappyPath_ReturnsLlmStory()
    {
        var story = new ResponseValidator().Parse(ValidPayload);
        Assert.Equal(3, story.Pitch.Count);
        Assert.Equal(1, story.CheckIn.Count);
        Assert.Equal(2, story.Payout.Count);
        Assert.Equal("Line 1.", story.Pitch[0]);
        Assert.Equal("Still out there?", story.CheckIn[0]);
        Assert.Equal("Well done.", story.Payout[0]);
    }

    [Fact]
    public void Parse_NonJson_Throws()
    {
        var ex = Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse("not json at all"));
        Assert.Contains("json", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_NonObjectRoot_Array_Throws()
    {
        var ex = Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse("[]"));
        Assert.Contains("object", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_NonObjectRoot_String_Throws()
    {
        Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse("\"hello\""));
    }

    [Fact]
    public void Parse_NonObjectRoot_Null_Throws()
    {
        Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse("null"));
    }

    [Fact]
    public void Parse_NonObjectRoot_Number_Throws()
    {
        Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse("42"));
    }

    [Fact]
    public void Parse_WrongSchemaValue_Throws()
    {
        var payload = ValidPayload.Replace("vganima/story/v1", "vganima/story/v2");
        var ex = Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
        Assert.Contains("schema", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MissingSchema_Throws()
    {
        const string payload = @"{
            ""pitch"":    [""a"",""b"",""c""],
            ""check_in"": [""d""],
            ""payout"":   [""e"",""f""]
        }";
        Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
    }

    [Fact]
    public void Parse_MissingPitch_Throws()
    {
        const string payload = @"{
            ""schema"":   ""vganima/story/v1"",
            ""check_in"": [""d""],
            ""payout"":   [""e"",""f""]
        }";
        var ex = Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
        Assert.Contains("pitch", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_MissingCheckIn_Throws()
    {
        const string payload = @"{
            ""schema"": ""vganima/story/v1"",
            ""pitch"":  [""a"",""b"",""c""],
            ""payout"": [""e"",""f""]
        }";
        var ex = Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
        Assert.Contains("check_in", ex.Message);
    }

    [Fact]
    public void Parse_MissingPayout_Throws()
    {
        const string payload = @"{
            ""schema"":   ""vganima/story/v1"",
            ""pitch"":    [""a"",""b"",""c""],
            ""check_in"": [""d""]
        }";
        var ex = Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
        Assert.Contains("payout", ex.Message);
    }

    [Fact]
    public void Parse_ExtraTopLevelField_Throws()
    {
        const string payload = @"{
            ""schema"":   ""vganima/story/v1"",
            ""pitch"":    [""a"",""b"",""c""],
            ""check_in"": [""d""],
            ""payout"":   [""e"",""f""],
            ""theme"":    ""salvage""
        }";
        var ex = Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
        Assert.Contains("theme", ex.Message);
    }

    [Fact]
    public void Parse_PitchTooShort_Throws()
    {
        const string payload = @"{
            ""schema"":   ""vganima/story/v1"",
            ""pitch"":    [""a"",""b""],
            ""check_in"": [""d""],
            ""payout"":   [""e"",""f""]
        }";
        Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
    }

    [Fact]
    public void Parse_PitchTooLong_Throws()
    {
        const string payload = @"{
            ""schema"":   ""vganima/story/v1"",
            ""pitch"":    [""a"",""b"",""c"",""d"",""e"",""f""],
            ""check_in"": [""g""],
            ""payout"":   [""h"",""i""]
        }";
        Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
    }

    [Fact]
    public void Parse_PitchEmptyArray_Throws()
    {
        const string payload = @"{
            ""schema"":   ""vganima/story/v1"",
            ""pitch"":    [],
            ""check_in"": [""d""],
            ""payout"":   [""e"",""f""]
        }";
        Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
    }

    [Fact]
    public void Parse_CheckInTooShort_Throws()
    {
        const string payload = @"{
            ""schema"":   ""vganima/story/v1"",
            ""pitch"":    [""a"",""b"",""c""],
            ""check_in"": [],
            ""payout"":   [""e"",""f""]
        }";
        Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
    }

    [Fact]
    public void Parse_CheckInTooLong_Throws()
    {
        const string payload = @"{
            ""schema"":   ""vganima/story/v1"",
            ""pitch"":    [""a"",""b"",""c""],
            ""check_in"": [""d"",""e"",""f""],
            ""payout"":   [""g"",""h""]
        }";
        Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
    }

    [Fact]
    public void Parse_PayoutTooShort_Throws()
    {
        const string payload = @"{
            ""schema"":   ""vganima/story/v1"",
            ""pitch"":    [""a"",""b"",""c""],
            ""check_in"": [""d""],
            ""payout"":   [""e""]
        }";
        Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
    }

    [Fact]
    public void Parse_PayoutTooLong_Throws()
    {
        const string payload = @"{
            ""schema"":   ""vganima/story/v1"",
            ""pitch"":    [""a"",""b"",""c""],
            ""check_in"": [""d""],
            ""payout"":   [""e"",""f"",""g"",""h"",""i""]
        }";
        Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
    }

    [Fact]
    public void Parse_NonStringArrayElement_Throws()
    {
        const string payload = @"{
            ""schema"":   ""vganima/story/v1"",
            ""pitch"":    [""a"",""b"",42],
            ""check_in"": [""d""],
            ""payout"":   [""e"",""f""]
        }";
        Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
    }

    [Fact]
    public void Parse_EmptyStringAfterTrim_Throws()
    {
        const string payload = @"{
            ""schema"":   ""vganima/story/v1"",
            ""pitch"":    [""a"","""",""c""],
            ""check_in"": [""d""],
            ""payout"":   [""e"",""f""]
        }";
        Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
    }

    [Fact]
    public void Parse_NonAsciiEmDash_InPitch_Throws()
    {
        // em-dash U+2014
        const string payload = "{\n" +
            "  \"schema\": \"vganima/story/v1\",\n" +
            "  \"pitch\":    [\"Captain \\u2014 a moment?\", \"line 2\", \"line 3\"],\n" +
            "  \"check_in\": [\"d\"],\n" +
            "  \"payout\":   [\"e\", \"f\"]\n" +
            "}";
        var ex = Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
        Assert.Contains("ascii", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_NonAsciiSmartQuote_InCheckIn_Throws()
    {
        // left smart quote U+201C
        const string payload = "{\n" +
            "  \"schema\": \"vganima/story/v1\",\n" +
            "  \"pitch\":    [\"a\", \"b\", \"c\"],\n" +
            "  \"check_in\": [\"\\u201chello\\u201d\"],\n" +
            "  \"payout\":   [\"e\", \"f\"]\n" +
            "}";
        Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
    }

    [Fact]
    public void Parse_NonAsciiEmoji_InPayout_Throws()
    {
        // rocket emoji U+1F680
        const string payload = "{\n" +
            "  \"schema\": \"vganima/story/v1\",\n" +
            "  \"pitch\":    [\"a\", \"b\", \"c\"],\n" +
            "  \"check_in\": [\"d\"],\n" +
            "  \"payout\":   [\"e\", \"\\uD83D\\uDE80 good job\"]\n" +
            "}";
        Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
    }

    [Fact]
    public void Parse_OverLengthString_121Chars_Throws()
    {
        var over = new string('x', 121);
        var payload = "{\n" +
            "  \"schema\": \"vganima/story/v1\",\n" +
            "  \"pitch\":    [\"a\", \"b\", \"" + over + "\"],\n" +
            "  \"check_in\": [\"d\"],\n" +
            "  \"payout\":   [\"e\", \"f\"]\n" +
            "}";
        var ex = Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
        Assert.Contains("120", ex.Message);
    }

    [Fact]
    public void Parse_Exactly120Chars_IsAccepted()
    {
        var at = new string('x', 120);
        var payload = "{\n" +
            "  \"schema\": \"vganima/story/v1\",\n" +
            "  \"pitch\":    [\"a\", \"b\", \"" + at + "\"],\n" +
            "  \"check_in\": [\"d\"],\n" +
            "  \"payout\":   [\"e\", \"f\"]\n" +
            "}";
        var story = new ResponseValidator().Parse(payload);
        Assert.Equal(120, story.Pitch[2].Length);
    }

    [Fact]
    public void Parse_LeadingWhitespace_Throws()
    {
        const string payload = @"{
            ""schema"":   ""vganima/story/v1"",
            ""pitch"":    ["" leading"",""b"",""c""],
            ""check_in"": [""d""],
            ""payout"":   [""e"",""f""]
        }";
        Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
    }

    [Fact]
    public void Parse_TrailingWhitespace_Throws()
    {
        const string payload = @"{
            ""schema"":   ""vganima/story/v1"",
            ""pitch"":    [""a"",""b"",""trailing ""],
            ""check_in"": [""d""],
            ""payout"":   [""e"",""f""]
        }";
        Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
    }

    [Fact]
    public void Parse_AllArrayBoundaries_Accepts()
    {
        // pitch=5, check_in=2, payout=4 — the upper bounds.
        const string payload = @"{
            ""schema"":   ""vganima/story/v1"",
            ""pitch"":    [""a"",""b"",""c"",""d"",""e""],
            ""check_in"": [""f"",""g""],
            ""payout"":   [""h"",""i"",""j"",""k""]
        }";
        var story = new ResponseValidator().Parse(payload);
        Assert.Equal(5, story.Pitch.Count);
        Assert.Equal(2, story.CheckIn.Count);
        Assert.Equal(4, story.Payout.Count);
    }

    // ---------- v2-mission schema dispatch ----------

    private const string ValidMissionPayload = @"{
        ""schema"": ""vganima/mission/v1"",
        ""pitch"":    [""Line 1."", ""Line 2."", ""Line 3.""],
        ""check_in"": [""Any luck?""],
        ""payout"":   [""Good job."", ""Here's your cut.""],
        ""mission"": {
            ""name"":            ""Test Run"",
            ""description"":     ""Go do a thing."",
            ""completion_text"": ""Thanks."",
            ""source_faction"":  ""TradingGuild"",
            ""steps"": [
                { ""objectives"": [
                    { ""type"": ""TriggerObjective"",
                      ""trigger"": ""DockedWithSpaceStation"",
                      ""required_amount"": 1,
                      ""description"": ""Dock."" } ] } ],
            ""rewards"": [
                { ""type"": ""Credits"", ""base_value"": 50 } ] } }";

    [Fact]
    public void Parse_V2Mission_HappyPath_ReturnsStoryWithMission()
    {
        var story = new ResponseValidator().Parse(ValidMissionPayload);
        Assert.NotNull(story.Mission);
        Assert.Equal("Test Run",     story.Mission!.Name);
        Assert.Equal("TradingGuild", story.Mission.SourceFaction);
        Assert.Single(story.Mission.Steps);
        Assert.Single(story.Mission.Rewards);
    }

    [Fact]
    public void Parse_V1Schema_StillWorks_MissionStaysNull()
    {
        var story = new ResponseValidator().Parse(ValidPayload);
        Assert.Null(story.Mission);
    }

    [Fact]
    public void Parse_V2Mission_MissingMissionField_Rejects()
    {
        const string payload = @"{
            ""schema"": ""vganima/mission/v1"",
            ""pitch"":    [""a"",""b"",""c""],
            ""check_in"": [""d""],
            ""payout"":   [""e"",""f""] }";
        var ex = Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
        Assert.Contains("mission", ex.Message);
    }

    [Fact]
    public void Parse_V2Mission_ExtraTopLevelField_Rejects()
    {
        var payload = ValidMissionPayload.Replace(
            @"""rewards"": [",
            @"""extra"": ""stuff"", ""rewards"": [");
        // Inject an unrelated extra key at the root, not inside mission.
        var brokenRoot = ValidMissionPayload.TrimEnd('}') + @", ""junk"": ""stuff"" }";
        Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(brokenRoot));
    }

    [Fact]
    public void Parse_V2Mission_DispatchesToMissionValidator_OnBadObjective()
    {
        var broken = ValidMissionPayload.Replace(
            @"""DockedWithSpaceStation""",
            @"""BountyTargetKilled""");
        var ex = Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(broken));
        Assert.Contains("trigger", ex.Message);
    }

    [Fact]
    public void Parse_UnknownSchema_Rejects()
    {
        var payload = ValidPayload.Replace(
            "vganima/story/v1",
            "vganima/story/v99");
        var ex = Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload));
        Assert.Contains("schema", ex.Message, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_V2Mission_WithAtWarContext_AcceptsHostileKill()
    {
        var payload = ValidMissionPayload.Replace(
            @"{ ""type"": ""TriggerObjective"",
                      ""trigger"": ""DockedWithSpaceStation"",
                      ""required_amount"": 1,
                      ""description"": ""Dock."" }",
            @"{ ""type"": ""KillEnemies"",
                      ""enemy_faction"": ""Marauders"",
                      ""required_amount"": 2,
                      ""description"": ""Kill them."" }");

        var atWar = new[] { "Marauders" };
        var rep   = new System.Collections.Generic.Dictionary<string, int>();
        var story = new ResponseValidator().Parse(payload, atWar, rep);
        Assert.NotNull(story.Mission);
        Assert.IsType<LlmKillEnemies>(story.Mission!.Steps[0].Objectives[0]);
    }

    [Fact]
    public void Parse_V2Mission_WithFriendlyContext_RejectsKillAlly()
    {
        var payload = ValidMissionPayload.Replace(
            @"{ ""type"": ""TriggerObjective"",
                      ""trigger"": ""DockedWithSpaceStation"",
                      ""required_amount"": 1,
                      ""description"": ""Dock."" }",
            @"{ ""type"": ""KillEnemies"",
                      ""enemy_faction"": ""TradingGuild"",
                      ""required_amount"": 2,
                      ""description"": ""Kill allies."" }");

        var atWar = System.Array.Empty<string>();
        var rep   = new System.Collections.Generic.Dictionary<string, int>
        {
            { "TradingGuild", 100 },
        };
        var ex = Assert.Throws<LlmValidationException>(
            () => new ResponseValidator().Parse(payload, atWar, rep));
        Assert.Contains("enemy_faction", ex.Message);
    }
}
