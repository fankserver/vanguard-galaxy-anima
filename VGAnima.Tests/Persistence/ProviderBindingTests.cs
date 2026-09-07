using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Xunit;

namespace VGAnima.Tests.Persistence;
public sealed class ProviderBindingTests
{
    private static IEnumerable<CustomAttributeArgument> PatchArguments(IEnumerable<CustomAttribute> attributes) =>
        attributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").SelectMany(a => a.ConstructorArguments);

    private static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module) =>
        module.Types.SelectMany(t => new[] { t }.Concat(t.NestedTypes));

    /// <summary>Assembly scopes a type actually depends on: signatures, fields and
    /// everything its method bodies reference.</summary>
    private static IEnumerable<string> ReferencedScopes(TypeDefinition type)
    {
        static IEnumerable<TypeReference> Unwrap(TypeReference? reference)
        {
            if (reference == null) yield break;
            if (reference is GenericInstanceType generic)
                foreach (var argument in generic.GenericArguments.SelectMany(Unwrap)) yield return argument;
            var element = reference.GetElementType();
            if (element.Scope != null) yield return element;
        }
        var references = new List<TypeReference>();
        references.AddRange(Unwrap(type.BaseType));
        references.AddRange(type.Interfaces.SelectMany(i => Unwrap(i.InterfaceType)));
        references.AddRange(type.Fields.SelectMany(f => Unwrap(f.FieldType)));
        foreach (var method in type.Methods)
        {
            references.AddRange(Unwrap(method.ReturnType));
            references.AddRange(method.Parameters.SelectMany(p => Unwrap(p.ParameterType)));
            if (!method.HasBody) continue;
            references.AddRange(method.Body.Variables.SelectMany(v => Unwrap(v.VariableType)));
            foreach (var instruction in method.Body.Instructions)
                references.AddRange(instruction.Operand switch
                {
                    TypeReference operand => Unwrap(operand),
                    MemberReference operand => Unwrap(operand.DeclaringType),
                    _ => Enumerable.Empty<TypeReference>()
                });
        }
        return references.Select(r => r.Scope.Name).Distinct();
    }

    [Fact]
    public void RemainingHooksResolveAgainstPinnedGameMetadata()
    {
        // Metadata only: inspecting UI hooks must not execute Unity or load its native UI runtime.
        using var plugin = ModuleDefinition.ReadModule(typeof(Plugin).Assembly.Location);
        using var game = ModuleDefinition.ReadModule(typeof(Source.MissionSystem.Mission).Assembly.Location);
        var count = 0;
        var types = plugin.Types.SelectMany(t => new[] { t }.Concat(t.NestedTypes));
        foreach (var type in types)
        {
            var classInfo = PatchArguments(type.CustomAttributes).ToArray();
            foreach (var method in type.Methods)
            {
                if (!method.CustomAttributes.Any(a => a.AttributeType.FullName is "HarmonyLib.HarmonyPrefix" or "HarmonyLib.HarmonyPostfix" or "HarmonyLib.HarmonyFinalizer")) continue;
                var info = classInfo.Concat(PatchArguments(method.CustomAttributes)).ToArray();
                var declaring = (TypeReference)info.Last(i => i.Type.FullName == "System.Type").Value;
                var name = (string)info.Last(i => i.Type.FullName == "System.String").Value;
                var arguments = info.Where(i => i.Type.FullName == "System.Type[]").Select(i => (CustomAttributeArgument[])i.Value).LastOrDefault();
                var targetType = game.GetType(declaring.FullName);
                Assert.NotNull(targetType);
                var matches = targetType.Methods.Where(m => m.Name == name && (arguments == null || m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(arguments.Select(a => ((TypeReference)a.Value).FullName)))).ToArray();
                Assert.True(matches.Length == 1, $"Ambiguous/missing target for {type.FullName}.{method.Name}: {declaring}.{name}");
                foreach (var parameter in method.Parameters.Where(p => !p.Name.StartsWith("__", StringComparison.Ordinal)))
                    Assert.True(matches[0].Parameters.Any(p => p.Name == parameter.Name), $"Unknown parameter {parameter.Name} on {declaring}.{name}");
                count++;
            }
        }
        Assert.True(count >= 10, "Binding inspection unexpectedly lost hook coverage.");
    }

    [Fact]
    public void SystemVisitsComeFromPublicTravelEventsInsteadOfANativeTravelHook()
    {
        using var plugin = ModuleDefinition.ReadModule(typeof(Plugin).Assembly.Location);
        var types = AllTypes(plugin).ToArray();
        foreach (var type in types)
        {
            var arguments = PatchArguments(type.CustomAttributes).Concat(type.Methods.SelectMany(m => PatchArguments(m.CustomAttributes)));
            foreach (var argument in arguments)
            {
                if (argument.Value is TypeReference target)
                    Assert.NotEqual("Behaviour.Managers.TravelManager", target.FullName);
                if (argument.Value is string name)
                    Assert.NotEqual("JumpToSystem", name);
            }
        }
        Assert.Null(typeof(Plugin).Assembly.GetType("VGAnima.Patches.SystemEntryPatch"));
        Assert.Null(typeof(Plugin).Assembly.GetType("VGAnima.Patches.SystemVisitRecorder"));

        // The observer is a pure consumer: published API contracts, no vanilla types.
        var scopes = ReferencedScopes(types.Single(t => t.FullName == "VGAnima.Persistence.SystemVisitObserver")).ToArray();
        Assert.Contains("VGModAPI.Abstractions", scopes);
        Assert.DoesNotContain("Assembly-CSharp", scopes);
        Assert.DoesNotContain("0Harmony", scopes);
    }

    [Fact]
    public void VisitObserverIsBoundThroughTheRefusalTolerantEntryPoint()
    {
        using var plugin = ModuleDefinition.ReadModule(typeof(Plugin).Assembly.Location);
        var bind = AllTypes(plugin).Single(t => t.FullName == "VGAnima.Plugin").Methods.Single(m => m.Name == "BindVisitObserver");
        var calls = bind.Body.Instructions.Select(i => i.Operand as MethodReference).Where(m => m != null).ToArray();
        Assert.Contains(calls, m => m!.Name == "TryBind" && m.DeclaringType.FullName == "VGAnima.Persistence.SystemVisitObserver");
        // A direct constructor call would let a refused subscription escape into Awake.
        Assert.DoesNotContain(calls, m => m!.Name == ".ctor" && m.DeclaringType.FullName == "VGAnima.Persistence.SystemVisitObserver");
    }

    [Fact]
    public void SlotLoadResetsVisitTrackingBeforeReadingTheSidecar()
    {
        using var plugin = ModuleDefinition.ReadModule(typeof(Plugin).Assembly.Location);
        var prefix = AllTypes(plugin).Single(t => t.FullName == "VGAnima.Patches.SaveLoadPatch").Methods.Single(m => m.Name == "Prefix");
        var instructions = prefix.Body.Instructions.ToArray();
        int IndexOf(string declaring, string name) => Array.FindIndex(instructions,
            i => i.Operand is MethodReference call && call.Name == name && call.DeclaringType.FullName == declaring);
        var reset = IndexOf("VGAnima.Persistence.SystemVisitObserver", "ResetVisitTracking");
        var read = IndexOf("VGAnima.Persistence.SidecarIO", "Read");
        Assert.True(reset >= 0 && read >= 0, "Load prefix lost its reset or sidecar read.");
        // A sidecar read failure must not skip the reset after the registry was cleared.
        Assert.True(reset < read, "Visit tracking must be reset before sidecar IO can fail.");
    }

    [Fact]
    public void RegionalRecognitionIsBuiltOnlyWhileVisitRecordingIsActive()
    {
        using var plugin = ModuleDefinition.ReadModule(typeof(Plugin).Assembly.Location);
        // The gate and the build live in the SAME shared decision, so the window can
        // never be built from a recording flag someone else decided earlier.
        var decision = AllTypes(plugin)
            .Single(t => t.FullName == "VGAnima.Llm.RegionalRecognition").Methods
            .Single(m => m.Name == "ForCurrentContext");
        Assert.Contains(decision.Body.Instructions,
            i => (i.Operand as MethodReference)?.Name == "get_VisitHistoryRecording");
        Assert.Contains(decision.Body.Instructions,
            i => (i.Operand as MethodReference)?.FullName.Contains("RegionallyKnownBuilder::Build") == true);
        // The bar context path composes through that decision instead of re-deriving it.
        var composing = AllTypes(plugin)
            .Single(t => t.FullName == "VGAnima.Patches.BarRefreshPatches").Methods
            .Where(m => m.HasBody)
            .Where(m => m.Body.Instructions.Any(i => (i.Operand as MethodReference)?.Name == "ForCurrentContext"))
            .ToArray();
        Assert.Single(composing);
    }
}
