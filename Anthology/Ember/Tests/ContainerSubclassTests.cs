using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Xunit;

namespace Prowl.Ember.Tests;

/// <summary>
/// User types deriving from a framework container. The container's contents and the subclass's own state both
/// have to survive, and the subclass may not offer the constructor the container migration would prefer.
/// </summary>
[Trait("Category", "Build")]
public class ContainerSubclassTests : MigrationTestBase
{
    // The previous design left a List subclass to the field walk, which copied both the elements and the
    // subclass's own state.
    [Fact]
    public void ListSubclass_WithExtraState_KeepsBoth()
    {
        Assembly v1 = Compile("using System.Collections.Generic; public class E { public int Id; } " +
            "public class Bag : List<E> { public string Name; } " +
            "public static class H { public static Bag B; " +
            "  public static void Setup() { B = new Bag { Name = \"tools\" }; B.Add(new E { Id = 1 }); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public class Bag : List<E> { public string Name; } " +
            "public static class H { public static Bag B; public static void Setup() { } }");

        Migrate(v1, v2);

        object bag = v2.GetType("H")!.GetField("B")!.GetValue(null)!;
        Assert.Equal("tools", v2.GetType("Bag")!.GetField("Name")!.GetValue(bag));
        Assert.Single((IList)bag);
    }

    [Fact]
    public void ListSubclass_WithoutParameterlessConstructor_StillMigrates()
    {
        Assembly v1 = Compile("using System.Collections.Generic; public class E { public int Id; } " +
            "public class Bag : List<E> { public string Name; public Bag(string name) { Name = name; } } " +
            "public static class H { public static Bag B; " +
            "  public static void Setup() { B = new Bag(\"tools\"); B.Add(new E { Id = 1 }); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public class Bag : List<E> { public string Name; public Bag(string name) { Name = name; } } " +
            "public static class H { public static Bag B; public static void Setup() { } }");

        var report = Migrate(v1, v2);

        object? bag = v2.GetType("H")!.GetField("B")!.GetValue(null);
        Assert.NotNull(bag);
        Assert.Same(v2.GetType("Bag"), bag!.GetType());
        Assert.Single((IList)bag);
        Assert.True(report.Succeeded, string.Join(" | ", report.Errors.Select(d => d.ToString())));
    }

    [Fact]
    public void GenericListSubclass_KeepsExtraState()
    {
        Assembly v1 = Compile("using System.Collections.Generic; public class E { public int Id; } " +
            "public class Bag<T> : List<T> { public string Name; } " +
            "public static class H { public static Bag<E> B; " +
            "  public static void Setup() { B = new Bag<E> { Name = \"g\" }; B.Add(new E { Id = 1 }); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public class Bag<T> : List<T> { public string Name; } " +
            "public static class H { public static Bag<E> B; public static void Setup() { } }");

        Migrate(v1, v2);

        object bag = v2.GetType("H")!.GetField("B")!.GetValue(null)!;
        Assert.Equal("g", bag.GetType().GetField("Name")!.GetValue(bag));
        Assert.Single((IList)bag);
    }

    [Fact]
    public void DeepContainerSubclass_KeepsEveryLevel()
    {
        Assembly v1 = Compile("using System.Collections.Generic; public class E { public int Id; } " +
            "public class Middle : List<E> { public int Tag; } " +
            "public class Leaf : Middle { public string Name; } " +
            "public static class H { public static Leaf L; " +
            "  public static void Setup() { L = new Leaf { Tag = 3, Name = \"leaf\" }; L.Add(new E { Id = 1 }); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public class Middle : List<E> { public int Tag; } " +
            "public class Leaf : Middle { public string Name; } " +
            "public static class H { public static Leaf L; public static void Setup() { } }");

        Migrate(v1, v2);

        object leaf = v2.GetType("H")!.GetField("L")!.GetValue(null)!;
        Assert.Equal(3, v2.GetType("Middle")!.GetField("Tag")!.GetValue(leaf));
        Assert.Equal("leaf", v2.GetType("Leaf")!.GetField("Name")!.GetValue(leaf));
        Assert.Single((IList)leaf);
    }

    // The element type did not move, so the container carries over untouched, subclass state included.
    [Fact]
    public void ContainerSubclass_WithUnchangedElements_IsUntouched()
    {
        Assembly v1 = Compile("using System.Collections.Generic; public class E { public int Id; } " +
            "public class Bag : List<string> { public string Name; } " +
            "public static class H { public static Bag B; public static E Other; " +
            "  public static void Setup() { B = new Bag { Name = \"n\" }; B.Add(\"x\"); Other = new E { Id = 1 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public class Bag : List<string> { public string Name; } " +
            "public static class H { public static Bag B; public static E Other; public static void Setup() { } }");

        Migrate(v1, v2);

        object bag = v2.GetType("H")!.GetField("B")!.GetValue(null)!;
        Assert.Same(v2.GetType("Bag"), bag.GetType());
        Assert.Equal("n", v2.GetType("Bag")!.GetField("Name")!.GetValue(bag));
        Assert.Equal(new[] { "x" }, ((IList)bag).Cast<string>().ToArray());
    }

    [Fact]
    public void DictionarySubclass_WithExtraState_KeepsBoth()
    {
        Assembly v1 = Compile("using System.Collections.Generic; public class E { public int Id; } " +
            "public class Registry : Dictionary<string, E> { public string Name; } " +
            "public static class H { public static Registry R; " +
            "  public static void Setup() { R = new Registry { Name = \"reg\" }; R[\"a\"] = new E { Id = 1 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public class Registry : Dictionary<string, E> { public string Name; } " +
            "public static class H { public static Registry R; public static void Setup() { } }");

        Migrate(v1, v2);

        object registry = v2.GetType("H")!.GetField("R")!.GetValue(null)!;
        Assert.Single((IDictionary)registry);
        Assert.Equal("reg", v2.GetType("Registry")!.GetField("Name")!.GetValue(registry));
    }

    [Fact]
    public void DictionarySubclass_WithoutParameterlessConstructor_StillMigrates()
    {
        Assembly v1 = Compile("using System.Collections.Generic; public class E { public int Id; } " +
            "public class Registry : Dictionary<string, E> { public string Name; public Registry(string n) { Name = n; } } " +
            "public static class H { public static Registry R; " +
            "  public static void Setup() { R = new Registry(\"reg\"); R[\"a\"] = new E { Id = 1 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public class Registry : Dictionary<string, E> { public string Name; public Registry(string n) { Name = n; } } " +
            "public static class H { public static Registry R; public static void Setup() { } }");

        var report = Migrate(v1, v2);

        object? registry = v2.GetType("H")!.GetField("R")!.GetValue(null);
        Assert.NotNull(registry);
        Assert.Same(v2.GetType("Registry"), registry!.GetType());
        Assert.Equal("reg", v2.GetType("Registry")!.GetField("Name")!.GetValue(registry));

        Assert.True(((IDictionary)registry).Count == 1,
            $"count={((IDictionary)registry).Count} diagnostics: " +
            string.Join(" | ", report.Diagnostics.Select(d => d.ToString())));
    }

    [Fact]
    public void HashSetSubclass_WithExtraState_KeepsBoth()
    {
        Assembly v1 = Compile("using System.Collections.Generic; public class E { public int Id; } " +
            "public class Bucket : HashSet<E> { public string Name; } " +
            "public static class H { public static Bucket B; " +
            "  public static void Setup() { B = new Bucket { Name = \"set\" }; B.Add(new E { Id = 1 }); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public class Bucket : HashSet<E> { public string Name; } " +
            "public static class H { public static Bucket B; public static void Setup() { } }");

        Migrate(v1, v2);

        object bucket = v2.GetType("H")!.GetField("B")!.GetValue(null)!;
        Assert.Equal("set", v2.GetType("Bucket")!.GetField("Name")!.GetValue(bucket));
        Assert.Single((IEnumerable)bucket);
    }

    [Fact]
    public void SortedDictionarySubclass_KeepsComparerAndState()
    {
        const string body =
            "using System; using System.Collections.Generic; public class E { public int Id; } " +
            "public class Desc : IComparer<string> { public int Compare(string a, string b) => string.CompareOrdinal(b, a); } " +
            "public class Ordered : SortedDictionary<string, E> { public string Name; " +
            "  public Ordered() : base(new Desc()) { } } " +
            "public static class H { public static Ordered O; " +
            "  public static void Setup() { O = new Ordered { Name = \"o\" }; O[\"a\"] = new E { Id = 1 }; O[\"b\"] = new E { Id = 2 }; } }";

        Assembly v1 = Compile(body);
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(body.Replace("public int Id;", "public int Id; public int Extra;"));
        Migrate(v1, v2);

        object ordered = v2.GetType("H")!.GetField("O")!.GetValue(null)!;

        Assert.Equal("o", v2.GetType("Ordered")!.GetField("Name")!.GetValue(ordered));

        var keys = ((IDictionary)ordered).Keys.Cast<string>().ToArray();
        Assert.Equal(new[] { "b", "a" }, keys); // the descending comparer survived
    }
}