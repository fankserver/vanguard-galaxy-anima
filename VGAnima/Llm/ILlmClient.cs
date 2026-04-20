using System.Threading;
using System.Threading.Tasks;

namespace VGAnima.Llm;

/// <summary>Transport abstraction for an OpenAI-compatible chat completion
/// endpoint. Single-call, no streaming, no retry. Returns raw assistant content
/// for the validator to parse — this layer has no opinion about JSON shape.
///
/// See <see cref="HttpLlmClient"/> for the production HTTP impl and
/// <see cref="InMemoryLlmClient"/> for the test double.</summary>
internal interface ILlmClient
{
    /// <summary>Issues the completion and returns the assistant's content string.
    /// Throws <see cref="System.Net.Http.HttpRequestException"/> on transport
    /// failure and <see cref="System.Threading.Tasks.TaskCanceledException"/> on
    /// timeout or caller cancellation. Does not retry.</summary>
    Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct);
}
