using System;
using Source.Galaxy;
using Source.Galaxy.POI;
using Source.MissionSystem;
using Source.MissionSystem.Objectives;
using Source.MissionSystem.Rewards;
using Source.Player;
using Source.Util;
// Registry and reward both claim the short name "StoryMission" —
// alias the registry so new StoryMission(...) stays unambiguous.
using StoryMissionRegistry = Source.MissionSystem.StoryMission;

namespace VGAnima.Missions;

/// <summary>Legacy v1 plugin-defined <see cref="StoryMission"/> registration.
/// Retained for save-load tolerance only: in-flight saves pinning the
/// <see cref="JobsiteSurveyId"/> need the factory in the registry so
/// <see cref="Mission.FromJson(string)"/> doesn't
/// <see cref="System.Collections.Generic.KeyNotFoundException"/>.
///
/// <para>v2-mission does NOT pitch this mission to new brokers — injection
/// uses <see cref="LlmMissionAssigner"/> with per-broker storyIds. Remove
/// this type (and <see cref="TestMissionAssigner"/>) once the stale-save
/// window has passed.</para></summary>
[Obsolete("retained for legacy save compatibility; v2-mission authors missions per broker via LlmMissionAssigner")]
internal static class TestStoryMissions
{
    public const string JobsiteSurveyId = "vganima_test_jobsite_survey";

    private static bool _registered;

    /// <summary>Idempotent. Called from <c>Plugin.Awake</c> so legacy saves
    /// load cleanly.</summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        StoryMissionRegistry.Add(new StoryMissionRegistry(
            JobsiteSurveyId,
            CreateJobsiteSurvey,
            available: null,
            pickupHint: "VGAnima Broker"));
    }

    private static Mission CreateJobsiteSurvey(GamePlayer player)
    {
        var sourcePoi = MapPointOfInterest.current;

        var mission = new Mission
        {
            name            = "Jobsite Survey",
            description     = "A local broker wants a quick survey of another docking facility. Undock and dock at any space station - they don't care which, they just want the logbook entry.",
            completionText  = "Survey logged. Easy credits, come back anytime.",
            sourcePoi       = sourcePoi,
            turnIn          = sourcePoi,
            sourceFaction   = Faction.tradingGuild,
            trackedOnHud    = true,
            difficulty      = MissionDifficulty.Story,
            iconName        = "Combat",
            canBeIdled      = false,
            dynamicLevel    = true,
        };

        var step = new MissionStep();
        step.objectives.Add(new TriggerObjective
        {
            requiredAmount = 1,
            trigger        = MissionTrigger.DockedWithSpaceStation,
            description    = "Dock at any space station",
        });
        mission.steps.Add(step);

        mission.rewards.Add(new Credits
        {
            amount = GameMath.GetCreditsValue(50f, player.level),
        });
        mission.rewards.Add(new Experience
        {
            amount = GameMath.GetExperienceRewardValue(30f, player.level),
        });

        return mission;
    }
}
