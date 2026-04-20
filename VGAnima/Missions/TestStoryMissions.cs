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

/// <summary>
/// Plugin-defined <see cref="StoryMission"/> registrations. Used instead of
/// wrapping vanilla side missions (which have progress gates / narrative
/// implications we shouldn't piggyback on). Static templates for now — one
/// simple mission (talk -> dock somewhere -> done) to prove the pipeline;
/// broader content comes later.
///
/// <para>Call <see cref="Register"/> from <c>Plugin.Awake</c> <b>before</b>
/// any save loads. The vanilla factory rehydration path
/// (<c>Mission.FromJson(string)</c> -> <c>StoryMission.Get(player, id)</c>)
/// throws <see cref="System.Collections.Generic.KeyNotFoundException"/> if
/// the storyId isn't in the registry when a save is loaded. Persistence is
/// explicitly out of scope at this stage — player is expected to complete
/// or abandon the mission in the same session.</para>
/// </summary>
internal static class TestStoryMissions
{
    /// <summary>
    /// Identifier for the single test mission. Prefixed so log lines and
    /// save archaeology make the plugin origin obvious.
    /// </summary>
    public const string JobsiteSurveyId = "vganima_test_jobsite_survey";

    private static bool _registered;

    /// <summary>
    /// Idempotent: multiple calls are safe (the dict indexer overwrites),
    /// but the guard avoids burning a dict slot on a re-add.
    /// </summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        StoryMissionRegistry.Add(new StoryMissionRegistry(
            JobsiteSurveyId,
            CreateJobsiteSurvey,
            available: null,             // always available
            pickupHint: "VGAnima Broker"));
    }

    private static Mission CreateJobsiteSurvey(GamePlayer player)
    {
        // sourcePoi / turnIn snap to whichever station the broker pitched at
        // (AddMissionWithLog runs at dialogue close, so MapPointOfInterest.current
        // is the station the player's docked at). Mirrors the vanilla SideMissions
        // pattern.
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
            iconName        = "Combat",      // reuse vanilla icon name; swap to map-location icon later
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
