// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Standard GLSL vector operation nodes.

using Prowl.Vector;
using System.Text;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

// -----------------------------------------------------------------------------
// Accent colour for vector-ops nodes
// -----------------------------------------------------------------------------

internal static class VectorAccents
{
    public static readonly System.Drawing.Color Vector = System.Drawing.Color.FromArgb(255, 110, 200, 130); // green
}

// =============================================================================
// AppendNode
// =============================================================================

/// <summary>
/// Packs up to four scalar inputs (X, Y, Z, W) into a vector. Three output
/// ports Vec2, Vec3, Vec4 let you grab whichever width you need from the
/// same node, instead of juggling dynamic output types.
/// </summary>
/// <remarks>
/// X and Y are required. Z and W default to 0 when unconnected. Each input is
/// promoted to a scalar (takes <c>.x</c> if a vector is wired in); if you need
/// to concatenate vectors of mixed widths, use Component Mask + a second
/// Append, or author a Custom Code node.
/// </remarks>
public sealed class AppendNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Append";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    protected override void DefineNode()
    {
        AddInput<float>("X", 0f, required: true);
        AddInput<float>("Y", 0f, required: true);
        AddInput<float>("Z", 0f, required: false);
        AddInput<float>("W", 0f, required: false);
        AddOutput<Float2>("Vec2");
        AddOutput<Float3>("Vec3");
        AddOutput<Float4>("Vec4");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        // Each input is forced to a scalar Promote takes .x of wider wires.
        string x = ctx.EvaluateInputAs(GetInput("X")!, ShaderType.Float);
        string y = ctx.EvaluateInputAs(GetInput("Y")!, ShaderType.Float);
        string z = ctx.EvaluateInputAs(GetInput("Z")!, ShaderType.Float);
        string w = ctx.EvaluateInputAs(GetInput("W")!, ShaderType.Float);

        return p.Name switch
        {
            "Vec2" => $"vec2({x}, {y})",
            "Vec3" => $"vec3({x}, {y}, {z})",
            _      => $"vec4({x}, {y}, {z}, {w})",
        };
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => p.Name switch
        {
            "Vec2" => ShaderType.Vec2,
            "Vec3" => ShaderType.Vec3,
            _      => ShaderType.Vec4,
        };
}

// =============================================================================
// SplitNode opposite of Append
// Breaks a vector back out into its component floats, plus Vec2/Vec3 prefixes
// for convenience when you only need a subset.
// =============================================================================

/// <summary>
/// Splits a vector into its component floats. Symmetric with <see cref="AppendNode"/>:
/// feed a vec4 in, pull any of X / Y / Z / W as individual floats, or the
/// Vec2 / Vec3 prefixes if you want a narrower vector. Scalars and narrower
/// vectors are accepted too missing channels read 0.
/// </summary>
public sealed class SplitNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Split";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    protected override void DefineNode()
    {
        AddInput<Float4>("In", Float4.Zero, required: true);
        AddOutput<float>("X");
        AddOutput<float>("Y");
        AddOutput<float>("Z");
        AddOutput<float>("W");
        AddOutput<Float2>("XY");
        AddOutput<Float3>("XYZ");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        // Promote to vec4 once so narrower inputs get zero-filled saves the
        // user from worrying about the source width when they just want .x.
        var src = $"_spl{Id:N}";
        ctx.EmitOnce("split:" + src, () =>
        {
            var inExpr = ctx.EvaluateInputAs(GetInput("In")!, ShaderType.Vec4);
            ctx.BodyPrelude.AppendLine($"    vec4 {src} = {inExpr};");
        });
        return outputPort.Name switch
        {
            "X"   => $"{src}.x",
            "Y"   => $"{src}.y",
            "Z"   => $"{src}.z",
            "W"   => $"{src}.w",
            "XY"  => $"{src}.xy",
            "XYZ" => $"{src}.xyz",
            _     => src,
        };
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx)
        => outputPort.Name switch
        {
            "XY"  => ShaderType.Vec2,
            "XYZ" => ShaderType.Vec3,
            _     => ShaderType.Float,   // X / Y / Z / W
        };
}

// =============================================================================
// ChannelBlendNode
// =============================================================================

/// <summary>
/// Blends up to four colour inputs (R/G/B/A slots) using a mask vector's channels.
/// <para>
/// Summed mode:  out = mask.r * Rcol + mask.g * Gcol + mask.b * Bcol + mask.a * Acol
/// </para>
/// <para>
/// Layered mode: out = mix(mix(mix(mix(Btm, Rcol, mask.r), Gcol, mask.g), Bcol, mask.b), Acol, mask.a)
/// (sequentially layers each colour on top of the previous using its mask channel as the blend factor)
/// </para>
/// The number of active colour slots is determined by the mask input's channel count -
/// a vec2 mask activates only R and G slots.
/// </summary>
public enum ChannelBlendType { Summed, Layered }

public sealed class ChannelBlendNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Channel Blend";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    /// <summary>Blend mode Summed (weighted sum) or Layered (sequential mix).</summary>
    public ChannelBlendType BlendType = ChannelBlendType.Summed;

    protected override void DefineNode()
    {
        AddInput<Float4>("Mask",  Float4.Zero, required: true);
        AddInput<Float4>("Rcol",  Float4.Zero, required: true);
        AddInput<Float4>("Gcol",  Float4.Zero, required: true);
        AddInput<Float4>("Bcol",  Float4.Zero, required: false);
        AddInput<Float4>("Acol",  Float4.Zero, required: false);
        AddInput<Float4>("Btm",   Float4.Zero, required: false);  // base layer for Layered mode
        AddOutput<Float4>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        // Determine how many channels the mask carries that sets how many colour slots are active.
        int maskChannels = ShaderTypeUtil.Channels(ctx.GetSourceType(GetInput("Mask")!));
        if (maskChannels < 1) maskChannels = 4; // unconnected / unknown assume 4

        var colType = ctx.GetSourceType(GetInput("Rcol")!); // output channels follow colour inputs
        string m = ctx.EvaluateInputAs(GetInput("Mask")!, ShaderType.Vec4);
        string[] chSwiz = { "x", "y", "z", "w" };
        string[] slotNames = { "Rcol", "Gcol", "Bcol", "Acol" };

        if (BlendType == ChannelBlendType.Summed)
        {
            // out = mask.r*Rcol + mask.g*Gcol + ...
            var sb = new StringBuilder("(");
            for (int i = 0; i < maskChannels; i++)
            {
                if (i > 0) sb.Append(" + ");
                string col = ctx.EvaluateInputAs(GetInput(slotNames[i])!, colType);
                sb.Append($"({m}).{chSwiz[i]} * {col}");
            }
            sb.Append(')');
            return sb.ToString();
        }
        else // Layered
        {
            // Start from the bottom layer, then sequentially mix in each colour.
            string inner = ctx.EvaluateInputAs(GetInput("Btm")!, colType);
            for (int i = 0; i < maskChannels; i++)
            {
                string col  = ctx.EvaluateInputAs(GetInput(slotNames[i])!, colType);
                string mask = $"({m}).{chSwiz[i]}";
                inner = $"mix({inner}, {col}, {mask})";
            }
            return inner;
        }
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => ctx.GetSourceType(GetInput("Rcol")!);
}

// =============================================================================
// ComponentMaskNode
// =============================================================================

/// <summary>
/// Extracts a user-chosen subset of channels from a vector input.
/// Each bool field (R/G/B/A) selects the corresponding channel.
/// Output type is Float/Vec2/Vec3/Vec4 based on the number of selected channels.
/// E.g. selecting R and B yields a Vec2 via swizzle ".xz".
/// </summary>
public sealed class ComponentMaskNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Component Mask";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    /// <summary>Include the R (x) channel in the output.</summary>
    public bool R = true;
    /// <summary>Include the G (y) channel in the output.</summary>
    public bool G = false;
    /// <summary>Include the B (z) channel in the output.</summary>
    public bool B = false;
    /// <summary>Include the A (w) channel in the output.</summary>
    public bool A = false;

    protected override void DefineNode()
    {
        AddInput<Float4>("In", Float4.Zero, required: true);
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        // Always evaluate the input as Vec4 so any swizzle is valid.
        var inExpr = ctx.EvaluateInputAs(GetInput("In")!, ShaderType.Vec4);
        var swiz = (R ? "x" : "") + (G ? "y" : "") + (B ? "z" : "") + (A ? "w" : "");
        if (swiz.Length == 0) swiz = "x"; // must output at least one channel
        return $"({inExpr}).{swiz}";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
    {
        int count = (R ? 1 : 0) + (G ? 1 : 0) + (B ? 1 : 0) + (A ? 1 : 0);
        return count switch
        {
            2 => ShaderType.Vec2,
            3 => ShaderType.Vec3,
            4 => ShaderType.Vec4,
            _ => ShaderType.Float, // 0 or 1
        };
    }
}

// =============================================================================
// CrossNode
// =============================================================================

/// <summary>cross(A, B) both inputs and output are Vec3.</summary>
public sealed class CrossNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Cross Product";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    protected override void DefineNode()
    {
        AddInput<Float3>("A", Float3.Zero, required: true);
        AddInput<Float3>("B", Float3.Zero, required: true);
        AddOutput<Float3>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var a = ctx.EvaluateInputAs(GetInput("A")!, ShaderType.Vec3);
        var b = ctx.EvaluateInputAs(GetInput("B")!, ShaderType.Vec3);
        return $"cross({a}, {b})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}

// =============================================================================
// DotNode
// =============================================================================

/// <summary>
/// dot(A, B) scalar output.
/// Supports optional post-process modes (Standard, Positive, Negative, Abs, Normalized).
/// </summary>
public enum DotMode { Standard, Positive, Negative, Abs, Normalized }

public sealed class DotNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Dot Product";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    /// <summary>Optional post-process applied to the raw dot product.</summary>
    public DotMode Mode = DotMode.Standard;

    protected override void DefineNode()
    {
        AddInput<Float4>("A", Float4.Zero, required: true);
        AddInput<Float4>("B", Float4.Zero, required: true);
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        // Inputs evaluated at the wider of the two source types.
        var t = ShaderEmit.MaxChannel(ctx.GetSourceType(GetInput("A")!), ctx.GetSourceType(GetInput("B")!));
        var a = ctx.EvaluateInputAs(GetInput("A")!, t);
        var b = ctx.EvaluateInputAs(GetInput("B")!, t);
        string d = $"dot({a}, {b})";
        return Mode switch
        {
            DotMode.Positive   => $"max(0.0, {d})",
            DotMode.Negative   => $"min(0.0, {d})",
            DotMode.Abs        => $"abs({d})",
            DotMode.Normalized => $"(0.5 * {d} + 0.5)",
            _                  => d,
        };
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

// =============================================================================
// DistanceNode
// =============================================================================

/// <summary>distance(A, B) scalar output.</summary>
public sealed class DistanceNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Distance";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    protected override void DefineNode()
    {
        AddInput<Float4>("A", Float4.Zero, required: true);
        AddInput<Float4>("B", Float4.Zero, required: true);
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var t = ShaderEmit.MaxChannel(ctx.GetSourceType(GetInput("A")!), ctx.GetSourceType(GetInput("B")!));
        var a = ctx.EvaluateInputAs(GetInput("A")!, t);
        var b = ctx.EvaluateInputAs(GetInput("B")!, t);
        return $"distance({a}, {b})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

// =============================================================================
// LengthNode
// =============================================================================

/// <summary>length(In) scalar output.</summary>
public sealed class LengthNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Length";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    protected override void DefineNode()
    {
        AddInput<Float4>("In", Float4.Zero, required: true);
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
        => ShaderEmit.UnaryFunc(this, "length", "In", ctx);

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

// =============================================================================
// NormalizeNode
// =============================================================================

/// <summary>normalize(In) output type matches the input's source type.</summary>
public sealed class NormalizeNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Normalize";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    protected override void DefineNode()
    {
        AddInput<Float4>("In", Float4.Zero, required: true);
        AddOutput<Float4>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
        => ShaderEmit.UnaryFunc(this, "normalize", "In", ctx);

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => ShaderEmit.TypeFromInput(this, "In", ctx);
}

// =============================================================================
// NormalBlendNode
// =============================================================================

/// <summary>
/// Blends two tangent-space normals using Reoriented Normal Mapping (RNM).
/// Algorithm:
/// <code>
///   t = base.xyz + vec3(0, 0, 1)
///   u = detail.xyz * vec3(-1, -1, 1)
///   out = normalize(t * dot(t, u) / t.z - u)
/// </code>
/// Both inputs and the output are Vec3 (tangent-space normals in [-1,1] range).
/// </summary>
public sealed class NormalBlendNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Normal Blend";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    protected override void DefineNode()
    {
        AddInput<Float3>("Base",   new Float3(0, 0, 1), required: true);
        AddInput<Float3>("Detail", new Float3(0, 0, 1), required: true);
        AddOutput<Float3>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        // Emit-once keyed on node id so multi-fanout doesn't duplicate the temp
        // declarations into BodyPrelude. Per-instance suffixes keep multiple
        // NormalBlend nodes in the same graph isolated.
        string tName = $"_nb_t_{Id:N}";
        string uName = $"_nb_u_{Id:N}";
        ctx.EmitOnce("normalblend:" + tName, () =>
        {
            var baseExpr   = ctx.EvaluateInputAs(GetInput("Base")!,   ShaderType.Vec3);
            var detailExpr = ctx.EvaluateInputAs(GetInput("Detail")!, ShaderType.Vec3);
            ctx.BodyPrelude.AppendLine($"    vec3 {tName} = ({baseExpr}) + vec3(0.0, 0.0, 1.0);");
            ctx.BodyPrelude.AppendLine($"    vec3 {uName} = ({detailExpr}) * vec3(-1.0, -1.0, 1.0);");
        });

        return $"normalize({tName} * dot({tName}, {uName}) / {tName}.z - {uName})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}

// =============================================================================
// ReflectNode
// =============================================================================

/// <summary>reflect(I, N) reflects incident vector I around surface normal N.
/// Output type matches the max-channel of the two inputs.</summary>
public sealed class ReflectNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Reflect";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    protected override void DefineNode()
    {
        AddInput<Float3>("I", Float3.Zero,           required: true,  tooltip: "Incident vector");
        AddInput<Float3>("N", new Float3(0, 0, 1),   required: true,  tooltip: "Surface normal");
        AddOutput<Float3>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var t = ShaderEmit.MaxChannel(ctx.GetSourceType(GetInput("I")!), ctx.GetSourceType(GetInput("N")!));
        var i = ctx.EvaluateInputAs(GetInput("I")!, t);
        var n = ctx.EvaluateInputAs(GetInput("N")!, t);
        return $"reflect({i}, {n})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => ShaderEmit.TypeFromInputs(this, "I", "N", ctx);
}

// =============================================================================
// RefractNode
// =============================================================================

/// <summary>
/// <c>refract(I, N, eta)</c> bends incident vector I through surface normal N by
/// the ratio of refractive indices <c>eta = n_in / n_out</c>. Returns the zero
/// vector under total internal reflection (when 1 - eta^2 * (1 - dot(N,I)^2) is
/// negative); pair with a Reflect path if you need the standard "TIR fallback".
/// </summary>
/// <remarks>
/// Typical eta values: <c>1/1.33</c> (air -> water), <c>1/1.45</c> (air -> glass),
/// <c>1/2.42</c> (air -> diamond). I should be normalised; N must be normalised.
/// </remarks>
public sealed class RefractNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Refract";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    protected override void DefineNode()
    {
        AddInput<Float3>("I",   Float3.Zero,         required: true, tooltip: "Incident vector (normalised).");
        AddInput<Float3>("N",   new Float3(0, 0, 1), required: true, tooltip: "Surface normal (normalised).");
        // 1/1.45 ~ 0.6897 air -> glass. Most common starting point.
        AddInput<float>("Eta",  0.6897f,             required: true,
            tooltip: "Ratio of refractive indices (n_in / n_out). 1/1.33 water, 1/1.45 glass, 1/2.42 diamond.");
        AddOutput<Float3>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        // GLSL refract requires the I/N pair to share a vector type and Eta is a
        // scalar. Match the I/N width to the wider operand so float-promoted
        // wires still compile cleanly.
        var t = ShaderEmit.MaxChannel(ctx.GetSourceType(GetInput("I")!), ctx.GetSourceType(GetInput("N")!));
        var i = ctx.EvaluateInputAs(GetInput("I")!,   t);
        var n = ctx.EvaluateInputAs(GetInput("N")!,   t);
        var e = ctx.EvaluateInputAs(GetInput("Eta")!, ShaderType.Float);
        return $"refract({i}, {n}, {e})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => ShaderEmit.TypeFromInputs(this, "I", "N", ctx);
}

// =============================================================================
// TransformNode
// =============================================================================

/// <summary>
/// Transforms a Vec3 between coordinate spaces.
/// Supported Prowl matrices:
/// <list type="bullet">
///   <item>PROWL_MATRIX_M   Object to World (position: vec4 w=1; direction: mat3)</item>
///   <item>prowl_WorldToObject World to Object</item>
///   <item>PROWL_MATRIX_V   World to View</item>
///   <item>PROWL_MATRIX_I_V View to World</item>
///   <item>PROWL_MATRIX_MV  Object to View (combined)</item>
/// </list>
///
/// Tangent space is supported via <see cref="ShaderEmit.EmitTBN"/> / EmitTBNTranspose,
/// which build the basis from the vNormal/vTangent/vBitangent varyings once per
/// compile. Screen / Clip space are not exposed those need a vec4 perspective divide
/// which doesn't map cleanly to a Vec3 transform.
/// </summary>
public enum TransformSpace { World, Object, View, Tangent }

public sealed class TransformNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Transform";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    /// <summary>Source coordinate space.</summary>
    public TransformSpace From = TransformSpace.World;
    /// <summary>Destination coordinate space.</summary>
    public TransformSpace To   = TransformSpace.Object;

    protected override void DefineNode()
    {
        AddInput<Float3>("In", Float3.Zero, required: true);
        AddOutput<Float3>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var inExpr = ctx.EvaluateInputAs(GetInput("In")!, ShaderType.Vec3);

        if (From == To)
            return inExpr;

        // -- World -> Object ----------------------------------------------------
        if (From == TransformSpace.World && To == TransformSpace.Object)
            return $"(mat3(prowl_WorldToObject) * ({inExpr}))";

        // -- World -> View ------------------------------------------------------
        if (From == TransformSpace.World && To == TransformSpace.View)
            return $"(mat3(PROWL_MATRIX_V) * ({inExpr}))";

        ctx.Includes.Add("ShaderVariables");

        // -- World -> Tangent ---------------------------------------------------
        if (From == TransformSpace.World && To == TransformSpace.Tangent)
        {
            var tbnT = ShaderEmit.EmitTBNTranspose(ctx);
            return $"({tbnT} * ({inExpr}))";
        }

        // -- Object -> World ----------------------------------------------------
        if (From == TransformSpace.Object && To == TransformSpace.World)
            return $"(mat3(PROWL_MATRIX_M) * ({inExpr}))";

        // -- Object -> View -----------------------------------------------------
        if (From == TransformSpace.Object && To == TransformSpace.View)
            return $"(mat3(PROWL_MATRIX_MV) * ({inExpr}))";

        // -- Object -> Tangent --------------------------------------------------
        if (From == TransformSpace.Object && To == TransformSpace.Tangent)
        {
            var tbnT = ShaderEmit.EmitTBNTranspose(ctx);
            return $"({tbnT} * (mat3(PROWL_MATRIX_M) * ({inExpr})))";
        }

        // -- View -> World ------------------------------------------------------
        if (From == TransformSpace.View && To == TransformSpace.World)
            return $"(mat3(PROWL_MATRIX_I_V) * ({inExpr}))";

        // -- View -> Object -----------------------------------------------------
        if (From == TransformSpace.View && To == TransformSpace.Object)
            return $"(mat3(prowl_WorldToObject) * (mat3(PROWL_MATRIX_I_V) * ({inExpr})))";

        // -- View -> Tangent ----------------------------------------------------
        if (From == TransformSpace.View && To == TransformSpace.Tangent)
        {
            var tbnT = ShaderEmit.EmitTBNTranspose(ctx);
            return $"({tbnT} * (mat3(PROWL_MATRIX_I_V) * ({inExpr})))";
        }

        // -- Tangent -> World / Object / View -----------------------------------
        if (From == TransformSpace.Tangent)
        {
            var tbn = ShaderEmit.EmitTBN(ctx);
            if (To == TransformSpace.World)  return $"({tbn} * ({inExpr}))";
            if (To == TransformSpace.Object) return $"(mat3(prowl_WorldToObject) * ({tbn} * ({inExpr})))";
            if (To == TransformSpace.View)   return $"(mat3(PROWL_MATRIX_V) * ({tbn} * ({inExpr})))";
        }

        // Fallback every pair should be covered above; pass through if not.
        return inExpr;
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}

// =============================================================================
// TransposeNode
// =============================================================================

/// <summary>transpose(M) output type matches input matrix type (Mat3 or Mat4).</summary>
public sealed class TransposeNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Transpose";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    protected override void DefineNode()
    {
        AddInput<Float4x4>("In",  Float4x4.Identity, required: true);
        AddOutput<Float4x4>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var t   = ctx.GetSourceType(GetInput("In")!);
        var inE = ctx.EvaluateInputAs(GetInput("In")!, t);
        return $"transpose({inE})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
    {
        var t = ctx.GetSourceType(GetInput("In")!);
        // Return whatever matrix type is wired in; default to Mat4 when unknown.
        return t == ShaderType.Mat3 ? ShaderType.Mat3 : ShaderType.Mat4;
    }
}

// =============================================================================
// VectorProjectionNode
// =============================================================================

/// <summary>
/// Projects vector A onto vector B.
/// Formula: (B * dot(A, B)) / dot(B, B)
/// Output type = max-channel of A and B.
/// </summary>
public sealed class VectorProjectionNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Vector Projection";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    protected override void DefineNode()
    {
        AddInput<Float4>("A", Float4.Zero, required: true, tooltip: "Vector to project");
        AddInput<Float4>("B", Float4.Zero, required: true, tooltip: "Target vector (projected onto)");
        AddOutput<Float4>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var t = ShaderEmit.MaxChannel(ctx.GetSourceType(GetInput("A")!), ctx.GetSourceType(GetInput("B")!));
        var a = ctx.EvaluateInputAs(GetInput("A")!, t);
        var b = ctx.EvaluateInputAs(GetInput("B")!, t);
        // Emit B once into a temp to avoid double-evaluation of potentially expensive subgraphs.
        string bName = ctx.FreshLocal("_vproj_b");
        string glslType = ShaderTypeUtil.ToGlsl(t);
        ctx.BodyPrelude.AppendLine($"    {glslType} {bName} = {b};");
        return $"({bName} * dot({a}, {bName}) / dot({bName}, {bName}))";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => ShaderEmit.TypeFromInputs(this, "A", "B", ctx);
}

// =============================================================================
// VectorRejectionNode
// =============================================================================

/// <summary>
/// Rejects vector A from vector B (the component of A perpendicular to B).
/// Formula: A - (B * dot(A, B)) / dot(B, B)
/// Output type = max-channel of A and B.
/// </summary>
public sealed class VectorRejectionNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Vector Rejection";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    protected override void DefineNode()
    {
        AddInput<Float4>("A", Float4.Zero, required: true, tooltip: "Vector to reject");
        AddInput<Float4>("B", Float4.Zero, required: true, tooltip: "Reference vector");
        AddOutput<Float4>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var t = ShaderEmit.MaxChannel(ctx.GetSourceType(GetInput("A")!), ctx.GetSourceType(GetInput("B")!));
        var a = ctx.EvaluateInputAs(GetInput("A")!, t);
        var b = ctx.EvaluateInputAs(GetInput("B")!, t);
        // Emit both into temps since A and B may each appear twice.
        string aName = ctx.FreshLocal("_vrej_a");
        string bName = ctx.FreshLocal("_vrej_b");
        string glslType = ShaderTypeUtil.ToGlsl(t);
        ctx.BodyPrelude.AppendLine($"    {glslType} {aName} = {a};");
        ctx.BodyPrelude.AppendLine($"    {glslType} {bName} = {b};");
        return $"({aName} - ({bName} * dot({aName}, {bName}) / dot({bName}, {bName})))";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => ShaderEmit.TypeFromInputs(this, "A", "B", ctx);
}

// =============================================================================
// DDXNode
// =============================================================================

/// <summary>dFdx(In) screen-space partial derivative in X. Fragment stage only.
/// Output type matches the input's source type.</summary>
public sealed class DDXNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "DDX";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    protected override void DefineNode()
    {
        AddInput<float>("In", 0f, required: true);
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
        => ShaderEmit.UnaryFunc(this, "dFdx", "In", ctx);

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => ShaderEmit.TypeFromInput(this, "In", ctx);
}

// =============================================================================
// DDYNode
// =============================================================================

/// <summary>dFdy(In) screen-space partial derivative in Y. Fragment stage only.
/// Output type matches the input's source type.</summary>
public sealed class DDYNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "DDY";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    protected override void DefineNode()
    {
        AddInput<float>("In", 0f, required: true);
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
        => ShaderEmit.UnaryFunc(this, "dFdy", "In", ctx);

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => ShaderEmit.TypeFromInput(this, "In", ctx);
}

// =============================================================================
// DDXYNode
// =============================================================================

/// <summary>
/// abs(dFdx(In)) + abs(dFdy(In)) approximates the total screen-space derivative
/// magnitude (equivalent to HLSL fwidth / GLSL fwidth, but emitted explicitly so the
/// formula is transparent).  Fragment stage only.
/// Output type matches the input's source type.
/// </summary>
public sealed class DDXYNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "DDXY";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    protected override void DefineNode()
    {
        AddInput<float>("In", 0f, required: true);
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var t   = ctx.GetSourceType(GetInput("In")!);
        var inE = ctx.EvaluateInputAs(GetInput("In")!, t);
        // Emit In into a temp so it isn't evaluated twice.
        string tmp = ctx.FreshLocal("_ddxy_in");
        string glslType = ShaderTypeUtil.ToGlsl(t);
        ctx.BodyPrelude.AppendLine($"    {glslType} {tmp} = {inE};");
        return $"(abs(dFdx({tmp})) + abs(dFdy({tmp})))";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => ShaderEmit.TypeFromInput(this, "In", ctx);
}

// =============================================================================
// DesaturateNode
// =============================================================================

/// <summary>
/// Desaturates a colour by blending it toward its luminance value.
/// <para>
/// Formula: mix(col, vec3(dot(col, vec3(0.2126, 0.7152, 0.0722))), amount)
/// </para>
/// Uses Rec.709 luminance coefficients (matching ProwlCG.glsl's luminance helper).
/// Requires ctx.Includes.Add("ProwlCG") so the luminance function is available
/// in the generated shader though the coefficients are inlined here for safety.
/// Inputs: Color (Vec3), Amount (float 0-1).
/// Output: Vec3.
/// </summary>
public sealed class DesaturateNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Desaturate";
    public override string Category => "Vector";
    public override System.Drawing.Color AccentColor => VectorAccents.Vector;

    protected override void DefineNode()
    {
        AddInput<Float3>("Color",  Float3.Zero, required: true,  tooltip: "Input RGB colour");
        AddInput<float>("Amount", 1.0f,         required: false, tooltip: "Desaturation amount (0=original, 1=greyscale)");
        AddOutput<Float3>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        // Pull in the Fragment include so luminance() is available if the graph uses it elsewhere.
        ctx.Includes.Add("ProwlCG");

        var col    = ctx.EvaluateInputAs(GetInput("Color")!,  ShaderType.Vec3);
        var amount = ctx.EvaluateInputAs(GetInput("Amount")!, ShaderType.Float);

        // Inline Rec.709 coefficients rather than calling luminance() so we don't
        // create a dependency on Fragment.glsl's exact function signature.
        string colTmp = ctx.FreshLocal("_desat_col");
        ctx.BodyPrelude.AppendLine($"    vec3 {colTmp} = {col};");
        string lum = $"dot({colTmp}, vec3(0.2126, 0.7152, 0.0722))";

        return $"mix({colTmp}, vec3({lum}), {amount})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}
