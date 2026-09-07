using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using VGAnima.Llm;
using Xunit;

namespace VGAnima.Tests.Llm;

/// <summary>The regional-recognition gate has exactly one production
/// implementation, and the actual prompt-context path is the one that uses it.
/// A second copy of the gate anywhere (a probe, a future context builder) could
/// pitch preserved-but-unmaintained visit counts after recording stopped, which
/// is precisely what the omission rule exists to prevent.</summary>
public sealed class RegionalRecognitionTests
{
    private const string Helper = "VGAnima.Llm.RegionalRecognition";
    private const string Builder = "VGAnima.Llm.RegionallyKnownBuilder";

    [Fact]
    public void WithoutALivePluginTheWindowIsOmitted()
    {
        // No plugin instance in a host test: the decision degrades to "omit",
        // never to an unguarded registry read.
        Assert.Null(RegionalRecognition.ForCurrentContext());
    }

    [Fact]
    public void TheProductionGatherSiteUsesTheSharedDecisionAndNothingElseBuildsTheWindow()
    {
        using var module = ModuleDefinition.ReadModule(typeof(Plugin).Assembly.Location);
        var callers = CallersOf(module, Builder, "Build").ToArray();
        // Only the shared decision may call the builder; every other caller would be
        // a second gate.
        Assert.Equal(new[] { Helper + ".ForCurrentContext" }, callers);
        var users = CallersOf(module, Helper, "ForCurrentContext").ToArray();
        Assert.Contains("VGAnima.Patches.BarRefreshPatches.StartInjectMissionBroker", users);

        // The decision reads the live recording gate and registry itself, so no caller
        // can substitute a flag or a visit history.
        var reads = Calls(module, Helper, "ForCurrentContext");
        Assert.Contains("get_Instance", reads);
        Assert.Contains("get_VisitHistoryRecording", reads);
        Assert.Contains("get_PersistedRegistry", reads);
        Assert.Contains("get_VisitedSystems", reads);
        Assert.Contains("get_GameSeconds", reads);
    }

    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module)
    {
        IEnumerable<TypeDefinition> Walk(TypeDefinition type)
            => new[] { type }.Concat(type.NestedTypes.SelectMany(Walk));
        return module.Types.SelectMany(Walk);
    }

    private static HashSet<string> Calls(ModuleDefinition module, string owner, string method)
    {
        var type = AllTypes(module).Single(candidate => candidate.FullName == owner);
        var result = new HashSet<string>();
        foreach (var instruction in type.Methods.Single(candidate => candidate.Name == method).Body.Instructions)
            if (instruction.Operand is MethodReference call) result.Add(call.Name);
        return result;
    }

    /// <summary>Fully qualified "Type.Method" of everything that calls the named
    /// member, attributing compiler-generated iterators/closures (at any nesting
    /// depth) back to the method that declares them.</summary>
    private static IEnumerable<string> CallersOf(ModuleDefinition module, string owner, string member)
    {
        var result = new SortedSet<string>();
        foreach (var type in AllTypes(module))
            foreach (var method in type.Methods.Where(candidate => candidate.HasBody))
                if (method.Body.Instructions.Any(instruction => instruction.Operand is MethodReference call
                    && call.Name == member && call.DeclaringType.FullName == owner))
                {
                    var declaring = type;
                    var name = method.Name;
                    while (declaring.DeclaringType != null && declaring.Name.StartsWith("<"))
                    {
                        name = declaring.Name.Substring(1, declaring.Name.IndexOf('>') - 1);
                        declaring = declaring.DeclaringType;
                    }
                    result.Add(declaring.FullName + "." + name);
                }
        return result;
    }
}
