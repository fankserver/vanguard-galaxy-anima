using System.Threading;
using System.Threading.Tasks;
using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Llm;

public class InMemoryLlmClientTests
{
    [Fact]
    public async Task CompleteAsync_ReturnsCannedResponse()
    {
        var client = new InMemoryLlmClient((sys, user) => "canned");
        var result = await client.CompleteAsync("sys", "user", CancellationToken.None);
        Assert.Equal("canned", result);
    }

    [Fact]
    public async Task CompleteAsync_PassesPromptsToCanner()
    {
        string? capturedSys = null;
        string? capturedUser = null;
        var client = new InMemoryLlmClient((sys, user) =>
        {
            capturedSys = sys;
            capturedUser = user;
            return "ok";
        });

        await client.CompleteAsync("system-prompt-here", "user-prompt-here", CancellationToken.None);

        Assert.Equal("system-prompt-here", capturedSys);
        Assert.Equal("user-prompt-here", capturedUser);
    }

    [Fact]
    public async Task CompleteAsync_HonoursCancellation()
    {
        var client = new InMemoryLlmClient((sys, user) => "never-returned");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<TaskCanceledException>(
            () => client.CompleteAsync("s", "u", cts.Token));
    }
}
