using System.Collections.Generic;

namespace VGAnima.Llm;

/// <summary>Validated output from a successful v1 LLM response. Immutable.
/// Cached inside <see cref="VGAnima.Cache.ConversionRecord"/> for the broker's
/// lifetime and replayed by <see cref="VGAnima.Pitch.LlmPitchProvider"/>.
///
/// Property names <c>Pitch</c> / <c>CheckIn</c> / <c>Payout</c> are stable —
/// <see cref="VGAnima.Pitch.LlmPitchProvider"/> keys on exactly these names.</summary>
internal sealed record LlmStory(
    IReadOnlyList<string> Pitch,
    IReadOnlyList<string> CheckIn,
    IReadOnlyList<string> Payout);
