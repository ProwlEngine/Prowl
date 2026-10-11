using Prowl.Echo;

namespace Benchmarks;

// Usage: Benchmarks [--filter text] [--save file.csv] [--baseline file.csv] [--batches n] [--quick] [--compare]
public static class Program
{
    public static void Main(string[] args)
    {
        System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.High;
        string? filter = Arg(args, "--filter");
        string? save = Arg(args, "--save");
        string? baselinePath = Arg(args, "--baseline");
        if (Arg(args, "--batches") is string batches)
            Harness.Batches = int.Parse(batches);
        if (args.Contains("--quick"))
        {
            Harness.Batches = 7;
            Harness.BatchMs = 10;
            Harness.WarmupMs = 60;
        }

        if (args.Contains("--compare"))
        {
            Comparison.Run();
            return;
        }

        var baseline = baselinePath != null ? Harness.Load(baselinePath) : null;
        var scenarios = Scenarios.All();

        PrintSizes(scenarios, filter);

        var results = new List<Measurement>();
        Harness.PrintHeader(baseline != null);
        foreach (var scenario in scenarios)
        {
            foreach (var (op, action) in Operations(scenario))
            {
                if (filter != null && !$"{scenario.Name}.{op}".Contains(filter, StringComparison.OrdinalIgnoreCase))
                    continue;

                var m = Harness.Measure(scenario.Name, op, action);
                results.Add(m);
                Harness.Print(m, baseline?.GetValueOrDefault($"{m.Scenario}.{m.Op}"));
            }
        }

        if (baseline != null)
            PrintTotals(results, baseline);

        if (save != null)
        {
            Harness.Save(save, results);
            Console.WriteLine($"Saved to {Path.GetFullPath(save)}");
        }
    }

    private static IEnumerable<(string, Action)> Operations(Scenario s)
    {
        var echo = Serializer.Serialize(s.Value);
        yield return ("Serialize", () => Serializer.Serialize(s.Value));
        yield return ("Deserialize", () => Serializer.Deserialize(echo, s.Type));

        if (!s.FileFormats)
            yield break;

        var perf = new EchoBinaryFormat();
        var size = new EchoBinaryFormat { Options = new BinarySerializationOptions { EncodingMode = BinaryEncodingMode.Size } };
        var stream = new MemoryStream();
        byte[] perfBytes = perf.WriteToBytes(echo);
        byte[] sizeBytes = size.WriteToBytes(echo);
        string text = EchoTextFormat.Write(echo);

        yield return ("BinWrite", () => { stream.SetLength(0); perf.WriteTo(echo, stream); });
        yield return ("BinRead", () => perf.ReadFrom(new MemoryStream(perfBytes)));
        yield return ("SizeWrite", () => { stream.SetLength(0); size.WriteTo(echo, stream); });
        yield return ("SizeRead", () => size.ReadFrom(new MemoryStream(sizeBytes)));
        yield return ("TextWrite", () => EchoTextFormat.Write(echo));
        yield return ("TextRead", () => EchoTextFormat.Read(text));

        IFileFormat[] others = { JsonFileFormat.Instance, YamlFileFormat.Instance, XmlFileFormat.Instance, BsonFileFormat.Instance };
        foreach (var format in others)
        {
            string name = format.GetType().Name.Replace("FileFormat", "");
            byte[] bytes = format.WriteToBytes(echo);
            yield return (name + "Write", () => { stream.SetLength(0); format.WriteTo(echo, stream); });
            yield return (name + "Read", () => format.ReadFrom(new MemoryStream(bytes)));
        }
    }

    private static void PrintSizes(List<Scenario> scenarios, string? filter)
    {
        Console.WriteLine($"{"Scenario",-22} {"Nodes",8} {"Binary",10} {"SizeMode",10} {"Text",10}  Roundtrip");
        Console.WriteLine(new string('-', 80));
        foreach (var s in scenarios)
        {
            if (filter != null && !s.Name.Contains(filter.Split('.')[0], StringComparison.OrdinalIgnoreCase))
                continue;

            var echo = Serializer.Serialize(s.Value);
            var again = Serializer.Serialize(Serializer.Deserialize(echo, s.Type));
            var size = new EchoBinaryFormat { Options = new BinarySerializationOptions { EncodingMode = BinaryEncodingMode.Size } };
            byte[] perfBytes = new EchoBinaryFormat().WriteToBytes(echo);
            byte[] sizeBytes = size.WriteToBytes(echo);
            string text = EchoTextFormat.Write(echo);

            bool ok = echo.Equals(again)
                && echo.Equals(new EchoBinaryFormat().ReadFromBytes(perfBytes))
                && echo.Equals(size.ReadFromBytes(sizeBytes));

            Console.Write($"{s.Name,-22} {CountNodes(echo),8} {Harness.Bytes(perfBytes.Length),10} {Harness.Bytes(sizeBytes.Length),10} {Harness.Bytes(text.Length),10}  ");
            Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine(ok ? "ok" : "MISMATCH");
            Console.ResetColor();
        }
        Console.WriteLine();
    }

    private static void PrintTotals(List<Measurement> results, Dictionary<string, Measurement> baseline)
    {
        var matched = results.Where(m => baseline.ContainsKey($"{m.Scenario}.{m.Op}")).ToList();
        if (matched.Count == 0) return;
        double time = Math.Exp(matched.Average(m => Math.Log(m.MedianNs / baseline[$"{m.Scenario}.{m.Op}"].MedianNs)));
        double alloc = Math.Exp(matched.Average(m => Math.Log(Math.Max(1, m.AllocBytes) / Math.Max(1, baseline[$"{m.Scenario}.{m.Op}"].AllocBytes))));
        Console.WriteLine();
        Console.WriteLine($"Geometric mean vs baseline: time {time:F2}x, alloc {alloc:F2}x");
    }

    private static int CountNodes(EchoObject echo) => echo.TagType switch
    {
        EchoType.Compound => 1 + echo.Tags.Values.Sum(CountNodes),
        EchoType.List => 1 + echo.List.Sum(CountNodes),
        _ => 1
    };

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
