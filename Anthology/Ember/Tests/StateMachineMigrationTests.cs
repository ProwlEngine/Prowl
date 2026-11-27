using System;
using System.Collections;
using System.Reflection;

using Xunit;

namespace Prowl.Ember.Tests;

/// <summary>Migration tests for live compiler-generated state machines: async tasks and iterators.</summary>
[Trait("Category", "Build")]
public class StateMachineMigrationTests : MigrationTestBase
{
    private const string EDef = "public class E { public int Id; }";
    private const string EDef2 = "public class E { public int Id; public int Extra; }";

    [Fact]
    public void Migrate_CompletedTaskOfSwappedType_CarriesResult()
    {
        Assembly v1 = Compile("using System.Threading.Tasks; " + EDef +
            "public static class H { public static E Shared; public static Task<E> T; " +
            "public static void Setup(){ Shared = new E{Id=5}; T = Task.FromResult(Shared); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Threading.Tasks; " + EDef2 +
            "public static class H { public static E Shared; public static Task<E> T; public static void Setup(){} }");
        Migrate(v1, v2);

        Type eV2 = v2.GetType("E")!;
        Type hV2 = v2.GetType("H")!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        object task = hV2.GetField("T")!.GetValue(null)!;
        object result = task.GetType().GetProperty("Result")!.GetValue(task)!;
        Assert.Same(eV2, result.GetType());
        Assert.Same(shared, result);
    }

    [Fact]
    public void Migrate_SuspendedAsyncStateMachine_CompletesWithMigratedResult()
    {
        Assembly v1 = Compile("using System.Threading.Tasks; " + EDef +
            "public static class H { public static E Shared; public static TaskCompletionSource<E> Tcs = new(); public static Task<int> Op; " +
            "public static async Task<int> Run() { var e = await Tcs.Task; return e.Id; } " +
            "public static void Setup(){ Shared = new E{Id=11}; Op = Run(); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Threading.Tasks; " + EDef2 +
            "public static class H { public static E Shared; public static TaskCompletionSource<E> Tcs = new(); public static Task<int> Op; " +
            "public static async Task<int> Run() { var e = await Tcs.Task; return e.Id; } public static void Setup(){} }");
        Migrate(v1, v2);

        Type hV2 = v2.GetType("H")!;
        object tcs = hV2.GetField("Tcs")!.GetValue(null)!;
        object shared = hV2.GetField("Shared")!.GetValue(null)!;
        tcs.GetType().GetMethod("SetResult")!.Invoke(tcs, new[] { shared });
        object op = hV2.GetField("Op")!.GetValue(null)!;
        int result = (int)op.GetType().GetProperty("Result")!.GetValue(op)!;
        Assert.Equal(11, result);
    }

    [Fact]
    public void Migrate_PartiallyAdvancedIterator_ContinuesAfterMigration()
    {
        Assembly v1 = Compile("using System.Collections.Generic; " +
            "public static class H { public static IEnumerator<int> It; " +
            "public static IEnumerable<int> Seq(){ yield return 1; yield return 2; yield return 3; } " +
            "public static void Setup(){ It = Seq().GetEnumerator(); It.MoveNext(); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        Assembly v2 = Compile("using System.Collections.Generic; " +
            "public static class H { public static IEnumerator<int> It; public static int Extra; " +
            "public static IEnumerable<int> Seq(){ yield return 1; yield return 2; yield return 3; } public static void Setup(){} }");
        Migrate(v1, v2);

        // Current/MoveNext are explicit interface implementations on the state machine, so drive it via IEnumerator.
        var it = (IEnumerator)v2.GetType("H")!.GetField("It")!.GetValue(null)!;
        Assert.Equal(1, it.Current);
        it.MoveNext();
        Assert.Equal(2, it.Current);
    }
}
