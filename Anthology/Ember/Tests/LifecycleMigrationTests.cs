using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Xunit;

namespace Prowl.Ember.Tests;

/// <summary>
/// Migration tests for the lifecycle/opt-out API: IReloadAware, IReloadObserver hooks, [ReloadInitializer], and [ReloadIgnore].
/// </summary>
[Trait("Category", "Build")]
public class LifecycleMigrationTests : MigrationTestBase
{
    private sealed class PersistProbe : IReloadAware, IReloadObserver
    {
        public bool Persisted, Destroyed, Created;
        public void OnReloadPreserved() => Persisted = true;
        public void OnReloadDetach(Prowl.Ember.ReloadState s) => Destroyed = true;
        public void OnReloadAttach(Prowl.Ember.ReloadState s) => Created = true;
    }

    private sealed class PersistCache : IReloadAware, IReloadObserver
    {
        [ReloadIgnore] public readonly Dictionary<Type, int> Entries = new();
        public bool Persisted;
        public void OnReloadPreserved() { Persisted = true; Entries.Clear(); }
    }

    [Fact]
    public void Lifecycle_StateRoundTrip_MultipleKeys()
    {
        const string body =
            "using System.Collections.Generic; public class M : Prowl.Ember.IReloadAware, Prowl.Ember.IReloadObserver { " +
            "public int A; public string B; public double C; " +
            "public void OnReloadDetach(Prowl.Ember.ReloadState s) { s.Set(\"a\", 11); s.Set(\"b\", \"two\"); s.Set(\"c\", 3.5); } " +
            "public void OnReloadAttach(Prowl.Ember.ReloadState s) { A = s.GetOrDefault<int>(\"a\"); B = s.GetOrDefault<string>(\"b\"); C = s.GetOrDefault<double>(\"c\"); } } ";

        Assembly v1 = Compile(body + "public static class H { public static M Inst; public static void Setup() { Inst = new M(); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(body.Replace("public double C;", "public double C; public int Extra;") +
            "public static class H { public static M Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type mV2 = v2.GetType("M")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;
        Assert.Equal(11, mV2.GetField("A")!.GetValue(inst));
        Assert.Equal("two", mV2.GetField("B")!.GetValue(inst));
        Assert.Equal(3.5, mV2.GetField("C")!.GetValue(inst));
    }

    [Fact]
    public void Lifecycle_StateWithSwappedReference_MigratedToNewType()
    {
        const string body =
            "using System.Collections.Generic; public class Other { public int Id; } " +
            "public class M : Prowl.Ember.IReloadAware, Prowl.Ember.IReloadObserver { " +
            "public Other Ref; public Other FromState; " +
            "public void OnReloadDetach(Prowl.Ember.ReloadState s) { s.Set(\"o\", Ref); } " +
            "public void OnReloadAttach(Prowl.Ember.ReloadState s) { FromState = s.GetOrDefault<Other>(\"o\"); } } ";

        Assembly v1 = Compile(body + "public static class H { public static M Inst; public static void Setup() { Inst = new M(); Inst.Ref = new Other{Id=9}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(body.Replace("public int Id;", "public int Id; public int Extra;") +
            "public static class H { public static M Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type mV2 = v2.GetType("M")!;
        Type otherV2 = v2.GetType("Other")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;
        object fromState = mV2.GetField("FromState")!.GetValue(inst)!;

        Assert.Same(otherV2, fromState.GetType());
        Assert.Same(mV2.GetField("Ref")!.GetValue(inst), fromState);
        Assert.Equal(9, otherV2.GetField("Id")!.GetValue(fromState));
    }

    [Fact]
    public void Lifecycle_DestroyedThrows_SiblingsStillMigrate()
    {
        Assembly v1 = Compile(
            "using System; using System.Collections.Generic; " +
            "public class M : Prowl.Ember.IReloadAware, Prowl.Ember.IReloadObserver { public int Val; " +
            "public void OnReloadDetach(Prowl.Ember.ReloadState s) { throw new Exception(\"boom\"); } } " +
            "public class N { public int Data; } " +
            "public static class H { public static M A; public static N B; public static void Setup() { A = new M{Val=5}; B = new N{Data=9}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; using System.Collections.Generic; " +
            "public class M : Prowl.Ember.IReloadAware, Prowl.Ember.IReloadObserver { public int Val; public int Extra; " +
            "public void OnReloadDetach(Prowl.Ember.ReloadState s) { throw new Exception(\"boom\"); } } " +
            "public class N { public int Data; public int Extra; } " +
            "public static class H { public static M A; public static N B; public static void Setup() { } }");
        Migrate(v1, v2);

        Type mV2 = v2.GetType("M")!;
        Type nV2 = v2.GetType("N")!;
        Type hV2 = v2.GetType("H")!;
        object a = hV2.GetField("A")!.GetValue(null)!;
        object b = hV2.GetField("B")!.GetValue(null)!;

        Assert.Same(nV2, b.GetType());
        Assert.Equal(9, nV2.GetField("Data")!.GetValue(b));
        Assert.Same(mV2, a.GetType());
        Assert.Equal(5, mV2.GetField("Val")!.GetValue(a));
    }

    [Fact]
    public void Lifecycle_CreatedThrows_SiblingsStillMigrate()
    {
        Assembly v1 = Compile(
            "using System; using System.Collections.Generic; " +
            "public class M : Prowl.Ember.IReloadAware, Prowl.Ember.IReloadObserver { public int R; " +
            "public void OnReloadDetach(Prowl.Ember.ReloadState s) { s.Set(\"v\", 1); } " +
            "public void OnReloadAttach(Prowl.Ember.ReloadState s) { throw new Exception(\"boom\"); } } " +
            "public class N : Prowl.Ember.IReloadAware, Prowl.Ember.IReloadObserver { public int R; " +
            "public void OnReloadDetach(Prowl.Ember.ReloadState s) { s.Set(\"v\", 7); } " +
            "public void OnReloadAttach(Prowl.Ember.ReloadState s) { R = s.GetOrDefault<int>(\"v\"); } } " +
            "public static class H { public static M A; public static N B; public static void Setup() { A = new M(); B = new N(); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; using System.Collections.Generic; " +
            "public class M : Prowl.Ember.IReloadAware, Prowl.Ember.IReloadObserver { public int R; public int Extra; " +
            "public void OnReloadDetach(Prowl.Ember.ReloadState s) { s.Set(\"v\", 1); } " +
            "public void OnReloadAttach(Prowl.Ember.ReloadState s) { throw new Exception(\"boom\"); } } " +
            "public class N : Prowl.Ember.IReloadAware, Prowl.Ember.IReloadObserver { public int R; public int Extra; " +
            "public void OnReloadDetach(Prowl.Ember.ReloadState s) { s.Set(\"v\", 7); } " +
            "public void OnReloadAttach(Prowl.Ember.ReloadState s) { R = s.GetOrDefault<int>(\"v\"); } } " +
            "public static class H { public static M A; public static N B; public static void Setup() { } }");
        Migrate(v1, v2);

        Type nV2 = v2.GetType("N")!;
        object b = v2.GetType("H")!.GetField("B")!.GetValue(null)!;
        Assert.Equal(7, nV2.GetField("R")!.GetValue(b));
    }

    [Fact]
    public void Lifecycle_Persisted_FiresForReachedUnchangedType()
    {
        Assembly v1 = Compile("public class E { public int Id; }");
        Assembly v2 = Compile("public class E { public int Id; public int Extra; }");

        var probe = new PersistProbe();
        Reload(o => o.Scope.Include(v1), b => b.Replace(v1, v2).Root(probe));

        Assert.True(probe.Persisted);
        Assert.False(probe.Destroyed);
        Assert.False(probe.Created);
    }

    [Fact]
    public void Lifecycle_Failed_FiresWhenTypeRemoved()
    {
        Assembly v1 = Compile(
            "using System.Collections.Generic; public class T : Prowl.Ember.IReloadAware, Prowl.Ember.IReloadObserver { " +
            "public bool FailedCalled; public bool DestroyedCalled; " +
            "public void OnReloadDetach(Prowl.Ember.ReloadState s) { DestroyedCalled = true; } " +
            "public void OnReloadDropped() { FailedCalled = true; } } " +
            "public static class H { public static object Held; public static void Setup() { Held = new T(); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Type tV1 = v1.GetType("T")!;
        object oldT = v1.GetType("H")!.GetField("Held")!.GetValue(null)!;

        Assembly v2 = Compile("public static class H { public static object Held; public static void Setup() { } }");
        Migrate(v1, v2);

        Assert.Null(v2.GetType("H")!.GetField("Held")!.GetValue(null));
        Assert.True((bool)tV1.GetField("FailedCalled")!.GetValue(oldT)!);
    }

    [Fact]
    public void Lifecycle_RemovedType_DestroyedNotCalled_OnlyFailed()
    {
        Assembly v1 = Compile(
            "using System.Collections.Generic; public class T : Prowl.Ember.IReloadAware, Prowl.Ember.IReloadObserver { " +
            "public bool FailedCalled; public bool DestroyedCalled; " +
            "public void OnReloadDetach(Prowl.Ember.ReloadState s) { DestroyedCalled = true; } " +
            "public void OnReloadDropped() { FailedCalled = true; } } " +
            "public static class H { public static object Held; public static void Setup() { Held = new T(); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Type tV1 = v1.GetType("T")!;
        object oldT = v1.GetType("H")!.GetField("Held")!.GetValue(null)!;

        Assembly v2 = Compile("public static class H { public static object Held; public static void Setup() { } }");
        Migrate(v1, v2);

        Assert.True((bool)tV1.GetField("FailedCalled")!.GetValue(oldT)!);
        Assert.False((bool)tV1.GetField("DestroyedCalled")!.GetValue(oldT)!);
    }

    [Fact]
    public void Lifecycle_OnStruct_CreatedResultPersists()
    {
        const string body =
            "using System.Collections.Generic; public struct S : Prowl.Ember.IReloadAware, Prowl.Ember.IReloadObserver { " +
            "public int Restored; " +
            "public void OnReloadDetach(Prowl.Ember.ReloadState s) { s.Set(\"v\", 42); } " +
            "public void OnReloadAttach(Prowl.Ember.ReloadState s) { Restored = s.GetOrDefault<int>(\"v\"); } } ";

        Assembly v1 = Compile(body + "public static class H { public static S Inst; public static void Setup() { Inst = new S(); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(body.Replace("public int Restored;", "public int Restored; public int Extra;") +
            "public static class H { public static S Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type sV2 = v2.GetType("S")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;
        Assert.Equal(42, sV2.GetField("Restored")!.GetValue(inst));
    }

    [Fact]
    public void Lifecycle_OnBaseClass_InheritedByDerivedSwapped()
    {
        const string body =
            "using System.Collections.Generic; public class B : Prowl.Ember.IReloadAware, Prowl.Ember.IReloadObserver { " +
            "public int Restored; " +
            "public void OnReloadDetach(Prowl.Ember.ReloadState s) { s.Set(\"v\", 99); } " +
            "public void OnReloadAttach(Prowl.Ember.ReloadState s) { Restored = s.GetOrDefault<int>(\"v\"); } } " +
            "public class D : B { public int DerivedVal; } ";

        Assembly v1 = Compile(body + "public static class H { public static D Inst; public static void Setup() { Inst = new D{DerivedVal=3}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(body.Replace("public int DerivedVal;", "public int DerivedVal; public int Extra;") +
            "public static class H { public static D Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type dV2 = v2.GetType("D")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;
        Assert.Equal(99, dV2.GetField("Restored")!.GetValue(inst));
        Assert.Equal(3, dV2.GetField("DerivedVal")!.GetValue(inst));
    }

    [Fact]
    public void ReloadInitializer_MethodReadsCarriedOverField()
    {
        Assembly v1 = Compile(
            "public class C { public int Level; } " +
            "public static class H { public static C Inst; public static void Setup() { Inst = new C{Level=7}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class C { public int Level; " +
            "[Prowl.Ember.ReloadInitializer(nameof(Derive))] public int Power; " +
            "void Derive() { Power = Level * 10; } } " +
            "public static class H { public static C Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;
        Assert.Equal(70, cV2.GetField("Power")!.GetValue(inst));
    }

    [Fact]
    public void ReloadInitializer_Null_LeavesDefault()
    {
        Assembly v1 = Compile(
            "public class C { public int Level; } " +
            "public static class H { public static C Inst; public static void Setup() { Inst = new C{Level=7}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class C { public int Level; [Prowl.Ember.ReloadInitializer(null)] public int Power = 55; } " +
            "public static class H { public static C Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;
        Assert.Equal(0, cV2.GetField("Power")!.GetValue(inst));
    }

    [Fact]
    public void ReloadInitializer_MethodThrows_DoesNotAbortMigration()
    {
        Assembly v1 = Compile(
            "public class C { public int Keep; } public class Sibling { public int Data; } " +
            "public static class H { public static C Inst; public static Sibling S; public static void Setup() { Inst = new C{Keep=3}; S = new Sibling{Data=8}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System; public class C { public int Keep; " +
            "[Prowl.Ember.ReloadInitializer(nameof(Boom))] public int Power; void Boom() { throw new Exception(\"x\"); } } " +
            "public class Sibling { public int Data; public int Extra; } " +
            "public static class H { public static C Inst; public static Sibling S; public static void Setup() { } }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        Type siblingV2 = v2.GetType("Sibling")!;
        Type hV2 = v2.GetType("H")!;
        object inst = hV2.GetField("Inst")!.GetValue(null)!;
        object sib = hV2.GetField("S")!.GetValue(null)!;

        Assert.Equal(3, cV2.GetField("Keep")!.GetValue(inst));
        Assert.Equal(0, cV2.GetField("Power")!.GetValue(inst));
        Assert.Equal(8, siblingV2.GetField("Data")!.GetValue(sib));
    }

    [Fact]
    public void ReloadInitializer_ForwardReferenceToNewField_Ordering()
    {
        Assembly v1 = Compile(
            "public class C { public int Level; } " +
            "public static class H { public static C Inst; public static void Setup() { Inst = new C{Level=1}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class C { public int Level; " +
            "[Prowl.Ember.ReloadInitializer(nameof(SetDoubled))] public int Doubled; " +
            "public int Baseline = 21; " +
            "void SetDoubled() { Doubled = Baseline * 2; } } " +
            "public static class H { public static C Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;
        Assert.Equal(21, cV2.GetField("Baseline")!.GetValue(inst));
        Assert.Equal(42, cV2.GetField("Doubled")!.GetValue(inst));
    }

    [Fact]
    public void ReloadInitializer_TwoNewFields_BothRun()
    {
        Assembly v1 = Compile(
            "public class C { public int Level; } " +
            "public static class H { public static C Inst; public static void Setup() { Inst = new C{Level=10}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class C { public int Level; " +
            "[Prowl.Ember.ReloadInitializer(nameof(SetX))] public int X; " +
            "[Prowl.Ember.ReloadInitializer(nameof(SetY))] public int Y; " +
            "void SetX() { X = Level + 1; } void SetY() { Y = Level + 2; } } " +
            "public static class H { public static C Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;
        Assert.Equal(11, cV2.GetField("X")!.GetValue(inst));
        Assert.Equal(12, cV2.GetField("Y")!.GetValue(inst));
    }

    [Fact]
    public void ReloadIgnore_OnStaticField_NotMigrated()
    {
        Assembly v1 = Compile(
            "public class T { public int Val; } " +
            "public static class H { [Prowl.Ember.ReloadIgnore] public static T Cached; public static T Live; " +
            "public static void Setup() { Cached = new T{Val=1}; Live = new T{Val=2}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class T { public int Val; public int Extra; } " +
            "public static class H { [Prowl.Ember.ReloadIgnore] public static T Cached; public static T Live; public static void Setup() { } }");
        Migrate(v1, v2);

        Type tV2 = v2.GetType("T")!;
        Type hV2 = v2.GetType("H")!;
        Assert.Null(hV2.GetField("Cached")!.GetValue(null));
        object live = hV2.GetField("Live")!.GetValue(null)!;
        Assert.Same(tV2, live.GetType());
        Assert.Equal(2, tV2.GetField("Val")!.GetValue(live));
    }

    [Fact]
    // Type-level [ReloadIgnore] is a perf hint for types that don't change; a type you actually recompile (here
    // it gains a field) still migrates - pinning it would strand stale instances of code you just edited.
    public void ReloadIgnore_OnRecompiledType_StillMigrates()
    {
        Assembly v1 = Compile(
            "[Prowl.Ember.ReloadIgnore] public class T { public int Val; } " +
            "public static class H { public static object Held; public static void Setup() { Held = new T{Val=5}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "[Prowl.Ember.ReloadIgnore] public class T { public int Val; public int Extra; } " +
            "public static class H { public static object Held; public static void Setup() { } }");
        Migrate(v1, v2);

        Type tV2 = v2.GetType("T")!;
        object newHeld = v2.GetType("H")!.GetField("Held")!.GetValue(null)!;
        Assert.Same(tV2, newHeld.GetType());
        Assert.Equal(5, tV2.GetField("Val")!.GetValue(newHeld));
    }

    [Fact]
    public void ReloadIgnore_OnReferenceField_GraphNotMigratedThrough()
    {
        Assembly v1 = Compile(
            "public class Node { public int Id; } " +
            "public class C { [Prowl.Ember.ReloadIgnore] public Node Child; public int Keep; } " +
            "public static class H { public static C Inst; public static void Setup() { Inst = new C{ Keep = 1, Child = new Node{Id=3} }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class Node { public int Id; public int Extra; } " +
            "public class C { [Prowl.Ember.ReloadIgnore] public Node Child; public int Keep; public int New; } " +
            "public static class H { public static C Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;
        Assert.Null(cV2.GetField("Child")!.GetValue(inst));
        Assert.Equal(1, cV2.GetField("Keep")!.GetValue(inst));
    }

    [Fact]
    public void ReloadIgnore_OnAutoProperty_BackingFieldProtected()
    {
        Assembly v1 = Compile(
            "public class C { [Prowl.Ember.ReloadIgnore] public int Cache { get; set; } public int Kept { get; set; } } " +
            "public static class H { public static C Inst; public static void Setup() { Inst = new C { Cache = 111, Kept = 222 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class C { [Prowl.Ember.ReloadIgnore] public int Cache { get; set; } public int Kept { get; set; } public int Added; } " +
            "public static class H { public static C Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;
        Assert.Equal(222, cV2.GetProperty("Kept")!.GetValue(inst));
        Assert.Equal(0, cV2.GetProperty("Cache")!.GetValue(inst));
    }

    [Fact]
    public void Field_Renamed_OldValueDropped_NewDefaults()
    {
        Assembly v1 = Compile(
            "public class C { public int Old; } " +
            "public static class H { public static C Inst; public static void Setup() { Inst = new C{Old=5}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class C { public int New; } " +
            "public static class H { public static C Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;
        Assert.Null(cV2.GetField("Old"));
        Assert.Equal(0, cV2.GetField("New")!.GetValue(inst));
    }

    [Fact]
    public void Field_TypeChanged_ValueDiscarded()
    {
        Assembly v1 = Compile(
            "public class C { public int V; } " +
            "public static class H { public static C Inst; public static void Setup() { Inst = new C{V=7}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class C { public string V; } " +
            "public static class H { public static C Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;
        Assert.Null(cV2.GetField("V")!.GetValue(inst));
    }

    [Fact]
    public void ExistingField_KeepsValue_WhileNewSiblingInitializes()
    {
        Assembly v1 = Compile(
            "public class C { public int Keep; } " +
            "public static class H { public static C Inst; public static void Setup() { Inst = new C{Keep=4}; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class C { public int Keep; public int Added = 9; } " +
            "public static class H { public static C Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;
        Assert.Equal(4, cV2.GetField("Keep")!.GetValue(inst));
        Assert.Equal(9, cV2.GetField("Added")!.GetValue(inst));
    }

    [Fact]
    public void LifecyclePersisted_FiresForCacheReachedByWalk()
    {
        Assembly v1 = Compile("public class E { public int Id; }");
        Assembly v2 = Compile("public class E { public int Id; public int Extra; }");

        var cache = new PersistCache();
        cache.Entries[typeof(string)] = 42;

        Reload(o => o.Scope.Include(v1), b => b.Replace(v1, v2).Root(cache));

        Assert.True(cache.Persisted);
        Assert.Empty(cache.Entries);
    }

    [Fact]
    public void UpdateReferences_NewField_ReloadInitializerMethod_IsInvoked()
    {
        Assembly v1 = Compile(
            "public static class Reg { public static Unit U; } public class Unit { public int Level; }");
        Type unitV1 = v1.GetType("Unit")!;
        object u = Activator.CreateInstance(unitV1)!;
        unitV1.GetField("Level")!.SetValue(u, 7);
        v1.GetType("Reg")!.GetField("U")!.SetValue(null, u);

        Assembly v2 = Compile(
            "public static class Reg { public static Unit U; } " +
            "public class Unit { public int Level; " +
            "[Prowl.Ember.ReloadInitializer(nameof(Derive))] public int Power; " +
            "void Derive() { Power = Level * 10; } }");

        Migrate(v1, v2);

        Type unitV2 = v2.GetType("Unit")!;
        object newU = v2.GetType("Reg")!.GetField("U")!.GetValue(null)!;
        Assert.Equal(7, unitV2.GetField("Level")!.GetValue(newU));
        Assert.Equal(70, unitV2.GetField("Power")!.GetValue(newU));
    }

    [Fact]
    public void ReloadIgnore_OnAutoProperty_SkipsBackingField()
    {
        Assembly v1 = Compile(
            "public class C { [Prowl.Ember.ReloadIgnore] public int Kept { get; set; } public int Migrated { get; set; } } " +
            "public static class H { public static C Obj; public static void Setup(){ Obj = new C { Kept = 111, Migrated = 222 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class C { [Prowl.Ember.ReloadIgnore] public int Kept { get; set; } public int Migrated { get; set; } public int Added; } " +
            "public static class H { public static C Obj; public static void Setup(){} }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        object obj = v2.GetType("H")!.GetField("Obj")!.GetValue(null)!;
        Assert.Equal(222, cV2.GetProperty("Migrated")!.GetValue(obj));
        Assert.Equal(0, cV2.GetProperty("Kept")!.GetValue(obj));
    }

    [Fact]
    public void Migrate_ReloadIgnoreField_IsNotCarried()
    {
        Assembly v1 = Compile(
            "public class C { public int Kept; [Prowl.Ember.ReloadIgnore] public int Cache; } " +
            "public static class H { public static C Inst; public static void Setup() { Inst = new C{ Kept = 8, Cache = 99 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class C { public int Kept; [Prowl.Ember.ReloadIgnore] public int Cache; public int New; } " +
            "public static class H { public static C Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type cV2 = v2.GetType("C")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;
        Assert.Equal(8, cV2.GetField("Kept")!.GetValue(inst));
        Assert.Equal(0, cV2.GetField("Cache")!.GetValue(inst));
    }

    [Fact]
    public void Migrate_Lifecycle_RoundTripsState()
    {
        Assembly v1 = Compile(
            "using System.Collections.Generic; public class M : Prowl.Ember.IReloadAware, Prowl.Ember.IReloadObserver { " +
            "public int Restored; " +
            "public void OnReloadDetach(Prowl.Ember.ReloadState s) { s.Set(\"v\", 77); } " +
            "public void OnReloadAttach(Prowl.Ember.ReloadState s) { Restored = s.GetOrDefault<int>(\"v\"); } } " +
            "public static class H { public static M Inst; public static void Setup() { Inst = new M(); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "using System.Collections.Generic; public class M : Prowl.Ember.IReloadAware, Prowl.Ember.IReloadObserver { " +
            "public int Restored; public int Extra; " +
            "public void OnReloadDetach(Prowl.Ember.ReloadState s) { s.Set(\"v\", 77); } " +
            "public void OnReloadAttach(Prowl.Ember.ReloadState s) { Restored = s.GetOrDefault<int>(\"v\"); } } " +
            "public static class H { public static M Inst; public static void Setup() { } }");
        Migrate(v1, v2);

        Type mV2 = v2.GetType("M")!;
        object inst = v2.GetType("H")!.GetField("Inst")!.GetValue(null)!;
        Assert.Equal(77, mV2.GetField("Restored")!.GetValue(inst));
    }

    [Fact]
    public void AttachOrder_DependenciesBeforeDependents()
    {
        const string body =
            "using System.Collections.Generic; using Prowl.Ember; " +
            "public class Node : IReloadAware { public static List<string> Order = new(); " +
            "  public string Name; public Node Child; " +
            "  public void OnReloadAttach(ReloadState s) { Order.Add(Name); } } " +
            "public static class H { public static Node Root; " +
            "  public static void Setup() { Root = new Node { Name = \"root\", Child = new Node { Name = \"leaf\" } }; } }";

        Assembly v1 = Compile(body);
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(body.Replace("public string Name;", "public string Name; public int Extra;"));
        Migrate(v1, v2);

        var order = (IList)v2.GetType("Node")!.GetField("Order")!.GetValue(null)!;
        Assert.Equal(new[] { "leaf", "root" }, order.Cast<string>().ToArray());
    }

    private sealed class PreservedHolder
    {
        // Held by a type outside the reload, so the instance carries over rather than being replaced.
        [ReloadIgnore] public System.Collections.Generic.List<int> Cache = new() { 7 };
        public int Id;
    }

    [Fact]
    public void ReloadIgnore_OnPreservedInstance_IsNotTouchedAtAll()
    {
        Assembly v1 = Compile("public class E { public int Id; }");
        Assembly v2 = Compile("public class E { public int Id; public int Extra; }");

        var holder = new PreservedHolder { Id = 1 };
        holder.Cache.Add(99);
        var original = holder.Cache;

        Reload(o => o.Scope.Include(v1), b => b.Replace(v1, v2).Root(holder));

        // Nothing was replaced here, so the opted-out field keeps the very list it had, contents and all.
        Assert.Same(original, holder.Cache);
        Assert.Equal(new[] { 7, 99 }, holder.Cache);
    }

    // The case that decides whether an opted-out field is safe to dereference after a reload.
    [Fact]
    public void ReloadIgnore_OnReplacedInstance_StillLeavesAUsableValue()
    {
        Assembly v1 = Compile("using System.Collections.Generic; using Prowl.Ember; " +
            "public class Holder { [ReloadIgnore] public List<int> Cache = new List<int> { 1 }; public int Id; } " +
            "public static class H { public static Holder Value; public static void Setup() { Value = new Holder { Id = 1 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; using Prowl.Ember; " +
            "public class Holder { [ReloadIgnore] public List<int> Cache = new List<int> { 1 }; public int Id; public int Extra; } " +
            "public static class H { public static Holder Value; public static void Setup() { } }");

        Migrate(v1, v2);

        object holder = v2.GetType("H")!.GetField("Value")!.GetValue(null)!;
        object? cache = holder.GetType().GetField("Cache")!.GetValue(holder);

        // What a freshly constructed instance would have held, rather than null.
        Assert.NotNull(cache);
        Assert.Equal(new[] { 1 }, ((IEnumerable)cache!).Cast<int>().ToArray());
    }

    [Fact]
    public void ReloadIgnore_DoesNotCarryThePreviousContents()
    {
        Assembly v1 = Compile("using System.Collections.Generic; using Prowl.Ember; " +
            "public class Holder { [ReloadIgnore] public List<int> Cache = new List<int>(); public int Id; } " +
            "public static class H { public static Holder Value; " +
            "  public static void Setup() { Value = new Holder { Id = 1 }; Value.Cache.Add(99); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; using Prowl.Ember; " +
            "public class Holder { [ReloadIgnore] public List<int> Cache = new List<int>(); public int Id; public int Extra; } " +
            "public static class H { public static Holder Value; public static void Setup() { } }");

        Migrate(v1, v2);

        object holder = v2.GetType("H")!.GetField("Value")!.GetValue(null)!;
        var cache = (IEnumerable?)holder.GetType().GetField("Cache")!.GetValue(holder);

        Assert.NotNull(cache);
        Assert.Empty(cache!); // the point of opting out: the previous entries are gone
    }
}
