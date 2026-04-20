using System.Collections.Generic;
using VGAnima.Patches;

namespace VGAnima.Missions;

/// <summary>
/// Temporary assigner used while we're still validating the plugin-authored
/// <see cref="TestStoryMissions"/> flow end-to-end. Always offers the single
/// test storyId; returns <c>null</c> once it's archived (player completed it
/// on this save) or already assigned to another broker in the same bar.
///
/// <para>Swap back to <see cref="VanillaSideMissionAssigner"/> or a
/// multi-mission variant once there are more plugin-authored missions to
/// rotate through.</para>
/// </summary>
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
