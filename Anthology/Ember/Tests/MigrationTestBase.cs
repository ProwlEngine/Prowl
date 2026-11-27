using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Prowl.Ember.Tests;

/// <summary>
/// Compiles C# source into fresh in-memory assemblies, keeping the raw bytes so the engine's metadata reader
/// can see them, then reloads between two of them. Self contained, so no editor or script compiler is needed.
/// </summary>
public abstract class MigrationTestBase
{
    private readonly Dictionary<Assembly, byte[]> _bytes = new();
    private static int s_counter;

    // Every framework assembly, so compiled snippets can use the BCL, plus the contracts assembly for
    // [ReloadIgnore] and the lifecycle interfaces.
    private static readonly MetadataReference[] s_references =
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(p => p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p))
        .Append(MetadataReference.CreateFromFile(typeof(ReloadIgnoreAttribute).Assembly.Location))
        .ToArray();

    /// <summary>A minimal user type, and the same type with a field added, for tests that only need a swap.</summary>
    protected const string EV1 = "public class E { public int Id; }";
    protected const string EV2 = "public class E { public int Id; public int Extra; }";

    /// <summary>The report from the most recent <see cref="Migrate"/>, for asserting on diagnostics.</summary>
    protected ReloadReport Report { get; private set; } = null!;

    /// <summary>Maps a compiled assembly back to the bytes it came from, for tests that build their own engine.</summary>
    protected Func<Assembly, byte[]?> AssemblyBytes
        => assembly => _bytes.TryGetValue(assembly, out var bytes) ? bytes : null;

    /// <summary>
    /// Inert analysis is an optimisation and must never change an observable outcome, so CI runs the whole
    /// suite once per mode. A test that passes under Off and fails under Full is unambiguously an analysis bug.
    /// </summary>
    protected virtual InertAnalysisMode InertAnalysis
        => Environment.GetEnvironmentVariable("EMBER_INERT_ANALYSIS") is { } setting
           && Enum.TryParse<InertAnalysisMode>(setting, ignoreCase: true, out var mode)
            ? mode
            : InertAnalysisMode.Full;

    /// <summary>
    /// Compiles into a fresh, distinctly named in-memory assembly. Each call yields a new one, so compiling
    /// the same source twice gives a pair to reload between.
    /// </summary>
    protected Assembly Compile(string source)
    {
        var name = $"Dyn{Interlocked.Increment(ref s_counter)}";
        var tree = CSharpSyntaxTree.ParseText(source);
        var compilation = CSharpCompilation.Create(name, new[] { tree }, s_references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Debug));

        using var stream = new MemoryStream();
        var result = compilation.Emit(stream);

        if (!result.Success)
        {
            var errors = string.Join(Environment.NewLine, result.Diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error));
            throw new InvalidOperationException($"Compilation failed:{Environment.NewLine}{errors}");
        }

        var bytes = stream.ToArray();
        var assembly = Assembly.Load(bytes);
        _bytes[assembly] = bytes;
        return assembly;
    }

    protected ReloadReport Migrate(Assembly previous, Assembly current, params object[] roots)
        => Migrate(previous, current, configure: null, roots);

    protected ReloadReport Migrate(Assembly previous, Assembly current, Action<ReloadOptions>? configure, params object[] roots)
    {
        var engine = ReloadEngine.Create(options =>
        {
            options.AssemblyBytes = assembly => _bytes.TryGetValue(assembly, out var bytes) ? bytes : null;
            options.Scope.Include(previous);
            options.InertAnalysis = InertAnalysis;

            // Tests assert at the call site that a lost delegate is loud, so they opt out of the Drop default.
            options.BrokenDelegates = BrokenDelegatePolicy.Throwing;

            configure?.Invoke(options);
        });

        var request = ReloadRequest.Create()
            .Replace(previous, current)
            .Roots(roots)
            .Build();

        return Report = engine.Apply(request);
    }

    /// <summary>Full control, for the cases that swap several assemblies at once or remove one outright.</summary>
    protected ReloadReport Reload(Action<ReloadOptions>? configure, Action<ReloadRequest.Builder> build)
    {
        var engine = ReloadEngine.Create(options =>
        {
            options.AssemblyBytes = assembly => _bytes.TryGetValue(assembly, out var bytes) ? bytes : null;
            options.InertAnalysis = InertAnalysis;
            options.BrokenDelegates = BrokenDelegatePolicy.Throwing;

            configure?.Invoke(options);
        });

        var builder = ReloadRequest.Create();
        build(builder);

        return Report = engine.Apply(builder.Build());
    }

    /// <summary>The plan a type would get for this reload, without touching any live object.</summary>
    protected PlanExplanation Explain(Type type, Assembly previous, Assembly current, InertAnalysisMode? mode = null)
    {
        var engine = ReloadEngine.Create(options =>
        {
            options.AssemblyBytes = assembly => _bytes.TryGetValue(assembly, out var bytes) ? bytes : null;
            options.InertAnalysis = mode ?? InertAnalysis;
            options.Scope.Include(previous);
        });

        return engine.Explain(type, ReloadRequest.Create().Replace(previous, current).Build());
    }

    /// <summary>Asserts a diagnostic with this code was reported, and returns it.</summary>
    protected ReloadDiagnostic Diagnostic(ReloadCode code)
        => Report.Diagnostics.FirstOrDefault(d => d.Code == code) is { Message: not null } found
            ? found
            : throw new InvalidOperationException(
                $"Expected {code}. Reported: {string.Join(", ", Report.Diagnostics.Select(d => d.Code))}");

    protected bool Reported(ReloadCode code) => Report.Diagnostics.Any(d => d.Code == code);
}
