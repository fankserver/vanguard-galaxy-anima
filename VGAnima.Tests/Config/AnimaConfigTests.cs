using System.IO;
using BepInEx.Configuration;
using VGAnima.Config;
using Xunit;

namespace VGAnima.Tests.Config;

public class AnimaConfigTests
{
    private static ConfigFile NewConfigFile()
    {
        // BepInEx ConfigFile needs a backing path; use a throwaway temp file per-test.
        var path = Path.Combine(Path.GetTempPath(), $"vganima-cfg-{System.Guid.NewGuid():N}.cfg");
        return new ConfigFile(path, saveOnInit: false);
    }

    [Fact]
    public void Llm_Enabled_DefaultsToFalse()
    {
        var cfg = new AnimaConfig(NewConfigFile());
        Assert.False(cfg.LlmEnabled.Value);
    }

    [Fact]
    public void Llm_BaseUrl_DefaultsToEmpty()
    {
        var cfg = new AnimaConfig(NewConfigFile());
        Assert.Equal(string.Empty, cfg.LlmBaseUrl.Value);
    }

    [Fact]
    public void Llm_Model_DefaultsToQwen()
    {
        var cfg = new AnimaConfig(NewConfigFile());
        Assert.Equal("qwen", cfg.LlmModel.Value);
    }

    [Fact]
    public void Llm_TimeoutSeconds_DefaultsTo60()
    {
        var cfg = new AnimaConfig(NewConfigFile());
        Assert.Equal(60, cfg.LlmTimeoutSeconds.Value);
    }

    [Fact]
    public void Llm_ApiKey_DefaultsToEmpty()
    {
        var cfg = new AnimaConfig(NewConfigFile());
        Assert.Equal(string.Empty, cfg.LlmApiKey.Value);
    }

    [Fact]
    public void Llm_EnableThinking_DefaultsToFalse()
    {
        var cfg = new AnimaConfig(NewConfigFile());
        Assert.False(cfg.LlmEnableThinking.Value);
    }

    [Fact]
    public void Llm_MaxTokens_DefaultsTo1200()
    {
        var cfg = new AnimaConfig(NewConfigFile());
        Assert.Equal(1200, cfg.LlmMaxTokens.Value);
    }

    [Fact]
    public void Llm_Temperature_DefaultsTo0p8()
    {
        var cfg = new AnimaConfig(NewConfigFile());
        Assert.Equal(0.8f, cfg.LlmTemperature.Value);
    }

    [Fact]
    public void Llm_ValuesPersistAcrossReload()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vganima-cfg-{System.Guid.NewGuid():N}.cfg");
        var file1 = new ConfigFile(path, saveOnInit: false);
        var cfg1 = new AnimaConfig(file1);
        cfg1.LlmEnabled.Value = true;
        cfg1.LlmBaseUrl.Value = "https://example/v1";
        cfg1.LlmTimeoutSeconds.Value = 42;
        cfg1.LlmMaxTokens.Value = 2048;
        cfg1.LlmTemperature.Value = 0.3f;
        file1.Save();

        var file2 = new ConfigFile(path, saveOnInit: false);
        var cfg2 = new AnimaConfig(file2);
        Assert.True(cfg2.LlmEnabled.Value);
        Assert.Equal("https://example/v1", cfg2.LlmBaseUrl.Value);
        Assert.Equal(42, cfg2.LlmTimeoutSeconds.Value);
        Assert.Equal(2048, cfg2.LlmMaxTokens.Value);
        Assert.Equal(0.3f, cfg2.LlmTemperature.Value);
    }
}
