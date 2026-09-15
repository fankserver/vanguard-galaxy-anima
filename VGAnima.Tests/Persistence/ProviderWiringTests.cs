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
        // This build consumes the 0.2.x service-root contracts (ModApi.Services,
        // typed Availability, transitioned events, owned bar declarations).
        // Per the API owner there is deliberately NO runtime exact-minor upper
        // gate: the hard dependency carries the floor and typed service
        // Availability carries health.
        Assert.Equal("0.2.8", metadata.ConstructorArguments[1].Value);
        Assert.Equal(BepInDependency.DependencyFlags.HardDependency, dependency.Flags);
        Assert.Null(type.Assembly.GetType("VGAnima.Patches.MissionLifecyclePatches"));
        // Save/load flush moved to witnessed Lifecycle SaveSucceeded; the
        // blind Store postfix is retired. Pre-deserialization construction
        // hooks (endorsed unsupported-timing boundary) remain.
        Assert.Null(type.Assembly.GetType("VGAnima.Patches.SaveWritePatch"));
        Assert.NotNull(type.Assembly.GetType("VGAnima.Patches.SaveLoadPatch"));
        Assert.NotNull(type.Assembly.GetType("VGAnima.Patches.MissionLookupPatch"));
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
