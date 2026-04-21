using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Llm;

public class FactionDisplayNamesTests
{
    [Fact]
    public void Lookup_KnownIdentifier_ReturnsDisplayName()
    {
        Assert.Equal("Corsair Syndicate",   FactionDisplayNames.Lookup("Marauders"));
        Assert.Equal("Luminate Combine",    FactionDisplayNames.Lookup("Gold"));
        Assert.Equal("Kolyatov Collective", FactionDisplayNames.Lookup("Red"));
        Assert.Equal("Stellar Industries",  FactionDisplayNames.Lookup("Blue"));
        Assert.Equal("Intertrade Network",  FactionDisplayNames.Lookup("TradingGuild"));
    }

    [Fact]
    public void Lookup_UnknownIdentifier_FallsBackToIdentifier()
    {
        Assert.Equal("NotAFaction", FactionDisplayNames.Lookup("NotAFaction"));
    }

    [Fact]
    public void Identifiers_CoverEntireFactionWhitelist()
    {
        // If the whitelist gains a new identifier, this fails — forces us
        // to add the display-name mapping in the same change.
        foreach (var id in FactionWhitelist.All)
        {
            Assert.True(FactionDisplayNames.Contains(id),
                $"FactionDisplayNames missing entry for whitelisted identifier `{id}`");
        }
    }
}
