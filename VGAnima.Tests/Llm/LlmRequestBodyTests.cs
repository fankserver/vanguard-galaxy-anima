using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Llm;

public class LlmRequestBodyTests
{
    [Fact]
    public void Serializes_WithOpenAiMessageShape()
    {
        var body = LlmRequestBody.Build(
            model: "qwen",
            systemPrompt: "sys",
            userPrompt: "usr",
            maxTokens: 1200,
            temperature: 0.8,
            enableThinking: false);

        var root = JObject.Parse(JsonConvert.SerializeObject(body));

        Assert.Equal("qwen", root.Value<string>("model"));
        Assert.Equal(1200, root.Value<int>("max_tokens"));
        Assert.Equal(0.8, root.Value<double>("temperature"));

        var messages = (JArray)root["messages"]!;
        Assert.Equal(2, messages.Count);
        Assert.Equal("system", messages[0].Value<string>("role"));
        Assert.Equal("sys",    messages[0].Value<string>("content"));
        Assert.Equal("user",   messages[1].Value<string>("role"));
        Assert.Equal("usr",    messages[1].Value<string>("content"));
    }

    [Fact]
    public void Serializes_WithChatTemplateKwargs_WhenEnableThinkingTrue()
    {
        var body = LlmRequestBody.Build(
            model: "qwen",
            systemPrompt: "sys",
            userPrompt: "usr",
            maxTokens: 1200,
            temperature: 0.8,
            enableThinking: true);

        var root = JObject.Parse(JsonConvert.SerializeObject(body));
        var kwargs = (JObject)root["chat_template_kwargs"]!;
        Assert.True(kwargs.Value<bool>("enable_thinking"));
    }

    [Fact]
    public void Serializes_WithChatTemplateKwargsFalse_WhenEnableThinkingFalse()
    {
        // We emit the field unconditionally so vLLM's Qwen template sees an
        // explicit false rather than relying on a server-side default that
        // could change across vLLM versions.
        var body = LlmRequestBody.Build(
            model: "qwen",
            systemPrompt: "sys",
            userPrompt: "usr",
            maxTokens: 1200,
            temperature: 0.8,
            enableThinking: false);

        var root = JObject.Parse(JsonConvert.SerializeObject(body));
        var kwargs = (JObject)root["chat_template_kwargs"]!;
        Assert.False(kwargs.Value<bool>("enable_thinking"));
    }
}
