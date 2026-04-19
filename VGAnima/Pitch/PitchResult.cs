using System.Collections.Generic;

namespace VGAnima.Pitch;

/// <summary>Output of <see cref="IPitchProvider"/>: the ordered list of lines
/// to splice into the patron's <c>dialogueLines</c>.</summary>
internal sealed record PitchResult(IReadOnlyList<string> Lines);
