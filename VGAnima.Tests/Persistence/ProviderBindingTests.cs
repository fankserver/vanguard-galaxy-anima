using System;
using System.Collections.Generic;
using System.Linq;
using Mono.Cecil;
using Xunit;

namespace VGAnima.Tests.Persistence;
public sealed class ProviderBindingTests
{
    private static IEnumerable<CustomAttributeArgument> PatchArguments(IEnumerable<CustomAttribute> attributes) =>
        attributes.Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").SelectMany(a => a.ConstructorArguments);

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
}
