// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Noise nodes — thin wrappers over FastNoiseLite.glsl (Jordan Peck's library,
// MIT-licensed, embedded as a default include). Each algorithm × dimension
// combination is its own concrete node so the browser lists real names instead
// of dropdown-buried variants, and port shapes are fixed per node (no dynamic
// hiding — DefineNode only runs on creation, so toggling an enum wouldn't
// actually reshape the ports anyway).
//
// Layout per node:
//   2D nodes: XY (vec2) + X, Y (floats) — vector port wins when wired
//   3D nodes: XYZ (vec3) + X, Y, Z (floats) — vector port wins when wired
//   Everything takes a wireable Frequency (and Jitter / Amplitude where applicable).

using System.Globalization;
using System.Text;

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

internal static class NoiseAccents
{
    public static readonly System.Drawing.Color Noise =
        System.Drawing.Color.FromArgb(255, 170, 130, 200);
}

public enum NoiseFractal { None = 0, FBM = 1, Ridged = 2, PingPong = 3 }

public enum CellularDistance { Euclidean = 0, EuclideanSq = 1, Manhattan = 2, Hybrid = 3 }

public enum CellularReturn
{
    CellValue = 0,
    Distance = 1,
    Distance2 = 2,
    Distance2Add = 3,
    Distance2Sub = 4,
    Distance2Mul = 5,
    Distance2Div = 6,
}

public enum DomainWarpType { OpenSimplex2 = 0, OpenSimplex2Reduced = 1, BasicGrid = 2 }

public enum DomainWarpFractal { None = 0, Progressive = 4, Independent = 5 }

internal static class NoiseEmit
{
    public static string F(float v) => v.ToString("0.0#######", CultureInfo.InvariantCulture);

    public static string FractalMacro(NoiseFractal f) => f switch
    {
        NoiseFractal.FBM      => "FNL_FRACTAL_FBM",
        NoiseFractal.Ridged   => "FNL_FRACTAL_RIDGED",
        NoiseFractal.PingPong => "FNL_FRACTAL_PINGPONG",
        _                      => "FNL_FRACTAL_NONE",
    };

    public static string CellularDistanceMacro(CellularDistance d) => d switch
    {
        CellularDistance.Euclidean   => "FNL_CELLULAR_DISTANCE_EUCLIDEAN",
        CellularDistance.EuclideanSq => "FNL_CELLULAR_DISTANCE_EUCLIDEANSQ",
        CellularDistance.Manhattan   => "FNL_CELLULAR_DISTANCE_MANHATTAN",
        CellularDistance.Hybrid      => "FNL_CELLULAR_DISTANCE_HYBRID",
        _                             => "FNL_CELLULAR_DISTANCE_EUCLIDEANSQ",
    };

    public static string CellularReturnMacro(CellularReturn r) => r switch
    {
        CellularReturn.CellValue     => "FNL_CELLULAR_RETURN_TYPE_CELLVALUE",
        CellularReturn.Distance      => "FNL_CELLULAR_RETURN_TYPE_DISTANCE",
        CellularReturn.Distance2     => "FNL_CELLULAR_RETURN_TYPE_DISTANCE2",
        CellularReturn.Distance2Add  => "FNL_CELLULAR_RETURN_TYPE_DISTANCE2ADD",
        CellularReturn.Distance2Sub  => "FNL_CELLULAR_RETURN_TYPE_DISTANCE2SUB",
        CellularReturn.Distance2Mul  => "FNL_CELLULAR_RETURN_TYPE_DISTANCE2MUL",
        CellularReturn.Distance2Div  => "FNL_CELLULAR_RETURN_TYPE_DISTANCE2DIV",
        _                             => "FNL_CELLULAR_RETURN_TYPE_DISTANCE",
    };

    public static string DomainWarpMacro(DomainWarpType w) => w switch
    {
        DomainWarpType.OpenSimplex2        => "FNL_DOMAIN_WARP_OPENSIMPLEX2",
        DomainWarpType.OpenSimplex2Reduced => "FNL_DOMAIN_WARP_OPENSIMPLEX2_REDUCED",
        DomainWarpType.BasicGrid           => "FNL_DOMAIN_WARP_BASICGRID",
        _                                   => "FNL_DOMAIN_WARP_OPENSIMPLEX2",
    };

    /// <summary>Resolve a 2D node's coord ports to a <c>vec2</c> expression. XY wins
    /// when wired; otherwise builds from X + Y scalars.</summary>
    public static string ResolveCoord2D(Node node, ShaderGenContext ctx)
    {
        var xy = node.GetInput("XY");
        if (xy != null && ctx.IsConnected(xy))
            return ctx.EvaluateInputAs(xy, ShaderType.Vec2);

        var x = ctx.EvaluateInputAs(node.GetInput("X")!, ShaderType.Float);
        var y = ctx.EvaluateInputAs(node.GetInput("Y")!, ShaderType.Float);
        return $"vec2({x}, {y})";
    }

    /// <summary>Resolve a 3D node's coord ports to a <c>vec3</c> expression. XYZ wins
    /// when wired; otherwise builds from X + Y + Z scalars.</summary>
    public static string ResolveCoord3D(Node node, ShaderGenContext ctx)
    {
        var xyz = node.GetInput("XYZ");
        if (xyz != null && ctx.IsConnected(xyz))
            return ctx.EvaluateInputAs(xyz, ShaderType.Vec3);

        var x = ctx.EvaluateInputAs(node.GetInput("X")!, ShaderType.Float);
        var y = ctx.EvaluateInputAs(node.GetInput("Y")!, ShaderType.Float);
        var z = ctx.EvaluateInputAs(node.GetInput("Z")!, ShaderType.Float);
        return $"vec3({x}, {y}, {z})";
    }

    /// <summary>Emit the common fractal-state fields into the generated helper.
    /// Caller is responsible for having already set frequency + noise_type.</summary>
    public static void EmitFractalState(StringBuilder sb, NoiseFractal fractal, int octaves,
        float lacunarity, float gain, float weightedStrength = 0f, float pingPongStrength = 2f)
    {
        if (fractal == NoiseFractal.None) return;
        sb.Append("    s.fractal_type = ").Append(FractalMacro(fractal)).AppendLine(";");
        sb.Append("    s.octaves = ").Append(System.Math.Max(1, octaves)).AppendLine(";");
        sb.Append("    s.lacunarity = ").Append(F(lacunarity)).AppendLine(";");
        sb.Append("    s.gain = ").Append(F(gain)).AppendLine(";");
        sb.Append("    s.weighted_strength = ").Append(F(weightedStrength)).AppendLine(";");
        if (fractal == NoiseFractal.PingPong)
            sb.Append("    s.ping_pong_strength = ").Append(F(pingPongStrength)).AppendLine(";");
    }
}

// ═════════════════════════════════════════════════════════════════════════════
// Base classes — 2D and 3D scalar-output noise
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Shared base for 2D scalar-output noise nodes (Perlin / Simplex / Value /
/// Value Cubic / Simplex Smooth). Subclasses supply the FNL noise-type macro and
/// a display name; everything else is common.</summary>
public abstract class Noise2DNodeBase : Node, IShaderNode, IShaderGraphNode
{
    public NoiseFractal Fractal = NoiseFractal.None;

    public int   Seed             = 1337;
    public int   Octaves          = 3;
    public float Lacunarity       = 2f;
    public float Gain             = 0.5f;
    public float WeightedStrength = 0f;
    public float PingPongStrength = 2f;

    protected abstract string NoiseTypeMacro { get; }
    protected abstract string DisplayName    { get; }

    public override string Title => Fractal == NoiseFractal.None
        ? $"{DisplayName} 2D"
        : $"{DisplayName} 2D · {Fractal}";
    public override string Category => "Noise";
    public override System.Drawing.Color AccentColor => NoiseAccents.Noise;

    protected override void DefineNode()
    {
        AddInput<Float2>("XY", Float2.Zero, tooltip: "2D sample coord. Takes priority over X/Y when wired.");
        AddInput<float> ("X",  0f);
        AddInput<float> ("Y",  0f);
        AddInput<float> ("Frequency", 1f, tooltip: "Coord multiplier. Lower = larger features.");
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("FastNoiseLite");

        string coord = NoiseEmit.ResolveCoord2D(this, ctx);
        string freq  = ctx.EvaluateInputAs(GetInput("Frequency")!, ShaderType.Float);

        string fn = $"_fnlNoise2D_{Id:N}";
        ctx.EmitOnce("fnlNoise2D:" + fn, () =>
        {
            var sb = new StringBuilder();
            sb.Append("float ").Append(fn).AppendLine("(vec2 p, float freq) {");
            sb.Append("    fnl_state s = fnlCreateState(").Append(Seed).AppendLine(");");
            sb.AppendLine("    s.frequency = freq;");
            sb.Append("    s.noise_type = ").Append(NoiseTypeMacro).AppendLine(";");
            NoiseEmit.EmitFractalState(sb, Fractal, Octaves, Lacunarity, Gain, WeightedStrength, PingPongStrength);
            sb.AppendLine("    return fnlGetNoise2D(s, p.x, p.y);");
            sb.AppendLine("}");
            ctx.TopLevelHelpers.AppendLine(sb.ToString());
        });

        return $"{fn}({coord}, {freq})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

/// <summary>Shared base for 3D scalar-output noise nodes. Same role as
/// <see cref="Noise2DNodeBase"/> but with vec3 coords.</summary>
public abstract class Noise3DNodeBase : Node, IShaderNode, IShaderGraphNode
{
    public NoiseFractal Fractal = NoiseFractal.None;

    public int   Seed             = 1337;
    public int   Octaves          = 3;
    public float Lacunarity       = 2f;
    public float Gain             = 0.5f;
    public float WeightedStrength = 0f;
    public float PingPongStrength = 2f;

    protected abstract string NoiseTypeMacro { get; }
    protected abstract string DisplayName    { get; }

    public override string Title => Fractal == NoiseFractal.None
        ? $"{DisplayName} 3D"
        : $"{DisplayName} 3D · {Fractal}";
    public override string Category => "Noise";
    public override System.Drawing.Color AccentColor => NoiseAccents.Noise;

    protected override void DefineNode()
    {
        AddInput<Float3>("XYZ", Float3.Zero, tooltip: "3D sample coord. Takes priority over X/Y/Z when wired.");
        AddInput<float> ("X",   0f);
        AddInput<float> ("Y",   0f);
        AddInput<float> ("Z",   0f);
        AddInput<float> ("Frequency", 1f, tooltip: "Coord multiplier. Lower = larger features.");
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("FastNoiseLite");

        string coord = NoiseEmit.ResolveCoord3D(this, ctx);
        string freq  = ctx.EvaluateInputAs(GetInput("Frequency")!, ShaderType.Float);

        string fn = $"_fnlNoise3D_{Id:N}";
        ctx.EmitOnce("fnlNoise3D:" + fn, () =>
        {
            var sb = new StringBuilder();
            sb.Append("float ").Append(fn).AppendLine("(vec3 p, float freq) {");
            sb.Append("    fnl_state s = fnlCreateState(").Append(Seed).AppendLine(");");
            sb.AppendLine("    s.frequency = freq;");
            sb.Append("    s.noise_type = ").Append(NoiseTypeMacro).AppendLine(";");
            NoiseEmit.EmitFractalState(sb, Fractal, Octaves, Lacunarity, Gain, WeightedStrength, PingPongStrength);
            sb.AppendLine("    return fnlGetNoise3D(s, p.x, p.y, p.z);");
            sb.AppendLine("}");
            ctx.TopLevelHelpers.AppendLine(sb.ToString());
        });

        return $"{fn}({coord}, {freq})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

// ═════════════════════════════════════════════════════════════════════════════
// Concrete scalar-output noise nodes (5 algorithms × 2 dimensions = 10)
// ═════════════════════════════════════════════════════════════════════════════

// Perlin — classic grid-aligned gradient noise. Cheap, widely recognised.
public sealed class PerlinNoise2DNode : Noise2DNodeBase { protected override string NoiseTypeMacro => "FNL_NOISE_PERLIN"; protected override string DisplayName => "Perlin"; }
public sealed class PerlinNoise3DNode : Noise3DNodeBase { protected override string NoiseTypeMacro => "FNL_NOISE_PERLIN"; protected override string DisplayName => "Perlin"; }

// Simplex (OpenSimplex2) — smoother than Perlin, no visible grid. Default pick.
public sealed class SimplexNoise2DNode : Noise2DNodeBase { protected override string NoiseTypeMacro => "FNL_NOISE_OPENSIMPLEX2"; protected override string DisplayName => "Simplex"; }
public sealed class SimplexNoise3DNode : Noise3DNodeBase { protected override string NoiseTypeMacro => "FNL_NOISE_OPENSIMPLEX2"; protected override string DisplayName => "Simplex"; }

// Simplex Smooth (OpenSimplex2S) — slightly smoother than OpenSimplex2 at extra cost.
public sealed class SimplexSmoothNoise2DNode : Noise2DNodeBase { protected override string NoiseTypeMacro => "FNL_NOISE_OPENSIMPLEX2S"; protected override string DisplayName => "Simplex Smooth"; }
public sealed class SimplexSmoothNoise3DNode : Noise3DNodeBase { protected override string NoiseTypeMacro => "FNL_NOISE_OPENSIMPLEX2S"; protected override string DisplayName => "Simplex Smooth"; }

// Value — bilinear hash-grid noise. Cheapest option. Boxy look.
public sealed class ValueNoise2DNode : Noise2DNodeBase { protected override string NoiseTypeMacro => "FNL_NOISE_VALUE"; protected override string DisplayName => "Value"; }
public sealed class ValueNoise3DNode : Noise3DNodeBase { protected override string NoiseTypeMacro => "FNL_NOISE_VALUE"; protected override string DisplayName => "Value"; }

// Value Cubic — bicubic value noise. Smoother than Value, cheaper than Simplex.
public sealed class ValueCubicNoise2DNode : Noise2DNodeBase { protected override string NoiseTypeMacro => "FNL_NOISE_VALUE_CUBIC"; protected override string DisplayName => "Value Cubic"; }
public sealed class ValueCubicNoise3DNode : Noise3DNodeBase { protected override string NoiseTypeMacro => "FNL_NOISE_VALUE_CUBIC"; protected override string DisplayName => "Value Cubic"; }

// ═════════════════════════════════════════════════════════════════════════════
// Cellular / Voronoi — 2D and 3D variants
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>2D cellular / Voronoi noise. Output is a scalar — CellValue is a
/// per-cell hash, distance-based modes return a distance metric.</summary>
public sealed class CellularNoise2DNode : Node, IShaderNode, IShaderGraphNode
{
    public CellularDistance DistanceFunction = CellularDistance.EuclideanSq;
    public CellularReturn   ReturnType       = CellularReturn.Distance;
    public NoiseFractal     Fractal          = NoiseFractal.None;

    public int   Seed       = 1337;
    public int   Octaves    = 3;
    public float Lacunarity = 2f;
    public float Gain       = 0.5f;

    public override string Title => $"Cellular 2D · {ReturnType}";
    public override string Category => "Noise";
    public override System.Drawing.Color AccentColor => NoiseAccents.Noise;

    protected override void DefineNode()
    {
        AddInput<Float2>("XY", Float2.Zero, tooltip: "2D sample coord. Takes priority over X/Y when wired.");
        AddInput<float> ("X",  0f);
        AddInput<float> ("Y",  0f);
        AddInput<float>("Frequency", 1f, tooltip: "Coord multiplier. Lower = larger cells.");
        AddInput<float>("Jitter",    1f, tooltip: "Feature-point jitter [0, 1]. 0 = clean grid, 1 = fully random.");
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("FastNoiseLite");

        string coord  = NoiseEmit.ResolveCoord2D(this, ctx);
        string freq   = ctx.EvaluateInputAs(GetInput("Frequency")!, ShaderType.Float);
        string jitter = ctx.EvaluateInputAs(GetInput("Jitter")!,    ShaderType.Float);

        string fn = $"_fnlCell2D_{Id:N}";
        ctx.EmitOnce("fnlCell2D:" + fn, () =>
        {
            var sb = new StringBuilder();
            sb.Append("float ").Append(fn).AppendLine("(vec2 p, float freq, float jit) {");
            sb.Append("    fnl_state s = fnlCreateState(").Append(Seed).AppendLine(");");
            sb.AppendLine("    s.frequency = freq;");
            sb.AppendLine("    s.noise_type = FNL_NOISE_CELLULAR;");
            sb.Append("    s.cellular_distance_func = ").Append(NoiseEmit.CellularDistanceMacro(DistanceFunction)).AppendLine(";");
            sb.Append("    s.cellular_return_type = ").Append(NoiseEmit.CellularReturnMacro(ReturnType)).AppendLine(";");
            sb.AppendLine("    s.cellular_jitter_mod = clamp(jit, 0.0, 1.0);");
            NoiseEmit.EmitFractalState(sb, Fractal, Octaves, Lacunarity, Gain);
            sb.AppendLine("    return fnlGetNoise2D(s, p.x, p.y);");
            sb.AppendLine("}");
            ctx.TopLevelHelpers.AppendLine(sb.ToString());
        });

        return $"{fn}({coord}, {freq}, {jitter})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

/// <summary>3D cellular / Voronoi noise. Same knobs as the 2D variant but samples
/// through a volumetric field — useful for animated or volumetric effects.</summary>
public sealed class CellularNoise3DNode : Node, IShaderNode, IShaderGraphNode
{
    public CellularDistance DistanceFunction = CellularDistance.EuclideanSq;
    public CellularReturn   ReturnType       = CellularReturn.Distance;
    public NoiseFractal     Fractal          = NoiseFractal.None;

    public int   Seed       = 1337;
    public int   Octaves    = 3;
    public float Lacunarity = 2f;
    public float Gain       = 0.5f;

    public override string Title => $"Cellular 3D · {ReturnType}";
    public override string Category => "Noise";
    public override System.Drawing.Color AccentColor => NoiseAccents.Noise;

    protected override void DefineNode()
    {
        AddInput<Float3>("XYZ", Float3.Zero, tooltip: "3D sample coord. Takes priority over X/Y/Z when wired.");
        AddInput<float> ("X",   0f);
        AddInput<float> ("Y",   0f);
        AddInput<float> ("Z",   0f);
        AddInput<float>("Frequency", 1f, tooltip: "Coord multiplier. Lower = larger cells.");
        AddInput<float>("Jitter",    1f, tooltip: "Feature-point jitter [0, 1]. 0 = clean grid, 1 = fully random.");
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("FastNoiseLite");

        string coord  = NoiseEmit.ResolveCoord3D(this, ctx);
        string freq   = ctx.EvaluateInputAs(GetInput("Frequency")!, ShaderType.Float);
        string jitter = ctx.EvaluateInputAs(GetInput("Jitter")!,    ShaderType.Float);

        string fn = $"_fnlCell3D_{Id:N}";
        ctx.EmitOnce("fnlCell3D:" + fn, () =>
        {
            var sb = new StringBuilder();
            sb.Append("float ").Append(fn).AppendLine("(vec3 p, float freq, float jit) {");
            sb.Append("    fnl_state s = fnlCreateState(").Append(Seed).AppendLine(");");
            sb.AppendLine("    s.frequency = freq;");
            sb.AppendLine("    s.noise_type = FNL_NOISE_CELLULAR;");
            sb.Append("    s.cellular_distance_func = ").Append(NoiseEmit.CellularDistanceMacro(DistanceFunction)).AppendLine(";");
            sb.Append("    s.cellular_return_type = ").Append(NoiseEmit.CellularReturnMacro(ReturnType)).AppendLine(";");
            sb.AppendLine("    s.cellular_jitter_mod = clamp(jit, 0.0, 1.0);");
            NoiseEmit.EmitFractalState(sb, Fractal, Octaves, Lacunarity, Gain);
            sb.AppendLine("    return fnlGetNoise3D(s, p.x, p.y, p.z);");
            sb.AppendLine("}");
            ctx.TopLevelHelpers.AppendLine(sb.ToString());
        });

        return $"{fn}({coord}, {freq}, {jitter})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

// ═════════════════════════════════════════════════════════════════════════════
// Domain Warp — 2D and 3D variants
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>Noise-displaces a 2D coord, producing a "swirled" vec2 suitable for
/// feeding into another noise sampler.</summary>
public sealed class DomainWarp2DNode : Node, IShaderNode, IShaderGraphNode
{
    public DomainWarpType    WarpType = DomainWarpType.OpenSimplex2;
    public DomainWarpFractal Fractal  = DomainWarpFractal.None;

    public int   Seed       = 1337;
    public int   Octaves    = 3;
    public float Lacunarity = 2f;
    public float Gain       = 0.5f;

    public override string Title => Fractal == DomainWarpFractal.None
        ? $"Domain Warp 2D · {WarpType}"
        : $"Domain Warp 2D · {WarpType} · {Fractal}";
    public override string Category => "Noise";
    public override System.Drawing.Color AccentColor => NoiseAccents.Noise;

    protected override void DefineNode()
    {
        AddInput<Float2>("XY", Float2.Zero, tooltip: "2D coord to warp. Takes priority over X/Y when wired.");
        AddInput<float> ("X",  0f);
        AddInput<float> ("Y",  0f);
        AddInput<float>("Frequency", 1f, tooltip: "Coord multiplier feeding the warp sampler. Lower = broader swirls.");
        AddInput<float>("Amplitude", 30f, tooltip: "Maximum displacement distance in input-space units.");
        AddOutput<Float2>("Warped");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("FastNoiseLite");

        // Compute once per node, cache into a body-local for repeat reads from
        // whichever downstream nodes wire the Warped output.
        string varName = $"_fnlWarp2D_{Id:N}";
        ctx.EmitOnce("fnlWarp2D:" + varName, () =>
        {
            string coord = NoiseEmit.ResolveCoord2D(this, ctx);
            string freq  = ctx.EvaluateInputAs(GetInput("Frequency")!, ShaderType.Float);
            string amp   = ctx.EvaluateInputAs(GetInput("Amplitude")!, ShaderType.Float);

            var sb = new StringBuilder();
            sb.AppendLine($"    vec2 {varName};");
            sb.AppendLine("    {");
            sb.AppendLine($"        vec2 _p = {coord};");
            sb.AppendLine($"        fnl_state _s = fnlCreateState({Seed});");
            sb.AppendLine($"        _s.frequency = {freq};");
            sb.AppendLine($"        _s.domain_warp_type = {NoiseEmit.DomainWarpMacro(WarpType)};");
            sb.AppendLine($"        _s.domain_warp_amp = {amp};");
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
            // fnlDomainWarp2D mutates via inout — use named locals so the arguments
            // are valid lvalues on every GLSL driver.
            sb.AppendLine("        float _x = _p.x, _y = _p.y;");
            sb.AppendLine("        fnlDomainWarp2D(_s, _x, _y);");
            sb.AppendLine($"        {varName} = vec2(_x, _y);");
            sb.AppendLine("    }");
            ctx.BodyPrelude.Append(sb);
        });

        return varName;
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec2;
}

// ═════════════════════════════════════════════════════════════════════════════
// Simplex 4D — Ashima / Stefan Gustavson, via SimplexNoise4D.glsl
// FastNoiseLite doesn't ship a 4D variant, so we link in the Ashima port. Useful
// when you want seamless animated 3D noise (feed time into the W channel) or
// cyclic 2D noise (feed sin/cos of an angle into Z/W).
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// 4D simplex noise, backed by Ashima Arts' <c>snoise(vec4)</c> from
/// <c>SimplexNoise4D.glsl</c>. Typical use is 3D space + a time axis for
/// seamless animated noise — feed <c>_Time.y</c> into W and the sample at the
/// same XYZ evolves smoothly forever without repeating.
/// </summary>
public sealed class SimplexNoise4DNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Simplex 4D";
    public override string Category => "Noise";
    public override System.Drawing.Color AccentColor => NoiseAccents.Noise;

    protected override void DefineNode()
    {
        AddInput<Float4>("XYZW", Float4.Zero, tooltip: "4D sample coord. Takes priority over X/Y/Z/W when wired.");
        AddInput<float> ("X",    0f);
        AddInput<float> ("Y",    0f);
        AddInput<float> ("Z",    0f);
        AddInput<float> ("W",    0f);
        AddInput<float>("Frequency", 1f, tooltip: "Coord multiplier. Lower = larger features.");
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("SimplexNoise4D");

        // Vector coord wins when wired; otherwise compose from the scalars.
        string coord;
        var xyzw = GetInput("XYZW");
        if (xyzw != null && ctx.IsConnected(xyzw))
        {
            coord = ctx.EvaluateInputAs(xyzw, ShaderType.Vec4);
        }
        else
        {
            var x = ctx.EvaluateInputAs(GetInput("X")!, ShaderType.Float);
            var y = ctx.EvaluateInputAs(GetInput("Y")!, ShaderType.Float);
            var z = ctx.EvaluateInputAs(GetInput("Z")!, ShaderType.Float);
            var w = ctx.EvaluateInputAs(GetInput("W")!, ShaderType.Float);
            coord = $"vec4({x}, {y}, {z}, {w})";
        }

        var freq = ctx.EvaluateInputAs(GetInput("Frequency")!, ShaderType.Float);
        return $"snoise(({coord}) * {freq})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

/// <summary>Noise-displaces a 3D coord into a "swirled" vec3.</summary>
public sealed class DomainWarp3DNode : Node, IShaderNode, IShaderGraphNode
{
    public DomainWarpType    WarpType = DomainWarpType.OpenSimplex2;
    public DomainWarpFractal Fractal  = DomainWarpFractal.None;

    public int   Seed       = 1337;
    public int   Octaves    = 3;
    public float Lacunarity = 2f;
    public float Gain       = 0.5f;

    public override string Title => Fractal == DomainWarpFractal.None
        ? $"Domain Warp 3D · {WarpType}"
        : $"Domain Warp 3D · {WarpType} · {Fractal}";
    public override string Category => "Noise";
    public override System.Drawing.Color AccentColor => NoiseAccents.Noise;

    protected override void DefineNode()
    {
        AddInput<Float3>("XYZ", Float3.Zero, tooltip: "3D coord to warp. Takes priority over X/Y/Z when wired.");
        AddInput<float> ("X",   0f);
        AddInput<float> ("Y",   0f);
        AddInput<float> ("Z",   0f);
        AddInput<float>("Frequency", 1f, tooltip: "Coord multiplier feeding the warp sampler. Lower = broader swirls.");
        AddInput<float>("Amplitude", 30f, tooltip: "Maximum displacement distance in input-space units.");
        AddOutput<Float3>("Warped");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        ctx.Includes.Add("FastNoiseLite");

        string varName = $"_fnlWarp3D_{Id:N}";
        ctx.EmitOnce("fnlWarp3D:" + varName, () =>
        {
            string coord = NoiseEmit.ResolveCoord3D(this, ctx);
            string freq  = ctx.EvaluateInputAs(GetInput("Frequency")!, ShaderType.Float);
            string amp   = ctx.EvaluateInputAs(GetInput("Amplitude")!, ShaderType.Float);

            var sb = new StringBuilder();
            sb.AppendLine($"    vec3 {varName};");
            sb.AppendLine("    {");
            sb.AppendLine($"        vec3 _p = {coord};");
            sb.AppendLine($"        fnl_state _s = fnlCreateState({Seed});");
            sb.AppendLine($"        _s.frequency = {freq};");
            sb.AppendLine($"        _s.domain_warp_type = {NoiseEmit.DomainWarpMacro(WarpType)};");
            sb.AppendLine($"        _s.domain_warp_amp = {amp};");
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
            sb.AppendLine("        float _x = _p.x, _y = _p.y, _z = _p.z;");
            sb.AppendLine("        fnlDomainWarp3D(_s, _x, _y, _z);");
            sb.AppendLine($"        {varName} = vec3(_x, _y, _z);");
            sb.AppendLine("    }");
            ctx.BodyPrelude.Append(sb);
        });

        return varName;
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}
