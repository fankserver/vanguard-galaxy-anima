using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Validated output from a successful LLM response. Immutable.
/// Cached inside <see cref="VGAnima.Cache.ConversionRecord"/> for the broker's
/// lifetime and replayed by <see cref="VGAnima.Pitch.LlmPitchProvider"/>.
///
/// <para>The dialogue properties (<see cref="Pitch"/> / <see cref="CheckIn"/> /
/// <see cref="Payout"/>) are always populated. The <see cref="Mission"/>
/// property is non-null only for the v2-mission schema — v1 (dialogue-only)
/// responses leave it null.</para>
///
/// Property names <c>Pitch</c> / <c>CheckIn</c> / <c>Payout</c> are stable —
/// <see cref="VGAnima.Pitch.LlmPitchProvider"/> keys on exactly these names.</summary>
internal sealed record LlmStory(
    IReadOnlyList<string> Pitch,
    IReadOnlyList<string> CheckIn,
    IReadOnlyList<string> Payout,
    LlmMissionBlock? Mission = null);
