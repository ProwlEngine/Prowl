using System.Reflection;
using System.Text;
using Prowl.Prism.Compiler;
using Prowl.Prism.SpirV;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Prowl.Prism.Tests;

/// <summary>A corpus shader: its display name, source, stage, the SPIR-V version its folder selects,
/// and whether it is an intentional-error case (Error Validation corpus).</summary>
public sealed record Snippet(string Name, string Source, ShaderStage Stage, SpirVVersion Target, bool Error = false)
{
    public override string ToString() => Name;
}

/// <summary>
/// The embedded test corpora and their golden references. Everything loads from the test assembly's
/// resources: the GLSL corpus, the pre-generated golden SPIR-V modules, the error-message goldens
/// for the error corpus, and the golden manifest. Nothing here touches the external toolchain -
/// tests execute only Prism and byte-compare.
/// </summary>
public static class TestCorpus
{
    const string CorpusPrefix = "corpus/";
    const string ErrorPrefix = "errors/";
    const string UnoptPrefix = "golden/unopt/";
    const string OptPrefix = "golden/opt/";

    static readonly Dictionary<string, ShaderStage> StageByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        ["vert"] = ShaderStage.Vertex, ["frag"] = ShaderStage.Fragment, ["comp"] = ShaderStage.Compute,
        ["geom"] = ShaderStage.Geometry, ["tesc"] = ShaderStage.TessControl, ["tese"] = ShaderStage.TessEvaluation,
        ["mesh"] = ShaderStage.Mesh, ["task"] = ShaderStage.Task, ["rgen"] = ShaderStage.RayGen,
        ["rint"] = ShaderStage.Intersection, ["rahit"] = ShaderStage.AnyHit, ["rchit"] = ShaderStage.ClosestHit,
        ["rmiss"] = ShaderStage.Miss, ["rcall"] = ShaderStage.Callable,
    };

    static readonly Dictionary<string, SpirVVersion> VersionByFolder = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Spirv 1.0"] = SpirVVersion.V1_0, ["Spirv 1.1"] = SpirVVersion.V1_1, ["Spirv 1.2"] = SpirVVersion.V1_2,
        ["Spirv 1.3"] = SpirVVersion.V1_3, ["Spirv 1.4"] = SpirVVersion.V1_4, ["Spirv 1.5"] = SpirVVersion.V1_5,
    };

    public static IReadOnlyList<Snippet> All { get; }
        = Load(CorpusPrefix, null).Concat(Load(ErrorPrefix, null)).ToList();
    /// <summary>The valid shaders: everything except the intentionally-broken error corpus and the
    /// auto-map flavors (which have their own golden flavor and suite).</summary>
    public static IReadOnlyList<Snippet> Parity { get; } = All.Where(s => !s.Error && !IsAutoMapFlavor(s)).ToList();
    /// <summary>The intentionally-broken shaders the compiler must reject cleanly.</summary>
    public static IReadOnlyList<Snippet> Errors { get; } = All.Where(s => s.Error).ToList();

    static bool IsAutoMapFlavor(Snippet s) =>
        s.Name.StartsWith("AutoMap/", StringComparison.OrdinalIgnoreCase)
        || s.Name.StartsWith("AutoMapColliding/", StringComparison.OrdinalIgnoreCase);

    /// <summary>Unoptimized golden for a corpus shader, or null when absent. Golden file
    /// names keep the stage extension ("quad.frag.spv") so same-named snippets across stages don't
    /// collide.</summary>
    public static byte[]? GoldenUnopt(Snippet s) => Resources.TryGetValue(UnoptPrefix + s.Name + ".spv", out var bytes) ? bytes : null;
    /// <summary>Optimized golden for a corpus shader (reference compiler + its performance pipeline), or null when absent.</summary>
    public static byte[]? GoldenOpt(Snippet s) => Resources.TryGetValue(OptPrefix + s.Name + ".spv", out var bytes) ? bytes : null;
    /// <summary>Auto-map golden for an AutoMap/ shader (locations auto-assigned). The goldens are
    /// stored under Golden/AutoMap with the AutoMap/ prefix stripped from the corpus path.</summary>
    public static byte[]? GoldenAutoMap(Snippet s)
    {
        var rel = s.Name["AutoMap/".Length..];
        return Resources.TryGetValue($"golden/automap/{rel}.spv", out var bytes) ? bytes : null;
    }
    /// <summary>The reference diagnostic for an error-corpus shader (the reference for what the compiler
    /// should complain about), with the generating tool's temp path normalized away, or null.</summary>
    public static string? GoldenError(Snippet s) =>
        Resources.TryGetValue($"golden/errors/{s.Name}.txt", out var bytes) ? Encoding.UTF8.GetString(bytes) : null;

    static readonly Dictionary<string, byte[]> Resources = BuildResources();
    public static GoldenManifest Manifest { get; } = ReadManifest();

    static Dictionary<string, byte[]> BuildResources()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var map = new Dictionary<string, byte[]>(4096);
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (resource.StartsWith(UnoptPrefix, StringComparison.Ordinal)
                || resource.StartsWith(OptPrefix, StringComparison.Ordinal)
                || resource.StartsWith("golden/automap/", StringComparison.Ordinal)
                || resource.StartsWith("golden/errors/", StringComparison.Ordinal))
                // MSBuild's %(RecursiveDir) yields backslashes on Windows; normalize so every lookup
                // can use the forward-slash corpus naming.
                map[resource.Replace('\\', '/')] = ReadAllBytes(assembly, resource);
        }
        return map;
    }

    static byte[] ReadAllBytes(Assembly assembly, string resource)
    {
        using var stream = assembly.GetManifestResourceStream(resource)!;
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    static GoldenManifest ReadManifest()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("golden/manifest.json")!;
        return JsonSerializer.Deserialize<GoldenManifest>(stream!) ?? new GoldenManifest();
    }

    static List<Snippet> Load(string prefix, string? category)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var snippets = new List<Snippet>();
        foreach (var resource in assembly.GetManifestResourceNames())
        {
            if (!resource.StartsWith(prefix, StringComparison.Ordinal)) continue;

            // MSBuild's %(RecursiveDir) yields backslashes on Windows; normalize to the forward-slash
            // naming everything else (goldens, manifest lists) uses.
            string relative = resource[prefix.Length..].Replace('\\', '/');
            string extension = resource[(resource.LastIndexOf('.') + 1)..];
            if (!StageByExtension.TryGetValue(extension, out var stage)) continue;

            var segments = relative.Split('/');
            if (category is not null && !segments.Contains(category, StringComparer.OrdinalIgnoreCase)) continue;

            // "Spirv 1.0/x.vert" -> SpirVVersion.V1_0; default 1.0 when no version folder.
            SpirVVersion target = SpirVVersion.V1_0;
            foreach (var seg in segments)
                if (VersionByFolder.TryGetValue(seg, out var v)) { target = v; break; }

            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            snippets.Add(new Snippet(relative, reader.ReadToEnd(), stage, target, prefix == ErrorPrefix));
        }
        return snippets.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}

public sealed class GoldenManifest
{
    [JsonPropertyName("glslangSha16")] public string GlslangSha { get; set; } = "";
    [JsonPropertyName("spirvOptSha16")] public string? SpirvOptSha { get; set; }
    [JsonPropertyName("spirvValSha16")] public string SpirvValSha { get; set; } = "";
    [JsonPropertyName("unoptimizedGoldens")] public int UnoptimizedCount { get; set; }
    [JsonPropertyName("optimizedGoldens")] public int? OptimizedCount { get; set; }
    [JsonPropertyName("autoMapGoldens")] public int AutoMapCount { get; set; }
    [JsonPropertyName("collidingVerified")] public int CollidingVerified { get; set; }
    [JsonPropertyName("optimizedMissing")] public List<string> OptimizedMissing { get; set; } = [];
    [JsonPropertyName("errorCorpusVerified")] public int ErrorCorpusCount { get; set; }
    [JsonPropertyName("spirvValExceptions")] public List<string> SpirvValExceptions { get; set; } = [];
    [JsonPropertyName("failures")] public List<string> Failures { get; set; } = [];
}
