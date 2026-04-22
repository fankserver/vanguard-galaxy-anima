using System.IO;
using VGAnima.Patches;
using Xunit;

namespace VGAnima.Tests;

/// <summary>One-off snapshot that writes the live system prompt to
/// <c>/tmp/vganima-system-prompt.txt</c> so external Python harnesses can
/// call the real LLM with the exact prompt VGAnima ships. Not a unit test
/// in any meaningful sense — just a plumbing trick. Skipped by default;
/// un-skip and run to refresh the snapshot.</summary>
public class PromptDumpTests
{
    [Fact(Skip = "Snapshot helper; un-skip to refresh /tmp/vganima-system-prompt.txt")]
    public void DumpSystemPrompt()
    {
        File.WriteAllText("/tmp/vganima-system-prompt.txt", BarRefreshPatches.BuildSystemPrompt());
    }
}
