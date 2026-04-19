using Source.Galaxy.POI;
using Source.Galaxy.POI.Station;

namespace VGAnima.Missions;

/// <summary>Inputs available to an <see cref="IMissionSource"/> when deciding
/// whether and how to generate a <see cref="Source.MissionSystem.Mission"/>
/// for a converted bar patron.</summary>
internal sealed record MissionContext(
    SpaceStation Station,
    int          StationLevel,
    BarPatron    Patron);
