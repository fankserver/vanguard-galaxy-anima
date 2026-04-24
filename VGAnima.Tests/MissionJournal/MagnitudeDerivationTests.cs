using System.Collections.Generic;
using VGAnima.MissionJournal;
using VGMissionJournal.Logging;
using Xunit;

namespace VGAnima.Tests.MissionJournal;

public class MagnitudeDerivationTests
{
    // ---- Baseline level ----

    [Fact]
    public void Derive_UsesMissionLevel_WhenPopulated()
    {
        var r = MakeRecord(missionLevel: 12);  // 12 / 2 = 6
        Assert.Equal(6, MagnitudeDerivation.Derive(r));
    }

    [Fact]
    public void Derive_FallsBackToPlayerLevel_WhenMissionLevelZero()
    {
        // VGMissionJournal 0.1.0 has MissionLevel always 0 (known gap).
        // PlayerLevel at accept time is the next-best baseline.
        var r = MakeRecord(missionLevel: 0, playerLevel: 16);  // 16 / 2 = 8
        Assert.Equal(8, MagnitudeDerivation.Derive(r));
    }

    [Fact]
    public void Derive_FallsBackToFallbackLevel_WhenBothZero()
    {
        var r = MakeRecord(missionLevel: 0, playerLevel: 0);
        Assert.Equal(5, MagnitudeDerivation.Derive(r, fallbackLevel: 10));  // 10 / 2 = 5
    }

    [Fact]
    public void Derive_ClampsToMinimumOne_WhenAllLevelsZero()
    {
        var r = MakeRecord(missionLevel: 0, playerLevel: 0);
        Assert.Equal(1, MagnitudeDerivation.Derive(r, fallbackLevel: 0));  // 0 clamped to 1
    }

    // ---- Step + archetype modifiers ----

    [Fact]
    public void Derive_MultiStep_AddsOne()
    {
        var single = MakeRecord(missionLevel: 10, steps: Steps(Step()));
        var multi  = MakeRecord(missionLevel: 10, steps: Steps(Step(), Step()));

        // 10/2 = 5, multi-step +1 = 6
        Assert.Equal(5, MagnitudeDerivation.Derive(single));
        Assert.Equal(6, MagnitudeDerivation.Derive(multi));
    }

    [Fact]
    public void Derive_BountyMission_BoostsCombat()
    {
        // 10/2 = 5, combat (+1) = 6
        var r = MakeRecord(missionLevel: 10, subclass: "BountyMission");
        Assert.Equal(6, MagnitudeDerivation.Derive(r));
    }

    [Fact]
    public void Derive_PatrolMission_BoostsCombat()
    {
        var r = MakeRecord(missionLevel: 10, subclass: "PatrolMission");
        Assert.Equal(6, MagnitudeDerivation.Derive(r));
    }

    [Fact]
    public void Derive_IndustryMission_NoCombatBoost()
    {
        // 10/2 = 5, no combat, no gather (no objectives) = 5
        var r = MakeRecord(missionLevel: 10, subclass: "IndustryMission");
        Assert.Equal(5, MagnitudeDerivation.Derive(r));
    }

    [Fact]
    public void Derive_KillEnemiesObjective_InfersCombat()
    {
        // Generic Mission subclass but the objective scan finds
        // combat → same +1 boost as a BountyMission would get.
        var r = MakeRecord(
            missionLevel: 10,
            subclass: "Mission",
            steps: Steps(Step(Obj("KillEnemies"))));
        Assert.Equal(6, MagnitudeDerivation.Derive(r));
    }

    [Fact]
    public void Derive_ProtectUnitObjective_InfersCombat()
    {
        var r = MakeRecord(
            missionLevel: 10,
            subclass: "Mission",
            steps: Steps(Step(Obj("ProtectUnit"))));
        Assert.Equal(6, MagnitudeDerivation.Derive(r));
    }

    [Fact]
    public void Derive_MiningObjectiveAlone_NoCombatBoost()
    {
        // Pure gather — no combat objective, no combat subclass.
        // 10/2 = 5, no bonus = 5.
        var r = MakeRecord(
            missionLevel: 10,
            subclass: "Mission",
            steps: Steps(Step(Obj("Mining"))));
        Assert.Equal(5, MagnitudeDerivation.Derive(r));
    }

    [Fact]
    public void Derive_DefendedCollect_AddsDefendedBonus()
    {
        // KillEnemies + Mining in the same mission → combat (+1) +
        // defended-collect (+2) stacking. 10/2 = 5, +1, +2 = 8.
        var r = MakeRecord(
            missionLevel: 10,
            subclass: "Mission",
            steps: Steps(
                Step(Obj("KillEnemies")),
                Step(Obj("Mining"))));
        // Multi-step also adds +1. So: 5 + 1 (multi) + 1 (combat) + 2 (defended) = 9.
        Assert.Equal(9, MagnitudeDerivation.Derive(r));
    }

    [Fact]
    public void Derive_DefendedCollect_SalvageVariant()
    {
        // ProtectUnit + Salvage in one step → defended-collect shape.
        var r = MakeRecord(
            missionLevel: 10,
            subclass: "Mission",
            steps: Steps(Step(Obj("ProtectUnit"), Obj("Salvage"))));
        // 5 + 0 (single step) + 1 (combat) + 2 (defended) = 8.
        Assert.Equal(8, MagnitudeDerivation.Derive(r));
    }

    // ---- Outcome modifiers ----

    [Fact]
    public void Derive_Completed_NoOutcomePenalty()
    {
        var r = MakeRecord(missionLevel: 10, outcome: Outcome.Completed);
        Assert.Equal(5, MagnitudeDerivation.Derive(r));
    }

    [Fact]
    public void Derive_Failed_AppliesMinusOne()
    {
        var r = MakeRecord(missionLevel: 10, outcome: Outcome.Failed);
        Assert.Equal(4, MagnitudeDerivation.Derive(r));  // 5 - 1
    }

    [Fact]
    public void Derive_Abandoned_AppliesMinusTwo()
    {
        var r = MakeRecord(missionLevel: 10, outcome: Outcome.Abandoned);
        Assert.Equal(3, MagnitudeDerivation.Derive(r));  // 5 - 2
    }

    [Fact]
    public void Derive_ActiveMission_NoOutcomePenalty()
    {
        // Null outcome = still in flight. Treated as no decay (the
        // reach formula uses in-flight records for the active window,
        // not for gossip propagation, so this value mostly doesn't
        // matter — but it must not crash on null outcome).
        var r = MakeRecord(missionLevel: 10, outcome: null);
        Assert.Equal(5, MagnitudeDerivation.Derive(r));
    }

    // ---- Clamping ----

    [Fact]
    public void Derive_ClampsToCeilingTen_ForHighMagnitudeMissions()
    {
        // Level 20 + multi-step + defended-collect = 10 + 1 + 1 + 2 = 14 → clamped 10.
        var r = MakeRecord(
            missionLevel: 20,
            subclass: "BountyMission",
            steps: Steps(
                Step(Obj("KillEnemies")),
                Step(Obj("Mining"))));
        Assert.Equal(10, MagnitudeDerivation.Derive(r));
    }

    [Fact]
    public void Derive_AbandonedAtLowLevel_ClampsToOne()
    {
        // Level 2, abandoned = 1 - 2 = -1 → clamped 1.
        var r = MakeRecord(missionLevel: 2, outcome: Outcome.Abandoned);
        Assert.Equal(1, MagnitudeDerivation.Derive(r));
    }

    // ---- Fixtures ----

    private static MissionRecord MakeRecord(
        int missionLevel = 10,
        int playerLevel = 0,
        string subclass = "Mission",
        IReadOnlyList<MissionStepDefinition>? steps = null,
        Outcome? outcome = Outcome.Completed)
    {
        var timeline = new List<TimelineEntry>
        {
            new(TimelineState.Accepted, GameSeconds: 100, RealUtc: "2026-04-24T00:00:00Z"),
        };
        if (outcome is { } o)
        {
            timeline.Add(new TimelineEntry(
                State: o switch
                {
                    Outcome.Completed => TimelineState.Completed,
                    Outcome.Failed    => TimelineState.Failed,
                    _                 => TimelineState.Abandoned,
                },
                GameSeconds: 200,
                RealUtc: "2026-04-24T00:01:00Z"));
        }

        return new MissionRecord(
            StoryId: "",
            MissionInstanceId: "test-mission",
            MissionName: "Test",
            MissionSubclass: subclass,
            MissionLevel: missionLevel,
            SourceStationId: null, SourceStationName: null,
            SourceSystemId: null, SourceSystemName: null,
            SourceSectorId: null, SourceSectorName: null,
            SourceFaction: null,
            TargetStationId: null, TargetStationName: null, TargetSystemId: null,
            PlayerLevel: playerLevel,
            PlayerShipName: null, PlayerShipLevel: null, PlayerCurrentSystemId: null,
            Steps: steps ?? new List<MissionStepDefinition>(),
            Rewards: new List<MissionRewardSnapshot>(),
            Timeline: timeline);
    }

    private static IReadOnlyList<MissionStepDefinition> Steps(params MissionStepDefinition[] steps) => steps;

    private static MissionStepDefinition Step(params MissionObjectiveDefinition[] objectives) =>
        new(Description: null, RequireAllObjectives: true, Hidden: false,
            Objectives: objectives);

    private static MissionObjectiveDefinition Obj(string type) =>
        new(Type: type, Fields: null);
}
