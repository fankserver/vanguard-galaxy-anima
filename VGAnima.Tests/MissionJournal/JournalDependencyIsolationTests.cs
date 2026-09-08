using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using VGAnima.MissionJournal;
using Xunit;

namespace VGAnima.Tests.MissionJournal;

public sealed class JournalDependencyIsolationTests
{
    private sealed class WithoutJournal : AssemblyLoadContext
    {
        internal int JournalResolutions;
        internal WithoutJournal() : base(isCollectible: true) { }
        protected override Assembly? Load(AssemblyName name)
        {
            if (name.Name != "VGMissionJournal") return null;
            JournalResolutions++;
            throw new FileNotFoundException("Journal deliberately unavailable.");
        }
    }

    [Fact]
    public void AbsentJournal_LoadsBridgeAndReturnsEmptyWithoutResolvingProducer()
    {
        var context = new WithoutJournal();
        try
        {
            var assembly = context.LoadFromAssemblyPath(typeof(VgMissionJournalBridge).Assembly.Location);
            var type = assembly.GetType(typeof(VgMissionJournalBridge).FullName!, true)!;
            var constructor = type.GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { typeof(object) }, null)!;
            foreach (var bridge in new[] { constructor.Invoke(new object?[] { null }), Activator.CreateInstance(type)! })
            {
                Assert.False((bool)type.GetProperty("IsAvailable")!.GetValue(bridge)!);
                Assert.Empty((IEnumerable)type.GetMethod("GetActiveMissions")!.Invoke(bridge, null)!);
                Assert.Empty((IEnumerable)type.GetMethod("GetMissionsInSystem")!.Invoke(bridge, new object[] { "system", 0d })!);
                Assert.Empty((IEnumerable)type.GetMethod("GetMissionsByFaction")!.Invoke(bridge, new object[] { "faction", 0d })!);
                Assert.Empty((IEnumerable)type.GetMethod("GetMissionsWithinJumps")!.Invoke(bridge,
                    new object[] { "system", 2, (Func<string, string, int>)((_, _) => 0), 0d })!);
            }
            Assert.Equal(0, context.JournalResolutions);
        }
        finally { context.Unload(); }
    }

    [Fact]
    public void TypedFactoriesCannotInlineAcrossPresenceGuard()
    {
        foreach (var name in new[] { "Open", "Wrap" })
        {
            var method = typeof(TypedJournalAdapter).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Static)!;
            Assert.True((method.MethodImplementationFlags & MethodImplAttributes.NoInlining) != 0);
            Assert.Equal(typeof(IJournalQueries), method.ReturnType);
        }
    }
}
