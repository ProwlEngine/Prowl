using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;

using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Columns;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

namespace Prowl.Graphite.Bench;

public static class BenchMetrics
{
    private static readonly ConcurrentDictionary<string, BenchSnapshot> s_results = new();

    public static void Store(object benchmark, BenchSnapshot snapshot)
    {
        Type type = benchmark.GetType();
        while (type.Assembly != typeof(BenchMetrics).Assembly && type.BaseType != null)
            type = type.BaseType;

        IEnumerable<(string, object?)> parameters = type
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.IsDefined(typeof(ParamsAttribute)) || p.IsDefined(typeof(ParamsAllValuesAttribute)) || p.IsDefined(typeof(ParamsSourceAttribute)))
            .Select(p => (p.Name, p.GetValue(benchmark)));

        s_results[Key(type, parameters)] = snapshot;
    }

    public static BenchSnapshot? Find(BenchmarkCase benchmarkCase)
    {
        IEnumerable<(string, object?)> parameters = benchmarkCase.Parameters.Items.Select(p => (p.Name, (object?)p.Value));
        return s_results.TryGetValue(Key(benchmarkCase.Descriptor.Type, parameters), out BenchSnapshot? snapshot) ? snapshot : null;
    }

    private static string Key(Type type, IEnumerable<(string Name, object? Value)> parameters)
        => type.FullName + "|" + string.Join(";", parameters
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => p.Name + "=" + Convert.ToString(p.Value, CultureInfo.InvariantCulture)));

    public static IColumn[] Columns { get; } =
    [
        new MetricColumn("GPU us/frame", "Summed GPU timestamp duration of every pass command buffer", s => s.GpuUsPerFrame, "F1"),
        new MetricColumn("Record us/frame", "CPU time inside pass Render plus command buffer end", s => s.RecordUsPerFrame, "F1"),
        new MetricColumn("Passes run", "Passes executed per frame", s => s.PassesPerFrame, "F0"),
        new MetricColumn("Draw calls", "Draw calls per frame", s => s.DrawsPerFrame, "F0"),
        new MetricColumn("Dispatches", "Compute dispatches per frame", s => s.DispatchesPerFrame, "F0"),
        new MetricColumn("Shader switches", "Shader program switches per frame", s => s.ShaderSwitchesPerFrame, "F0"),
        new MetricColumn("Pipeline binds", "Native pipeline binds per frame", s => s.PipelineBindsPerFrame, "F0"),
        new MetricColumn("Set binds", "Resource set binds per frame", s => s.SetBindsPerFrame, "F0"),
        new MetricColumn("Barriers", "Barriers per frame", s => s.BarriersPerFrame, "F0"),
    ];

    private sealed class MetricColumn(string name, string legend, Func<BenchSnapshot, double> select, string format) : IColumn
    {
        public string Id => nameof(MetricColumn) + "." + name;
        public string ColumnName => name;
        public bool AlwaysShow => true;
        public ColumnCategory Category => ColumnCategory.Custom;
        public int PriorityInCategory => 0;
        public bool IsNumeric => true;
        public UnitType UnitType => UnitType.Dimensionless;
        public string Legend => legend;

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase)
            => BenchMetrics.Find(benchmarkCase) is { } snapshot
                ? select(snapshot).ToString(format, CultureInfo.InvariantCulture)
                : "-";

        public string GetValue(Summary summary, BenchmarkCase benchmarkCase, SummaryStyle style) => GetValue(summary, benchmarkCase);
        public bool IsDefault(Summary summary, BenchmarkCase benchmarkCase) => false;
        public bool IsAvailable(Summary summary) => true;
    }
}
