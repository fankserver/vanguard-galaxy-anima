using System.Threading;
using System.Threading.Tasks;
using VGAnima.Tts;
using Xunit;

namespace VGAnima.Tests.Tts;

public class VgttsBridgeTests
{
    // When VGTTS is not loaded (normal test context — VGTTS.dll isn't in the
    // test project's references), every bridge method must be a silent no-op.
    // This guards the "user installed VGAnima without VGTTS" path.

    [Fact]
    public void IsAvailable_ReturnsFalse_WhenVgttsTypeAbsent()
    {
        var bridge = new VgttsBridge();
        Assert.False(bridge.IsAvailable);
    }

    [Fact]
    public void RegisterVoice_DoesNotThrow_WhenVgttsAbsent()
    {
        var bridge = new VgttsBridge();
        bridge.RegisterVoice("Robert Miyama", "kokoro:12");  // must be silent
    }

    [Fact]
    public async Task WarmCacheAsync_ReturnsCompletedTask_WhenVgttsAbsent()
    {
        var bridge = new VgttsBridge();
        await bridge.WarmCacheAsync("Robert Miyama", "hello world", CancellationToken.None);
    }

    [Fact]
    public void DropCache_DoesNotThrow_WhenVgttsAbsent()
    {
        var bridge = new VgttsBridge();
        bridge.DropCache("Robert Miyama", "hello world");
    }
}
