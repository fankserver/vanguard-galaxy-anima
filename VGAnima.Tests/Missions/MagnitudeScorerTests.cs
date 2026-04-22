using System.Collections.Generic;
using VGAnima.Llm;
using VGAnima.Missions;
using VGAnima.Persistence;
using Xunit;

namespace VGAnima.Tests.Missions;

public class MagnitudeScorerTests
{
    private static LlmMissionBlock Block(int steps = 1)
    {
        var stepList = new List<LlmMissionStep>();
        for (var i = 0; i < steps; i++)
            stepList.Add(new LlmMissionStep(
                new LlmObjective[] { new LlmClearPoi("Marauders", "d") }));
        return new LlmMissionBlock(
            Name: "Test", Description: "x", CompletionText: "x",
            SourceFaction: "SalvageGuild",
            Steps:   stepList,
            Rewards: new List<LlmReward>());
    }

    [Fact]
    public void Score_IsClampedBetween1And10()
    {
        // Extreme inputs — negative level + abandoned outcome should still
        // clamp to at least 1.
        var block = Block();
        var low   = MagnitudeScorer.Score(block, MissionArchetypes.Gather,
                                          CompletedMissionOutcomes.Abandoned,
                                          missionLevel: 0);
        Assert.InRange(low, 1, 10);

        // Upper end — high level + hybrid archetype + multi-step.
        var big = Block(steps: 3);
        var high = MagnitudeScorer.Score(big, MissionArchetypes.DefendedCollect,
                                         CompletedMissionOutcomes.Completed,
                                         missionLevel: 40);
        Assert.InRange(high, 1, 10);
    }

    [Fact]
    public void Score_CombatGetsBoost()
    {
        var block  = Block();
        var combat = MagnitudeScorer.Score(block, MissionArchetypes.Combat,
                                           CompletedMissionOutcomes.Completed, 10);
        var gather = MagnitudeScorer.Score(block, MissionArchetypes.Gather,
                                           CompletedMissionOutcomes.Completed, 10);
        Assert.True(combat > gather,
            $"combat score ({combat}) should exceed gather score ({gather}) at same level");
    }

    [Fact]
    public void Score_DefendedCollectGetsLargerBoostThanCombat()
    {
        // Defended-collect is rarer + more dramatic, per the scorer formula.
        var block     = Block();
        var combat    = MagnitudeScorer.Score(block, MissionArchetypes.Combat,
                                              CompletedMissionOutcomes.Completed, 10);
        var defended  = MagnitudeScorer.Score(block, MissionArchetypes.DefendedCollect,
                                              CompletedMissionOutcomes.Completed, 10);
        Assert.True(defended > combat);
    }

    [Fact]
    public void Score_AbandonedOutcomePenalizesMore_ThanFailed()
    {
        var block     = Block();
        var completed = MagnitudeScorer.Score(block, MissionArchetypes.Combat,
                                              CompletedMissionOutcomes.Completed, 12);
        var failed    = MagnitudeScorer.Score(block, MissionArchetypes.Combat,
                                              CompletedMissionOutcomes.Failed,    12);
        var abandoned = MagnitudeScorer.Score(block, MissionArchetypes.Combat,
                                              CompletedMissionOutcomes.Abandoned, 12);
        Assert.True(completed > failed);
        Assert.True(failed    > abandoned);
    }

    [Fact]
    public void Score_MultiStepMissionScoresHigherThanSingleStep()
    {
        var single = Block(steps: 1);
        var multi  = Block(steps: 3);
        var singleScore = MagnitudeScorer.Score(single, MissionArchetypes.Combat,
                                                CompletedMissionOutcomes.Completed, 10);
        var multiScore  = MagnitudeScorer.Score(multi,  MissionArchetypes.Combat,
                                                CompletedMissionOutcomes.Completed, 10);
        Assert.True(multiScore > singleScore);
    }
}
