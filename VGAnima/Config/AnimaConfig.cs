using BepInEx.Configuration;

namespace VGAnima.Config;

/// <summary>Typed accessors for every BepInEx <see cref="ConfigEntry{T}"/> the
/// plugin binds. Constructed once in <see cref="Plugin.Awake"/>; passed around
/// by value so every consumer sees the same config source.</summary>
internal sealed class AnimaConfig
{
    public ConfigEntry<bool>   Enabled           { get; }
    public ConfigEntry<float>  MissionChance     { get; }

    public ConfigEntry<bool>   LlmEnabled           { get; }
    public ConfigEntry<string> LlmBaseUrl           { get; }
    public ConfigEntry<string> LlmModel             { get; }
    public ConfigEntry<int>    LlmTimeoutSeconds    { get; }
    public ConfigEntry<string> LlmApiKey            { get; }
    public ConfigEntry<bool>   LlmEnableThinking    { get; }
    public ConfigEntry<int>    LlmMaxTokens         { get; }
    public ConfigEntry<float>  LlmTemperature       { get; }

    public ConfigEntry<int>    StageDirectionLevel  { get; }
    public ConfigEntry<bool>   IncludePlayerJournal { get; }

    public AnimaConfig(ConfigFile cf)
    {
        Enabled = cf.Bind("General", "Enabled", true,
            "Master toggle. When false, VGAnima does nothing.");
        MissionChance = cf.Bind("General", "MissionChance", 1.0f,
            "Per-salesman conversion probability (0.0..1.0). 1.0 converts every eligible patron into a broker.");

        LlmEnabled = cf.Bind("Llm", "Enabled", false,
            "Master switch for LLM-authored broker dialogue. When false, no broker is injected anywhere.");
        LlmBaseUrl = cf.Bind("Llm", "BaseUrl", string.Empty,
            "OpenAI-compatible endpoint base, e.g. https://host/v1. Blank disables LLM dispatch.");
        LlmModel = cf.Bind("Llm", "Model", "qwen",
            "Model identifier passed in the chat completions request body.");
        LlmTimeoutSeconds = cf.Bind("Llm", "TimeoutSeconds", 60,
            "Per-call timeout. On expiry the call is cancelled and no broker is injected. " +
            "Dispatch is fire-and-forget on a background task so a generous value only raises " +
            "the success rate — bump if your backend is slow or thinking tokens are enabled.");
        LlmApiKey = cf.Bind("Llm", "ApiKey", string.Empty,
            "Optional Bearer token. Never logged in cleartext (only as <set>/<empty>).");
        LlmEnableThinking = cf.Bind("Llm", "EnableThinking", false,
            "Passed as chat_template_kwargs.enable_thinking for vLLM Qwen. Harmless on other backends.");
        LlmMaxTokens = cf.Bind("Llm", "MaxTokens", 1200,
            "Token ceiling on the completion. 1200 comfortably covers the v1 dialogue schema.");
        LlmTemperature = cf.Bind("Llm", "Temperature", 0.8f,
            "Sampling temperature for the completion. Higher = more varied, lower = more deterministic.");

        IncludePlayerJournal = cf.Bind("Style", "IncludePlayerJournal", true,
            "Feed the broker a filtered view of past broker-mission outcomes so " +
            "they can reference player history organically (\"you've been hunting " +
            "Corsairs here lately\", \"the Steel Vultures appreciate your work\"). " +
            "Three windows per broker — local (same station), factional (same " +
            "faction elsewhere), and notable (high-magnitude events galaxy-wide). " +
            "Set to false to fall back to stateless brokers.");

        StageDirectionLevel = cf.Bind("Style", "StageDirectionLevel", 0,
            "Level of bracketed stage directions in broker dialogue (e.g. '[Spits " +
            "on floor]' / '[Glances at the door]'). 0 = off (default, plain " +
            "dialogue only); 1 = sparse (1-2 across the whole pitch/check_in/" +
            "payout); 2 = rich (most lines include one). Heads-up: TTS will " +
            "read the bracketed text aloud unless the TTS plugin filters it, " +
            "so keep this at 0 until you've verified your voice setup.");
    }
}
