using BepInEx.Configuration;

namespace VGAnima.Config;

/// <summary>Typed accessors for all BepInEx <see cref="ConfigEntry{T}"/> bindings.
/// Constructed once in <see cref="Plugin.Awake"/>; passed around by value so
/// every consumer sees the same config source.</summary>
internal sealed class AnimaConfig
{
    public ConfigEntry<bool>   Enabled         { get; }
    public ConfigEntry<float>  MissionChance   { get; }
    public ConfigEntry<string> MissionTypes    { get; }
    public ConfigEntry<string> LlmBackend      { get; }
    public ConfigEntry<string> LlmEndpoint     { get; }
    public ConfigEntry<string> LlmApiKey       { get; }
    public ConfigEntry<string> LlmModel        { get; }

    public AnimaConfig(ConfigFile cf)
    {
        Enabled = cf.Bind("General", "Enabled", true,
            "Master toggle. When false, VGAnima does nothing.");
        MissionChance = cf.Bind("General", "MissionChance", 1.0f,
            "Per-salesman conversion probability (0.0..1.0). 1.0 means every salesman offers a board mission.");
        MissionTypes = cf.Bind("General", "MissionTypes", "Courier",
            "Comma-separated list of MissionGenerator identifiers eligible for conversion. " +
            "v0.1 supports Courier only.");

        LlmBackend = cf.Bind("LLM", "Backend", "static",
            "Pitch text backend. v0.1 supports only 'static' (templated strings). " +
            "v0.2 adds 'openai' (OpenAI-compatible HTTP endpoints).");
        LlmEndpoint = cf.Bind("LLM", "Endpoint", "https://api.openai.com/v1",
            "OpenAI-compatible base URL. Change to point at Ollama, LM Studio, Groq, etc.");
        LlmApiKey = cf.Bind("LLM", "ApiKey", string.Empty,
            "Bearer token for the LLM endpoint. Never logged.");
        LlmModel = cf.Bind("LLM", "Model", "gpt-4o-mini",
            "Model identifier passed to the LLM endpoint.");
    }
}
