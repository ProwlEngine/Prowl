using System.Diagnostics;
using System.Text.Json;
using Newtonsoft.Json;
using Prowl.Echo;

namespace Benchmarks;

// Test data models
[GenerateSerializer]
public partial class Person
{
    public string? Name;
    public int Age;
    public string? Email;
    public Address? Address;
    public List<string>? Hobbies;
}

[GenerateSerializer]
public partial class Address
{
    public string? Street;
    public string? City;
    public string? State;
    public string? ZipCode;
}

[GenerateSerializer]
public partial class ComplexData
{
    public List<Person>? People;
    public Dictionary<string, int>? Scores;
    public DateTime Timestamp;
    public Guid Id;
    public double[] Coordinates = Array.Empty<double>();
}

public class BenchmarkResult
{
    public string Operation = "";
    public double MinMs;
    public double MaxMs;
    public double AvgMs;
    public double MedianMs;
    public double StdDevMs;
    public double TotalMs;
    public int Iterations;
}

public class Program
{
    private const int WarmupIterations = 100;
    private const int BenchmarkIterations = 1000;

    public static void Main(string[] args)
    {
        PrintHeader("Prowl.Echo Serialization Benchmarks");
        Console.WriteLine($"Warmup: {WarmupIterations} iterations");
        Console.WriteLine($"Benchmark: {BenchmarkIterations} iterations");
        Console.WriteLine();

        // Create test data
        var testData = CreateComplexTestData();

        // Run benchmarks for each serializer
        var results = new Dictionary<string, List<BenchmarkResult>>();

        PrintSectionHeader("Prowl.Echo");
        results["Echo"] = RunEchoBenchmark(testData);

        PrintSectionHeader("System.Text.Json");
        results["System.Text.Json"] = RunSystemTextJsonBenchmark(testData);

        PrintSectionHeader("Newtonsoft.Json");
        results["Newtonsoft.Json"] = RunNewtonsoftJsonBenchmark(testData);

        // Print comparison summary
        PrintComparisonSummary(results);
    }

    private static ComplexData CreateComplexTestData()
    {
        var random = new Random(42); // Fixed seed for consistent results
        return new ComplexData
        {
            Id = Guid.NewGuid(),
            Timestamp = DateTime.Now,
            Coordinates = Enumerable.Range(0, 100).Select(i => random.NextDouble() * 100).ToArray(),
            Scores = Enumerable.Range(0, 50)
                .ToDictionary(i => $"Player{i}", i => random.Next(0, 1000)),
            People = Enumerable.Range(0, 20).Select(i => new Person
            {
                Name = $"Person {i}",
                Age = random.Next(18, 80),
                Email = $"person{i}@example.com",
                Address = new Address
                {
                    Street = $"{random.Next(1, 9999)} Main St",
                    City = "TestCity",
                    State = "TS",
                    ZipCode = $"{random.Next(10000, 99999)}"
                },
                Hobbies = Enumerable.Range(0, random.Next(2, 6))
                    .Select(j => $"Hobby {j}")
                    .ToList()
            }).ToList()
        };
    }

    private static List<BenchmarkResult> RunEchoBenchmark(ComplexData testData)
    {
        var results = new List<BenchmarkResult>();

        // Warmup
        Console.WriteLine("  Warming up...");
        for (int i = 0; i < WarmupIterations; i++)
        {
            var serialized = Serializer.Serialize(testData);
            var deserialized = Serializer.Deserialize<ComplexData>(serialized);
        }

        // Serialization benchmark
        Console.WriteLine("  Benchmarking serialization...");
        results.Add(BenchmarkOperation("Serialize", BenchmarkIterations, () =>
        {
            var serialized = Serializer.Serialize(testData);
        }));

        // Deserialization benchmark
        Console.WriteLine("  Benchmarking deserialization...");
        var echoSerialized = Serializer.Serialize(testData);
        results.Add(BenchmarkOperation("Deserialize", BenchmarkIterations, () =>
        {
            var deserialized = Serializer.Deserialize<ComplexData>(echoSerialized);
        }));

        // Round-trip benchmark
        Console.WriteLine("  Benchmarking round-trip...");
        results.Add(BenchmarkOperation("Round-trip", BenchmarkIterations, () =>
        {
            var serialized = Serializer.Serialize(testData);
            var deserialized = Serializer.Deserialize<ComplexData>(serialized);
        }));

        PrintResults(results);
        return results;
    }

    private static List<BenchmarkResult> RunSystemTextJsonBenchmark(ComplexData testData)
    {
        var results = new List<BenchmarkResult>();
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            IncludeFields = true
        };

        // Warmup
        Console.WriteLine("  Warming up...");
        for (int i = 0; i < WarmupIterations; i++)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(testData, options);
            var deserialized = System.Text.Json.JsonSerializer.Deserialize<ComplexData>(json, options);
        }

        // Serialization benchmark
        Console.WriteLine("  Benchmarking serialization...");
        results.Add(BenchmarkOperation("Serialize", BenchmarkIterations, () =>
        {
            var json = System.Text.Json.JsonSerializer.Serialize(testData, options);
        }));

        // Deserialization benchmark
        Console.WriteLine("  Benchmarking deserialization...");
        var stjSerialized = System.Text.Json.JsonSerializer.Serialize(testData, options);
        results.Add(BenchmarkOperation("Deserialize", BenchmarkIterations, () =>
        {
            var deserialized = System.Text.Json.JsonSerializer.Deserialize<ComplexData>(stjSerialized, options);
        }));

        // Round-trip benchmark
        Console.WriteLine("  Benchmarking round-trip...");
        results.Add(BenchmarkOperation("Round-trip", BenchmarkIterations, () =>
        {
            var json = System.Text.Json.JsonSerializer.Serialize(testData, options);
            var deserialized = System.Text.Json.JsonSerializer.Deserialize<ComplexData>(json, options);
        }));

        PrintResults(results);
        return results;
    }

    private static List<BenchmarkResult> RunNewtonsoftJsonBenchmark(ComplexData testData)
    {
        var results = new List<BenchmarkResult>();
        var settings = new JsonSerializerSettings
        {
            Formatting = Formatting.None
        };

        // Warmup
        Console.WriteLine("  Warming up...");
        for (int i = 0; i < WarmupIterations; i++)
        {
            var json = JsonConvert.SerializeObject(testData, settings);
            var deserialized = JsonConvert.DeserializeObject<ComplexData>(json, settings);
        }

        // Serialization benchmark
        Console.WriteLine("  Benchmarking serialization...");
        results.Add(BenchmarkOperation("Serialize", BenchmarkIterations, () =>
        {
            var json = JsonConvert.SerializeObject(testData, settings);
        }));

        // Deserialization benchmark
        Console.WriteLine("  Benchmarking deserialization...");
        var newtonsoftSerialized = JsonConvert.SerializeObject(testData, settings);
        results.Add(BenchmarkOperation("Deserialize", BenchmarkIterations, () =>
        {
            var deserialized = JsonConvert.DeserializeObject<ComplexData>(newtonsoftSerialized, settings);
        }));

        // Round-trip benchmark
        Console.WriteLine("  Benchmarking round-trip...");
        results.Add(BenchmarkOperation("Round-trip", BenchmarkIterations, () =>
        {
            var json = JsonConvert.SerializeObject(testData, settings);
            var deserialized = JsonConvert.DeserializeObject<ComplexData>(json, settings);
        }));

        PrintResults(results);
        return results;
    }

    private static BenchmarkResult BenchmarkOperation(string operation, int iterations, Action action)
    {
        var times = new List<double>();
        var sw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            var iterStart = sw.Elapsed.TotalMilliseconds;
            action();
            var iterEnd = sw.Elapsed.TotalMilliseconds;
            times.Add(iterEnd - iterStart);
        }

        sw.Stop();

        // Calculate statistics
        times.Sort();
        var min = times.Min();
        var max = times.Max();
        var avg = times.Average();
        var median = times[times.Count / 2];
        var variance = times.Average(t => Math.Pow(t - avg, 2));
        var stdDev = Math.Sqrt(variance);

        return new BenchmarkResult
        {
            Operation = operation,
            MinMs = min,
            MaxMs = max,
            AvgMs = avg,
            MedianMs = median,
            StdDevMs = stdDev,
            TotalMs = sw.Elapsed.TotalMilliseconds,
            Iterations = iterations
        };
    }

    private static void PrintHeader(string title)
    {
        var width = 80;
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("╔" + new string('═', width - 2) + "╗");
        Console.WriteLine("║" + title.PadLeft((width + title.Length) / 2).PadRight(width - 2) + "║");
        Console.WriteLine("╚" + new string('═', width - 2) + "╝");
        Console.ResetColor();
    }

    private static void PrintSectionHeader(string title)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Yellow;
        Console.WriteLine("┌─" + new string('─', title.Length) + "─┐");
        Console.WriteLine("│ " + title + " │");
        Console.WriteLine("└─" + new string('─', title.Length) + "─┘");
        Console.ResetColor();
    }

    private static void PrintResults(List<BenchmarkResult> results)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.White;
        Console.WriteLine($"  {"Operation",-15} {"Min (ms)",10} {"Max (ms)",10} {"Avg (ms)",10} {"Median (ms)",12} {"StdDev",10} {"Total (ms)",12}");
        Console.ForegroundColor = ConsoleColor.DarkGray;
        Console.WriteLine("  " + new string('─', 85));
        Console.ResetColor();

        foreach (var result in results)
        {
            Console.WriteLine($"  {result.Operation,-15} " +
                            $"{result.MinMs,10:F4} " +
                            $"{result.MaxMs,10:F4} " +
                            $"{result.AvgMs,10:F4} " +
                            $"{result.MedianMs,12:F4} " +
                            $"{result.StdDevMs,10:F4} " +
                            $"{result.TotalMs,12:F2}");
        }
    }

    private static void PrintComparisonSummary(Dictionary<string, List<BenchmarkResult>> allResults)
    {
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("╔" + new string('═', 78) + "╗");
        Console.WriteLine("║" + "Performance Comparison Summary".PadLeft(54).PadRight(78) + "║");
        Console.WriteLine("╚" + new string('═', 78) + "╝");
        Console.ResetColor();
        Console.WriteLine();

        var operations = new[] { "Serialize", "Deserialize", "Round-trip" };

        foreach (var operation in operations)
        {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine($"  {operation}:");
            Console.ResetColor();

            var operationResults = allResults
                .Select(kvp => new
                {
                    Library = kvp.Key,
                    Result = kvp.Value.First(r => r.Operation == operation)
                })
                .OrderBy(x => x.Result.AvgMs)
                .ToList();

            var fastest = operationResults.First();

            foreach (var item in operationResults)
            {
                var ratio = item.Result.AvgMs / fastest.Result.AvgMs;
                var color = item.Library == fastest.Library
                    ? ConsoleColor.Green
                    : ratio < 2 ? ConsoleColor.Yellow : ConsoleColor.Red;

                Console.ForegroundColor = color;
                Console.Write($"    {item.Library,-20}");
                Console.ResetColor();
                Console.Write($" Avg: {item.Result.AvgMs,8:F4} ms");

                if (item.Library != fastest.Library)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write($"  ({ratio:F2}x slower)");
                    Console.ResetColor();
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.Write("  ★ Fastest");
                    Console.ResetColor();
                }

                Console.WriteLine();
            }

            Console.WriteLine();
        }

        // Calculate overall winner
        var overallScores = allResults.Select(kvp => new
        {
            Library = kvp.Key,
            TotalAvg = kvp.Value.Average(r => r.AvgMs)
        }).OrderBy(x => x.TotalAvg).ToList();

        Console.ForegroundColor = ConsoleColor.Magenta;
        Console.WriteLine($"  Overall Winner: {overallScores.First().Library}");
        Console.ResetColor();
        Console.WriteLine($"    (Lowest average across all operations: {overallScores.First().TotalAvg:F4} ms)");
        Console.WriteLine();
    }
}
