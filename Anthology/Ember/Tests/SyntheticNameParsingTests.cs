using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Xunit;

namespace Prowl.Ember.Tests;

/// <summary>
/// Checks the synthetic name scanner against the names real .NET 10 Roslyn emits, so the lambda and closure
/// matching built on top of it is not relying on an assumed convention.
/// </summary>
[Trait("Category", "Build")]
public class SyntheticNameParsingTests : MigrationTestBase
{
    private const BindingFlags AllDeclared =
        BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

    [Fact]
    public void SyntheticName_Parses_RealDotNet10_LambdaClosureLocalFuncIterator()
    {
        Assembly assembly = Compile(
            "using System; using System.Collections.Generic; " +
            "public static class H { " +
            "  public static Action A; public static Action B; public static Func<int> C; " +
            "  public static void Setup() { " +
            "    A = () => GC.KeepAlive(A); " +
            "    int local = 5; B = () => GC.KeepAlive(local); " +
            "    int Local() => local + 1; C = Local; " +
            "  } " +
            "  public static IEnumerable<int> Iter() { yield return 1; yield return 2; } " +
            "}");

        var types = new List<Type>();
        void Collect(Type t)
        {
            types.Add(t);
            foreach (var nested in t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic)) Collect(nested);
        }
        foreach (var type in assembly.GetTypes().Where(t => t.DeclaringType == null)) Collect(type);

        var typeNames = types.Select(t => t.Name).Where(n => n.StartsWith('<')).ToList();
        var methodNames = types.SelectMany(t => t.GetMethods(AllDeclared)).Select(m => m.Name).Where(n => n.StartsWith('<')).ToList();

        var lambdas = methodNames.Where(n => n.StartsWith("<Setup>b__")).ToList();
        Assert.NotEmpty(lambdas);

        foreach (var lambda in lambdas)
        {
            Assert.True(SyntheticName.TryParse(lambda, out var parsed), $"failed to parse {lambda}");
            Assert.Equal(SyntheticKind.LambdaMethod, parsed.Kind);
            Assert.Equal("Setup", parsed.Scope);
            Assert.True(parsed.Ordinal >= 0);
        }

        Assert.True(SyntheticName.TryParse("<>c", out var cacheClass));
        Assert.Equal(SyntheticKind.LambdaDisplayClass, cacheClass.Kind);
        Assert.Null(cacheClass.Suffix);

        string display = Assert.Single(typeNames.Where(n => n.StartsWith("<>c__DisplayClass")));
        Assert.True(SyntheticName.TryParse(display, out var closure));
        Assert.Equal(SyntheticKind.LambdaDisplayClass, closure.Kind);
        Assert.Equal("DisplayClass", closure.Suffix);
        Assert.True(closure.Ordinal >= 0 && closure.SubOrdinal >= 0);

        string localFunction = Assert.Single(methodNames.Where(n => n.StartsWith("<Setup>g__Local")));
        Assert.True(SyntheticName.TryParse(localFunction, out var local));
        Assert.Equal(SyntheticKind.LocalFunction, local.Kind);
        Assert.Equal("Setup", local.Scope);
        Assert.Equal("Local", local.Suffix);

        string iterator = Assert.Single(typeNames.Where(n => n.StartsWith("<Iter>d__")));
        Assert.True(SyntheticName.TryParse(iterator, out var machine));
        Assert.Equal(SyntheticKind.StateMachine, machine.Kind);
        Assert.Equal("Iter", machine.Scope);
    }

    [Theory]
    [InlineData("Plain")]
    [InlineData("<>")]
    [InlineData("<Scope>")]
    [InlineData("<Scope>B__0")]
    [InlineData("<Scope>b__0trailing")]
    public void SyntheticName_Rejects_NamesThatAreNotSynthetic(string name)
        => Assert.False(SyntheticName.TryParse(name, out _));

    [Fact]
    public void SyntheticName_Parses_GenerationAndArity()
    {
        Assert.True(SyntheticName.TryParse("<Scope>c__DisplayClass1#2_3#4`5", out var parsed));

        Assert.Equal("Scope", parsed.Scope);
        Assert.Equal("DisplayClass", parsed.Suffix);
        Assert.Equal(1, parsed.Ordinal);
        Assert.Equal(2, parsed.Generation);
        Assert.Equal(3, parsed.SubOrdinal);
        Assert.Equal(4, parsed.SubGeneration);
        Assert.Equal(5, parsed.Arity);
    }

    [Fact]
    public void SyntheticName_Parses_AutoPropertyBackingField()
    {
        Assert.True(SyntheticName.TryParse("<Value>k__BackingField", out var parsed));

        Assert.Equal(SyntheticKind.AutoPropertyBackingField, parsed.Kind);
        Assert.Equal("Value", parsed.Scope);
        Assert.Equal("BackingField", parsed.Suffix);
    }
}
