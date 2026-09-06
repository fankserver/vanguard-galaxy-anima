using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Llm;

public sealed class CrewDisplayTests
{
    [Theory]
    [InlineData("Charlie", "Sniper", "Churchill", "Charlie 'Sniper' Churchill")]
    [InlineData("Charlie", null, "Churchill", "Charlie Churchill")]
    [InlineData(null, "Sniper", null, "'Sniper'")]
    [InlineData("", "", "", "")]
    public void CurrentOfficerNamesKeepExistingPromptFormat(string? first, string? callsign, string? last, string expected)
        => Assert.Equal(expected, GameStateView.BuildCrewDisplayName(first, callsign, last));
}
