// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Noise nodes — OpenSimplex2/S, Perlin, Value, Value Cubic, cellular / Voronoi,
// and domain warping. All of these are thin wrappers over FastNoiseLite.glsl
// (Jordan Peck's FastNoiseLite, MIT-licensed, embedded as a default include).

using System.Globalization;
using System.Text;

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

internal static class NoiseAccents
{
    /// <summary>Warm purple — noise sits between math (amber) and colour (warm pink)
    /// visually, matching its role as procedural data feeding other ops.</summary>
    public static readonly System.Drawing.Color Noise =
        System.Drawing.Color.FromArgb(255, 170, 130, 200);
}

/// <summary>2D samples every axis, 3D threads through the z coord as well.
/// 2D is slightly cheaper and has no rotation artefacts for flat surfaces.</summary>
public enum NoiseDimension
{
    /// <summary>Sample as 2D noise using the coord's X and Y.</summary>
    Noise2D = 0,
    /// <summary>Sample as 3D noise using the full coord. Good for animated or
    /// volumetric noise.</summary>
    Noise3D = 1,
}

/// <summary>Base pattern for <see cref="NoiseNode"/>. Names map directly to FNL.</summary>
public enum NoiseType
{
    /// <summary>Default. Fast, smooth, no visible grid artefacts.</summary>
    OpenSimplex2 = 0,
    /// <summary>Smoother variant of OpenSimplex2 at slightly higher cost.</summary>
    OpenSimplex2S = 1,
    /// <summary>Classic Perlin. Slightly grid-aligned look.</summary>
    Perlin = 3,
    /// <summary>Bilinear value noise. Coarse, boxy look. Cheapest.</summary>
    Value = 5,
    /// <summary>Bicubic value noise. Smoother than plain Value, still cheap.</summary>
    ValueCubic = 4,
}

/// <summary>Fractal layering applied on top of the base noise.</summary>
public enum NoiseFractal
{
    /// <summary>No fractal layering — single octave.</summary>
    None = 0,
    /// <summary>Fractal Brownian Motion — stack octaves at decreasing amplitude.</summary>
    FBM = 1,
    /// <summary>Ridged — classic mountain / veins look.</summary>
    Ridged = 2,
    /// <summary>PingPong — triangle-folded octaves for a cell-like layered effect.</summary>
    PingPong = 3,
}

/// <summary>Distance function for cellular / Voronoi noise.</summary>
public enum CellularDistance
{
    Euclidean = 0,
    EuclideanSq = 1,
    Manhattan = 2,
    Hybrid = 3,
}

/// <summary>What a cellular node should hand back for each pixel.</summary>
public enum CellularReturn
{
    /// <summary>A hash of the owning cell — discrete per-cell value, nearest-neighbour style.</summary>
    CellValue = 0,
    /// <summary>Distance to the closest feature point.</summary>
    Distance = 1,
    /// <summary>Distance to the second-closest feature point.</summary>
    Distance2 = 2,
    /// <summary>F2 + F1. Blocky crystal look.</summary>
    Distance2Add = 3,
    /// <summary>F2 - F1. Classic Voronoi edges.</summary>
    Distance2Sub = 4,
    /// <summary>F2 * F1.</summary>
    Distance2Mul = 5,
    /// <summary>F1 / F2.</summary>
    Distance2Div = 6,
}

/// <summary>Algorithm used when domain-warping a coord.</summary>
public enum DomainWarpType
{
    OpenSimplex2 = 0,
    OpenSimplex2Reduced = 1,
    BasicGrid = 2,
}

/// <summary>Fractal variants for domain warping (different from base-noise fractals).</summary>
public enum DomainWarpFractal
{
    /// <summary>No fractal warping — single pass.</summary>
    None = 0,
    /// <summary>Warp progressively — each octave warps the already-warped coord.</summary>
    Progressive = 4,
    /// <summary>Warp each octave from the original coord and accumulate.</summary>
    Independent = 5,
}

internal static class NoiseEmit
{
    /// <summary>Format a float with invariant culture, no trailing junk — GLSL wants "0.5", not "0,5".</summary>
    public static string F(float v) => v.ToString("0.0#######", CultureInfo.InvariantCulture);

    /// <summary>Map a <see cref="NoiseType"/> to the FNL macro.</summary>
    public static string TypeMacro(NoiseType t) => t switch
    {
        NoiseType.OpenSimplex2 => "FNL_NOISE_OPENSIMPLEX2",
        NoiseType.OpenSimplex2S => "FNL_NOISE_OPENSIMPLEX2S",
        NoiseType.Perlin => "FNL_NOISE_PERLIN",
        NoiseType.Value => "FNL_NOISE_VALUE",
        NoiseType.ValueCubic => "FNL_NOISE_VALUE_CUBIC",
        _ => "FNL_NOISE_OPENSIMPLEX2",
    };

    public static string FractalMacro(NoiseFractal f) => f switch
    {
        NoiseFractal.FBM => "FNL_FRACTAL_FBM",
        NoiseFractal.Ridged => "FNL_FRACTAL_RIDGED",
        NoiseFractal.PingPong => "FNL_FRACTAL_PINGPONG",
        _ => "FNL_FRACTAL_NONE",
    };

    public static string CellularDistanceMacro(CellularDistance d) => d switch
    {
        CellularDistance.Euclidean => "FNL_CELLULAR_DISTANCE_EUCLIDEAN",
        CellularDistance.EuclideanSq => "FNL_CELLULAR_DISTANCE_EUCLIDEANSQ",
        CellularDistance.Manhattan => "FNL_CELLULAR_DISTANCE_MANHATTAN",
        CellularDistance.Hybrid => "FNL_CELLULAR_DISTANCE_HYBRID",
        _ => "FNL_CELLULAR_DISTANCE_EUCLIDEANSQ",
    };

    public static string CellularReturnMacro(CellularReturn r) => r switch
    {
        CellularReturn.CellValue => "FNL_CELLULAR_RETURN_TYPE_CELLVALUE",
        CellularReturn.Distance => "FNL_CELLULAR_RETURN_TYPE_DISTANCE",
        CellularReturn.Distance2 => "FNL_CELLULAR_RETURN_TYPE_DISTANCE2",
        CellularReturn.Distance2Add => "FNL_CELLULAR_RETURN_TYPE_DISTANCE2ADD",
        CellularReturn.Distance2Sub => "FNL_CELLULAR_RETURN_TYPE_DISTANCE2SUB",
        CellularReturn.Distance2Mul => "FNL_CELLULAR_RETURN_TYPE_DISTANCE2MUL",
        CellularReturn.Distance2Div => "FNL_CELLULAR_RETURN_TYPE_DISTANCE2DIV",
        _ => "FNL_CELLULAR_RETURN_TYPE_DISTANCE",
    };

    public static string DomainWarpMacro(DomainWarpType w) => w switch
    {
        DomainWarpType.OpenSimplex2 => "FNL_DOMAIN_WARP_OPENSIMPLEX2",
        DomainWarpType.OpenSimplex2Reduced => "FNL_DOMAIN_WARP_OPENSIMPLEX2_REDUCED",
        DomainWarpType.BasicGrid => "FNL_DOMAIN_WARP_BASICGRID",
        _ => "FNL_DOMAIN_WARP_OPENSIMPLEX2",
    };
}

// ═════════════════════════════════════════════════════════════════════════════
// NoiseNode
// General-purpose noise sampler. OpenSimplex2 / OpenSimplex2S / Perlin / Value /
// Value Cubic, with optional FBM / Ridged / PingPong fractal stacking.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Samples a scalar noise value in the range [-1, 1]. Pattern, fractal mode, and
/// the usual octave knobs are on the node itself — wire a vec2 (or vec3) coord
/// in and you're done.
/// </summary>
/// <remarks>
/// <para>For wind / wobble vertex displacement, feed a world-position vec3 in
/// 3D mode with time added to one component.</para>
/// </remarks>
public sealed class NoiseNode : Node, IShaderNode, IShaderGraphNode
{
    /// <summary>2D or 3D sampling.</summary>
    public NoiseDimension Dimension = NoiseDimension.Noise2D;

    /// <summary>Base noise algorithm.</summary>
    public NoiseType Type = NoiseType.OpenSimplex2;

    /// <summary>Fractal stacking mode.</summary>
    public NoiseFractal Fractal = NoiseFractal.None;

    /// <summary>Seed. Changes the pattern without changing the look.</summary>
    public int Seed = 1337;

    /// <summary>Input coord multiplier. Lower = larger features.</summary>
    public float Frequency = 1f;

    /// <summary>Fractal octave count. Ignored if Fractal = None.</summary>
    public int Octaves = 3;

    /// <summary>Per-octave frequency multiplier. 2.0 is the classic doubling.</summary>
    public float Lacunarity = 2f;

    /// <summary>Per-octave amplitude multiplier. 0.5 is the classic half.</summary>
    public float Gain = 0.5f;

    /// <summary>Pulls higher octaves toward the dominant lower octaves. 0 = neutral.</summary>
    public float WeightedStrength = 0f;

    /// <summary>Fold strength for <see cref="NoiseFractal.PingPong"/>. 2.0 is a reasonable start.</summary>
    public float PingPongStrength = 2f;

    public override string Title => Fractal == NoiseFractal.None ? $"Noise · {Type}" : $"Noise · {Type} · {Fractal}";
    public override string Category => "Noise";
    public override System.Drawing.Color AccentColor => NoiseAccents.Noise;

    protected override void DefineNode()
    {
        AddInput<Float3>("Coord", Float3.Zero,
            tooltip: "Sample position. Usually UV (for screen / surface patterns) or world-position (for object-space noise). Float2 inputs are auto-promoted.");
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("FastNoiseLite");

        string fnName = $"_fnlNoise_{Id:N}";
        ctx.EmitOnce("fnlNoise:" + fnName, () =>
        {
            var sb = new StringBuilder();
            sb.Append("float ").Append(fnName).AppendLine("(vec3 p) {");
            sb.Append("    fnl_state s = fnlCreateState(").Append(Seed).AppendLine(");");
            sb.Append("    s.frequency = ").Append(NoiseEmit.F(Frequency)).AppendLine(";");
            sb.Append("    s.noise_type = ").Append(NoiseEmit.TypeMacro(Type)).AppendLine(";");
            if (Fractal != NoiseFractal.None)
            {
                sb.Append("    s.fractal_type = ").Append(NoiseEmit.FractalMacro(Fractal)).AppendLine(";");
                sb.Append("    s.octaves = ").Append(System.Math.Max(1, Octaves)).AppendLine(";");
                sb.Append("    s.lacunarity = ").Append(NoiseEmit.F(Lacunarity)).AppendLine(";");
                sb.Append("    s.gain = ").Append(NoiseEmit.F(Gain)).AppendLine(";");
                sb.Append("    s.weighted_strength = ").Append(NoiseEmit.F(WeightedStrength)).AppendLine(";");
                if (Fractal == NoiseFractal.PingPong)
                    sb.Append("    s.ping_pong_strength = ").Append(NoiseEmit.F(PingPongStrength)).AppendLine(";");
            }
            sb.AppendLine(Dimension == NoiseDimension.Noise2D
                ? "    return fnlGetNoise2D(s, p.x, p.y);"
                : "    return fnlGetNoise3D(s, p.x, p.y, p.z);");
            sb.AppendLine("}");
            ctx.TopLevelHelpers.AppendLine(sb.ToString());
        });

        string coord = ctx.EvaluateInputAs(GetInput("Coord")!, ShaderType.Vec3);
        return $"{fnName}({coord})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

// ═════════════════════════════════════════════════════════════════════════════
// CellularNoiseNode
// Voronoi / cellular noise. Distance function, return mode, and jitter are all
// configurable — same expression surface as FNL's cellular output.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Cellular / Voronoi noise. Output range depends on <see cref="ReturnType"/>:
/// CellValue is a per-cell hash, distance-based modes return a distance metric.
/// </summary>
public sealed class CellularNoiseNode : Node, IShaderNode, IShaderGraphNode
{
    public NoiseDimension Dimension = NoiseDimension.Noise2D;

    /// <summary>Distance metric used to decide "closest point".</summary>
    public CellularDistance DistanceFunction = CellularDistance.EuclideanSq;

    /// <summary>What the node emits per pixel.</summary>
    public CellularReturn ReturnType = CellularReturn.Distance;

    public int Seed = 1337;
    public float Frequency = 1f;

    /// <summary>Feature-point jitter in [0, 1]. 0 is a clean grid; 1 is fully random inside each cell. Higher values artefact.</summary>
    public float Jitter = 1f;

    /// <summary>Fractal stacking (None/FBM/Ridged/PingPong).</summary>
    public NoiseFractal Fractal = NoiseFractal.None;

    public int Octaves = 3;
    public float Lacunarity = 2f;
    public float Gain = 0.5f;

    public override string Title => $"Cellular · {ReturnType}";
    public override string Category => "Noise";
    public override System.Drawing.Color AccentColor => NoiseAccents.Noise;

    protected override void DefineNode()
    {
        AddInput<Float3>("Coord", Float3.Zero,
            tooltip: "Sample position. Usually UV or world-position. Float2 inputs are auto-promoted.");
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("FastNoiseLite");

        string fnName = $"_fnlCell_{Id:N}";
        ctx.EmitOnce("fnlCell:" + fnName, () =>
        {
            var sb = new StringBuilder();
            sb.Append("float ").Append(fnName).AppendLine("(vec3 p) {");
            sb.Append("    fnl_state s = fnlCreateState(").Append(Seed).AppendLine(");");
            sb.Append("    s.frequency = ").Append(NoiseEmit.F(Frequency)).AppendLine(";");
            sb.AppendLine("    s.noise_type = FNL_NOISE_CELLULAR;");
            sb.Append("    s.cellular_distance_func = ").Append(NoiseEmit.CellularDistanceMacro(DistanceFunction)).AppendLine(";");
            sb.Append("    s.cellular_return_type = ").Append(NoiseEmit.CellularReturnMacro(ReturnType)).AppendLine(";");
            sb.Append("    s.cellular_jitter_mod = ").Append(NoiseEmit.F(System.Math.Clamp(Jitter, 0f, 1f))).AppendLine(";");
            if (Fractal != NoiseFractal.None)
            {
                sb.Append("    s.fractal_type = ").Append(NoiseEmit.FractalMacro(Fractal)).AppendLine(";");
                sb.Append("    s.octaves = ").Append(System.Math.Max(1, Octaves)).AppendLine(";");
                sb.Append("    s.lacunarity = ").Append(NoiseEmit.F(Lacunarity)).AppendLine(";");
                sb.Append("    s.gain = ").Append(NoiseEmit.F(Gain)).AppendLine(";");
            }
            sb.AppendLine(Dimension == NoiseDimension.Noise2D
                ? "    return fnlGetNoise2D(s, p.x, p.y);"
                : "    return fnlGetNoise3D(s, p.x, p.y, p.z);");
            sb.AppendLine("}");
            ctx.TopLevelHelpers.AppendLine(sb.ToString());
        });

        string coord = ctx.EvaluateInputAs(GetInput("Coord")!, ShaderType.Vec3);
        return $"{fnName}({coord})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

// ═════════════════════════════════════════════════════════════════════════════
// DomainWarpNode
// Noise-displaced coordinates. Typical pattern: warp → sample noise at warped
// coord. Produces that "swirly / organic" look much cheaper than doing it by
// hand with multiple noise nodes chained through vector math.
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Displaces the input coord by a noise field, producing a "swirled" coord
/// suitable for feeding into another noise sampler. 2D output is vec2, 3D is vec3.
/// </summary>
public sealed class DomainWarpNode : Node, IShaderNode, IShaderGraphNode
{
    public NoiseDimension Dimension = NoiseDimension.Noise2D;

    /// <summary>Warp algorithm.</summary>
    public DomainWarpType WarpType = DomainWarpType.OpenSimplex2;

    /// <summary>Fractal warping mode.</summary>
    public DomainWarpFractal Fractal = DomainWarpFractal.None;

    public int Seed = 1337;
    public float Frequency = 1f;

    /// <summary>Maximum displacement distance.</summary>
    public float Amplitude = 30f;

    public int Octaves = 3;
    public float Lacunarity = 2f;
    public float Gain = 0.5f;

    public override string Title => Fractal == DomainWarpFractal.None
        ? $"Domain Warp · {WarpType}"
        : $"Domain Warp · {WarpType} · {Fractal}";
    public override string Category => "Noise";
    public override System.Drawing.Color AccentColor => NoiseAccents.Noise;

    protected override void DefineNode()
    {
        AddInput<Float3>("Coord", Float3.Zero,
            tooltip: "Coord to warp. Float2 inputs are auto-promoted.");
        AddOutput<Float2>("Warped2");
        AddOutput<Float3>("Warped3");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("FastNoiseLite");

        // Compute once per node into a locally-scoped var so both output ports
        // share the same warp result and we don't re-run the warp per output.
        string varName = $"_fnlWarp_{Id:N}";
        ctx.EmitOnce("fnlWarp:" + varName, () =>
        {
            string coord = ctx.EvaluateInputAs(GetInput("Coord")!, ShaderType.Vec3);
            var sb = new StringBuilder();
            sb.AppendLine($"    vec3 {varName};");
            sb.AppendLine("    {");
            sb.AppendLine($"        vec3 _p = {coord};");
            sb.AppendLine($"        fnl_state _s = fnlCreateState({Seed});");
            sb.AppendLine($"        _s.frequency = {NoiseEmit.F(Frequency)};");
            sb.AppendLine($"        _s.domain_warp_type = {NoiseEmit.DomainWarpMacro(WarpType)};");
            sb.AppendLine($"        _s.domain_warp_amp = {NoiseEmit.F(Amplitude)};");
            if (Fractal != DomainWarpFractal.None)
            {
                string fracMacro = Fractal == DomainWarpFractal.Progressive
                    ? "FNL_FRACTAL_DOMAIN_WARP_PROGRESSIVE"
                    : "FNL_FRACTAL_DOMAIN_WARP_INDEPENDENT";
                sb.AppendLine($"        _s.fractal_type = {fracMacro};");
                sb.AppendLine($"        _s.octaves = {System.Math.Max(1, Octaves)};");
                sb.AppendLine($"        _s.lacunarity = {NoiseEmit.F(Lacunarity)};");
                sb.AppendLine($"        _s.gain = {NoiseEmit.F(Gain)};");
            }
            // fnlDomainWarp* mutates via inout — assign to locals so the components
            // are valid lvalues on every driver.
            sb.AppendLine("        float _x = _p.x, _y = _p.y, _z = _p.z;");
            sb.AppendLine(Dimension == NoiseDimension.Noise2D
                ? "        fnlDomainWarp2D(_s, _x, _y);"
                : "        fnlDomainWarp3D(_s, _x, _y, _z);");
            sb.AppendLine(Dimension == NoiseDimension.Noise2D
                ? $"        {varName} = vec3(_x, _y, 0.0);"
                : $"        {varName} = vec3(_x, _y, _z);");
            sb.AppendLine("    }");
            ctx.BodyPrelude.Append(sb);
        });

        return p.Name == "Warped2" ? $"{varName}.xy" : varName;
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => p.Name == "Warped2" ? ShaderType.Vec2 : ShaderType.Vec3;
}
