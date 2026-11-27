using System;
using System.Collections;
using System.Linq;
using System.Reflection;

using Xunit;

namespace Prowl.Ember.Tests;

/// <summary>
/// How the engine decides what to do with a type before it sees an instance: whether the type is inert, which
/// migrator claims it, and what happens when one misbehaves.
/// </summary>
[Trait("Category", "Build")]
public class PlanningTests : MigrationTestBase
{
    // Nothing here can reach a reloaded type, and the cycle through Next is the case a pessimistic
    // recursion breaker gets wrong.
    private sealed class InertNode
    {
        public InertNode? Next;
        public int Value;
    }

    private sealed class HookedLeaf : IReloadObserver
    {
        public int PreservedCount;
        public void OnReloadPreserved() => PreservedCount++;
    }

    [Fact]
    public void InertAnalysis_SelfReferentialType_IsStillInert()
    {
        Assembly v1 = Compile(EV1);
        Assembly v2 = Compile(EV2);

        // Pinned to Full: this is the fixpoint's answer, and the weaker modes are entitled to say no.
        var explanation = Explain(typeof(InertNode), v1, v2, InertAnalysisMode.Full);

        Assert.True(explanation.Facts.IsInert);
        Assert.Same(MigrationPlan.Preserved, explanation.Plan);
    }

    [Fact]
    public void InertAnalysis_TypeWithHooks_IsNeverInert()
    {
        Assembly v1 = Compile(EV1);
        Assembly v2 = Compile(EV2);

        // Its only storage is an int, so nothing but the hooks makes it worth visiting.
        Assert.False(Explain(typeof(HookedLeaf), v1, v2).Facts.IsInert);
    }

    [Fact]
    public void HookedLeaf_ReachedAsRoot_IsNotifiedEvenWithNoMigratableState()
    {
        Assembly v1 = Compile(EV1);
        Assembly v2 = Compile(EV2);

        var leaf = new HookedLeaf();
        Reload(o => o.Scope.Include(v1), b => b.Replace(v1, v2).Root(leaf));

        Assert.Equal(1, leaf.PreservedCount);
    }

    [Fact]
    public void Registry_Describe_ListsResolutionOrder()
    {
        var options = new ReloadOptions();
        string order = options.Migrators.Describe();

        Assert.Contains(nameof(DelegateMigrator), order);
        Assert.Contains(nameof(HashContainerMigrator), order);

        // Delegates are asked before the generic container shapes, which is what stops a delegate that happens
        // to present as a collection from being rebuilt as one.
        Assert.True(order.IndexOf(nameof(DelegateMigrator), StringComparison.Ordinal)
                    < order.IndexOf(nameof(HashContainerMigrator), StringComparison.Ordinal));
    }

    [Fact]
    public void Registry_InsertBefore_PlacesTheMigratorAhead()
    {
        var options = new ReloadOptions();
        options.Migrators.InsertBefore<ArrayMigrator>(new EnumMigrator());

        int inserted = -1, array = -1;
        for (int i = 0; i < options.Migrators.Count; i++)
        {
            if (inserted < 0 && options.Migrators[i] is EnumMigrator && i > 0) inserted = i;
            if (options.Migrators[i] is ArrayMigrator) array = i;
        }

        Assert.True(inserted >= 0 && inserted < array);
    }

    private sealed class ThrowingMigrator : IValueMigrator
    {
        public bool Handles(Type type) => type.Name == "E";
        public MigrationPlan Plan(Type type, PlanContext context) => throw new InvalidOperationException("deliberate");
    }

    [Fact]
    public void MigratorThatThrows_IsReportedNotFatal()
    {
        Assembly v1 = Compile("public class E { public int Id; } " +
            "public static class H { public static E Value; public static void Setup() { Value = new E { Id = 1 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("public class E { public int Id; public int Extra; } " +
            "public static class H { public static E Value; public static void Setup() { } }");

        var report = Reload(o =>
        {
            o.Scope.Include(v1);
            o.Migrators.InsertBefore<EnumMigrator>(new ThrowingMigrator());
        }, b => b.Replace(v1, v2));

        Assert.Contains(report.Diagnostics, d => d.Code == ReloadCode.MigratorThrew);
    }

    [Fact]
    public void CollectionShapeProbe_IsStableAcrossRepeatedQueries()
    {
        Assembly v1 = Compile("using System.Collections.Generic; public class E { public int Id; } " +
            "public static class H { public static List<E> A = new(); public static List<E> B = new(); " +
            "  public static void Setup() { A.Add(new E { Id = 1 }); B.Add(new E { Id = 2 }); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public static class H { public static List<E> A = new(); public static List<E> B = new(); public static void Setup() { } }");

        Migrate(v1, v2);

        var a = (IList)v2.GetType("H")!.GetField("A")!.GetValue(null)!;
        var b = (IList)v2.GetType("H")!.GetField("B")!.GetValue(null)!;

        Assert.Single(a);
        Assert.Single(b);
        Assert.NotSame(a[0], b[0]);
    }
}