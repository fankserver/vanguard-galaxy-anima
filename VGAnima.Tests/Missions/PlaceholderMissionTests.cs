using VGAnima.Missions;
using Xunit;

namespace VGAnima.Tests.Missions;

public class PlaceholderMissionTests
{
    [Fact]
    public void Build_SetsStoryIdOnReturnedMission()
    {
        var mission = PlaceholderMission.Build("vganima_llm_missing_xyz");
        Assert.Equal("vganima_llm_missing_xyz", mission.storyId);
    }

    [Fact]
    public void Build_ProducesZeroStepsSoVanillaTreatsItAsComplete()
    {
        var mission = PlaceholderMission.Build("vganima_llm_missing_xyz");
        Assert.Empty(mission.steps);
    }

    [Fact]
    public void Build_PopulatesNameAndDescriptionSoUiDoesntCrashOnNull()
    {
        var mission = PlaceholderMission.Build("vganima_llm_missing_xyz");
        Assert.False(string.IsNullOrEmpty(mission.name));
        Assert.False(string.IsNullOrEmpty(mission.description));
    }
}
