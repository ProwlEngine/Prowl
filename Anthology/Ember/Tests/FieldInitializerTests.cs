using System;
using System.Collections;
using System.Linq;
using System.Reflection;

using Xunit;

namespace Prowl.Ember.Tests;

/// <summary>
/// What a field added since the previous build starts out holding. The value is replayed from the field
/// initializer IL without running a constructor, so the shape of the constructor prologue is what these
/// exercise: a lone assignment, one preceded by a generic construction, and the same inside a generic type.
/// </summary>
[Trait("Category", "Build")]
public class FieldInitializerTests : MigrationTestBase
{
    // The same initializer in a non-generic type, to prove the generic context is what breaks it.
    [Fact]
    public void NewFieldInitializer_InPlainType_Works()
    {
        Assembly v1 = Compile(
            "public class Box { public int Id; } " +
            "public static class H { public static Box B; public static void Setup() { B = new Box { Id = 1 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class Box { public int Id; public int Count = 3; } " +
            "public static class H { public static Box B; public static void Setup() { } }");

        Migrate(v1, v2);

        object box = v2.GetType("H")!.GetField("B")!.GetValue(null)!;
        Assert.Equal(3, v2.GetType("Box")!.GetField("Count")!.GetValue(box));
    }

    // Each instance must get its own object from a "= new()" initializer, not a shared one.
    [Fact]
    public void NewFieldInitializer_IsEvaluatedPerInstance()
    {
        Assembly v1 = Compile("public class E { public int Id; } " +
            "public static class H { public static E A; public static E B; " +
            "  public static void Setup() { A = new E { Id = 1 }; B = new E { Id = 2 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; " +
            "public class E { public int Id; public List<int> Log = new List<int>(); } " +
            "public static class H { public static E A; public static E B; public static void Setup() { } }");

        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        object a = v2.GetType("H")!.GetField("A")!.GetValue(null)!;
        object b = v2.GetType("H")!.GetField("B")!.GetValue(null)!;

        object logA = eV2.GetField("Log")!.GetValue(a)!;
        object logB = eV2.GetField("Log")!.GetValue(b)!;

        Assert.NotSame(logA, logB);
    }

    // A constant initializer in a generic type, with nothing else in the constructor prologue.
    [Fact]
    public void NewFieldInitializer_InGenericType_ConstantOnly()
    {
        Assembly v1 = Compile(
            "public class Box<T> { public int Id; } " +
            "public static class H { public static Box<string> B; public static void Setup() { B = new Box<string> { Id = 1 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class Box<T> { public int Id; public int Count = 3; } " +
            "public static class H { public static Box<string> B; public static void Setup() { } }");

        var report = Migrate(v1, v2);
        object box = v2.GetType("H")!.GetField("B")!.GetValue(null)!;

        Assert.True(Equals(3, box.GetType().GetField("Count")!.GetValue(box)),
            $"Id={box.GetType().GetField("Id")!.GetValue(box)} " +
            $"Count={box.GetType().GetField("Count")!.GetValue(box)} " +
            $"diagnostics: {string.Join(" | ", report.Diagnostics.Select(d => d.ToString()))}");
    }

    // The same, but with an initializer that constructs a generic instance ahead of it in the prologue.
    [Fact]
    public void NewFieldInitializer_InGenericType_WithGenericConstruction()
    {
        Assembly v1 = Compile("using System.Collections.Generic; " +
            "public class Box<T> { public int Id; } " +
            "public static class H { public static Box<string> B; public static void Setup() { B = new Box<string> { Id = 1 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; " +
            "public class Box<T> { public int Id; public List<T> Items = new List<T>(); public int Count = 3; } " +
            "public static class H { public static Box<string> B; public static void Setup() { } }");

        var report = Migrate(v1, v2);
        object box = v2.GetType("H")!.GetField("B")!.GetValue(null)!;

        Assert.Equal(3, box.GetType().GetField("Count")!.GetValue(box));
        Assert.NotNull(box.GetType().GetField("Items")!.GetValue(box));
        Assert.True(report.Succeeded, string.Join(" | ", report.Diagnostics.Select(d => d.ToString())));
    }

    [Fact]
    public void NewFieldInitializer_InGenericType_UsesTheDeclaredValue()
    {
        Assembly v1 = Compile("using System.Collections.Generic; " +
            "public class Box<T> { public int Id; } " +
            "public static class H { public static Box<string> B; public static void Setup() { B = new Box<string> { Id = 1 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; " +
            "public class Box<T> { public int Id; public int Count = 3; public List<T> Items = new List<T>(); " +
            "  public string Label = \"boxed\"; } " +
            "public static class H { public static Box<string> B; public static void Setup() { } }");

        Migrate(v1, v2);

        object box = v2.GetType("H")!.GetField("B")!.GetValue(null)!;
        Type boxed = box.GetType();

        Assert.Equal(1, boxed.GetField("Id")!.GetValue(box));
        Assert.Equal(3, boxed.GetField("Count")!.GetValue(box));
        Assert.Equal("boxed", boxed.GetField("Label")!.GetValue(box));
        Assert.NotNull(boxed.GetField("Items")!.GetValue(box));
    }

    // A newly added field with a "= new()" initializer, carried into an existing readonly static target.
    [Fact]
    public void ReadOnlyStatic_NewFieldWithInitializer_NotDoubled()
    {
        Assembly v1 = Compile("using System.Collections.Generic; " +
            "public class Box { public int Id; } " +
            "public static class H { public static readonly Box B = new Box { Id = 1 }; }");
        _ = v1.GetType("H")!.GetField("B")!.GetValue(null);

        Assembly v2 = Compile("using System.Collections.Generic; " +
            "public class Box { public int Id; public List<int> Log = new List<int> { 42 }; } " +
            "public static class H { public static readonly Box B = new Box { Id = 1 }; }");

        Migrate(v1, v2);

        object box = v2.GetType("H")!.GetField("B")!.GetValue(null)!;
        var log = (IList)v2.GetType("Box")!.GetField("Log")!.GetValue(box)!;

        Assert.Equal(new[] { 42 }, log.Cast<int>().ToArray());
    }
}