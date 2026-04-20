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

        // Use the STATIC wrapper MissionGenerator.GenerateMission(...) — not the
        // instance method. The wrapper calls the instance generator to build the
        // Mission and then sets Mission.sourcePoi, sourceFaction, sourceName,
        // turnIn, and difficulty on the result. Calling the instance directly
        // leaves those fields null and MissionDetails.ShowMission NREs on them.
        //   static: Mission GenerateMission(MissionGenerator gen, MissionDifficulty,
        //                                   MapPointOfInterest poi, SeededRandom,
        //                                   Nullable<TargetLayer>)
        try
        {
            return MissionGenerator.GenerateMission(generator, MissionDifficulty.Normal, station, rng, null);
        }
        catch (System.Exception ex)
        {
            Plugin.Log.LogWarning($"[vganima] MissionGenerator.GenerateMission({type}) threw: {ex.Message}");
            return null;
        }
    }
}
