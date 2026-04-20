using System.Collections.Generic;
using Source.Galaxy.POI;
using Source.MissionSystem;

namespace VGAnima.Cache;

/// <summary>Everything the broker lifecycle needs:
///   <list type="bullet">
///     <item>The injected <see cref="Mission"/> — removed from the board on patron rolloff.</item>
///     <item>Warmed TTS lines — dropped from VGTTS cache on rolloff.</item>
///     <item>The <see cref="SpaceStation"/> for mission-board access.</item>
///     <item>A <see cref="Pitched"/> flag, flipped on the first dialogue close.
///       Used to distinguish "never talked to broker" (Initial state) from
///       "mission cycled through, rewarded" (Done state) — both of which have
///       the mission absent from both board and player missions.</item>
///   </list>
/// Not a record (was one originally) because <c>Pitched</c> must be mutable.</summary>
internal sealed class ConversionRecord
{
    public Mission Mission { get; }
    public IReadOnlyList<(string Speaker, string Text)> WarmedLines { get; }
    public SpaceStation Station { get; }
    public bool Pitched { get; set; }

    public ConversionRecord(
        Mission mission,
        IReadOnlyList<(string Speaker, string Text)> warmedLines,
        SpaceStation station)
    {
        Mission = mission;
        WarmedLines = warmedLines;
        Station = station;
    }
}
