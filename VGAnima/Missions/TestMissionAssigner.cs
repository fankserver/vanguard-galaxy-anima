using System;
using System.Collections.Generic;
using VGAnima.Patches;

namespace VGAnima.Missions;

/// <summary>Legacy assigner from v1. Retained for save-load tolerance:
/// if a player has an in-flight save pinning the old
/// <see cref="TestStoryMissions.JobsiteSurveyId"/> storyId,
/// <see cref="TestStoryMissions.Register"/> keeps the factory in the vanilla
/// registry so the save doesn't <see cref="KeyNotFoundException"/>-crash on
/// load. That registration is still wired in <c>Plugin.Awake</c>.
///
/// <para>The v2-mission pipeline does NOT call this assigner — broker
/// injection now uses <see cref="LlmMissionAssigner"/> post-LLM instead of a
/// pre-flight storyId decision. Remove this type (and
/// <see cref="TestStoryMissions"/>) once we're confident nobody has stale
/// saves referencing the legacy storyId.</para></summary>
[Obsolete("retained for legacy save compatibility; v2-mission uses LlmMissionAssigner")]
internal sealed class TestMissionAssigner : IMissionAssigner
{
    public string? Assign(string salesmanSeed,
                          ISet<string> alreadyAssignedInThisBar,
                          IGamePlayerView player)
    {
        var id = TestStoryMissions.JobsiteSurveyId;

        if (player.IsArchived(id)) return null;
        if (alreadyAssignedInThisBar.Contains(id)) return null;

        return id;
    }
}
