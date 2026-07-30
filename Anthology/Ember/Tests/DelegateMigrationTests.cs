using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Xunit;

namespace Prowl.Ember.Tests;

/// <summary>Migration tests for delegates, events, lambdas, and closures.</summary>
[Trait("Category", "Build")]
public class DelegateMigrationTests : MigrationTestBase
{
    private static object? Invoke(object del, params object?[] args) => ((Delegate)del).DynamicInvoke(args);

    [Fact]
    public void Delegate_ToStaticMethod_NoTarget_RunsNewBody()
    {
        Assembly v1 = Compile(
            "using System; public static class H { public static int Marker; public static Action D; " +
            "public static void Fire(){ Marker = 1; } public static void Setup(){ D = Fire; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public static class H { public static int Marker; public static Action D; " +
            "public static void Fire(){ Marker = 2; } public static void Setup(){ } }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        var d = (Action)hV2.GetField("D")!.GetValue(null)!;
        d();
        Assert.Equal(2, hV2.GetField("Marker")!.GetValue(null));
    }

    [Fact]
    public void Delegate_ToStaticMethod_DeclaringTypeSwapped_MutatesNewStatic()
    {
        Assembly v1 = Compile(
            "using System; public class C { public static int Count; public static void Inc(){ Count++; } } " +
            "public static class H { public static Action D; public static void Setup(){ D = C.Inc; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class C { public int Extra; public static int Count; public static void Inc(){ Count++; } } " +
            "public static class H { public static Action D; public static void Setup(){ } }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        var d = (Action)v2.GetType("H")!.GetField("D")!.GetValue(null)!;
        d();
        Assert.Equal(1, cV2.GetField("Count")!.GetValue(null));
    }

    [Fact]
    public void Delegate_NamedInstanceHandler_TargetSwapped_RunsOnMigratedInstance()
    {
        Assembly v1 = Compile(
            "using System; public class Sub { public int Hits; public void OnA(){ Hits++; } } " +
            "public static class H { public static Sub S; public static Action D; public static void Setup(){ S = new Sub(); D = S.OnA; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class Sub { public int Hits; public int Extra; public void OnA(){ Hits++; } } " +
            "public static class H { public static Sub S; public static Action D; public static void Setup(){ } }");
        Migrate(v1, v2);

        Type subV2 = v2.GetType("Sub")!;
        Type hV2 = v2.GetType("H")!;
        object sub = hV2.GetField("S")!.GetValue(null)!;
        var d = (Action)hV2.GetField("D")!.GetValue(null)!;

        Assert.Same(sub, d.Target);
        d();
        Assert.Equal(1, subV2.GetField("Hits")!.GetValue(sub));
    }

    [Fact]
    public void Multicast_KeptLambdaPlusNamed_BothMigrateAndRun()
    {
        Assembly v1 = Compile(
            "using System; public class Sub { public int Hits; public void OnA(){ Hits++; } } " +
            "public static class H { public static Sub S; public static int LamRan; public static Action Evt; " +
            "public static void Setup(){ S = new Sub(); Evt = () => LamRan++; Evt += S.OnA; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class Sub { public int Hits; public int Extra; public void OnA(){ Hits++; } } " +
            "public static class H { public static Sub S; public static int LamRan; public static Action Evt; " +
            "public static void Setup(){ Evt = () => LamRan++; Evt += S.OnA; } }");
        Migrate(v1, v2);

        Type subV2 = v2.GetType("Sub")!;
        Type hV2 = v2.GetType("H")!;
        object sub = hV2.GetField("S")!.GetValue(null)!;
        var evt = (Action)hV2.GetField("Evt")!.GetValue(null)!;

        Assert.Equal(2, evt.GetInvocationList().Length);
        evt();
        Assert.Equal(1, subV2.GetField("Hits")!.GetValue(sub));
        Assert.Equal(1, hV2.GetField("LamRan")!.GetValue(null));
    }

    [Fact]
    public void Delegate_ToGenericMethodInstantiation_Migrates()
    {
        Assembly v1 = Compile(
            "using System; public class C { public static T Echo<T>(T x) => x; } " +
            "public static class H { public static Func<int,int> D; public static void Setup(){ D = C.Echo<int>; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class C { public int Extra; public static T Echo<T>(T x) => x; } " +
            "public static class H { public static Func<int,int> D; public static void Setup(){ } }");
        Migrate(v1, v2);

        var d = (Func<int, int>)v2.GetType("H")!.GetField("D")!.GetValue(null)!;
        Assert.Equal(5, d(5));
    }

    [Fact]
    public void Delegate_OpenInstance_Migrates()
    {
        Assembly v1 = Compile(
            "using System; public class C { public int V; public int Read(){ return V; } } " +
            "public static class H { public static C Inst; public static Func<C,int> D; " +
            "public static void Setup(){ Inst = new C{ V = 9 }; D = (Func<C,int>)System.Delegate.CreateDelegate(typeof(Func<C,int>), null, typeof(C).GetMethod(\"Read\")); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class C { public int V; public int Extra; public int Read(){ return V; } } " +
            "public static class H { public static C Inst; public static Func<C,int> D; public static void Setup(){ } }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        object inst = hV2.GetField("Inst")!.GetValue(null)!;
        object d = hV2.GetField("D")!.GetValue(null)!;
        Assert.Equal(9, (int)Invoke(d, inst)!);
    }

    [Fact]
    public void Delegate_RenamedStaticMethod_BecomesErrorDelegate()
    {
        Assembly v1 = Compile(
            "using System; public static class H { public static Action D; public static void OldName(){ } public static void Setup(){ D = OldName; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public static class H { public static Action D; public static void NewName(){ } public static void Setup(){ } }");
        Migrate(v1, v2);

        var d = (Action)v2.GetType("H")!.GetField("D")!.GetValue(null)!;
        Assert.NotNull(d);
        Assert.Throws<ReloadedDelegateException>(() => d()); // a loud error delegate
    }

    [Fact]
    public void UserEvent_FieldLike_OnSwappedType_SubscribersMigrate()
    {
        Assembly v1 = Compile(
            "using System; public class Emitter { public event Action Foo; public void Raise(){ Foo?.Invoke(); } } " +
            "public class Sub { public int Hits; public void OnA(){ Hits++; } } " +
            "public static class H { public static Emitter E; public static Sub S; public static void Setup(){ E = new Emitter(); S = new Sub(); E.Foo += S.OnA; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class Emitter { public int Extra; public event Action Foo; public void Raise(){ Foo?.Invoke(); } } " +
            "public class Sub { public int Hits; public int Extra; public void OnA(){ Hits++; } } " +
            "public static class H { public static Emitter E; public static Sub S; public static void Setup(){ } }");
        Migrate(v1, v2);

        Type emitterV2 = v2.GetType("Emitter")!;
        Type subV2 = v2.GetType("Sub")!;
        Type hV2 = v2.GetType("H")!;
        object emitter = hV2.GetField("E")!.GetValue(null)!;
        object sub = hV2.GetField("S")!.GetValue(null)!;

        emitterV2.GetMethod("Raise")!.Invoke(emitter, null);
        Assert.Equal(1, subV2.GetField("Hits")!.GetValue(sub));
    }

    [Fact]
    public void UserEvent_CustomAddRemove_BackingFieldMigrates()
    {
        Assembly v1 = Compile(
            "using System; public class Emitter { private Action _h; public event Action Foo { add { _h += value; } remove { _h -= value; } } public void Raise(){ _h?.Invoke(); } } " +
            "public class Sub { public int Hits; public void OnA(){ Hits++; } } " +
            "public static class H { public static Emitter E; public static Sub S; public static void Setup(){ E = new Emitter(); S = new Sub(); E.Foo += S.OnA; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class Emitter { public int Extra; private Action _h; public event Action Foo { add { _h += value; } remove { _h -= value; } } public void Raise(){ _h?.Invoke(); } } " +
            "public class Sub { public int Hits; public int Extra; public void OnA(){ Hits++; } } " +
            "public static class H { public static Emitter E; public static Sub S; public static void Setup(){ } }");
        Migrate(v1, v2);

        Type emitterV2 = v2.GetType("Emitter")!;
        Type subV2 = v2.GetType("Sub")!;
        Type hV2 = v2.GetType("H")!;
        object emitter = hV2.GetField("E")!.GetValue(null)!;
        object sub = hV2.GetField("S")!.GetValue(null)!;

        emitterV2.GetMethod("Raise")!.Invoke(emitter, null);
        Assert.Equal(1, subV2.GetField("Hits")!.GetValue(sub));
    }

    [Fact]
    public void FuncIntInt_WithClosure_PreservesCapturedValue()
    {
        Assembly v1 = Compile(
            "using System; public static class H { public static Func<int,int> D; public static void Setup(){ int cap = 10; D = x => x + cap; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public static class H { public static Func<int,int> D; public static void Setup(){ int cap = 0; D = x => x + cap; } }");
        Migrate(v1, v2);

        var d = (Func<int, int>)v2.GetType("H")!.GetField("D")!.GetValue(null)!;
        Assert.Equal(15, d(5));
    }

    [Fact]
    public void HigherArityAction_WithClosure_Migrates()
    {
        Assembly v1 = Compile(
            "using System; public static class H { public static int Sum; public static Action<int,int,int> D; public static void Setup(){ int cap = 100; D = (a,b,c) => Sum = a + b + c + cap; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public static class H { public static int Sum; public static Action<int,int,int> D; public static void Setup(){ int cap = 0; D = (a,b,c) => Sum = a + b + c + cap; } }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        var d = (Action<int, int, int>)hV2.GetField("D")!.GetValue(null)!;
        d(1, 2, 3);
        Assert.Equal(106, hV2.GetField("Sum")!.GetValue(null));
    }

    [Fact]
    public void NestedLambda_ReturningLambda_PreservesCapture()
    {
        Assembly v1 = Compile(
            "using System; public static class H { public static Func<int,Func<int,int>> Outer; public static Func<int,int> Inner; " +
            "public static void Setup(){ Outer = a => (b => a + b); Inner = Outer(10); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public static class H { public static Func<int,Func<int,int>> Outer; public static Func<int,int> Inner; " +
            "public static void Setup(){ Outer = a => (b => a + b); Inner = Outer(0); } }");
        Migrate(v1, v2);

        var inner = (Func<int, int>)v2.GetType("H")!.GetField("Inner")!.GetValue(null)!;
        Assert.Equal(15, inner(5));
    }

    [Fact]
    public void Lambda_CapturingThisAndLocal_Migrates()
    {
        Assembly v1 = Compile(
            "using System; public class C { public int V = 3; public int Result; public Action Make(int extra){ return () => Result = V + extra; } } " +
            "public static class H { public static C Inst; public static Action D; public static void Setup(){ Inst = new C(); D = Inst.Make(4); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class C { public int V = 3; public int Result; public int Extra; public Action Make(int extra){ return () => Result = V + extra; } } " +
            "public static class H { public static C Inst; public static Action D; public static void Setup(){ } }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        Type hV2 = v2.GetType("H")!;
        object inst = hV2.GetField("Inst")!.GetValue(null)!;
        var d = (Action)hV2.GetField("D")!.GetValue(null)!;
        d();
        Assert.Equal(7, cV2.GetField("Result")!.GetValue(inst));
    }

    [Fact]
    public void TwoLambdasInOneMethod_BothMigrateDistinctBodies()
    {
        Assembly v1 = Compile(
            "using System; public static class H { public static int A; public static int B; public static Action D1; public static Action D2; " +
            "public static void Setup(){ D1 = () => A = 1; D2 = () => B = 2; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public static class H { public static int A; public static int B; public static Action D1; public static Action D2; " +
            "public static void Setup(){ D1 = () => A = 10; D2 = () => B = 20; } }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        var d1 = (Action)hV2.GetField("D1")!.GetValue(null)!;
        var d2 = (Action)hV2.GetField("D2")!.GetValue(null)!;
        d1();
        d2();
        Assert.Equal(10, hV2.GetField("A")!.GetValue(null));
        Assert.Equal(20, hV2.GetField("B")!.GetValue(null));
    }

    [Fact]
    public void DelegatesInList_Migrate()
    {
        Assembly v1 = Compile(
            "using System; using System.Collections.Generic; public class Sub { public int Hits; public void OnA(){ Hits++; } } " +
            "public static class H { public static Sub S; public static int LamRan; public static List<Action> L = new(); " +
            "public static void Setup(){ S = new Sub(); L.Add(S.OnA); L.Add(() => LamRan++); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; using System.Collections.Generic; public class Sub { public int Hits; public int Extra; public void OnA(){ Hits++; } } " +
            "public static class H { public static Sub S; public static int LamRan; public static List<Action> L = new(); " +
            "public static void Setup(){ L.Add(S.OnA); L.Add(() => LamRan++); } }");
        Migrate(v1, v2);

        Type subV2 = v2.GetType("Sub")!;
        Type hV2 = v2.GetType("H")!;
        object sub = hV2.GetField("S")!.GetValue(null)!;
        var list = (IEnumerable)hV2.GetField("L")!.GetValue(null)!;

        foreach (Action a in list) a();
        Assert.Equal(1, subV2.GetField("Hits")!.GetValue(sub));
        Assert.Equal(1, hV2.GetField("LamRan")!.GetValue(null));
    }

    [Fact]
    public void DelegatesInArray_Migrate()
    {
        Assembly v1 = Compile(
            "using System; public class Sub { public int Hits; public void OnA(){ Hits++; } } " +
            "public static class H { public static Sub S; public static int LamRan; public static Action[] Arr; " +
            "public static void Setup(){ S = new Sub(); Arr = new Action[]{ S.OnA, () => LamRan++ }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class Sub { public int Hits; public int Extra; public void OnA(){ Hits++; } } " +
            "public static class H { public static Sub S; public static int LamRan; public static Action[] Arr; " +
            "public static void Setup(){ Arr = new Action[]{ S.OnA, () => LamRan++ }; } }");
        Migrate(v1, v2);

        Type subV2 = v2.GetType("Sub")!;
        Type hV2 = v2.GetType("H")!;
        object sub = hV2.GetField("S")!.GetValue(null)!;
        var arr = (Action[])hV2.GetField("Arr")!.GetValue(null)!;

        foreach (var a in arr) a();
        Assert.Equal(1, subV2.GetField("Hits")!.GetValue(sub));
        Assert.Equal(1, hV2.GetField("LamRan")!.GetValue(null));
    }

    [Fact]
    public void ComparisonLambda_OverSwappedType_Migrates()
    {
        Assembly v1 = Compile(
            "using System; public class E { public int Id; } " +
            "public static class H { public static Comparison<E> Cmp; public static void Setup(){ Cmp = (a,b) => a.Id.CompareTo(b.Id); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class E { public int Id; public int Extra; } " +
            "public static class H { public static Comparison<E> Cmp; public static void Setup(){ Cmp = (a,b) => a.Id.CompareTo(b.Id); } }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        object cmp = v2.GetType("H")!.GetField("Cmp")!.GetValue(null)!;

        object lo = Activator.CreateInstance(eV2)!;
        object hi = Activator.CreateInstance(eV2)!;
        eV2.GetField("Id")!.SetValue(lo, 1);
        eV2.GetField("Id")!.SetValue(hi, 9);
        Assert.True((int)Invoke(cmp, lo, hi)! < 0);
    }

    [Fact]
    public void PredicateStaticMethod_OverSwappedType_Migrates()
    {
        Assembly v1 = Compile(
            "using System; public class E { public int Id; public static bool IsPositive(E e) => e.Id > 0; } " +
            "public static class H { public static Predicate<E> P; public static void Setup(){ P = E.IsPositive; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class E { public int Id; public int Extra; public static bool IsPositive(E e) => e.Id > 0; } " +
            "public static class H { public static Predicate<E> P; public static void Setup(){ } }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        object p = v2.GetType("H")!.GetField("P")!.GetValue(null)!;

        object e = Activator.CreateInstance(eV2)!;
        eV2.GetField("Id")!.SetValue(e, 5);
        Assert.True((bool)Invoke(p, e)!);
    }

    [Fact]
    public void Delegate_CombinedThenOneRemoved_RemainingRuns()
    {
        Assembly v1 = Compile(
            "using System; public class Sub { public int Hits; public void OnA(){ Hits++; } public void OnB(){ Hits += 100; } } " +
            "public static class H { public static Sub S; public static Action D; public static void Setup(){ S = new Sub(); D = S.OnA; D += S.OnB; D -= S.OnA; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class Sub { public int Hits; public int Extra; public void OnA(){ Hits++; } public void OnB(){ Hits += 100; } } " +
            "public static class H { public static Sub S; public static Action D; public static void Setup(){ } }");
        Migrate(v1, v2);

        Type subV2 = v2.GetType("Sub")!;
        Type hV2 = v2.GetType("H")!;
        object sub = hV2.GetField("S")!.GetValue(null)!;
        var d = (Action)hV2.GetField("D")!.GetValue(null)!;

        Assert.Single(d.GetInvocationList());
        d();
        Assert.Equal(100, subV2.GetField("Hits")!.GetValue(sub));
    }

    [Fact]
    public void StaticLambdaCache_NonCapturing_SharedIdentityAndNewBody()
    {
        Assembly v1 = Compile(
            "using System; public static class H { public static int Marker; public static Action D1; public static Action D2; " +
            "public static void Setup(){ Action a = () => Marker++; D1 = a; D2 = a; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public static class H { public static int Marker; public static Action D1; public static Action D2; " +
            "public static void Setup(){ Action a = () => Marker += 5; D1 = a; D2 = a; } }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        var d1 = (Action)hV2.GetField("D1")!.GetValue(null)!;
        var d2 = (Action)hV2.GetField("D2")!.GetValue(null)!;

        Assert.Same(d1, d2);
        d1();
        Assert.Equal(5, hV2.GetField("Marker")!.GetValue(null));
    }

    [Fact]
    public void UpdateReferences_RebindsDelegate_ToMigratedTargetAndMethod()
    {
        Assembly v1 = Compile(
            "public static class Hub { public static System.Action Callback; public static Counter C; } " +
            "public class Counter { public int Count; public void Increment() { Count++; } }");

        Type counterV1 = v1.GetType("Counter")!;
        Type hubV1 = v1.GetType("Hub")!;
        object counter = Activator.CreateInstance(counterV1)!;
        var callback = (Action)Delegate.CreateDelegate(typeof(Action), counter, counterV1.GetMethod("Increment")!);
        hubV1.GetField("Callback")!.SetValue(null, callback);
        hubV1.GetField("C")!.SetValue(null, counter);

        Assembly v2 = Compile(
            "public static class Hub { public static System.Action Callback; public static Counter C; } " +
            "public class Counter { public int Count; public int Extra; public void Increment() { Count++; } }");

        Migrate(v1, v2);

        Type counterV2 = v2.GetType("Counter")!;
        Type hubV2 = v2.GetType("Hub")!;
        var newCallback = (Action)hubV2.GetField("Callback")!.GetValue(null)!;
        object newCounter = hubV2.GetField("C")!.GetValue(null)!;

        Assert.Same(counterV2, newCounter.GetType());
        Assert.Same(newCounter, newCallback.Target);
        newCallback();
        Assert.Equal(1, counterV2.GetField("Count")!.GetValue(newCounter));
    }

    [Fact]
    public void C_Lambda_NoCapture_MigratesAndRunsNewBody()
    {
        Assembly v1 = Compile(
            "using System; public static class H { public static int Marker; public static Action Lam; " +
            "public static void Setup(){ Lam = () => Marker = 1; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public static class H { public static int Marker; public static Action Lam; " +
            "public static void Setup(){ Lam = () => Marker = 2; } }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        var lam = (Action)hV2.GetField("Lam")!.GetValue(null)!;
        lam();
        Assert.Equal(2, hV2.GetField("Marker")!.GetValue(null));
    }

    [Fact]
    public void C_Lambda_CapturingLocal_PreservesCapturedValue()
    {
        Assembly v1 = Compile(
            "using System; public static class H { public static int Result; public static Action Lam; " +
            "public static void Setup(){ int captured = 42; Lam = () => Result = captured; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public static class H { public static int Result; public static Action Lam; " +
            "public static void Setup(){ int captured = 0; Lam = () => Result = captured; } }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        var lam = (Action)hV2.GetField("Lam")!.GetValue(null)!;
        lam();
        Assert.Equal(42, hV2.GetField("Result")!.GetValue(null));
    }

    [Fact]
    public void C_Multicast_LambdaAndNamed_BothSurvive()
    {
        Assembly v1 = Compile(
            "using System; public class Sub { public int Hits; public void OnA(){ Hits++; } } " +
            "public static class H { public static Sub S; public static int LamRan; public static Action Evt; " +
            "public static void Setup(){ S = new Sub(); Evt = () => LamRan++; Evt += S.OnA; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class Sub { public int Hits; public int Extra; public void OnA(){ Hits++; } } " +
            "public static class H { public static Sub S; public static int LamRan; public static Action Evt; public static void Setup(){ Evt = () => LamRan++; Evt += S.OnA; } }");
        Migrate(v1, v2);

        Type subV2 = v2.GetType("Sub")!;
        Type hV2 = v2.GetType("H")!;
        object sub = hV2.GetField("S")!.GetValue(null)!;

        var evt = (Action)hV2.GetField("Evt")!.GetValue(null)!;
        evt();
        Assert.Equal(1, subV2.GetField("Hits")!.GetValue(sub));
        Assert.Equal(1, hV2.GetField("LamRan")!.GetValue(null));
    }

    [Fact]
    public void C_Lambda_CapturingThis_MigratesWithInstance()
    {
        Assembly v1 = Compile(
            "using System; public class C { public int V = 7; public int Result; public Action Make(){ return () => Result = V; } } " +
            "public static class H { public static C Inst; public static Action Lam; public static void Setup(){ Inst = new C(); Lam = Inst.Make(); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class C { public int V = 7; public int Result; public int Extra; public Action Make(){ return () => Result = V; } } " +
            "public static class H { public static C Inst; public static Action Lam; public static void Setup(){ } }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        Type hV2 = v2.GetType("H")!;
        object inst = hV2.GetField("Inst")!.GetValue(null)!;

        var lam = (Action)hV2.GetField("Lam")!.GetValue(null)!;
        lam();
        Assert.Equal(7, cV2.GetField("Result")!.GetValue(inst));
    }

    [Fact]
    public void Migrate_MulticastEvent_AndLambdaBecomesErrorDelegate()
    {
        Assembly v1 = Compile(
            "using System; public class Sub { public int Hits; public void OnA(){ Hits++; } public void OnB(){ Hits += 100; } } " +
            "public static class H { public static Action Evt; public static Action Lam; public static Sub S; public static int Marker; " +
            "public static void Setup() { S = new Sub(); Evt = S.OnA; Evt += S.OnB; Lam = () => Marker = 5; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class Sub { public int Hits; public int Extra; public void OnA(){ Hits++; } public void OnB(){ Hits += 100; } } " +
            "public static class H { public static Action Evt; public static Action Lam; public static Sub S; public static int Marker; public static void Setup() { } }");
        Migrate(v1, v2);

        Type subV2 = v2.GetType("Sub")!;
        Type hV2 = v2.GetType("H")!;
        object sub = hV2.GetField("S")!.GetValue(null)!;

        var evt = (Action)hV2.GetField("Evt")!.GetValue(null)!;
        evt();
        Assert.Equal(101, subV2.GetField("Hits")!.GetValue(sub));

        var lam = (Action)hV2.GetField("Lam")!.GetValue(null)!;
        Assert.NotNull(lam);
        Assert.Throws<ReloadedDelegateException>(() => lam()); // it is a clear error delegate
    }

    [Fact]
    public void Migrate_MulticastEvent_DeletedLambda_DroppedSoNamedSurvives()
    {
        Assembly v1 = Compile(
            "using System; public class Sub { public int Hits; public void OnA(){ Hits++; } } " +
            "public static class H { public static Action Evt; public static Sub S; public static int LamRan; " +
            "public static void Setup() { S = new Sub(); Evt = () => LamRan++; Evt += S.OnA; } }"); // lambda FIRST
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class Sub { public int Hits; public int Extra; public void OnA(){ Hits++; } } " +
            "public static class H { public static Action Evt; public static Sub S; public static int LamRan; public static void Setup() { } }");
        Migrate(v1, v2);

        Type subV2 = v2.GetType("Sub")!;
        Type hV2 = v2.GetType("H")!;
        object sub = hV2.GetField("S")!.GetValue(null)!;
        var evt = (Action)hV2.GetField("Evt")!.GetValue(null)!;

        Assert.Single(evt.GetInvocationList());
        evt();
        Assert.Equal(1, subV2.GetField("Hits")!.GetValue(sub));
    }

    // A static local function converted to a delegate lives directly on the user type and has no target. The
    // previous implementation classified this as a cache class lambda and looked in the wrong place.
    [Fact]
    public void StaticLocalFunctionDelegate_Survives()
    {
        const string body =
            "using System; public static class H { public static Func<int> F; " +
            "  public static void Setup() { static int Local() => 11; F = Local; } }";

        Assembly v1 = Compile(body);
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(body);
        Migrate(v1, v2);

        var f = (Delegate)v2.GetType("H")!.GetField("F")!.GetValue(null)!;
        Assert.Same(v2, f.Method.DeclaringType!.Assembly);
        Assert.Equal(11, f.DynamicInvoke());
    }

    [Fact]
    public void InstanceLocalFunctionDelegate_Survives()
    {
        const string body =
            "using System; public class Holder { public int N = 4; public Func<int> F; " +
            "  public void Setup() { int Local() => N + 1; F = Local; } } " +
            "public static class H { public static Holder Value; " +
            "  public static void Setup() { Value = new Holder(); Value.Setup(); } }";

        Assembly v1 = Compile(body);
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(body.Replace("public int N = 4;", "public int N = 4; public int Extra;"));
        Migrate(v1, v2);

        object holder = v2.GetType("H")!.GetField("Value")!.GetValue(null)!;
        var f = (Delegate)v2.GetType("Holder")!.GetField("F")!.GetValue(holder)!;

        Assert.Same(holder, f.Target);
        Assert.Equal(5, f.DynamicInvoke());
    }

    [Fact]
    public void LambdaInGenericMethod_Survives()
    {
        const string body =
            "using System; public static class H { public static Func<string> F; " +
            "  public static Func<string> Make<T>(T value) { return () => value.ToString(); } " +
            "  public static void Setup() { F = Make(41); } }";

        Assembly v1 = Compile(body);
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(body);
        Migrate(v1, v2);

        var f = (Delegate)v2.GetType("H")!.GetField("F")!.GetValue(null)!;
        Assert.Equal("41", f.DynamicInvoke());
    }

    [Fact]
    public void LambdaInGenericType_Survives()
    {
        const string body =
            "using System; public class Box<T> { public T Value; public Func<string> F; " +
            "  public void Setup() { F = () => Value.ToString(); } } " +
            "public static class H { public static Box<int> B; " +
            "  public static void Setup() { B = new Box<int> { Value = 12 }; B.Setup(); } }";

        Assembly v1 = Compile(body);
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(body.Replace("public T Value;", "public T Value; public int Extra;"));
        Migrate(v1, v2);

        object box = v2.GetType("H")!.GetField("B")!.GetValue(null)!;
        var f = (Delegate)box.GetType().GetField("F")!.GetValue(box)!;

        Assert.Equal("12", f.DynamicInvoke());
    }

    [Fact]
    public void LambdaInConstructor_Survives()
    {
        const string body =
            "using System; public class Holder { public int N; public Func<int> F; " +
            "  public Holder(int n) { N = n; F = () => N * 2; } } " +
            "public static class H { public static Holder Value; public static void Setup() { Value = new Holder(6); } }";

        Assembly v1 = Compile(body);
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(body.Replace("public int N;", "public int N; public int Extra;"));
        Migrate(v1, v2);

        object holder = v2.GetType("H")!.GetField("Value")!.GetValue(null)!;
        var f = (Delegate)v2.GetType("Holder")!.GetField("F")!.GetValue(holder)!;

        Assert.Equal(12, f.DynamicInvoke());
    }

    [Fact]
    public void LambdaInStaticConstructor_Survives()
    {
        const string body =
            "using System; public static class H { public static int Seed; public static Func<int> F; " +
            "  static H() { Seed = 7; F = () => Seed + 1; } }";

        Assembly v1 = Compile(body);
        _ = v1.GetType("H")!.GetField("F")!.GetValue(null);

        Assembly v2 = Compile(body);
        Migrate(v1, v2);

        var f = (Delegate)v2.GetType("H")!.GetField("F")!.GetValue(null)!;
        Assert.Equal(8, f.DynamicInvoke());
    }

    // A lambda declared inside another lambda, both capturing.
    [Fact]
    public void NestedClosure_Survives()
    {
        const string body =
            "using System; public static class H { public static Func<int> Outer; " +
            "  public static void Setup() { int a = 2; Func<Func<int>> make = () => { int b = 3; return () => a * b; }; Outer = make(); } }";

        Assembly v1 = Compile(body);
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(body);
        Migrate(v1, v2);

        var outer = (Delegate)v2.GetType("H")!.GetField("Outer")!.GetValue(null)!;
        Assert.Equal(6, outer.DynamicInvoke());
    }

    // An open delegate has a null target and takes its instance as the first argument.
    [Fact]
    public void OpenInstanceDelegate_Survives()
    {
        const string body =
            "using System; public class Holder { public int N; public int Get() { return N; } } " +
            "public static class H { public static Func<Holder, int> F; public static Holder Value; " +
            "  public static void Setup() { Value = new Holder { N = 13 }; " +
            "    F = (Func<Holder, int>)Delegate.CreateDelegate(typeof(Func<Holder, int>), typeof(Holder).GetMethod(\"Get\")); } }";

        Assembly v1 = Compile(body);
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(body.Replace("public int N;", "public int N; public int Extra;"));
        Migrate(v1, v2);

        var f = (Delegate)v2.GetType("H")!.GetField("F")!.GetValue(null)!;
        object holder = v2.GetType("H")!.GetField("Value")!.GetValue(null)!;

        Assert.Null(f.Target);
        Assert.Equal(13, f.DynamicInvoke(holder));
    }

    [Fact]
    public void LambdaSurvives_TwoConsecutiveReloads()
    {
        const string body =
            "public static class H { public static System.Func<int> F; public static int Seed; " +
            "  public static void Setup() { Seed = 3; F = () => Seed * 2; } }";

        Assembly v1 = Compile(body);
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(body);
        Migrate(v1, v2);
        Assert.Equal(6, ((Delegate)v2.GetType("H")!.GetField("F")!.GetValue(null)!).DynamicInvoke());

        Assembly v3 = Compile(body);
        Migrate(v2, v3);
        Assert.Equal(6, ((Delegate)v3.GetType("H")!.GetField("F")!.GetValue(null)!).DynamicInvoke());
    }

    [Fact]
    public void ArrayOfDelegates_Survives()
    {
        const string body =
            "using System; public static class H { public static Func<int>[] Handlers; public static int Seed; " +
            "  public static void Setup() { Seed = 4; Handlers = new Func<int>[] { () => Seed, () => Seed * 2 }; } }";

        Assembly v1 = Compile(body);
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(body);
        Migrate(v1, v2);

        var handlers = (Array)v2.GetType("H")!.GetField("Handlers")!.GetValue(null)!;

        Assert.Equal(4, ((Delegate)handlers.GetValue(0)!).DynamicInvoke());
        Assert.Equal(8, ((Delegate)handlers.GetValue(1)!).DynamicInvoke());
    }

    [Fact]
    public void MulticastEvent_KeepsSubscriberOrder()
    {
        const string body =
            "using System; using System.Collections.Generic; " +
            "public class Sink { public static List<int> Log = new(); public int N; public void Hit() { Log.Add(N); } } " +
            "public static class H { public static Action A; public static Sink S1; public static Sink S2; public static Sink S3; " +
            "  public static void Setup() { S1 = new Sink { N = 1 }; S2 = new Sink { N = 2 }; S3 = new Sink { N = 3 }; " +
            "    A = S1.Hit; A += S2.Hit; A += S3.Hit; } }";

        Assembly v1 = Compile(body);
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(body.Replace("public int N;", "public int N; public int Extra;"));
        Migrate(v1, v2);

        var action = (Delegate)v2.GetType("H")!.GetField("A")!.GetValue(null)!;
        action.DynamicInvoke();

        var log = (IList)v2.GetType("Sink")!.GetField("Log")!.GetValue(null)!;
        Assert.Equal(new[] { 1, 2, 3 }, log.Cast<int>().ToArray());
    }

    [Fact]
    public void StaticEvent_KeepsSubscribers()
    {
        const string body =
            "using System; public class Sink { public static int Hits; public void Hit() { Hits++; } } " +
            "public static class H { public static event Action Fired; public static Sink S; " +
            "  public static void Setup() { S = new Sink(); Fired += S.Hit; } " +
            "  public static void Raise() { Fired?.Invoke(); } }";

        Assembly v1 = Compile(body);
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(body.Replace("public static int Hits;", "public static int Hits; public int Extra;"));
        Migrate(v1, v2);

        v2.GetType("H")!.GetMethod("Raise")!.Invoke(null, null);
        Assert.Equal(1, v2.GetType("Sink")!.GetField("Hits")!.GetValue(null));
    }

    // Isolates the stand-in delegate from lambda matching: the method is simply renamed away.
    [Fact]
    public void BrokenDelegate_WithReturnValue_ThrowsTheRightException()
    {
        Assembly v1 = Compile(
            "using System; public static class H { public static Func<int> F; " +
            "  public static int Old() { return 1; } " +
            "  public static void Setup() { F = Old; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public static class H { public static Func<int> F; " +
            "  public static int Renamed() { return 1; } " +
            "  public static void Setup() { } }");

        Migrate(v1, v2);

        var f = (Delegate?)v2.GetType("H")!.GetField("F")!.GetValue(null);
        Assert.NotNull(f);

        var thrown = Record.Exception(() => f!.DynamicInvoke());
        var inner = (thrown as TargetInvocationException)?.InnerException ?? thrown;

        Assert.IsType<ReloadedDelegateException>(inner);
    }

    [Fact]
    public void BrokenDelegate_ReturningVoid_ThrowsTheRightException()
    {
        Assembly v1 = Compile(
            "using System; public static class H { public static Action F; " +
            "  public static void Old() { } " +
            "  public static void Setup() { F = Old; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public static class H { public static Action F; " +
            "  public static void Renamed() { } " +
            "  public static void Setup() { } }");

        Migrate(v1, v2);

        var f = (Delegate?)v2.GetType("H")!.GetField("F")!.GetValue(null);
        Assert.NotNull(f);

        var thrown = Record.Exception(() => f!.DynamicInvoke());
        var inner = (thrown as TargetInvocationException)?.InnerException ?? thrown;

        Assert.IsType<ReloadedDelegateException>(inner);
    }

    // A stand-in delegate from an earlier reload has to keep throwing, and keep saying why.
    [Fact]
    public void BrokenDelegate_SurvivesASecondReload()
    {
        const string withLambda =
            "public static class H { public static System.Action A; " +
            "  public static void Setup() { int x = 1; A = () => System.GC.KeepAlive(x); } }";
        const string withoutLambda =
            "public static class H { public static System.Action A; public static void Setup() { } }";

        Assembly v1 = Compile(withLambda);
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(withoutLambda);
        Migrate(v1, v2);

        var first = (Delegate?)v2.GetType("H")!.GetField("A")!.GetValue(null);
        Assert.NotNull(first);

        Assembly v3 = Compile(withoutLambda);
        Migrate(v2, v3);

        var second = (Delegate?)v3.GetType("H")!.GetField("A")!.GetValue(null);
        Assert.NotNull(second);

        var raised = Record.Exception(() => second!.DynamicInvoke());
        var thrown = Assert.IsType<ReloadedDelegateException>(
            (raised as TargetInvocationException)?.InnerException ?? raised);

        Assert.Equal(BrokenDelegateReason.NoLambdaMatch, thrown.Reason);
    }

    [Fact]
    public void BrokenDelegate_ReasonSurvivesThreeReloads()
    {
        const string withLambda =
            "using System; public static class H { public static Func<int> F; " +
            "  public static void Setup() { int x = 1; F = () => x; } }";
        const string withoutLambda =
            "using System; public static class H { public static Func<int> F; public static void Setup() { } }";

        Assembly current = Compile(withLambda);
        current.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        for (int generation = 0; generation < 3; generation++)
        {
            Assembly next = Compile(withoutLambda);
            Migrate(current, next);

            var f = (Delegate?)next.GetType("H")!.GetField("F")!.GetValue(null);
            Assert.NotNull(f);

            var raised = Record.Exception(() => f!.DynamicInvoke());
            var thrown = Assert.IsType<ReloadedDelegateException>(
                (raised as TargetInvocationException)?.InnerException ?? raised);

            Assert.Equal(BrokenDelegateReason.NoLambdaMatch, thrown.Reason);
            current = next;
        }
    }

    [Fact]
    public void BrokenDelegate_UnderDropPolicy_BecomesNull()
    {
        Assembly v1 = Compile("using System; public static class H { public static Func<int> F; " +
            "  public static int Old() => 1; public static void Setup() { F = Old; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System; public static class H { public static Func<int> F; " +
            "  public static int Renamed() => 1; public static void Setup() { } }");

        var report = Reload(o =>
        {
            o.Scope.Include(v1);
            o.BrokenDelegates = BrokenDelegatePolicy.Drop;
        }, b => b.Replace(v1, v2));

        Assert.Null(v2.GetType("H")!.GetField("F")!.GetValue(null));
        Assert.Equal(1, report.Statistics.DelegatesBroken);
    }
}
