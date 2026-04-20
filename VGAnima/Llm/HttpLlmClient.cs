using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace VGAnima.Llm;

/// <summary>Production impl — wraps a single shared <see cref="HttpClient"/>.
/// Per spec Section 6: POSTs to <c>{baseUrl}/chat/completions</c> with the
/// OpenAI body, respects a caller-controlled timeout via a linked CTS,
/// unwraps <c>choices[0].message.content</c>, and returns the raw string.
///
/// No retry. No streaming. No response-shape validation — that's the job of
/// <see cref="ResponseValidator"/>. A malformed response bubbles up as a
/// deserialization exception, which the orchestrator in
/// <see cref="VGAnima.Patches.BarRefreshPatches"/> logs and swallows per §10.</summary>
internal sealed class HttpLlmClient : ILlmClient, IDisposable
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly string _model;
    private readonly string? _apiKey;
    private readonly bool _enableThinking;
    private readonly int _maxTokens;
    private readonly double _temperature;
    private readonly TimeSpan _timeout;

    /// <param name="baseUrl">OpenAI-compatible endpoint base (no trailing slash enforced).</param>
    /// <param name="model">Model identifier passed in the body.</param>
    /// <param name="apiKey">Optional Bearer token. Null/empty omits the header.</param>
    /// <param name="enableThinking">Maps to <c>chat_template_kwargs.enable_thinking</c>.</param>
    /// <param name="maxTokens">Ceiling on completion length.</param>
    /// <param name="temperature">Sampling temperature.</param>
    /// <param name="timeout">Per-call timeout; enforced via CTS not HttpClient.Timeout
    /// so the caller can cancel independently.</param>
    public HttpLlmClient(
        string baseUrl,
        string model,
        string? apiKey,
        bool enableThinking,
        int maxTokens,
        double temperature,
        TimeSpan timeout)
    {
        _baseUrl = baseUrl.TrimEnd('/');
        _model = model;
        _apiKey = string.IsNullOrEmpty(apiKey) ? null : apiKey;
        _enableThinking = enableThinking;
        _maxTokens = maxTokens;
        _temperature = temperature;
        _timeout = timeout;

        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("ngrok-skip-browser-warning", "true");
    }

    public async Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct)
    {
        var body = LlmRequestBody.Build(
            model: _model,
            systemPrompt: systemPrompt,
            userPrompt: userPrompt,
            maxTokens: _maxTokens,
            temperature: _temperature,
            enableThinking: _enableThinking);

        var json = JsonConvert.SerializeObject(body);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(_timeout);

        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/chat/completions")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (_apiKey != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

        using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var parsed = JsonConvert.DeserializeObject<LlmResponseBody>(responseJson);

        if (parsed == null || parsed.Choices == null || parsed.Choices.Count == 0)
            throw new HttpRequestException("LLM response had no choices array");

        var content = parsed.Choices[0].Message?.Content;
        if (string.IsNullOrEmpty(content))
            throw new HttpRequestException("LLM response choices[0].message.content was empty");

        return content!;
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
