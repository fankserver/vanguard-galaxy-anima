using Source.MissionSystem;

namespace VGAnima.Missions;

/// <summary>Produces a <see cref="Mission"/> for a converted bar patron, or
/// <c>null</c> if the source declines (e.g. the station's faction doesn't
/// permit the configured mission types).</summary>
internal interface IMissionSource
{
    Mission? Generate(MissionContext ctx);
}
