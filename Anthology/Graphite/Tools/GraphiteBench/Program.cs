using System;
using System.Linq;

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

using Perfolizer.Horology;

using Prowl.Graphite.Bench;

internal static class Program
{
    private static int Main(string[] args)
    {
        BenchDevice.Validation = args.Contains("--validation");
        Job baseJob = args.Contains("--dry") ? Job.Dry
            : args.Contains("--quick") ? Job.Default.WithWarmupCount(1).WithIterationCount(3)
            : Job.Default.WithWarmupCount(3).WithIterationCount(10);
        args = args.Where(a => a is not ("--validation" or "--quick" or "--dry")).ToArray();

        Job job = baseJob
            .WithEvaluateOverhead(false)
            .WithMinIterationTime(TimeInterval.FromMilliseconds(100))
            .WithUnrollFactor(1)
            .WithToolchain(InProcessEmitToolchain.Instance)
            .WithId("InProcess");

        IConfig config = DefaultConfig.Instance
            .AddJob(job)
            .AddDiagnoser(MemoryDiagnoser.Default)
            .AddColumn(BenchMetrics.Columns)
            .WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(40));

        BenchmarkSwitcher.FromAssembly(typeof(GraphiteBenchmark).Assembly).Run(args, config);
        return 0;
    }
}
