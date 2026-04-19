using Source.Galaxy.POI;
using Source.MissionSystem;

namespace VGAnima.Pitch;

/// <summary>Inputs available to an <see cref="IPitchProvider"/> when writing
/// pitch lines for a converted bar patron.</summary>
internal sealed record PatronContext(
    string       NpcName,
    bool         IsMale,
    SpaceStation Station,
    Mission      Mission);
