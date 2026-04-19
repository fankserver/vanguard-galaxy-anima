using System.Collections.Generic;
using Source.Galaxy.POI;
using Source.MissionSystem;

namespace VGAnima.Cache;

/// <summary>Everything the eviction hook needs to clean up after a converted
/// patron rolls off the bar: the injected <see cref="Mission"/> (remove from
/// the station's board if still present), the warmed TTS lines (drop from
/// VGTTS cache), and the station itself (mission board accessor).</summary>
internal sealed record ConversionRecord(
    Mission                                              Mission,
    IReadOnlyList<(string Speaker, string Text)>         WarmedLines,
    SpaceStation                                         Station);
