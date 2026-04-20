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
}
