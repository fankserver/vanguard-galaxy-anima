using System;
using Source.Galaxy.POI;
using Source.MissionSystem;
using VGAnima.Llm;
// Alias to avoid confusion with Source.MissionSystem.Rewards.StoryMission.
using StoryMissionRegistry = Source.MissionSystem.StoryMission;

namespace VGAnima.Missions;

/// <summary>Builds + registers an LLM-authored mission at broker-injection time.
/// Unlike <see cref="VanillaSideMissionAssigner"/> / the legacy
/// <c>TestMissionAssigner</c>, this runs AFTER the LLM response validates —
/// the pre-flight path no longer decides which storyId the broker will offer.
///
/// Spec §7: calls <see cref="MissionFactoryFromJson.Build"/> to materialize
/// the Mission, wraps it in a <see cref="StoryMissionRegistry"/> whose
/// factory delegate returns the pre-built instance, and registers via
/// <c>StoryMission.Add</c>. Returns the minted storyId.
///
/// Save/load caveat (spec §7): registered factories live in the vanilla
/// <c>StoryMission.allMissions</c> dict for the session's remainder. A save
/// mid-mission reloaded in the same session will rehydrate; across sessions
/// the factory is gone → <see cref="System.Collections.Generic.KeyNotFoundException"/>.
/// Documented limitation; v1.1 adds persistence.</summary>
internal sealed class LlmMissionAssigner
{
    private readonly Action<StoryMissionRegistry> _register;

    /// <summary>Production ctor — registers into the real vanilla registry
    /// via <c>StoryMission.Add</c>.</summary>
    public LlmMissionAssigner()
        : this(StoryMissionRegistry.Add)
    { }

    /// <summary>Test ctor — injects the registration action so tests can
    /// observe without touching the real Unity-bound static dict.</summary>
    public LlmMissionAssigner(Action<StoryMissionRegistry> register)
    {
        _register = register;
    }

    public string Assign(
        LlmMissionBlock block,
        int missionLevel,
        SpaceStation? brokerStation,
        string brokerSeed)
    {
        var mission = MissionFactoryFromJson.Build(
            block, missionLevel, brokerStation, brokerSeed);

        var storyId = mission.storyId;
        var entry = new StoryMissionRegistry(
            storyId,
            _ => mission,           // factory always returns the pre-built instance
            available: null,        // always available
            pickupHint: "VGAnima Broker");

        _register(entry);
        return storyId;
    }
}
