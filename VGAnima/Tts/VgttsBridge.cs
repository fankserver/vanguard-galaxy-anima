using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;

namespace VGAnima.Tts;

/// <summary>Soft dependency on VGTTS via reflection. If VGTTS is not installed
/// or its <c>TtsController.Instance</c> is null (e.g. Kokoro bundle missing),
/// every method is a silent no-op. Callers never have to check
/// <see cref="IsAvailable"/> unless they want to log or short-circuit.</summary>
internal sealed class VgttsBridge
{
    private const string TypeFullName = "VGTTS.Audio.TtsController";

    private readonly object?     _controller;
    private readonly MethodInfo? _registerVoice;
    private readonly MethodInfo? _warmCacheAsync;
    private readonly MethodInfo? _dropCache;

    public VgttsBridge()
    {
        var type = AccessTools.TypeByName(TypeFullName);
        if (type == null) return;  // VGTTS not loaded — all methods stay null, IsAvailable=false

        // Instance is a public static property (see VGTTS/Audio/TtsController.cs:23);
        // no access-modifier games needed.
        _controller = AccessTools.Property(type, "Instance")?.GetValue(null);
        if (_controller == null) return;  // VGTTS loaded but uninitialized

        _registerVoice  = AccessTools.Method(type, "RegisterVoice",   new[] { typeof(string), typeof(string) });
        _warmCacheAsync = AccessTools.Method(type, "WarmCacheAsync",  new[] { typeof(string), typeof(string), typeof(CancellationToken) });
        _dropCache      = AccessTools.Method(type, "DropCache",       new[] { typeof(string), typeof(string) });
    }

    public bool IsAvailable => _controller != null;

    public void RegisterVoice(string speaker, string voice)
    {
        if (_controller == null || _registerVoice == null) return;
        _registerVoice.Invoke(_controller, new object[] { speaker, voice });
    }

    public Task WarmCacheAsync(string speaker, string text, CancellationToken ct)
    {
        if (_controller == null || _warmCacheAsync == null) return Task.CompletedTask;
        var result = _warmCacheAsync.Invoke(_controller, new object[] { speaker, text, ct });
        // VGTTS's method signature returns Task<WarmResult>. We don't need the
        // result, just the awaitable. Cast to Task for the common base type.
        return result is Task t ? t : Task.CompletedTask;
    }

    public void DropCache(string speaker, string text)
    {
        if (_controller == null || _dropCache == null) return;
        _dropCache.Invoke(_controller, new object[] { speaker, text });
    }
}
