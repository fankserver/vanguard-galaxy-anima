using System;

namespace VGAnima.Llm;

/// <summary>Thrown by <see cref="ResponseValidator"/> when the raw LLM response
/// fails any schema rule. Caught by the orchestrator in
/// <see cref="VGAnima.Patches.BarRefreshPatches"/>, logged per spec §10, and
/// results in no broker injection. Message identifies the failing field and
/// rule for log-driven diagnosis.</summary>
internal sealed class LlmValidationException : Exception
{
    public LlmValidationException(string message) : base(message) { }
    public LlmValidationException(string message, Exception inner) : base(message, inner) { }
}
