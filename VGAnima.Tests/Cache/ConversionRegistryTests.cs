using VGAnima.Cache;
using Xunit;

namespace VGAnima.Tests.Cache;

public class ConversionRegistryTests
{
    // Sentinel object stands in for BarPatron (we don't need the game type to
    // test the ConditionalWeakTable semantics).
    private sealed class FakePatron { }

    [Fact]
    public void NotRegistered_IsConvertedFalse()
    {
        var reg = new ConversionRegistry<FakePatron, string>();
        Assert.False(reg.TryGet(new FakePatron(), out _));
    }

    [Fact]
    public void Register_ThenTryGet_ReturnsValue()
    {
        var reg = new ConversionRegistry<FakePatron, string>();
        var key = new FakePatron();
        reg.Register(key, "hello");

        Assert.True(reg.TryGet(key, out var value));
        Assert.Equal("hello", value);
    }

    [Fact]
    public void Register_Idempotent_SecondCallOverwrites()
    {
        var reg = new ConversionRegistry<FakePatron, string>();
        var key = new FakePatron();
        reg.Register(key, "first");
        reg.Register(key, "second");

        Assert.True(reg.TryGet(key, out var value));
        Assert.Equal("second", value);
    }

    [Fact]
    public void Remove_ClearsEntry()
    {
        var reg = new ConversionRegistry<FakePatron, string>();
        var key = new FakePatron();
        reg.Register(key, "x");
        reg.Remove(key);

        Assert.False(reg.TryGet(key, out _));
    }

    [Fact]
    public void FindByValue_ReturnsNull_WhenEmpty()
    {
        var reg = new ConversionRegistry<FakePatron, string>();
        Assert.Null(reg.FindByValue(s => s == "anything"));
    }

    [Fact]
    public void FindByValue_ReturnsMatch_WhenPredicateMatches()
    {
        var reg = new ConversionRegistry<FakePatron, string>();
        var a = new FakePatron();
        var b = new FakePatron();
        reg.Register(a, "alpha");
        reg.Register(b, "beta");

        Assert.Equal("beta", reg.FindByValue(s => s == "beta"));
    }

    [Fact]
    public void FindByValue_ReturnsNull_WhenNoMatch()
    {
        var reg = new ConversionRegistry<FakePatron, string>();
        reg.Register(new FakePatron(), "alpha");
        Assert.Null(reg.FindByValue(s => s == "omega"));
    }

    [Fact]
    public void FindByValue_ReturnsNullForReclaimedKeys_AfterGC()
    {
        // Smoke test that FindByValue doesn't throw when the table has
        // entries whose weak keys have been reclaimed. Not deterministic —
        // tolerate either null or "alpha"; just must not throw.
        var reg = new ConversionRegistry<FakePatron, string>();
        reg.Register(new FakePatron(), "alpha");
        System.GC.Collect();
        System.GC.WaitForPendingFinalizers();
        var result = reg.FindByValue(s => s == "alpha");
        Assert.True(result is null or "alpha", $"unexpected: {result}");
    }
}
