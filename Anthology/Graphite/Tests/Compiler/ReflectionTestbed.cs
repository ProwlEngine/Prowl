using System.Collections.Generic;
using System.Linq;

using Xunit;

namespace Prowl.Graphite.Compiler.Tests;


// Reusable cross-backend assertions for a compiled program's reflection. Each backend suite compiles
// the same shared shader and hands its own statically-authored expectation (the backend reflects the
// same source into different sets / bindings / names) to the same checker here, so the comparison
// logic and its failure reporting live in one place. Comparisons are order-insensitive; resource
// elements are matched by name, then every field is diffed for a readable message.
internal static class ReflectionTestbed
{
    public static void AssertStages(ShaderDescription actual, params (ShaderStages Stage, string EntryPoint)[] expected)
    {
        Assert.Equal(expected.Length, actual.Stages.Length);

        foreach ((ShaderStages stage, string entryPoint) in expected)
        {
            ShaderStageDescription s = CompilerTestHarness.StageOf(actual, stage);
            Assert.Equal(entryPoint, s.EntryPoint);
        }
    }


    // Vertex inputs keyed by shader location (the GL / Vulkan binding model).
    public static void AssertVertexLocations(
        ShaderDescription actual, params (uint Location, VertexElementFormat Format)[] expected)
    {
        Assert.Equal(expected.Length, actual.VertexLayouts.Length);

        foreach ((uint location, VertexElementFormat format) in expected)
            Assert.Equal(format, CompilerTestHarness.ElementAtLocation(actual, location).Format);
    }


    // Resource layouts compared as a whole: the set each resource lands in, plus every reflected field
    // of the element (kind, binding, stages, GL name, uniform fields). Authored per backend.
    public static void AssertResourceLayouts(ShaderDescription actual, params ResourceLayoutDescription[] expected)
    {
        Dictionary<string, (uint Set, ResourceLayoutElementDescription Element)> exp = Flatten(expected);
        Dictionary<string, (uint Set, ResourceLayoutElementDescription Element)> act = Flatten(actual.ResourceLayouts);

        HashSet<string> expNames = [.. exp.Keys];
        HashSet<string> actNames = [.. act.Keys];

        Assert.True(expNames.SetEquals(actNames),
            $"Resource set mismatch.\n  missing from output: {Join(expNames.Except(actNames))}\n  unexpected in output: {Join(actNames.Except(expNames))}");

        foreach (string name in expNames)
        {
            (uint expSet, ResourceLayoutElementDescription e) = exp[name];
            (uint actSet, ResourceLayoutElementDescription a) = act[name];

            Assert.True(expSet == actSet && e.Equals(a), Describe(name, expSet, e, actSet, a));
        }
    }


    static Dictionary<string, (uint, ResourceLayoutElementDescription)> Flatten(ResourceLayoutDescription[] layouts)
    {
        Dictionary<string, (uint, ResourceLayoutElementDescription)> map = [];

        foreach (ResourceLayoutDescription layout in layouts)
            foreach (ResourceLayoutElementDescription element in layout.Elements)
                map[PropertyID.ToString(element.Name) ?? string.Empty] = (layout.Set, element);

        return map;
    }


    static string Join(IEnumerable<string> names)
    {
        string joined = string.Join(", ", names.OrderBy(n => n));
        return joined.Length == 0 ? "(none)" : joined;
    }


    static string Describe(string name, uint expSet, ResourceLayoutElementDescription e, uint actSet, ResourceLayoutElementDescription a)
        => $"Resource '{name}' differs:\n"
            + $"  set:        expected {expSet},          actual {actSet}\n"
            + $"  kind:       expected {e.Kind},          actual {a.Kind}\n"
            + $"  binding:    expected {e.BindingIndex},  actual {a.BindingIndex}\n"
            + $"  stages:     expected {e.Stages},        actual {a.Stages}\n"
            + $"  glName:     expected '{e.GLUniformName}', actual '{a.GLUniformName}'\n"
            + $"  fields:     expected [{Fields(e)}]\n"
            + $"              actual   [{Fields(a)}]";


    static string Fields(ResourceLayoutElementDescription e)
        => string.Join(", ", e.UniformFields.Select(f => $"{PropertyID.ToString(f.Name)}@{f.Offset}+{f.Size}:{f.Type}"));
}
