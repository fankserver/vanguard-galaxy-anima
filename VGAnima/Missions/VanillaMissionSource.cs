using System.Collections.Generic;
using HarmonyLib;
using Source.MissionSystem;

namespace VGAnima.Missions;

/// <summary>Wraps the game's own <see cref="MissionGenerator"/> factory.
/// Picks one of the configured mission types at random (well — at call time;
/// per-call randomness is fine since patrons only convert once each) and
/// asks the concrete generator to build a real <see cref="Mission"/>.
///
/// The call shape matches what <c>MissionGenerator.GenerateRandomMission</c>
/// does internally: get the named generator via <c>Get</c>, then invoke its
/// instance <c>GenerateMission</c> with the station's seeded RNG.</summary>
internal sealed class VanillaMissionSource : IMissionSource
{
    private readonly IReadOnlyList<string> _missionTypes;

    public VanillaMissionSource(IReadOnlyList<string> missionTypes)
    {
        _missionTypes = missionTypes;
    }

    public Mission? Generate(MissionContext ctx)
    {
        if (_missionTypes.Count == 0) return null;

        var station = ctx.Station;
        if (station.missionBoard == null) return null;

        // Deterministic-enough: pick the first configured type. v0.1 ships with
        // "Courier" only, so randomness would be pointless here. When multiple
        // types are supported, swap for station.missionBoard.GetSeededRandom()
        // -based selection.
        var type = _missionTypes[0];

        var generator = MissionGenerator.Get(type);
        if (generator == null)
        {
            Plugin.Log.LogWarning($"[vganima] Unknown MissionGenerator type: '{type}' — skipping");
            return null;
        }

        // MissionBoard.GetSeededRandom is `private` at runtime (the publicized
        // stub lies). Invoke via Traverse to bypass access checks.
        var rng = Traverse.Create(station.missionBoard)
            .Method("GetSeededRandom")
            .GetValue<SeededRandom>();

        // Instance GenerateMission signature (4 params, verified via IL dump):
        //   Mission GenerateMission(MapPointOfInterest poi, MissionDifficulty difficulty,
        //                           SeededRandom random, Nullable<TargetLayer> targetLayer)
        // SpaceStation : MapPointOfInterest so we can pass it directly. Difficulty
        // defaults to Normal; the game's own MissionBoard path uses MissionDifficultyExtension
        // to randomise across unlocked tiers, but we prefer the simple default here.
        // targetLayer is left null so the generator picks its own default.
        try
        {
            return generator.GenerateMission(station, MissionDifficulty.Normal, rng, null);
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogWarning($"[vganima] MissionGenerator.{type}.GenerateMission threw: {ex.Message}");
            return null;
        }
    }
}
