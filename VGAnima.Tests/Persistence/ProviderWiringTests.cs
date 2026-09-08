using System;
using System.Linq;
using BepInEx;
using VGAnima.Missions;
using VGModAPI;
using Xunit;

namespace VGAnima.Tests.Persistence;
public sealed class ProviderWiringTests
{
    [Fact]
    public void RequiresApiBeforeAwakeAndRetiresDirectLifecyclePatches()
    {
        var type = typeof(Plugin);
        var dependency = type.GetCustomAttributes(typeof(BepInDependency), false).Cast<BepInDependency>().Single(d => d.DependencyGUID == ModApi.PluginId);
        var metadata = type.GetCustomAttributesData().Single(a => a.AttributeType == typeof(BepInDependency) && Equals(a.ConstructorArguments[0].Value, ModApi.PluginId));
        // This build also consumes the owned bar contracts introduced in API 0.1.31.
        Assert.Equal("0.1.31", metadata.ConstructorArguments[1].Value);
        Assert.Equal(BepInDependency.DependencyFlags.HardDependency, dependency.Flags);
        Assert.Null(type.Assembly.GetType("VGAnima.Patches.MissionLifecyclePatches"));
        Assert.Equal(new Version(Plugin.PluginVersion), new Version(type.Assembly.GetName().Version!.ToString(3)));
    }
    [Fact]
    public void UnavailableProviderRefusesBeforeBuildingOrRegisteringMission()
    {
        var registered = false;
        var assigner = new LlmMissionAssigner(_ => registered = true, null, null, () => false);
        // Null input would fail inside the factory if the gate ran after world-building began.
        var error = Assert.Throws<InvalidOperationException>(() => assigner.Assign(null!, 1, null, "seed"));
        Assert.Contains("no world or registry changes", error.Message);
        Assert.False(registered);
    }
}
