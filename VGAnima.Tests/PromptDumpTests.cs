using System.IO;
using VGAnima.Patches;
using Xunit;

namespace VGAnima.Tests;

/// <summary>One-off snapshot that writes the live system prompt to
/// <c>/tmp/vganima-system-prompt.txt</c> so external Python harnesses can
/// call the real LLM with the exact prompt VGAnima ships. Not a unit test
/// in any meaningful sense — just a plumbing trick. Skipped by default;
/// un-skip and run to refresh the snapshot.</summary>
public class PromptDumpTests
{
    [Fact(Skip = "Snapshot helper; un-skip to refresh /tmp/vganima-system-prompt.txt")]
    public void DumpSystemPrompt()
    {
        File.WriteAllText("/tmp/vganima-system-prompt.txt", BarRefreshPatches.BuildSystemPrompt());
    }

    [Fact(Skip = "Snapshot helper; un-skip to refresh stage-direction-level variants")]
    public void DumpSystemPromptAllLevels()
    {
        File.WriteAllText("/tmp/vganima-system-prompt-l0.txt", BarRefreshPatches.BuildSystemPrompt(0));
        File.WriteAllText("/tmp/vganima-system-prompt-l1.txt", BarRefreshPatches.BuildSystemPrompt(1));
        File.WriteAllText("/tmp/vganima-system-prompt-l2.txt", BarRefreshPatches.BuildSystemPrompt(2));
    }

    [Fact]
    public void BuildStageDirectionRule_Level0_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, BarRefreshPatches.BuildStageDirectionRule(0));
    }

    [Fact]
    public void BuildStageDirectionRule_NegativeLevel_ReturnsEmpty()
    {
        // Out-of-range safety: BepInEx doesn't enforce int ranges so a user
        // could put -5 in the config; treat anything <=0 as off.
        Assert.Equal(string.Empty, BarRefreshPatches.BuildStageDirectionRule(-5));
    }

    [Fact]
    public void BuildStageDirectionRule_Level1_SparseWording()
    {
        var rule = BarRefreshPatches.BuildStageDirectionRule(1);
        Assert.Contains("STAGE DIRECTIONS", rule);
        Assert.Contains("1-2 dialogue lines", rule);
        Assert.DoesNotContain("MOST dialogue lines", rule);
    }

    [Fact]
    public void BuildStageDirectionRule_Level2_RichWording()
    {
        var rule = BarRefreshPatches.BuildStageDirectionRule(2);
        Assert.Contains("STAGE DIRECTIONS", rule);
        Assert.Contains("MOST dialogue lines", rule);
    }

    [Fact]
    public void BuildStageDirectionRule_AboveMax_ClampsToRich()
    {
        // Level 5 should behave like level 2 (rich) — out-of-range on the
        // high side maps to the most intense supported level.
        var rule5 = BarRefreshPatches.BuildStageDirectionRule(5);
        var rule2 = BarRefreshPatches.BuildStageDirectionRule(2);
        Assert.Equal(rule2, rule5);
    }

    [Fact]
    public void BuildSystemPrompt_DefaultLevel_OmitsStageDirections()
    {
        // The default must produce a prompt byte-identical to the pre-feature
        // baseline so users who don't opt into stage directions see no
        // behavior change from the LLM.
        var prompt = BarRefreshPatches.BuildSystemPrompt();
        Assert.DoesNotContain("STAGE DIRECTIONS", prompt);
        Assert.DoesNotContain("[Spits on the floor]", prompt);
    }

    [Fact]
    public void BuildSystemPrompt_Level0_OmitsStageDirections()
    {
        Assert.DoesNotContain("STAGE DIRECTIONS", BarRefreshPatches.BuildSystemPrompt(0));
    }

    [Fact]
    public void BuildSystemPrompt_Level1_IncludesSparseRule()
    {
        var prompt = BarRefreshPatches.BuildSystemPrompt(1);
        Assert.Contains("STAGE DIRECTIONS", prompt);
        Assert.Contains("1-2 dialogue lines", prompt);
    }
}
