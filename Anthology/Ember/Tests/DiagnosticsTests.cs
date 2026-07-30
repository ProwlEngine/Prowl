using System;
using System.Linq;
using System.Reflection;

using Xunit;

namespace Prowl.Ember.Tests;

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
}