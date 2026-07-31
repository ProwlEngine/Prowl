using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Xunit;

namespace Prowl.Ember.Tests;

/// <summary>
/// Migration tests for the type system: enums, records, structs, tuples, nullable, generics, polymorphism,
/// boxing, anonymous types, and modern member shapes.
/// </summary>
[Trait("Category", "Build")]
public class TypeSystemMigrationTests : MigrationTestBase
{
    private const string EDef = "public class E { public int Id; }";
    private const string EDef2 = "public class E { public int Id; public int Extra; }";
    private static long Num(object boxedEnum) => Convert.ToInt64(boxedEnum);

    [Fact]
    public void RecordClass_HoldingSwappedType()
    {
        Assembly v1 = Compile(
            "public class E { public int Id; } public record W { public E Item; } " +
            "public static class H { public static W Wrap; public static E Shared; " +
            "public static void Setup() { Shared = new E{Id=5}; Wrap = new W{ Item = Shared }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class E { public int Id; public int Extra; } public record W { public E Item; } " +
            "public static class H { public static W Wrap; public static E Shared; public static void Setup() { } }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object wrap = hV2.GetField("Wrap")!.GetValue(null)!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object item = v2.GetType("W")!.GetField("Item")!.GetValue(wrap)!;

        Assert.Same(eV2, item.GetType());
        Assert.Equal(5, eV2.GetField("Id")!.GetValue(item));
        Assert.Same(shared, item);
    }

    [Fact]
    public void Record_ThatIsSwappedType()
    {
        Assembly v1 = Compile(
            "public record R { public int Id; } " +
            "public static class H { public static R Inst; public static void Setup() { Inst = new R{Id=9}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public record R { public int Id; public int Extra; } " +
            "public static class H { public static R Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type rV2 = v2.GetType("R")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;
        Assert.Same(rV2, inst.GetType());
        Assert.Equal(9, rV2.GetField("Id")!.GetValue(inst));
        Assert.Equal(0, rV2.GetField("Extra")!.GetValue(inst));
    }

    [Fact]
    public void RecordStruct_HoldingSwappedType()
    {
        Assembly v1 = Compile(
            "public class E { public int Id; } public record struct RS { public int N; public E Item; } " +
            "public static class H { public static RS Slot; public static E Shared; " +
            "public static void Setup() { Shared = new E{Id=3}; Slot = new RS{ N = 8, Item = Shared }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class E { public int Id; public int Extra; } public record struct RS { public int N; public E Item; } " +
            "public static class H { public static RS Slot; public static E Shared; public static void Setup() { } }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type rsV2 = v2.GetType("RS")!;
        Type hV2 = v2.GetType("H")!;
        object slot = hV2.GetField("Slot")!.GetValue(null)!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object item = rsV2.GetField("Item")!.GetValue(slot)!;

        Assert.Equal(8, rsV2.GetField("N")!.GetValue(slot));
        Assert.Same(eV2, item.GetType());
        Assert.Same(shared, item);
    }

    [Fact]
    public void ReadonlyStruct_WithReferenceField()
    {
        Assembly v1 = Compile(
            "public class E { public int Id; } " +
            "public readonly struct RO { public readonly E Item; public readonly int N; public RO(E e, int n){ Item = e; N = n; } } " +
            "public static class H { public static RO Slot; public static E Shared; " +
            "public static void Setup() { Shared = new E{Id=2}; Slot = new RO(Shared, 4); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class E { public int Id; public int Extra; } " +
            "public readonly struct RO { public readonly E Item; public readonly int N; public RO(E e, int n){ Item = e; N = n; } } " +
            "public static class H { public static RO Slot; public static E Shared; public static void Setup() { } }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type roV2 = v2.GetType("RO")!;
        Type hV2 = v2.GetType("H")!;
        object slot = hV2.GetField("Slot")!.GetValue(null)!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object item = roV2.GetField("Item")!.GetValue(slot)!;

        Assert.Equal(4, roV2.GetField("N")!.GetValue(slot));
        Assert.Same(eV2, item.GetType());
        Assert.Same(shared, item);
    }

    [Fact]
    public void NestedStructInStructInClass_SharedRef()
    {
        Assembly v1 = Compile(
            "public class E { public int Id; } " +
            "public struct Inner { public E Item; public int B; } public struct Mid { public Inner I; public int A; } " +
            "public class C { public Mid M; } " +
            "public static class H { public static C Obj; public static Inner Other; public static E Shared; " +
            "public static void Setup() { Shared = new E{Id=1}; Obj = new C{ M = new Mid{ A = 2, I = new Inner{ B = 3, Item = Shared } } }; Other = new Inner{ B = 9, Item = Shared }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class E { public int Id; public int Extra; } " +
            "public struct Inner { public E Item; public int B; } public struct Mid { public Inner I; public int A; } " +
            "public class C { public Mid M; } " +
            "public static class H { public static C Obj; public static Inner Other; public static E Shared; public static void Setup() { } }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type innerV2 = v2.GetType("Inner")!;
        Type midV2 = v2.GetType("Mid")!;
        Type hV2 = v2.GetType("H")!;
        object obj = hV2.GetField("Obj")!.GetValue(null)!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object mid = v2.GetType("C")!.GetField("M")!.GetValue(obj)!;
        object inner = midV2.GetField("I")!.GetValue(mid)!;
        object deep = innerV2.GetField("Item")!.GetValue(inner)!;
        object other = hV2.GetField("Other")!.GetValue(null)!;
        object otherItem = innerV2.GetField("Item")!.GetValue(other)!;

        Assert.Same(eV2, deep.GetType());
        Assert.Same(shared, deep);
        Assert.Same(shared, otherItem);
    }

    [Fact]
    public void ValueTuple_E_Int()
    {
        Assembly v1 = Compile(
            "public class E { public int Id; } " +
            "public static class H { public static (E, int) T; public static E Shared; " +
            "public static void Setup() { Shared = new E{Id=5}; T = (Shared, 9); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class E { public int Id; public int Extra; } " +
            "public static class H { public static (E, int) T; public static E Shared; public static void Setup() { } }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object tup = hV2.GetField("T")!.GetValue(null)!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object item1 = tup.GetType().GetField("Item1")!.GetValue(tup)!;

        Assert.Same(eV2, item1.GetType());
        Assert.Same(shared, item1);
        Assert.Equal(9, tup.GetType().GetField("Item2")!.GetValue(tup));
    }

    // The swapped element at position 8 lives in the nested TRest.
    [Fact]
    public void ValueTuple_EightElements()
    {
        Assembly v1 = Compile(
            "public class E { public int Id; } " +
            "public static class H { public static (E, int, int, int, int, int, int, E) T; public static E Shared; " +
            "public static void Setup() { Shared = new E{Id=1}; T = (Shared, 2, 3, 4, 5, 6, 7, Shared); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class E { public int Id; public int Extra; } " +
            "public static class H { public static (E, int, int, int, int, int, int, E) T; public static E Shared; public static void Setup() { } }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object tup = hV2.GetField("T")!.GetValue(null)!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object item1 = tup.GetType().GetField("Item1")!.GetValue(tup)!;
        object rest = tup.GetType().GetField("Rest")!.GetValue(tup)!;
        object item8 = rest.GetType().GetField("Item1")!.GetValue(rest)!;

        Assert.Same(eV2, item1.GetType());
        Assert.Same(shared, item1);
        Assert.Same(eV2, item8.GetType());
        Assert.Same(shared, item8);
    }

    [Fact]
    public void NestedTuple_EE_E()
    {
        Assembly v1 = Compile(
            "public class E { public int Id; } " +
            "public static class H { public static ((E, E), E) T; public static E Shared; " +
            "public static void Setup() { Shared = new E{Id=1}; T = ((Shared, new E{Id=2}), new E{Id=3}); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class E { public int Id; public int Extra; } " +
            "public static class H { public static ((E, E), E) T; public static E Shared; public static void Setup() { } }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object tup = hV2.GetField("T")!.GetValue(null)!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object inner = tup.GetType().GetField("Item1")!.GetValue(tup)!;
        object innerA = inner.GetType().GetField("Item1")!.GetValue(inner)!;
        object innerB = inner.GetType().GetField("Item2")!.GetValue(inner)!;
        object outerC = tup.GetType().GetField("Item2")!.GetValue(tup)!;

        Assert.Same(eV2, innerA.GetType());
        Assert.Same(shared, innerA);
        Assert.Same(eV2, innerB.GetType());
        Assert.Equal(2, eV2.GetField("Id")!.GetValue(innerB));
        Assert.Same(eV2, outerC.GetType());
        Assert.Equal(3, eV2.GetField("Id")!.GetValue(outerC));
    }

    [Fact]
    public void ReferenceTuple_Tuple_E_Int()
    {
        Assembly v1 = Compile(
            "using System; public class E { public int Id; } " +
            "public static class H { public static Tuple<E, int> T; public static E Shared; " +
            "public static void Setup() { Shared = new E{Id=5}; T = Tuple.Create(Shared, 9); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class E { public int Id; public int Extra; } " +
            "public static class H { public static Tuple<E, int> T; public static E Shared; public static void Setup() { } }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object tup = hV2.GetField("T")!.GetValue(null)!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object item1 = tup.GetType().GetProperty("Item1")!.GetValue(tup)!;

        Assert.Same(eV2, item1.GetType());
        Assert.Same(shared, item1);
        Assert.Equal(9, tup.GetType().GetProperty("Item2")!.GetValue(tup));
    }

    [Fact]
    public void NullableStruct_Swapped()
    {
        Assembly v1 = Compile(
            "public struct S { public int V; } " +
            "public static class H { public static S? Maybe; public static void Setup() { Maybe = new S{V=7}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public struct S { public int V; public int Extra; } " +
            "public static class H { public static S? Maybe; public static void Setup() { } }");
        Migrate(v1, v2);

        Type sV2 = v2.GetType("S")!;
        object? maybe = v2.GetType("H")!.GetField("Maybe")!.GetValue(null);

        Assert.NotNull(maybe);
        Assert.Same(sV2, maybe!.GetType()); // reflection unwraps Nullable to the v2 struct
        Assert.Equal(7, sV2.GetField("V")!.GetValue(maybe));
    }

    // Enum underlying byte -> int must keep its numeric value (known danger).
    [Fact]
    public void EnumUnderlying_ByteToInt()
    {
        Assembly v1 = Compile(
            "public enum En : byte { A, B, C } " +
            "public static class H { public static En Cur; public static void Setup() { Cur = En.B; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public enum En : int { A, B, C } " +
            "public static class H { public static En Cur; public static void Setup() { } }");
        Migrate(v1, v2);

        Type enV2 = v2.GetType("En")!;
        object cur = v2.GetType("H")!.GetField("Cur")!.GetValue(null)!;
        Assert.Same(enV2, cur.GetType());
        Assert.Equal(1, Num(cur)); // B preserved through the width change
    }

    // Enum underlying int -> long must keep its numeric value (known danger).
    [Fact]
    public void EnumUnderlying_IntToLong()
    {
        Assembly v1 = Compile(
            "public enum En : int { A, B, C } " +
            "public static class H { public static En Cur; public static void Setup() { Cur = En.C; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public enum En : long { A, B, C } " +
            "public static class H { public static En Cur; public static void Setup() { } }");
        Migrate(v1, v2);

        Type enV2 = v2.GetType("En")!;
        object cur = v2.GetType("H")!.GetField("Cur")!.GetValue(null)!;
        Assert.Same(enV2, cur.GetType());
        Assert.Equal(2, Num(cur));
    }

    [Fact]
    public void FlagsEnum_ValuePreserved()
    {
        Assembly v1 = Compile(
            "[System.Flags] public enum F : int { None = 0, A = 1, B = 2, C = 4 } " +
            "public static class H { public static F Cur; public static void Setup() { Cur = F.A | F.C; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "[System.Flags] public enum F : int { None = 0, A = 1, B = 2, C = 4, D = 8 } " +
            "public static class H { public static F Cur; public static void Setup() { } }");
        Migrate(v1, v2);

        Type fV2 = v2.GetType("F")!;
        object cur = v2.GetType("H")!.GetField("Cur")!.GetValue(null)!;
        Assert.Same(fV2, cur.GetType());
        Assert.Equal(5, Num(cur)); // A | C == 5
    }

    [Fact]
    public void GenericBox_OfSwappedType()
    {
        Assembly v1 = Compile(
            "public class E { public int Id; } public class Box<T> { public T Value; } " +
            "public static class H { public static Box<E> B; public static E Shared; " +
            "public static void Setup() { Shared = new E{Id=6}; B = new Box<E>{ Value = Shared }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class E { public int Id; public int Extra; } public class Box<T> { public T Value; } " +
            "public static class H { public static Box<E> B; public static E Shared; public static void Setup() { } }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object box = hV2.GetField("B")!.GetValue(null)!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object value = box.GetType().GetField("Value")!.GetValue(box)!;

        Assert.Same(eV2, value.GetType());
        Assert.Same(shared, value);
    }

    [Fact]
    public void DeepNestedGeneric_DictStringListEArray()
    {
        Assembly v1 = Compile(
            "using System.Collections.Generic; public class E { public int Id; } " +
            "public static class H { public static Dictionary<string, List<E[]>> D = new(); public static E Shared; " +
            "public static void Setup() { Shared = new E{Id=1}; D[\"k\"] = new List<E[]>{ new E[]{ Shared, new E{Id=2} } }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public static class H { public static Dictionary<string, List<E[]>> D = new(); public static E Shared; public static void Setup() { } }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        var dict = (IDictionary)hV2.GetField("D")!.GetValue(null)!;
        var list = (IList)dict["k"]!;
        var arr = (Array)list[0]!;
        object first = arr.GetValue(0)!;

        Assert.Same(eV2, arr.GetType().GetElementType());
        Assert.Same(eV2, first.GetType());
        Assert.Same(shared, first);
    }

    [Fact]
    public void SelfReferentialGeneric_Node()
    {
        Assembly v1 = Compile(
            "using System; public class Node : IComparable<Node> { public int Id; public Node Next; public int CompareTo(Node o) => Id.CompareTo(o.Id); } " +
            "public static class H { public static Node Head; " +
            "public static void Setup() { Head = new Node{Id=1}; Head.Next = new Node{Id=2}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class Node : IComparable<Node> { public int Id; public Node Next; public int Extra; public int CompareTo(Node o) => Id.CompareTo(o.Id); } " +
            "public static class H { public static Node Head; public static void Setup() { } }");
        Migrate(v1, v2);

        Type nodeV2 = v2.GetType("Node")!;
        object head = v2.GetType("H")!.GetField("Head")!.GetValue(null)!;
        object next = nodeV2.GetField("Next")!.GetValue(head)!;

        Assert.Same(nodeV2, head.GetType());
        Assert.Same(nodeV2, next.GetType());
        Assert.Equal(1, nodeV2.GetField("Id")!.GetValue(head));
        Assert.Equal(2, nodeV2.GetField("Id")!.GetValue(next));
    }

    [Fact]
    public void TypeGainsBaseClass()
    {
        Assembly v1 = Compile(
            "public class D { public int X; } " +
            "public static class H { public static D Inst; public static void Setup() { Inst = new D{X=11}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class Base { public int Y; } public class D : Base { public int X; } " +
            "public static class H { public static D Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type dV2 = v2.GetType("D")!;
        Type baseV2 = v2.GetType("Base")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;

        Assert.Same(dV2, inst.GetType());
        Assert.Equal(11, dV2.GetField("X")!.GetValue(inst));
        Assert.Equal(0, baseV2.GetField("Y")!.GetValue(inst));
    }

    [Fact]
    public void TypeLosesBaseClass()
    {
        Assembly v1 = Compile(
            "public class Base { public int Y; } public class D : Base { public int X; } " +
            "public static class H { public static D Inst; public static void Setup() { var d = new D{X=1}; d.Y = 2; Inst = d; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class D { public int X; } " +
            "public static class H { public static D Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type dV2 = v2.GetType("D")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;

        Assert.Same(dV2, inst.GetType());
        Assert.Equal(1, dV2.GetField("X")!.GetValue(inst));
    }

    [Fact]
    public void TypeChangesBaseClass()
    {
        Assembly v1 = Compile(
            "public class A { public int Ay; } public class D : A { public int X; } " +
            "public static class H { public static D Inst; public static void Setup() { var d = new D{X=3}; d.Ay = 4; Inst = d; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class B { public int By; } public class D : B { public int X; } " +
            "public static class H { public static D Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type dV2 = v2.GetType("D")!;
        Type bV2 = v2.GetType("B")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;

        Assert.Same(dV2, inst.GetType());
        Assert.Equal(3, dV2.GetField("X")!.GetValue(inst));
        Assert.Equal(0, bV2.GetField("By")!.GetValue(inst));
    }

    [Fact]
    public void GenericArityChanges_FieldDiscarded()
    {
        Assembly v1 = Compile(
            "public class Box<T> { public T V; } " +
            "public static class H { public static Box<int> B; public static void Setup() { B = new Box<int>{ V = 5 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class Box<T, U> { public T V; public U W; } " +
            "public static class H { public static Box<int, int> B; public static void Setup() { } }");
        Migrate(v1, v2);

        object? b = v2.GetType("H")!.GetField("B")!.GetValue(null);
        Assert.Null(b);
    }

    [Fact]
    public void FuncEE_DelegateRebinds()
    {
        Assembly v1 = Compile(
            "using System; public class E { public int Id; } public class Ops { public E Echo(E x) => x; } " +
            "public static class H { public static Func<E, E> Fn; public static Ops O; public static E Shared; " +
            "public static void Setup() { O = new Ops(); Shared = new E{Id=1}; Fn = O.Echo; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class E { public int Id; public int Extra; } public class Ops { public E Echo(E x) => x; } " +
            "public static class H { public static Func<E, E> Fn; public static Ops O; public static E Shared; public static void Setup() { } }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        var fn = (Delegate)hV2.GetField("Fn")!.GetValue(null)!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object result = fn.DynamicInvoke(shared)!;

        Assert.Same(eV2, result.GetType());
        Assert.Same(shared, result);
    }

    [Fact]
    public void BoxedStruct_InObjectField()
    {
        Assembly v1 = Compile(
            "public struct S { public int V; } " +
            "public static class H { public static object Boxed; public static void Setup() { Boxed = new S{V=7}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public struct S { public int V; public int Extra; } " +
            "public static class H { public static object Boxed; public static void Setup() { } }");
        Migrate(v1, v2);

        Type sV2 = v2.GetType("S")!;
        object boxed = v2.GetType("H")!.GetField("Boxed")!.GetValue(null)!;
        Assert.Same(sV2, boxed.GetType());
        Assert.Equal(7, sV2.GetField("V")!.GetValue(boxed));
    }

    [Fact]
    public void BoxedEnum_InObjectField()
    {
        Assembly v1 = Compile(
            "public enum E { A, B, C } " +
            "public static class H { public static object Boxed; public static void Setup() { Boxed = E.B; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public enum E { A, B, C, D } " +
            "public static class H { public static object Boxed; public static void Setup() { } }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        object boxed = v2.GetType("H")!.GetField("Boxed")!.GetValue(null)!;
        Assert.Same(eV2, boxed.GetType());
        Assert.Equal(1, Num(boxed));
    }

    [Fact]
    public void KeyValuePair_StringE()
    {
        Assembly v1 = Compile(
            "using System.Collections.Generic; public class E { public int Id; } " +
            "public static class H { public static KeyValuePair<string, E> Pair; public static E Shared; " +
            "public static void Setup() { Shared = new E{Id=4}; Pair = new KeyValuePair<string, E>(\"k\", Shared); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public static class H { public static KeyValuePair<string, E> Pair; public static E Shared; public static void Setup() { } }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object pair = hV2.GetField("Pair")!.GetValue(null)!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object value = pair.GetType().GetProperty("Value")!.GetValue(pair)!;

        Assert.Equal("k", pair.GetType().GetProperty("Key")!.GetValue(pair));
        Assert.Same(eV2, value.GetType());
        Assert.Same(shared, value);
    }

    [Fact]
    public void StaticFieldTypeChanges_IntToString_Discarded()
    {
        Assembly v1 = Compile(
            "public static class H { public static int Val; public static void Setup() { Val = 5; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public static class H { public static string Val; public static void Setup() { } }");
        Migrate(v1, v2);

        object? val = v2.GetType("H")!.GetField("Val")!.GetValue(null);
        Assert.Null(val);
    }

    [Fact]
    public void FieldRemovedInV2_NoCrash()
    {
        Assembly v1 = Compile(
            "public class C { public int A; public int B; } " +
            "public static class H { public static C Inst; public static void Setup() { Inst = new C{A=1, B=2}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class C { public int A; } " +
            "public static class H { public static C Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;
        Assert.Same(cV2, inst.GetType());
        Assert.Equal(1, cV2.GetField("A")!.GetValue(inst));
    }

    [Fact]
    public void StructGainsField()
    {
        Assembly v1 = Compile(
            "public struct S { public int A; } " +
            "public static class H { public static S Val; public static void Setup() { Val = new S{A=5}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public struct S { public int A; public int B; } " +
            "public static class H { public static S Val; public static void Setup() { } }");
        Migrate(v1, v2);

        Type sV2 = v2.GetType("S")!;
        object val = v2.GetType("H")!.GetField("Val")!.GetValue(null)!;
        Assert.Equal(5, sV2.GetField("A")!.GetValue(val));
        Assert.Equal(0, sV2.GetField("B")!.GetValue(val));
    }

    [Fact]
    public void EnumUnderlying_ShortToInt()
    {
        Assembly v1 = Compile("public enum En : short { A, B, C } public static class H { public static En Cur; public static void Setup(){ Cur = En.C; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("public enum En : int { A, B, C } public static class H { public static En Cur; public static void Setup(){} }");
        Migrate(v1, v2);
        object cur = v2.GetType("H")!.GetField("Cur")!.GetValue(null)!;
        Assert.Equal(2, Num(cur));
    }

    // Enum long -> int (narrowing; the value still fits).
    [Fact]
    public void EnumUnderlying_LongToInt_ValueFits()
    {
        Assembly v1 = Compile("public enum En : long { A, B, C } public static class H { public static En Cur; public static void Setup(){ Cur = En.C; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("public enum En : int { A, B, C } public static class H { public static En Cur; public static void Setup(){} }");
        Migrate(v1, v2);
        object cur = v2.GetType("H")!.GetField("Cur")!.GetValue(null)!;
        Assert.Equal(2, Num(cur));
    }

    [Fact]
    public void EnumUnderlying_SByteToInt()
    {
        Assembly v1 = Compile("public enum En : sbyte { A, B, C } public static class H { public static En Cur; public static void Setup(){ Cur = En.B; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("public enum En : int { A, B, C } public static class H { public static En Cur; public static void Setup(){} }");
        Migrate(v1, v2);
        object cur = v2.GetType("H")!.GetField("Cur")!.GetValue(null)!;
        Assert.Equal(1, Num(cur));
    }

    [Fact]
    public void EnumUnderlying_UIntToULong()
    {
        Assembly v1 = Compile("public enum En : uint { A, B, C } public static class H { public static En Cur; public static void Setup(){ Cur = En.C; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("public enum En : ulong { A, B, C } public static class H { public static En Cur; public static void Setup(){} }");
        Migrate(v1, v2);
        object cur = v2.GetType("H")!.GetField("Cur")!.GetValue(null)!;
        Assert.Equal(2, Num(cur));
    }

    [Fact]
    public void Migrate_InitOnlyProperty_HoldingSwappedType()
    {
        Assembly v1 = Compile(EDef +
            "public class C { public E Item { get; init; } } " +
            "public static class H { public static E Shared; public static C Inst; public static void Setup(){ Shared = new E{Id=4}; Inst = new C { Item = Shared }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile(EDef2 +
            "public class C { public E Item { get; init; } public int Extra; } " +
            "public static class H { public static E Shared; public static C Inst; public static void Setup(){} }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object inst = hV2.GetField("Inst")!.GetValue(null)!;
        Assert.Same(shared, cV2.GetProperty("Item")!.GetValue(inst));
    }

    [Fact]
    public void Migrate_RequiredMember_HoldingSwappedType()
    {
        Assembly v1 = Compile(EDef +
            "public class C { public required E Item { get; set; } } " +
            "public static class H { public static E Shared; public static C Inst; public static void Setup(){ Shared = new E{Id=6}; Inst = new C { Item = Shared }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile(EDef2 +
            "public class C { public required E Item { get; set; } public int Extra; } " +
            "public static class H { public static E Shared; public static C Inst; public static void Setup(){} }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object inst = hV2.GetField("Inst")!.GetValue(null)!;
        Assert.Same(shared, cV2.GetProperty("Item")!.GetValue(inst));
    }

    [Fact]
    public void Migrate_GenericStruct_HoldingSwappedRefs()
    {
        Assembly v1 = Compile(EDef +
            "public struct Pair<T> { public T A; public T B; } " +
            "public static class H { public static E Shared; public static Pair<E> P; public static void Setup(){ Shared = new E{Id=1}; P = new Pair<E>{ A = Shared, B = new E{Id=2} }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile(EDef2 +
            "public struct Pair<T> { public T A; public T B; } " +
            "public static class H { public static E Shared; public static Pair<E> P; public static void Setup(){} }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object pair = hV2.GetField("P")!.GetValue(null)!;
        object a = pair.GetType().GetField("A")!.GetValue(pair)!;
        Assert.Same(eV2, a.GetType());
        Assert.Same(shared, a);
    }

    [Fact]
    public void Migrate_StructHoldingList_OfSwappedType()
    {
        Assembly v1 = Compile("using System.Collections.Generic; " + EDef +
            "public struct Holder { public List<E> Items; } " +
            "public static class H { public static E Shared; public static Holder Val; public static void Setup(){ Shared = new E{Id=1}; Val = new Holder{ Items = new List<E>{ Shared, new E{Id=2} } }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.Generic; " + EDef2 +
            "public struct Holder { public List<E> Items; } " +
            "public static class H { public static E Shared; public static Holder Val; public static void Setup(){} }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object holder = hV2.GetField("Val")!.GetValue(null)!;
        var items = (IList)holder.GetType().GetField("Items")!.GetValue(holder)!;
        Assert.Same(shared, items[0]);
    }

    // Guards SkipUpgrader: swapped enums must migrate their value across recompile.
    [Fact]
    public void UpdateReferences_PreservesUserEnumFieldValue_AcrossRecompile()
    {
        Assembly v1 = Compile(
            "public static class Reg { public static Mob M; } " +
            "public enum Team { Red, Blue, Green } " +
            "public class Mob { public Team Side; }");

        Type mobV1 = v1.GetType("Mob")!;
        Type teamV1 = v1.GetType("Team")!;
        object m = Activator.CreateInstance(mobV1)!;
        mobV1.GetField("Side")!.SetValue(m, Enum.Parse(teamV1, "Blue"));
        v1.GetType("Reg")!.GetField("M")!.SetValue(null, m);

        Assembly v2 = Compile(
            "public static class Reg { public static Mob M; } " +
            "public enum Team { Red, Blue, Green } " +
            "public class Mob { public Team Side; public int Hp; }");

        Migrate(v1, v2);

        Type mobV2 = v2.GetType("Mob")!;
        Type teamV2 = v2.GetType("Team")!;
        object newM = v2.GetType("Reg")!.GetField("M")!.GetValue(null)!;
        object side = mobV2.GetField("Side")!.GetValue(newM)!;

        Assert.Same(teamV2, side.GetType());
        Assert.Equal("Blue", side.ToString());
    }

    [Fact]
    public void UpdateReferences_MigratesFieldsAcrossBaseClass_WhenBaseGainsField()
    {
        Assembly v1 = Compile(
            "public static class Reg { public static Derived D; } " +
            "public class Base { public int BaseHp; } " +
            "public class Derived : Base { public int DerivedMana; }");

        Type baseV1 = v1.GetType("Base")!;
        Type derivedV1 = v1.GetType("Derived")!;
        object d = Activator.CreateInstance(derivedV1)!;
        baseV1.GetField("BaseHp")!.SetValue(d, 40);
        derivedV1.GetField("DerivedMana")!.SetValue(d, 15);
        v1.GetType("Reg")!.GetField("D")!.SetValue(null, d);

        Assembly v2 = Compile(
            "public static class Reg { public static Derived D; } " +
            "public class Base { public int BaseHp; public int BaseArmor; } " +
            "public class Derived : Base { public int DerivedMana; }");

        Migrate(v1, v2);

        Type baseV2 = v2.GetType("Base")!;
        Type derivedV2 = v2.GetType("Derived")!;
        object newD = v2.GetType("Reg")!.GetField("D")!.GetValue(null)!;

        Assert.Same(derivedV2, newD.GetType());
        Assert.Equal(40, baseV2.GetField("BaseHp")!.GetValue(newD));
        Assert.Equal(15, derivedV2.GetField("DerivedMana")!.GetValue(newD));
        Assert.Equal(0, baseV2.GetField("BaseArmor")!.GetValue(newD));
    }

    [Fact]
    public void C_AnonymousType_MigratesByPropertyNames_PreservingValues()
    {
        Assembly v1 = Compile(
            "public static class H { public static object Data; " +
            "public static void Setup(){ Data = new { Name = \"hi\", Count = 42 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public static class H { public static object Data; " +
            "public static void Setup(){ Data = new { Name = \"different\", Count = 0 }; } }");
        Migrate(v1, v2);

        object? data = v2.GetType("H")!.GetField("Data")!.GetValue(null);
        Assert.NotNull(data);
        Assert.Same(v2, data!.GetType().Assembly);
        Assert.Equal("hi", data.GetType().GetProperty("Name")!.GetValue(data));
        Assert.Equal(42, data.GetType().GetProperty("Count")!.GetValue(data));
    }

    [Fact]
    public void Migrate_PolymorphicFields_InterfaceAndBaseTypedAndArray()
    {
        Assembly v1 = Compile(
            "public interface IWeapon { int Dmg { get; } } public abstract class Base { public int Hp; } " +
            "public class Sword : Base, IWeapon { public int Dmg { get; set; } } " +
            "public static class H { public static IWeapon W; public static Base B; public static IWeapon[] Arr; " +
            "public static void Setup() { var s = new Sword{Hp=5, Dmg=9}; W = s; B = s; Arr = new IWeapon[]{ s, new Sword{Dmg=1} }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public interface IWeapon { int Dmg { get; } } public abstract class Base { public int Hp; public int Armor; } " +
            "public class Sword : Base, IWeapon { public int Dmg { get; set; } } " +
            "public static class H { public static IWeapon W; public static Base B; public static IWeapon[] Arr; public static void Setup() { } }");
        Migrate(v1, v2);

        Type swordV2 = v2.GetType("Sword")!;
        Type hV2 = v2.GetType("H")!;
        object w = hV2.GetField("W")!.GetValue(null)!;
        object b = hV2.GetField("B")!.GetValue(null)!;
        var arr = (Array)hV2.GetField("Arr")!.GetValue(null)!;

        Assert.Same(swordV2, w.GetType());
        Assert.Same(w, b);
        Assert.Equal(9, swordV2.GetProperty("Dmg")!.GetValue(w));
        Assert.Equal(5, swordV2.GetField("Hp")!.GetValue(w));
        Assert.Same(w, arr.GetValue(0));
    }

    [Fact]
    public void Migrate_AutoProperty_Readonly_StructWithRef()
    {
        Assembly v1 = Compile(
            "public class Gun { public int Ammo; } public struct Slot { public Gun Gun; public int Count; } " +
            "public class P { public int Level { get; set; } public readonly int Seed; public Slot S; public P(){ Seed = 42; } } " +
            "public static class H { public static P Inst; public static Gun SharedGun; " +
            "public static void Setup() { SharedGun = new Gun{Ammo=7}; Inst = new P(); Inst.Level = 3; Inst.S = new Slot{ Gun = SharedGun, Count = 2 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class Gun { public int Ammo; public int Clip; } public struct Slot { public Gun Gun; public int Count; } " +
            "public class P { public int Level { get; set; } public readonly int Seed; public Slot S; public P(){ Seed = 42; } } " +
            "public static class H { public static P Inst; public static Gun SharedGun; public static void Setup() { } }");
        Migrate(v1, v2);

        Type pV2 = v2.GetType("P")!;
        Type slotV2 = v2.GetType("Slot")!;
        Type hV2 = v2.GetType("H")!;
        object inst = hV2.GetField("Inst")!.GetValue(null)!;
        object sharedGun = hV2.GetField("SharedGun")!.GetValue(null)!;

        Assert.Equal(3, pV2.GetProperty("Level")!.GetValue(inst));
        Assert.Equal(42, pV2.GetField("Seed")!.GetValue(inst));
        object slot = pV2.GetField("S")!.GetValue(inst)!;
        Assert.Equal(2, slotV2.GetField("Count")!.GetValue(slot));
        Assert.Same(sharedGun, slotV2.GetField("Gun")!.GetValue(slot));
    }

    // class Node<T> where T : Node<T>, which makes resolution re-enter itself.
    [Fact]
    public void SelfConstrainedGeneric_Resolves()
    {
        Assembly v1 = Compile(
            "public class Node<T> where T : Node<T> { public int Id; public T Self; } " +
            "public class Concrete : Node<Concrete> { } " +
            "public static class H { public static Node<Concrete> Value; " +
            "  public static void Setup() { var c = new Concrete { Id = 1 }; c.Self = c; Value = c; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class Node<T> where T : Node<T> { public int Id; public T Self; public int Extra; } " +
            "public class Concrete : Node<Concrete> { } " +
            "public static class H { public static Node<Concrete> Value; public static void Setup() { } }");

        Migrate(v1, v2);

        object value = v2.GetType("H")!.GetField("Value")!.GetValue(null)!;
        Assert.Same(v2.GetType("Concrete"), value.GetType());
        Assert.Equal(1, v2.GetType("Node`1")!.MakeGenericType(v2.GetType("Concrete")!).GetField("Id")!.GetValue(value));
    }

    [Fact]
    public void NullableStructField_OfSwappedType()
    {
        Assembly v1 = Compile("public struct P { public int X; } " +
            "public static class H { public static P? Maybe; public static void Setup() { Maybe = new P { X = 5 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("public struct P { public int X; public int Y; } " +
            "public static class H { public static P? Maybe; public static void Setup() { } }");

        Migrate(v1, v2);

        object? maybe = v2.GetType("H")!.GetField("Maybe")!.GetValue(null);
        Assert.NotNull(maybe);
        Assert.Equal(5, v2.GetType("P")!.GetField("X")!.GetValue(maybe));
    }

    [Fact]
    public void GenericMethodHandle_IsRemapped()
    {
        Assembly v1 = Compile("using System.Reflection; public class E { public int Id; } " +
            "public static class H { public static MethodInfo M; " +
            "  public static T Echo<T>(T value) { return value; } " +
            "  public static void Setup() { M = typeof(H).GetMethod(\"Echo\").MakeGenericMethod(typeof(E)); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Reflection; public class E { public int Id; public int Extra; } " +
            "public static class H { public static MethodInfo M; " +
            "  public static T Echo<T>(T value) { return value; } " +
            "  public static void Setup() { } }");

        Migrate(v1, v2);

        var method = (MethodInfo)v2.GetType("H")!.GetField("M")!.GetValue(null)!;
        Assert.Same(v2.GetType("E"), method.GetGenericArguments()[0]);
    }
}
