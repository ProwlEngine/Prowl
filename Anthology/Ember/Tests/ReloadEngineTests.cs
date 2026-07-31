using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

using Xunit;

namespace Prowl.Ember.Tests;

/// <summary>
/// The engine's own contract rather than the migration of any particular value: what a request accepts, where
/// a walk starts, how a slot that cannot be written is handled, and what carries over between reloads.
/// </summary>
[Trait("Category", "Build")]
public class ReloadEngineTests : MigrationTestBase
{

    [Fact]
    public void ReadOnlyStatic_IsUpgradedInPlace()
    {
        Assembly v1 = Compile("using System.Collections.Generic; " + EV1 +
            "public static class H { public static readonly List<E> Items = new(); " +
            "public static void Setup() { Items.Add(new E { Id = 5 }); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System.Collections.Generic; " + EV2 +
            "public static class H { public static readonly List<E> Items = new(); public static void Setup() { } }");

        Migrate(v1, v2);

        var items = (IList)v2.GetType("H")!.GetField("Items")!.GetValue(null)!;
        Assert.Single(items);
        Assert.Same(v2.GetType("E"), items[0]!.GetType());
        Assert.Equal(5, v2.GetType("E")!.GetField("Id")!.GetValue(items[0]));
    }

    [Fact]
    public void ReadOnlyStatic_HoldingNull_IsReportedRatherThanFailing()
    {
        Assembly v1 = Compile(EV1 +
            "public static class H { public static readonly E Value = null; }");
        Assembly v2 = Compile(EV2 +
            "public static class H { public static readonly E Value = null; }");

        Migrate(v1, v2);

        Assert.True(Report.Succeeded);
    }

    private sealed class SlotProvider : IRootProvider
    {
        private readonly ValueSlot _source;
        private readonly ValueSlot _destination;

        public SlotProvider(ValueSlot source, ValueSlot destination)
        {
            _source = source;
            _destination = destination;
        }

        public IEnumerable<Root> Enumerate(RootContext context)
        {
            yield return Root.At(_source, _destination);
        }
    }

    // Storage the engine cannot see for itself, exposed through a read only custom slot.
    [Fact]
    public void CustomReadOnlySlot_UpgradesInPlace()
    {
        Assembly v1 = Compile("using System.Collections.Generic; public class E { public int Id; } " +
            "public static class H { public static List<E> Make() { return new List<E> { new E { Id = 9 } }; } }");

        var previous = (IList)v1.GetType("H")!.GetMethod("Make")!.Invoke(null, null)!;

        Assembly v2 = Compile("using System.Collections.Generic; public class E { public int Id; public int Extra; } " +
            "public static class H { public static List<E> Make() { return new List<E>(); } }");

        var destination = (IList)v2.GetType("H")!.GetMethod("Make")!.Invoke(null, null)!;

        var provider = new SlotProvider(
            ValueSlot.Custom(() => previous, null, typeof(object), "previous list"),
            ValueSlot.Custom(() => destination, null, typeof(object), "current list"));

        Reload(o => { o.Scope.Include(v1); o.Roots.Add(provider); }, b => b.Replace(v1, v2));

        Assert.Single(destination);
        Assert.Same(v2.GetType("E"), destination[0]!.GetType());
    }

    [Fact]
    public void ScopeTypeFilter_ExcludesStaticsOfFilteredTypes()
    {
        Assembly v1 = Compile("public class E { public int Id; } " +
            "public static class Kept { public static E Value; public static void Setup() { Value = new E { Id = 1 }; } } " +
            "public static class Filtered { public static E Value; public static void Setup() { Value = new E { Id = 2 }; } }");
        v1.GetType("Kept")!.GetMethod("Setup")!.Invoke(null, null);
        v1.GetType("Filtered")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("public class E { public int Id; public int Extra; } " +
            "public static class Kept { public static E Value; public static void Setup() { } } " +
            "public static class Filtered { public static E Value; public static void Setup() { } }");

        Reload(o => o.Scope.Include(v1, t => t.Name != "Filtered"), b => b.Replace(v1, v2));

        Assert.NotNull(v2.GetType("Kept")!.GetField("Value")!.GetValue(null));
        Assert.Null(v2.GetType("Filtered")!.GetField("Value")!.GetValue(null));
    }

    [Fact]
    public void Request_RejectsTheSameAssemblyTwice()
    {
        Assembly v1 = Compile(EV1);
        Assembly v2 = Compile(EV2);
        Assembly v3 = Compile(EV2);

        Assert.Throws<ArgumentException>(() => ReloadRequest.Create().Replace(v1, v2).Replace(v1, v3).Build());
    }

    [Fact]
    public void Request_RejectsReplacingAnAssemblyWithItself()
    {
        Assembly v1 = Compile(EV1);

        Assert.Throws<ArgumentException>(() => ReloadRequest.Create().Replace(v1, v1));
    }

    [Fact]
    public void RequestWithNoChanges_IsANoOp()
    {
        var engine = ReloadEngine.Create(_ => { });
        var report = engine.Apply(ReloadRequest.Create().Build());

        Assert.True(report.Succeeded);
        Assert.Empty(report.Replaced);
    }

    [Fact]
    public void AddOnlyRequest_WalksTheNewAssembly()
    {
        Assembly added = Compile("public class Other { public static int Marker = 5; }");

        var engine = ReloadEngine.Create(_ => { });
        var report = engine.Apply(ReloadRequest.Create().Add(added).Build());

        Assert.True(report.Succeeded, string.Join(" | ", report.Errors.Select(d => d.ToString())));
        Assert.Contains(added, engine.Options.Scope.Included);
    }

    [Fact]
    public void AddedAssembly_IsWalkedOnTheNextReload()
    {
        Assembly v1 = Compile("public class E { public int Id; } " +
            "public static class H { public static E Value; public static void Setup() { Value = new E { Id = 1 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly added = Compile("public class Other { public static int Marker = 5; }");

        Assembly v2 = Compile("public class E { public int Id; public int Extra; } " +
            "public static class H { public static E Value; public static void Setup() { } }");

        var engine = ReloadEngine.Create(o => o.Scope.Include(v1));
        engine.Apply(ReloadRequest.Create().Replace(v1, v2).Add(added).Build());

        Assert.Contains(v2, engine.Options.Scope.Included);
        Assert.Contains(added, engine.Options.Scope.Included);
        Assert.DoesNotContain(v1, engine.Options.Scope.Included);
    }

    [Fact]
    public void ExplicitRootsHeldAcrossReloads_DoNotCorruptTheSecond()
    {
        Assembly v1 = Compile("public class E { public int Id; } " +
            "public static class H { public static E Value; public static void Setup() { Value = new E { Id = 1 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        object stale = v1.GetType("H")!.GetField("Value")!.GetValue(null)!;

        var roots = new ExplicitRoots();
        roots.Add(stale);

        var engine = ReloadEngine.Create(o =>
        {
            o.AssemblyBytes = AssemblyBytes;
            o.Scope.Include(v1);
            o.Roots.Add(roots);
        });

        Assembly v2 = Compile("public class E { public int Id; public int Extra; } " +
            "public static class H { public static E Value; public static void Setup() { } }");
        engine.Apply(ReloadRequest.Create().Replace(v1, v2).Build());

        // The host never refreshed its root set, so it still holds a previous side instance.
        Assembly v3 = Compile("public class E { public int Id; public int Extra; } " +
            "public static class H { public static E Value; public static void Setup() { } }");
        var second = engine.Apply(ReloadRequest.Create().Replace(v2, v3).Build());

        Assert.True(second.Succeeded, string.Join(" | ", second.Errors.Select(d => d.ToString())));
        Assert.Same(v3.GetType("E"), v3.GetType("H")!.GetField("Value")!.GetValue(null)!.GetType());
    }

    [Fact]
    public void Reload_ThreeTimes_CarriesStateEachTime()
    {
        Assembly v1 = Compile(EV1 + "public static class H { public static E Value; public static void Setup() { Value = new E { Id = 7 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly current = v1;

        for (int generation = 0; generation < 3; generation++)
        {
            Assembly next = Compile(EV2 + "public static class H { public static E Value; public static void Setup() { } }");
            Migrate(current, next);

            var value = next.GetType("H")!.GetField("Value")!.GetValue(null)!;
            Assert.Same(next.GetType("E"), value.GetType());
            Assert.Equal(7, next.GetType("E")!.GetField("Id")!.GetValue(value));

            current = next;
        }
    }

    private sealed class ReentrantRoot : IReloadObserver
    {
        private readonly Action _reenter;

        public ReentrantRoot(Action reenter) => _reenter = reenter;

        public Exception? Caught;

        public void OnReloadPreserved()
        {
            try { _reenter(); }
            catch (Exception e) { Caught = e; }
        }
    }

    [Fact]
    public void Engine_RejectsReentrantApply()
    {
        Assembly v1 = Compile(EV1);
        Assembly v2 = Compile(EV2);

        var engine = ReloadEngine.Create(o => o.Scope.Include(v1));
        var request = ReloadRequest.Create().Replace(v1, v2).Build();

        var reentrant = new ReentrantRoot(() => engine.Apply(request));
        var outerRoots = new ExplicitRoots();
        outerRoots.Add(reentrant);
        engine.Options.Roots.Add(outerRoots);

        engine.Apply(request);

        Assert.IsType<InvalidOperationException>(reentrant.Caught);
    }
}