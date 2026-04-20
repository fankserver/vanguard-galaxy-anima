using System.Collections.Generic;
using Newtonsoft.Json;

namespace VGAnima.Llm;

/// <summary>DTOs for the OpenAI-compatible chat completions request body.
/// Structure matches spec Section 6:
/// <code>
/// { "model", "messages": [{role, content}...], "max_tokens",
///   "temperature", "chat_template_kwargs": { "enable_thinking" } }
/// </code>
/// Fields are <c>public</c> so <see cref="Newtonsoft.Json.JsonConvert"/>
/// can reach them — internal records wouldn't surface properties in the
/// serialized JSON.</summary>
internal sealed class LlmRequestBody
{
    [JsonProperty("model")]
    public string Model { get; set; } = string.Empty;

    [JsonProperty("messages")]
    public List<LlmMessage> Messages { get; set; } = new();

    [JsonProperty("max_tokens")]
    public int MaxTokens { get; set; }

    [JsonProperty("temperature")]
    public double Temperature { get; set; }

    [JsonProperty("chat_template_kwargs")]
    public LlmChatTemplateKwargs ChatTemplateKwargs { get; set; } = new();

    public static LlmRequestBody Build(
        string model,
        string systemPrompt,
        string userPrompt,
        int maxTokens,
        double temperature,
        bool enableThinking)
    {
        return new LlmRequestBody
        {
            Model = model,
            MaxTokens = maxTokens,
            Temperature = temperature,
            ChatTemplateKwargs = new LlmChatTemplateKwargs { EnableThinking = enableThinking },
            Messages = new List<LlmMessage>
            {
                new() { Role = "system", Content = systemPrompt },
                new() { Role = "user",   Content = userPrompt   },
            },
        };
    }
}

internal sealed class LlmMessage
{
    [JsonProperty("role")]
    public string Role { get; set; } = string.Empty;

    [JsonProperty("content")]
    public string Content { get; set; } = string.Empty;
}

internal sealed class LlmChatTemplateKwargs
{
    [JsonProperty("enable_thinking")]
    public bool EnableThinking { get; set; }
}

/// <summary>DTOs for the OpenAI response body — only the subset we need to
/// reach the assistant content string. Unknown fields are ignored during
/// deserialization (default <see cref="Newtonsoft.Json"/> behaviour).</summary>
internal sealed class LlmResponseBody
{
    [JsonProperty("choices")]
    public List<LlmChoice> Choices { get; set; } = new();
}

internal sealed class LlmChoice
{
    [JsonProperty("message")]
    public LlmMessage? Message { get; set; }
}
