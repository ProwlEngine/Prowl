using System.Diagnostics;
using System.Globalization;

namespace Benchmarks;

public record Measurement(string Scenario, string Op, double MedianNs, double MinNs, double AllocBytes, double Gen0Per1k, double Gen2Per1k);

public static class Harness
{
    public static int Batches = 21;
    public static double BatchMs = 20;
    public static double WarmupMs = 400;

    public static Measurement Measure(string scenario, string op, Action action)
    {
        var warm = Stopwatch.StartNew();
        int warmCalls = 0;
        while (warmCalls < 3 || warm.Elapsed.TotalMilliseconds < WarmupMs)
        {
            action();
            warmCalls++;
        }
        double estimateNs = warm.Elapsed.TotalMilliseconds * 1e6 / warmCalls;
        int batchSize = Math.Max(1, (int)(BatchMs * 1e6 / estimateNs));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        var perOp = new double[Batches];
        long allocStart = GC.GetAllocatedBytesForCurrentThread();
        int gen0Start = GC.CollectionCount(0);
        int gen2Start = GC.CollectionCount(2);

        var sw = new Stopwatch();
        for (int b = 0; b < Batches; b++)
        {
            sw.Restart();
            for (int i = 0; i < batchSize; i++)
                action();
            sw.Stop();
            perOp[b] = sw.Elapsed.TotalMilliseconds * 1e6 / batchSize;
        }

        double totalOps = (double)Batches * batchSize;
        double alloc = (GC.GetAllocatedBytesForCurrentThread() - allocStart) / totalOps;
        double gen0 = (GC.CollectionCount(0) - gen0Start) * 1000.0 / totalOps;
        double gen2 = (GC.CollectionCount(2) - gen2Start) * 1000.0 / totalOps;

        Array.Sort(perOp);
        return new Measurement(scenario, op, perOp[Batches / 2], perOp[0], alloc, gen0, gen2);
    }

    public static string Time(double ns) => ns switch
    {
        < 1_000 => $"{ns:F0} ns",
        < 1_000_000 => $"{ns / 1_000:F2} us",
        _ => $"{ns / 1_000_000:F2} ms"
    };

    public static string Bytes(double b) => b switch
    {
        < 1024 => $"{b:F0} B",
        < 1024 * 1024 => $"{b / 1024:F1} KB",
        _ => $"{b / 1024 / 1024:F2} MB"
    };

    public static void PrintHeader(bool withBaseline)
    {
        string line = $"{"Scenario",-22} {"Op",-12} {"Median",11} {"Min",11} {"Alloc/op",11} {"Gen0/1k",8} {"Gen2/1k",8}";
        if (withBaseline) line += $" {"Time",8} {"Alloc",8}";
        Console.WriteLine(line);
        Console.WriteLine(new string('-', line.Length));
    }

    public static void Print(Measurement m, Measurement? baseline)
    {
        string line = $"{m.Scenario,-22} {m.Op,-12} {Time(m.MedianNs),11} {Time(m.MinNs),11} {Bytes(m.AllocBytes),11} {m.Gen0Per1k,8:F2} {m.Gen2Per1k,8:F2}";
        Console.Write(line);
        if (baseline != null)
        {
            WriteRatio(m.MedianNs / baseline.MedianNs);
            WriteRatio(baseline.AllocBytes == 0 ? 1 : m.AllocBytes / baseline.AllocBytes);
        }
        Console.WriteLine();
    }

    private static void WriteRatio(double ratio)
    {
        Console.ForegroundColor = ratio < 0.95 ? ConsoleColor.Green : ratio > 1.05 ? ConsoleColor.Red : ConsoleColor.Gray;
        Console.Write($" {ratio,7:F2}x");
        Console.ResetColor();
    }

    public static void Save(string path, IEnumerable<Measurement> results)
    {
        var lines = new List<string> { "scenario,op,median_ns,min_ns,alloc_bytes,gen0_per_1k,gen2_per_1k" };
        foreach (var m in results)
            lines.Add(string.Join(',', m.Scenario, m.Op,
                m.MedianNs.ToString(CultureInfo.InvariantCulture), m.MinNs.ToString(CultureInfo.InvariantCulture),
                m.AllocBytes.ToString(CultureInfo.InvariantCulture), m.Gen0Per1k.ToString(CultureInfo.InvariantCulture),
                m.Gen2Per1k.ToString(CultureInfo.InvariantCulture)));
        File.WriteAllLines(path, lines);
    }

    public static Dictionary<string, Measurement> Load(string path)
    {
        var map = new Dictionary<string, Measurement>();
        foreach (var line in File.ReadAllLines(path).Skip(1))
        {
            var p = line.Split(',');
            double D(int i) => double.Parse(p[i], CultureInfo.InvariantCulture);
            map[p[0] + "." + p[1]] = new Measurement(p[0], p[1], D(2), D(3), D(4), D(5), D(6));
        }
        return map;
    }
}
