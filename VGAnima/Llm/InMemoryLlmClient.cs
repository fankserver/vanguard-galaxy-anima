using System;
using System.Threading;
using System.Threading.Tasks;

namespace VGAnima.Llm;

/// <summary>Test impl — runs the canner synchronously inside
/// <see cref="Task.FromResult{T}"/>, respecting cancellation. No real network
/// involved. Used by <c>ResponseValidatorTests</c>, <c>LlmPitchProviderTests</c>,
/// and any future integration test that needs a predictable LLM response.</summary>
internal sealed class InMemoryLlmClient : ILlmClient
{
    private readonly Func<string, string, string> _canner;

    public InMemoryLlmClient(Func<string, string, string> canner)
    {
        _canner = canner;
    }

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
            throw new TaskCanceledException();
        var result = _canner(systemPrompt, userPrompt);
        return Task.FromResult(result);
    }
}
