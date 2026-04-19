using Xunit;

namespace VGAnima.Tests;

public class SmokeTest
{
    [Fact]
    public void ProjectReferences_VGAnima()
    {
        Assert.Equal("vganima", Plugin.PluginGuid);
    }
}
