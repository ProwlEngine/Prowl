using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

using Xunit;

namespace Prowl.Ember.Tests;

/// <summary>
/// Core migration tests: object graphs, identity and cycles, base-class hierarchies, new-field defaults,
/// reflection handles, multi-assembly swaps, and robustness.
/// </summary>
[Trait("Category", "Build")]
public class CoreMigrationTests : MigrationTestBase
{
    private const string EDef = "public class E { public int Id; }";
    private const string EDef2 = "public class E { public int Id; public int Extra; }";
    private static int Id(object e) => (int)e.GetType().GetField("Id")!.GetValue(e)!;

    [Fact]
    public void Migrate_DeepChain_DoesNotOverflow()
    {
        Assembly v1 = Compile(
            "public class Node { public int Depth; public Node Next; } " +
            "public static class H { public static Node Head; public static void Setup(){ " +
            "  Node head = null; for (int i = 0; i < 2000; i++) head = new Node { Depth = i, Next = head }; Head = head; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile(
            "public class Node { public int Depth; public Node Next; public int Extra; } " +
            "public static class H { public static Node Head; public static void Setup(){} }");
        Migrate(v1, v2);

        Type nodeV2 = v2.GetType("Node")!;
        object head = v2.GetType("H")!.GetField("Head")!.GetValue(null)!;
        Assert.Same(nodeV2, head.GetType());
        int count = 0;
        object? cur = head;
        while (cur != null) { count++; cur = nodeV2.GetField("Next")!.GetValue(cur); }
        Assert.Equal(2000, count);
    }

    [Fact]
    public void Migrate_SharedInstance_ViaThreePaths_KeepsOneIdentity()
    {
        Assembly v1 = Compile("using System.Collections.Generic; " + EDef +
            "public static class H { public static E Direct; public static E[] Arr; public static List<E> List = new(); " +
            "public static void Setup(){ var e = new E{Id=9}; Direct = e; Arr = new E[]{ e }; List.Add(e); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.Generic; " + EDef2 +
            "public static class H { public static E Direct; public static E[] Arr; public static List<E> List = new(); public static void Setup(){} }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        object direct = hV2.GetField("Direct")!.GetValue(null)!;
        var arr = (Array)hV2.GetField("Arr")!.GetValue(null)!;
        var list = (IList)hV2.GetField("List")!.GetValue(null)!;
        Assert.Same(direct, arr.GetValue(0));
        Assert.Same(direct, list[0]);
    }

    [Fact]
    public void Migrate_CycleThroughCollection_PreservesIdentity()
    {
        Assembly v1 = Compile("using System.Collections.Generic; " +
            "public class Hub { public int Id; public List<Hub> Peers = new(); } " +
            "public static class H { public static Hub Root; public static void Setup(){ Root = new Hub{Id=1}; Root.Peers.Add(Root); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.Generic; " +
            "public class Hub { public int Id; public List<Hub> Peers = new(); public int Extra; } " +
            "public static class H { public static Hub Root; public static void Setup(){} }");
        Migrate(v1, v2);

        Type hubV2 = v2.GetType("Hub")!;
        object root = v2.GetType("H")!.GetField("Root")!.GetValue(null)!;
        var peers = (IList)hubV2.GetField("Peers")!.GetValue(root)!;
        Assert.Same(root, peers[0]);
    }

    [Fact]
    public void Migrate_TypeHandleField_RepointedToNewType()
    {
        Assembly v1 = Compile(EDef +
            "public static class H { public static System.Type T; public static void Setup(){ T = typeof(E); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile(EDef2 +
            "public static class H { public static System.Type T; public static void Setup(){} }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        var t = (Type)v2.GetType("H")!.GetField("T")!.GetValue(null)!;
        Assert.Same(eV2, t);
    }

    [Fact]
    public void Migrate_MethodInfoHandleField_RepointedToNewMethod()
    {
        Assembly v1 = Compile(
            "using System.Reflection; public class C { public void Go(){} } " +
            "public static class H { public static MethodInfo M; public static void Setup(){ M = typeof(C).GetMethod(\"Go\"); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile(
            "using System.Reflection; public class C { public void Go(){} public int Extra; } " +
            "public static class H { public static MethodInfo M; public static void Setup(){} }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        var m = (MethodInfo)v2.GetType("H")!.GetField("M")!.GetValue(null)!;
        Assert.Same(cV2, m.DeclaringType);
    }

    [Fact]
    public void Migrate_SwappedInstance_InObjectArray_Migrates()
    {
        Assembly v1 = Compile(EDef +
            "public static class H { public static E Shared; public static object[] Bag; " +
            "public static void Setup(){ Shared = new E{Id=3}; Bag = new object[]{ Shared, \"str\", 42 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile(EDef2 +
            "public static class H { public static E Shared; public static object[] Bag; public static void Setup(){} }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        var bag = (object[])hV2.GetField("Bag")!.GetValue(null)!;
        Assert.Same(eV2, bag[0]!.GetType());
        Assert.Same(shared, bag[0]);
        Assert.Equal("str", bag[1]);
        Assert.Equal(42, bag[2]);
    }

    [Fact]
    public void Migrate_StaticReadonlyField_UpgradedInPlace()
    {
        Assembly v1 = Compile(
            "public class Box { public int V; } " +
            "public static class H { public static readonly Box B = new Box{ V = 5 }; }");
        _ = v1.GetType("H")!.GetField("B")!.GetValue(null);
        Assembly v2 = Compile(
            "public class Box { public int V; public int Extra; } " +
            "public static class H { public static readonly Box B = new Box{ V = 5 }; }");
        Migrate(v1, v2);

        Type boxV2 = v2.GetType("Box")!;
        object b = v2.GetType("H")!.GetField("B")!.GetValue(null)!;
        Assert.Same(boxV2, b.GetType());
        Assert.Equal(5, boxV2.GetField("V")!.GetValue(b));
    }

    [Fact]
    public void Migrate_LazyOfSwappedType_MigratesCreatedValue()
    {
        Assembly v1 = Compile("using System; " + EDef +
            "public static class H { public static E Shared; public static Lazy<E> L; " +
            "public static void Setup(){ Shared = new E{Id=7}; L = new Lazy<E>(() => Shared); var _ = L.Value; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System; " + EDef2 +
            "public static class H { public static E Shared; public static Lazy<E> L; public static void Setup(){} }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object lazy = hV2.GetField("L")!.GetValue(null)!;
        object val = lazy.GetType().GetProperty("Value")!.GetValue(lazy)!;
        Assert.Same(eV2, val.GetType());
        Assert.Same(shared, val);
    }

    [Fact]
    public void Migrate_TwoAssembliesAtOnce_MigratesBoth()
    {
        Assembly a1 = Compile("public class A { public int Id; public object B; }");
        Assembly b1 = Compile("public class B { public int Id; }");
        Type aT1 = a1.GetType("A")!;
        Type bT1 = b1.GetType("B")!;
        object bInst = Activator.CreateInstance(bT1)!;
        bT1.GetField("Id")!.SetValue(bInst, 2);
        object aInst = Activator.CreateInstance(aT1)!;
        aT1.GetField("Id")!.SetValue(aInst, 1);
        aT1.GetField("B")!.SetValue(aInst, bInst);

        Assembly a2 = Compile("public class A { public int Id; public object B; public int Extra; }");
        Assembly b2 = Compile("public class B { public int Id; public int Extra; }");

        var report = Reload(null, b => b.Replace(a1, a2).Replace(b1, b2).Root(aInst));

        object newA = report.Replaced[aInst];
        Type aT2 = a2.GetType("A")!;
        Type bT2 = b2.GetType("B")!;
        Assert.Same(aT2, newA.GetType());
        object newB = aT2.GetField("B")!.GetValue(newA)!;
        Assert.Same(bT2, newB.GetType()); // the B from the OTHER swapped assembly migrated too
        Assert.Equal(2, bT2.GetField("Id")!.GetValue(newB));
    }

    [Fact]
    // A reference held by KEPT code into a deleted assembly's type is cleared to null, so the removed assembly
    // can be collected rather than pinned by the stale reference.
    public void Migrate_WholeAssemblyDeleted_ReferenceFromKeptCodeCleared()
    {
        Assembly v1 = Compile(EDef);
        Type eV1 = v1.GetType("E")!;
        var holder = new object?[] { Activator.CreateInstance(eV1), "keep" }; // kept code holding a v1 E

        Reload(null, b => b.Remove(v1).Root(holder)); // the whole assembly is deleted, no replacement

        Assert.Null(holder[0]);        // reference to the removed-assembly type is cleared
        Assert.Equal("keep", holder[1]); // unrelated entries are untouched
    }

    [Fact]
    public void Migrate_ThreadStaticField_OnCurrentThread_Migrates()
    {
        Assembly v1 = Compile(EDef +
            "public static class H { [System.ThreadStatic] public static E T; public static void Setup(){ T = new E{Id=8}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile(EDef2 +
            "public static class H { [System.ThreadStatic] public static E T; public static void Setup(){} }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        object? t = v2.GetType("H")!.GetField("T")!.GetValue(null);
        Assert.NotNull(t);
        Assert.Same(eV2, t!.GetType());
        Assert.Equal(8, Id(t));
    }

    [Fact]
    public void Migrate_WideGraph_AllChildrenMigrate()
    {
        Assembly v1 = Compile("using System.Collections.Generic; " + EDef +
            "public static class H { public static List<E> All = new(); public static void Setup(){ for (int i=0;i<500;i++) All.Add(new E{Id=i}); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.Generic; " + EDef2 +
            "public static class H { public static List<E> All = new(); public static void Setup(){} }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        var all = (IList)v2.GetType("H")!.GetField("All")!.GetValue(null)!;
        Assert.Equal(500, all.Count);
        Assert.All(all.Cast<object>(), x => Assert.Same(eV2, x.GetType()));
        Assert.Equal(Enumerable.Range(0, 500), all.Cast<object>().Select(Id));
    }

    [Fact]
    public void UpdateReferences_MigratesStaticGraph_CarryingStateAndPreservingIdentity()
    {
        Assembly v1 = Compile(
            "public static class Root { public static Node A; public static Node B; } " +
            "public class Node { public int Value; public Node Link; }");

        Type nodeV1 = v1.GetType("Node")!;
        Type rootV1 = v1.GetType("Root")!;
        object n1 = Activator.CreateInstance(nodeV1)!;
        object n2 = Activator.CreateInstance(nodeV1)!;
        nodeV1.GetField("Value")!.SetValue(n1, 100);
        nodeV1.GetField("Value")!.SetValue(n2, 50);
        nodeV1.GetField("Link")!.SetValue(n1, n2);
        nodeV1.GetField("Link")!.SetValue(n2, n1);
        rootV1.GetField("A")!.SetValue(null, n1);
        rootV1.GetField("B")!.SetValue(null, n2);

        Assembly v2 = Compile(
            "public static class Root { public static Node A; public static Node B; } " +
            "public class Node { public int Value; public Node Link; public int Extra; }");
        Assert.NotSame(v1, v2);

        Migrate(v1, v2);

        Type nodeV2 = v2.GetType("Node")!;
        Type rootV2 = v2.GetType("Root")!;
        object newA = rootV2.GetField("A")!.GetValue(null)!;
        object newB = rootV2.GetField("B")!.GetValue(null)!;

        Assert.Same(nodeV2, newA.GetType());
        Assert.Same(nodeV2, newB.GetType());
        Assert.Equal(100, nodeV2.GetField("Value")!.GetValue(newA));
        Assert.Equal(50, nodeV2.GetField("Value")!.GetValue(newB));
        Assert.Equal(0, nodeV2.GetField("Extra")!.GetValue(newA));

        Assert.Same(newB, nodeV2.GetField("Link")!.GetValue(newA));
        Assert.Same(newA, nodeV2.GetField("Link")!.GetValue(newB));
        Assert.NotSame(newA, newB);
    }

    [Fact]
    public void UpdateReferences_NewField_TakesItsDeclaredInitializer_PerInstance()
    {
        Assembly v1 = Compile(
            "public static class Cfg { public static Node X; public static Node Y; } " +
            "public class Node { public int Hp; }");
        Type nodeV1 = v1.GetType("Node")!;
        Type cfgV1 = v1.GetType("Cfg")!;
        cfgV1.GetField("X")!.SetValue(null, Activator.CreateInstance(nodeV1));
        cfgV1.GetField("Y")!.SetValue(null, Activator.CreateInstance(nodeV1));

        Assembly v2 = Compile(
            "using System.Collections.Generic; " +
            "public static class Cfg { public static Node X; public static Node Y; } " +
            "public class Node { public int Hp; public int Shield = 25; public List<int> Tags = new(); }");

        Migrate(v1, v2);

        Type nodeV2 = v2.GetType("Node")!;
        Type cfgV2 = v2.GetType("Cfg")!;
        object x = cfgV2.GetField("X")!.GetValue(null)!;
        object y = cfgV2.GetField("Y")!.GetValue(null)!;

        Assert.Equal(25, nodeV2.GetField("Shield")!.GetValue(x));
        object xTags = nodeV2.GetField("Tags")!.GetValue(x)!;
        object yTags = nodeV2.GetField("Tags")!.GetValue(y)!;
        Assert.NotNull(xTags);
        Assert.NotSame(xTags, yTags);
    }

    [Fact]
    public void D_NewField_TypeWithoutParameterlessCtor_GetsInitializerValue()
    {
        Assembly v1 = Compile(
            "public class C { public int A; public C(int a){ A = a; } public static C Make() => new C(5); } " +
            "public static class H { public static C Obj; public static void Setup(){ Obj = C.Make(); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class C { public int A; public int B = 77; public C(int a){ A = a; } public static C Make() => new C(5); } " +
            "public static class H { public static C Obj; public static void Setup(){ } }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        object obj = v2.GetType("H")!.GetField("Obj")!.GetValue(null)!;
        Assert.Equal(5, cV2.GetField("A")!.GetValue(obj));
        Assert.Equal(77, cV2.GetField("B")!.GetValue(obj));
    }

    [Fact]
    public void D_NewFieldDefault_DoesNotRunConstructorSideEffects()
    {
        Assembly v1 = Compile(
            "public class C { [Prowl.Ember.ReloadIgnore] public static int CtorRuns; public int A = 1; public C(){ CtorRuns++; } } " +
            "public static class H { public static C Obj; public static void Setup(){ Obj = new C(); C.CtorRuns = 0; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class C { [Prowl.Ember.ReloadIgnore] public static int CtorRuns; public int A = 1; public int B = 5; public C(){ CtorRuns++; } } " +
            "public static class H { public static C Obj; public static void Setup(){ } }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        object obj = v2.GetType("H")!.GetField("Obj")!.GetValue(null)!;
        Assert.Equal(5, cV2.GetField("B")!.GetValue(obj));
        Assert.Equal(0, cV2.GetField("CtorRuns")!.GetValue(null));
    }

    [Fact]
    public void Migrate_WeakReferenceOfT_RepointsLiveTarget()
    {
        Assembly v1 = Compile(
            "using System; public class E { public int Id; } " +
            "public static class H { public static E Target; public static WeakReference<E> Ref; " +
            "public static void Setup() { Target = new E{Id=7}; Ref = new WeakReference<E>(Target); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class E { public int Id; public int Extra; } " +
            "public static class H { public static E Target; public static WeakReference<E> Ref; public static void Setup() { } }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object target = hV2.GetField("Target")!.GetValue(null)!;
        object wr = hV2.GetField("Ref")!.GetValue(null)!;

        var args = new object?[1];
        Assert.True((bool)wr.GetType().GetMethod("TryGetTarget")!.Invoke(wr, args)!);
        Assert.Same(eV2, args[0]!.GetType());
        Assert.Same(target, args[0]);
    }

    [Fact]
    public void Migrate_NullFields_AndEmptyCollections_DoNotCrash()
    {
        Assembly v1 = Compile(
            "using System.Collections.Generic; public class E { public int Id; } " +
            "public static class H { public static E Ref; public static E[] Arr = new E[0]; public static List<E> L = new(); public static Dictionary<string,E> D = new(); " +
            "public static void Setup() { Ref = null; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public static class H { public static E Ref; public static E[] Arr = new E[0]; public static List<E> L = new(); public static Dictionary<string,E> D = new(); public static void Setup() { } }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        Assert.Null(hV2.GetField("Ref")!.GetValue(null));
        Assert.Empty((IEnumerable)hV2.GetField("Arr")!.GetValue(null)!);
        Assert.Empty((IEnumerable)hV2.GetField("L")!.GetValue(null)!);
        Assert.Empty((IEnumerable)hV2.GetField("D")!.GetValue(null)!);
    }

    [Fact]
    public void InterfaceTypedStatic_HoldingSwappedImplementation()
    {
        Assembly v1 = Compile("public interface IThing { int Id { get; } } " +
            "public class Thing : IThing { public int Id { get; set; } } " +
            "public static class H { public static IThing Value; public static void Setup() { Value = new Thing { Id = 7 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("public interface IThing { int Id { get; } } " +
            "public class Thing : IThing { public int Id { get; set; } public int Extra; } " +
            "public static class H { public static IThing Value; public static void Setup() { } }");

        Migrate(v1, v2);

        object value = v2.GetType("H")!.GetField("Value")!.GetValue(null)!;
        Assert.Same(v2.GetType("Thing"), value.GetType());
        Assert.Equal(7, v2.GetType("Thing")!.GetProperty("Id")!.GetValue(value));
    }

    // A struct held in an object field, where the struct itself references a replaced type.
    [Fact]
    public void BoxedStructHoldingSwappedReference()
    {
        Assembly v1 = Compile("public class E { public int Id; } public struct Wrap { public E Inner; } " +
            "public static class H { public static object Boxed; " +
            "  public static void Setup() { Boxed = new Wrap { Inner = new E { Id = 8 } }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("public class E { public int Id; public int Extra; } public struct Wrap { public E Inner; } " +
            "public static class H { public static object Boxed; public static void Setup() { } }");

        Migrate(v1, v2);

        object boxed = v2.GetType("H")!.GetField("Boxed")!.GetValue(null)!;
        Assert.Same(v2.GetType("Wrap"), boxed.GetType());

        object inner = v2.GetType("Wrap")!.GetField("Inner")!.GetValue(boxed)!;
        Assert.Same(v2.GetType("E"), inner.GetType());
        Assert.Equal(8, v2.GetType("E")!.GetField("Id")!.GetValue(inner));
    }

    [Fact]
    public void ReadOnlyInstanceField_IsStillCarried()
    {
        Assembly v1 = Compile("using System.Collections.Generic; public class E { public int Id; } " +
            "public class Holder { public readonly List<E> Items = new(); } " +
            "public static class H { public static Holder Value; " +
            "  public static void Setup() { Value = new Holder(); Value.Items.Add(new E { Id = 1 }); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public class Holder { public readonly List<E> Items = new(); public int Tag; } " +
            "public static class H { public static Holder Value; public static void Setup() { } }");

        Migrate(v1, v2);

        object holder = v2.GetType("H")!.GetField("Value")!.GetValue(null)!;
        var items = (IList)v2.GetType("Holder")!.GetField("Items")!.GetValue(holder)!;

        Assert.Single(items);
        Assert.Same(v2.GetType("E"), items[0]!.GetType());
    }

    [Fact]
    public void FieldMovedToBaseClass_StillCarriesOver()
    {
        Assembly v1 = Compile("public class Base { } public class Derived : Base { public int Id; } " +
            "public static class H { public static Derived Value; public static void Setup() { Value = new Derived { Id = 21 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("public class Base { public int Id; } public class Derived : Base { } " +
            "public static class H { public static Derived Value; public static void Setup() { } }");

        Migrate(v1, v2);

        object value = v2.GetType("H")!.GetField("Value")!.GetValue(null)!;
        Assert.Equal(21, v2.GetType("Base")!.GetField("Id")!.GetValue(value));
    }

    [Fact]
    public void TypeGainsABaseClass_KeepsOwnFields()
    {
        Assembly v1 = Compile("public class Derived { public int Id; } " +
            "public static class H { public static Derived Value; public static void Setup() { Value = new Derived { Id = 22 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("public class Base { public int Tag; } public class Derived : Base { public int Id; } " +
            "public static class H { public static Derived Value; public static void Setup() { } }");

        Migrate(v1, v2);

        object value = v2.GetType("H")!.GetField("Value")!.GetValue(null)!;
        Assert.Equal(22, v2.GetType("Derived")!.GetField("Id")!.GetValue(value));
    }

    // Relocating a field must not steal one that a hierarchy level already claimed.
    [Fact]
    public void MovedField_DoesNotStealAFieldAlreadyMatchedAtItsOwnLevel()
    {
        Assembly v1 = Compile(
            "public class Base { } public class Derived : Base { public int Id; } " +
            "public static class H { public static Derived Value; public static void Setup() { Value = new Derived { Id = 31 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class Base { public int Id; } public class Derived : Base { public int Id; } " +
            "public static class H { public static Derived Value; public static void Setup() { } }");

        Migrate(v1, v2);

        object value = v2.GetType("H")!.GetField("Value")!.GetValue(null)!;

        // Derived.Id matched at its own level and keeps the value. Base.Id is genuinely new and stays at zero.
        Assert.Equal(31, v2.GetType("Derived")!.GetField("Id")!.GetValue(value));
        Assert.Equal(0, v2.GetType("Base")!.GetField("Id")!.GetValue(value));
    }

    // Two source fields of the same name give no basis for choosing, so neither is relocated.
    [Fact]
    public void MovedField_AmbiguousName_IsLeftAtItsDefault()
    {
        Assembly v1 = Compile(
            "public class Base { public int Id; } public class Derived : Base { public new int Id; } " +
            "public class Other { } " +
            "public static class H { public static Derived Value; " +
            "  public static void Setup() { Value = new Derived(); Value.Id = 1; ((Base)Value).Id = 2; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        // Both declarations move onto a third level, so neither source field matches at a level.
        Assembly v2 = Compile(
            "public class Base { } public class Middle : Base { public int Id; } public class Derived : Middle { } " +
            "public static class H { public static Derived Value; public static void Setup() { } }");

        var report = Migrate(v1, v2);

        object value = v2.GetType("H")!.GetField("Value")!.GetValue(null)!;
        Assert.Equal(0, v2.GetType("Middle")!.GetField("Id")!.GetValue(value));
        Assert.True(report.Succeeded);
    }

    // The replacement owns any handle copied out of the old instance, so the old finalizer must not run and
    // free it out from under the new one. The instance is held only as a root, and neither it nor the report
    // outlives the helper, so it really is collectable once the reload returns.
    [Fact]
    public void ReplacedInstance_FinalizerIsSuppressed()
    {
        const string body =
            "public class Native { public int Handle; public static int Freed; ~Native() { Freed++; } }";

        Assembly v1 = Compile(body);
        Assembly v2 = Compile(body.Replace("public int Handle;", "public int Handle; public int Extra;"));

        var tracker = MigrateAndDrop(v1, v2);

        for (int i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(tracker.IsAlive, "the previous instance was not collected, so this probe proves nothing");
        Assert.Equal(0, v1.GetType("Native")!.GetField("Freed")!.GetValue(null));
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private WeakReference MigrateAndDrop(Assembly previous, Assembly current)
    {
        var engine = ReloadEngine.Create(o => o.AssemblyBytes = AssemblyBytes);

        object instance = Activator.CreateInstance(previous.GetType("Native")!)!;
        previous.GetType("Native")!.GetField("Handle")!.SetValue(instance, 7);

        // The report is deliberately not kept: it holds every replaced object by strong reference.
        engine.Apply(ReloadRequest.Create().Replace(previous, current).Root(instance).Build());

        return new WeakReference(instance);
    }

    // Suppression applies to instances that were replaced, not to ones that carried over.
    [Fact]
    public void PreservedInstance_FinalizerStillRuns()
    {
        Assembly v1 = Compile("public class E { public int Id; }");
        Assembly v2 = Compile("public class E { public int Id; public int Extra; }");

        var tracker = PreserveAndDrop(v1, v2);

        for (int i = 0; i < 3; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        Assert.False(tracker.IsAlive, "the preserved instance was not collected, so this probe proves nothing");
        Assert.Equal(1, FinalizerProbe.Finalized);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference PreserveAndDrop(Assembly previous, Assembly current)
    {
        FinalizerProbe.Finalized = 0;

        var engine = ReloadEngine.Create(o => o.AssemblyBytes = AssemblyBytes);
        object instance = new FinalizerProbe();

        engine.Apply(ReloadRequest.Create().Replace(previous, current).Root(instance).Build());

        return new WeakReference(instance);
    }

    private sealed class FinalizerProbe
    {
        public static int Finalized;
        public int Value;

        ~FinalizerProbe() => Finalized++;
    }
}
