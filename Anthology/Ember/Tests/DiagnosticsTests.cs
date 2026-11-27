using System;
using System.Linq;
using System.Reflection;

using System.Collections.Generic;

using Xunit;

namespace Prowl.Ember.Tests;

file sealed class ThrowingRootProvider : IRootProvider
{
    public IEnumerable<Root> Enumerate(RootContext context) => throw new InvalidOperationException("root boom");
}

file sealed class RecordingScopedMigrator : IValueMigrator, IReloadScopedMigrator
{
    public int Started;
    public int Finished;

    public bool Handles(Type type) => false;
    public MigrationPlan Plan(Type type, PlanContext context) => throw new NotSupportedException();

    public void OnReloadStarting(PlanContext context) => Started++;
    public void OnReloadFinished(PlanContext context) => Finished++;
}

/// <summary>
/// What a reload reports about itself: the codes raised, the replacement map, and the counts a host displays.
/// An ordinary reload of well formed code has to stay quiet, or the noise makes the real warnings worthless.
/// </summary>
[Trait("Category", "Build")]
public class DiagnosticsTests : MigrationTestBase
{
    // A plain reload of well formed code should not report anything at error severity.
    [Fact]
    public void OrdinaryReload_ReportsNoErrors()
    {
        Assembly v1 = Compile("using System; using System.Collections.Generic; " +
            "public class E { public int Id; public List<string> Tags = new(); } " +
            "public static class H { public static E Value; public static Action A; " +
            "  public static void Setup() { Value = new E { Id = 1 }; Value.Tags.Add(\"x\"); A = () => GC.KeepAlive(Value); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System; using System.Collections.Generic; " +
            "public class E { public int Id; public List<string> Tags = new(); public int Extra; } " +
            "public static class H { public static E Value; public static Action A; " +
            "  public static void Setup() { Value = new E { Id = 1 }; Value.Tags.Add(\"x\"); A = () => GC.KeepAlive(Value); } }");

        var report = Migrate(v1, v2);

        Assert.True(report.Succeeded,
            "unexpected errors: " + string.Join(" | ", report.Errors.Select(d => d.ToString())));
    }

    [Fact]
    public void OrdinaryReloadWithLambdas_ReportsNoMetadataFailures()
    {
        Assembly v1 = Compile("using System; public class E { public int Id; } " +
            "public static class H { public static Action A; public static Func<int> B; public static E Value; " +
            "  public static void Setup() { Value = new E { Id = 1 }; int local = 2; A = () => GC.KeepAlive(Value); B = () => local; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("using System; public class E { public int Id; public int Extra; } " +
            "public static class H { public static Action A; public static Func<int> B; public static E Value; " +
            "  public static void Setup() { Value = new E { Id = 1 }; int local = 2; A = () => GC.KeepAlive(Value); B = () => local; } }");

        var report = Migrate(v1, v2);

        Assert.DoesNotContain(report.Diagnostics, d => d.Code == ReloadCode.MetadataResolveFailed);
        Assert.DoesNotContain(report.Diagnostics, d => d.Code == ReloadCode.MemberUnmatched);
    }

    [Fact]
    public void Report_TypeRemoval_IsReportedByCode()
    {
        Assembly v1 = Compile(EV1 + "public class Gone { public int X; } " +
            "public static class H { public static Gone Value; public static void Setup() { Value = new Gone(); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(EV1 + "public static class H { public static Gone Value; public static void Setup() { } }"
            .Replace("public static Gone Value;", "public static object Value;"));

        Migrate(v1, v2);

        Assert.Contains(Report.Diagnostics, d => d.Code == ReloadCode.TypeRemoved);
        Assert.StartsWith("EMB2001", Diagnostic(ReloadCode.TypeRemoved).Id);
    }

    [Fact]
    public void Report_ExposesReplacementsByIdentity()
    {
        Assembly v1 = Compile(EV1 + "public static class H { public static E Value; public static void Setup() { Value = new E { Id = 3 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);
        object previous = v1.GetType("H")!.GetField("Value")!.GetValue(null)!;

        Assembly v2 = Compile(EV2 + "public static class H { public static E Value; public static void Setup() { } }");
        var report = Migrate(v1, v2);

        Assert.True(report.TryGetReplacement(previous, out object current));
        Assert.Same(v2.GetType("E"), current.GetType());
        Assert.True(report.Statistics.ObjectsReplaced >= 1);
    }

    [Fact]
    public void Statistics_CountVisitedObjects()
    {
        Assembly v1 = Compile("public class E { public int Id; public E Next; } " +
            "public static class H { public static E Value; " +
            "  public static void Setup() { Value = new E { Id = 1, Next = new E { Id = 2 } }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("public class E { public int Id; public E Next; public int Extra; } " +
            "public static class H { public static E Value; public static void Setup() { } }");

        var report = Migrate(v1, v2);

        Assert.Equal(2, report.Statistics.ObjectsReplaced);
        Assert.True(report.Statistics.ObjectsVisited >= 2);
    }

    [Fact]
    public void Statistics_DroppedObjectsAreAlsoVisited()
    {
        Assembly v1 = Compile("public class Gone { public int X; } " +
            "public static class H { public static object A; public static object B; " +
            "  public static void Setup() { A = new Gone(); B = new Gone(); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("public static class H { public static object A; public static object B; public static void Setup() { } }");

        var report = Migrate(v1, v2);

        Assert.Equal(2, report.Statistics.ObjectsDropped);
        Assert.True(report.Statistics.ObjectsVisited >= 2,
            $"dropped objects were visited but not counted: ObjectsVisited={report.Statistics.ObjectsVisited}");
    }

    [Fact]
    public void RemovedType_FiresDroppedHookAndCounts()
    {
        Assembly v1 = Compile("public class Gone { public int X; } " +
            "public static class H { public static object Value; public static void Setup() { Value = new Gone(); } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile("public static class H { public static object Value; public static void Setup() { } }");

        var report = Migrate(v1, v2);

        Assert.Null(v2.GetType("H")!.GetField("Value")!.GetValue(null));
        Assert.Equal(1, report.Statistics.ObjectsDropped);
    }

    /// <summary>
    /// A readonly static is upgraded in place, which means reading it on the current side. That read runs the
    /// type initializer for the first time, so it is exactly as able to throw as the source read that is already
    /// guarded, and one bad type must not take the whole reload down with it.
    /// </summary>
    [Fact]
    public void ReadOnlyStaticWhoseInitializerThrows_IsReportedNotFatal()
    {
        Assembly v1 = Compile(
            "public class Boom { public static readonly object F = new object(); } " +
            "public class Keep { public int Id; } " +
            "public static class H { public static Keep K; public static void Setup(){ K = new Keep { Id = 7 }; } }");
        v1.GetType("H")!.GetMethod("Setup")!.Invoke(null, null);

        Assembly v2 = Compile(
            "public class Boom { public static readonly object F = Make(); static object Make() => throw new System.Exception(\"boom\"); } " +
            "public class Keep { public int Id; public int Extra; } " +
            "public static class H { public static Keep K; public static void Setup(){ } }");

        var report = Reload(o => o.Scope.Include(v1), b => b.Replace(v1, v2));

        // The rest of the reload still has to happen.
        object? kept = v2.GetType("H")!.GetField("K")!.GetValue(null);
        Assert.NotNull(kept);
        Assert.Equal(v2.GetType("Keep"), kept!.GetType());
        Assert.Equal(7, v2.GetType("Keep")!.GetField("Id")!.GetValue(kept));
        Assert.Contains(report.Diagnostics, d => d.Severity >= ReloadSeverity.Warning);
    }

    /// <summary>
    /// OnReloadStarting and OnReloadFinished bracket a reload, and the documented use is clearing a framework
    /// cache keyed on the previous types. A walk that throws must still close the bracket, or that cache stays
    /// holding whatever the half finished migration put in it.
    /// </summary>
    [Fact]
    public void ScopedMigrator_WalkThrows_StillRunsOnReloadFinished()
    {
        Assembly v1 = Compile(EV1);
        Assembly v2 = Compile(EV2);

        var scoped = new RecordingScopedMigrator();

        Assert.Throws<InvalidOperationException>(() => Reload(o =>
        {
            o.Migrators.Add(scoped);
            o.Roots.Add(new ThrowingRootProvider());
        }, b => b.Replace(v1, v2)));

        Assert.Equal(1, scoped.Started);
        Assert.Equal(1, scoped.Finished);
    }
}