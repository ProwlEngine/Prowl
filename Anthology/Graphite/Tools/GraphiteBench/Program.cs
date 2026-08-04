using System;
using System.Collections.Generic;
using System.Globalization;

using Prowl.Graphite;
using Prowl.Graphite.Bench;

internal static class Program
{
    private static int Main(string[] args)
    {
        bool validation = HasFlag(args, "--validation");
        string? only = GetOption(args, "--scenario");
        int? passes = GetInt(args, "--passes");
        int? draws = GetInt(args, "--draws");
        int? frames = GetInt(args, "--frames");
        int? warmup = GetInt(args, "--warmup");
        int? reps = GetInt(args, "--reps");

        string[] selected = only != null ? [only] : Scenarios.Names;

        BenchProfiler profiler = new();
        GraphicsDevice gd;
        try
        {
            gd = BenchDevice.Create(profiler, validation);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to create a Vulkan device: {ex.Message}");
            return 1;
        }

        try
        {
            Console.WriteLine(BenchDevice.Describe(gd));
            Console.WriteLine($"validation={(validation ? "on" : "off")}");
            Console.WriteLine();

            using BenchScene scene = new(gd);

            List<BenchResult> results = new();
            foreach (string name in selected)
            {
                ScenarioConfig config = Scenarios.DefaultConfig(name);
                config = config with
                {
                    Passes = passes ?? config.Passes,
                    DrawsPerPass = draws ?? config.DrawsPerPass,
                    Frames = frames ?? config.Frames,
                    WarmupFrames = warmup ?? config.WarmupFrames,
                    Repetitions = reps ?? config.Repetitions
                };

                results.Add(Scenarios.Run(gd, scene, profiler, name, config));
            }

            PrintTimings(results);
            Console.WriteLine();
            PrintCounters(results);
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            gd.WaitForIdle();
            gd.Dispose();
        }
    }

    private static void PrintTimings(List<BenchResult> results)
    {
        Console.WriteLine("| Scenario | Passes | Draws/pass | Frames x reps | Record ns/draw | Best ns/draw | Record us/frame | Submit us/frame | Other us/frame | Alloc B/frame |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|");

        foreach (BenchResult r in results)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"| {r.Name} | {r.Passes} | {r.DrawsPerPass} | {r.Frames}x{r.Repetitions} | {r.RecordNsPerDraw:F1} | {r.BestRecordNsPerDraw:F1} | {r.RecordUsPerFrame:F1} | {r.SubmitUsPerFrame:F1} | {r.OtherUsPerFrame:F1} | {r.AllocBytesPerFrame:F0} |"));
        }
    }

    private static void PrintCounters(List<BenchResult> results)
    {
        Console.WriteLine("| Scenario | Draws/frame | Set binds/frame | Bound sets/frame | Pipeline switches/frame | Submits/frame |");
        Console.WriteLine("|---|---:|---:|---:|---:|---:|");

        foreach (BenchResult r in results)
        {
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture,
                $"| {r.Name} | {r.DrawsPerFrame:F0} | {r.SetBindsPerFrame:F0} | {r.BoundSetsPerFrame:F0} | {r.PipelineSwitchesPerFrame:F0} | {r.SubmitsPerFrame:F0} |"));
        }
    }

    private static bool HasFlag(string[] args, string name) => Array.IndexOf(args, name) >= 0;

    private static string? GetOption(string[] args, string name)
    {
        int index = Array.IndexOf(args, name);
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static int? GetInt(string[] args, string name)
        => GetOption(args, name) is string value && int.TryParse(value, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
}
