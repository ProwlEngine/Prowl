// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

// ─────────────────────────────────────────────────────────────────────────────
// PropertyAndConstantNodes.cs
//
// Constant nodes  — emit a hard-coded GLSL literal; no material binding.
// Property nodes  — implement IShaderProperty; emit a named uniform that the
//                   material inspector binds to.
//
// See IShaderNode.cs, IShaderProperty.cs, and ShaderGenContext.cs for contracts.
// ─────────────────────────────────────────────────────────────────────────────

using System.Globalization;

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

// ═════════════════════════════════════════════════════════════════════════════
// SHARED HELPERS
// ═════════════════════════════════════════════════════════════════════════════

internal static class PropNodeUtil
{
    /// <summary>Ensure the property name starts with an underscore.
    /// <c>null</c> / empty → "_Property".</summary>
    public static string NormaliseName(string n)
        => string.IsNullOrEmpty(n) ? "_Property" : (n.StartsWith("_") ? n : "_" + n);

    /// <summary>Format a float as a GLSL/Prowl-property literal —
    /// invariant culture, always contains a decimal point.</summary>
    public static string F(double v)
    {
        var s = v.ToString("0.0######", CultureInfo.InvariantCulture);
        if (!s.Contains('.')) s += ".0";
        return s;
    }

    /// <summary>Accent colour shared by all property nodes (blue-ish).</summary>
    public static readonly System.Drawing.Color PropertyAccent =
        System.Drawing.Color.FromArgb(255, 60, 130, 200);

    /// <summary>Accent colour shared by all constant nodes (purple-ish,
    /// matches MathAccents.Constant in MathNodes.cs).</summary>
    public static readonly System.Drawing.Color ConstantAccent =
        System.Drawing.Color.FromArgb(255, 130, 90, 160);
}

// ═════════════════════════════════════════════════════════════════════════════
// ── CONSTANT NODES ───────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Constant scalar (single float). No material binding — emits a GLSL float literal.
/// </summary>
public sealed class FloatConstantNode : Node, IShaderNode, IShaderGraphNode
{
    public float Value = 0f;

    public override string Title    => "Float";
    public override string Category => "Constants";
    public override System.Drawing.Color AccentColor => PropNodeUtil.ConstantAccent;

    protected override void DefineNode() => AddOutput<float>("Out");

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
        => PropNodeUtil.F(Value);

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

/// <summary>
/// Constant two-component vector. No material binding — emits <c>vec2(x, y)</c>.
/// </summary>
public sealed class Vector2ConstantNode : Node, IShaderNode, IShaderGraphNode
{
    public Float2 Value = Float2.Zero;

    public override string Title    => "Vector 2";
    public override string Category => "Constants";
    public override System.Drawing.Color AccentColor => PropNodeUtil.ConstantAccent;

    protected override void DefineNode() => AddOutput<Float2>("Out");

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
        => $"vec2({PropNodeUtil.F(Value.X)}, {PropNodeUtil.F(Value.Y)})";

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec2;
}

/// <summary>
/// Constant three-component vector. No material binding — emits <c>vec3(x, y, z)</c>.
/// </summary>
public sealed class Vector3ConstantNode : Node, IShaderNode, IShaderGraphNode
{
    public Float3 Value = Float3.Zero;

    public override string Title    => "Vector 3";
    public override string Category => "Constants";
    public override System.Drawing.Color AccentColor => PropNodeUtil.ConstantAccent;

    protected override void DefineNode() => AddOutput<Float3>("Out");

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
        => $"vec3({PropNodeUtil.F(Value.X)}, {PropNodeUtil.F(Value.Y)}, {PropNodeUtil.F(Value.Z)})";

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec3;
}

/// <summary>
/// Constant four-component vector. No material binding — emits <c>vec4(x, y, z, w)</c>.
/// </summary>
public sealed class Vector4ConstantNode : Node, IShaderNode, IShaderGraphNode
{
    public Float4 Value = Float4.Zero;

    public override string Title    => "Vector 4";
    public override string Category => "Constants";
    public override System.Drawing.Color AccentColor => PropNodeUtil.ConstantAccent;

    protected override void DefineNode() => AddOutput<Float4>("Out");

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
        => $"vec4({PropNodeUtil.F(Value.X)}, {PropNodeUtil.F(Value.Y)}, {PropNodeUtil.F(Value.Z)}, {PropNodeUtil.F(Value.W)})";

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec4;
}

/// <summary>
/// Constant colour (RGBA) with multi-channel outputs. No material binding —
/// emits <c>vec4(r, g, b, a)</c> and .rgb/.r/.g/.b/.a swizzle variants.
/// </summary>
public sealed class ColorConstantNode : Node, IShaderNode, IShaderGraphNode
{
    /// <summary>RGBA colour value stored as a Prowl Color (each channel 0..1).</summary>
    public Color Value = new Color(0.5f, 0.5f, 0.5f, 1f);

    public override string Title    => "Color";
    public override string Category => "Constants";
    public override System.Drawing.Color AccentColor => PropNodeUtil.ConstantAccent;

    protected override void DefineNode()
    {
        AddOutput<Float3>("RGB");
        AddOutput<float>("R");
        AddOutput<float>("G");
        AddOutput<float>("B");
        AddOutput<float>("A");
    }

    // Cache the base vec4 expression and swizzle per output port.
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var v = $"vec4({PropNodeUtil.F(Value.R)}, {PropNodeUtil.F(Value.G)}, {PropNodeUtil.F(Value.B)}, {PropNodeUtil.F(Value.A)})";
        return p.Name switch
        {
            "RGB" => $"({v}).rgb",
            "R"   => $"({v}).r",
            "G"   => $"({v}).g",
            "B"   => $"({v}).b",
            "A"   => $"({v}).a",
            _     => v,
        };
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => p.Name switch
    {
        "RGB"            => ShaderType.Vec3,
        "R" or "G" or "B" or "A" => ShaderType.Float,
        _                => ShaderType.Vec4,
    };
}

/// <summary>
/// Constant 4x4 matrix. No material binding — emits a <c>mat4(...)</c> constructor
/// literal with all 16 elements written column-major (GLSL mat4 convention).
/// </summary>
public sealed class Matrix4x4ConstantNode : Node, IShaderNode, IShaderGraphNode
{
    // Echo only serialises public fields, so we store the 16 elements flat.
    // Row-major storage (row * 4 + col) to match typical editor grid display.
    public float M00 = 1f; public float M01 = 0f; public float M02 = 0f; public float M03 = 0f;
    public float M10 = 0f; public float M11 = 1f; public float M12 = 0f; public float M13 = 0f;
    public float M20 = 0f; public float M21 = 0f; public float M22 = 1f; public float M23 = 0f;
    public float M30 = 0f; public float M31 = 0f; public float M32 = 0f; public float M33 = 1f;

    public override string Title    => "Matrix 4x4";
    public override string Category => "Constants";
    public override System.Drawing.Color AccentColor => PropNodeUtil.ConstantAccent;

    protected override void DefineNode() => AddOutput<Float4x4>("Out");

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        // GLSL mat4(c0r0, c0r1, c0r2, c0r3,  c1r0 ...) — column-major.
        // Our fields are row-major: M[row][col].
        // Column-major layout: for column c, row r → element index (c*4+r).
        var F = PropNodeUtil.F;
        return $"mat4(" +
               $"{F(M00)}, {F(M10)}, {F(M20)}, {F(M30)}, " +   // col 0
               $"{F(M01)}, {F(M11)}, {F(M21)}, {F(M31)}, " +   // col 1
               $"{F(M02)}, {F(M12)}, {F(M22)}, {F(M32)}, " +   // col 2
               $"{F(M03)}, {F(M13)}, {F(M23)}, {F(M33)})";     // col 3
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Mat4;
}

// ═════════════════════════════════════════════════════════════════════════════
// ── PROPERTY NODES ───────────────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Material-bindable float (scalar) property. Emits <c>uniform float _Name;</c>.
/// </summary>
public sealed class FloatPropertyNode : Node, IShaderGraphNode, IShaderProperty, IShaderNode
{
    public string Name  = "_Value";
    public string Label = "Value";
    public float  Value = 0.5f;
    public bool   ExposedToInspector = true;

    public override string Title    => $"Float \u00b7 {Label}";   // "Float · Label"
    public override string Category => "Properties";
    public override System.Drawing.Color AccentColor => PropNodeUtil.PropertyAccent;

    // ── IShaderProperty ────────────────────────────────────────────────────
    string     IShaderProperty.PropertyName  => PropNodeUtil.NormaliseName(Name);
    string     IShaderProperty.DisplayName   => string.IsNullOrEmpty(Label) ? Name : Label;
    bool       IShaderProperty.Exposed       => ExposedToInspector;
    ShaderType IShaderProperty.PropertyType  => ShaderType.Float;
    string     IShaderProperty.DefaultLiteral => PropNodeUtil.F(Value);

    // ── Node ───────────────────────────────────────────────────────────────
    protected override void DefineNode() => AddOutput<float>("Out");

    // ── IShaderNode ────────────────────────────────────────────────────────
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
        => PropNodeUtil.NormaliseName(Name);

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

/// <summary>
/// Material-bindable vec4 property with multi-channel outputs (XYZ, X, Y, Z, W).
/// Emits <c>uniform vec4 _Name;</c>.
/// </summary>
public sealed class Vector4PropertyNode : Node, IShaderGraphNode, IShaderProperty, IShaderNode
{
    public string Name  = "_Vector";
    public string Label = "Vector";
    public Float4 Value = new Float4(0.5f, 0.5f, 0.5f, 1f);
    public bool   ExposedToInspector = true;

    public override string Title    => $"Vector 4 \u00b7 {Label}";
    public override string Category => "Properties";
    public override System.Drawing.Color AccentColor => PropNodeUtil.PropertyAccent;

    // ── IShaderProperty ────────────────────────────────────────────────────
    string     IShaderProperty.PropertyName  => PropNodeUtil.NormaliseName(Name);
    string     IShaderProperty.DisplayName   => string.IsNullOrEmpty(Label) ? Name : Label;
    bool       IShaderProperty.Exposed       => ExposedToInspector;
    ShaderType IShaderProperty.PropertyType  => ShaderType.Vec4;
    string     IShaderProperty.DefaultLiteral
        => $"({PropNodeUtil.F(Value.X)}, {PropNodeUtil.F(Value.Y)}, {PropNodeUtil.F(Value.Z)}, {PropNodeUtil.F(Value.W)})";

    // ── Node ───────────────────────────────────────────────────────────────
    protected override void DefineNode()
    {
        AddOutput<Float4>("Vec4");
        AddOutput<Float3>("XYZ");
        AddOutput<float>("X");
        AddOutput<float>("Y");
        AddOutput<float>("Z");
        AddOutput<float>("W");
    }

    // ── IShaderNode ────────────────────────────────────────────────────────
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var name = PropNodeUtil.NormaliseName(Name);
        return p.Name switch
        {
            "X"   => $"{name}.x",
            "Y"   => $"{name}.y",
            "Z"   => $"{name}.z",
            "W"   => $"{name}.w",
            "XYZ" => $"{name}.xyz",
            _     => name,
        };
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => p.Name switch
    {
        "XYZ"                      => ShaderType.Vec3,
        "X" or "Y" or "Z" or "W"  => ShaderType.Float,
        _                          => ShaderType.Vec4,
    };
}

/// <summary>
/// Material-bindable float with min/max metadata for a slider UI. Implements
/// <see cref="IShaderPropertyRange"/> so the compiler emits
/// <c>Range(min, max)</c> instead of plain <c>Float</c>; the parser picks that up
/// and stores the bounds on the runtime <c>ShaderProperty</c> for the inspector.
/// </summary>
public sealed class SliderPropertyNode : Node, IShaderGraphNode, IShaderProperty, IShaderPropertyRange, IShaderNode
{
    public string Name  = "_Slider";
    public string Label = "Slider";
    public float  Min   = 0f;
    public float  Max   = 1f;
    public float  Value = 0f;
    public bool   ExposedToInspector = true;

    public override string Title    => $"Slider \u00b7 {Label}";
    public override string Category => "Properties";
    public override System.Drawing.Color AccentColor => PropNodeUtil.PropertyAccent;

    // ── IShaderProperty ────────────────────────────────────────────────────
    string     IShaderProperty.PropertyName  => PropNodeUtil.NormaliseName(Name);
    string     IShaderProperty.DisplayName   => string.IsNullOrEmpty(Label) ? Name : Label;
    bool       IShaderProperty.Exposed       => ExposedToInspector;
    ShaderType IShaderProperty.PropertyType  => ShaderType.Float;
    string     IShaderProperty.DefaultLiteral => PropNodeUtil.F(Value);

    // ── IShaderPropertyRange — compiler emits `Range(Min, Max)` as the type keyword ──
    float IShaderPropertyRange.RangeMin => Min;
    float IShaderPropertyRange.RangeMax => Max;

    // ── Node ───────────────────────────────────────────────────────────────
    protected override void DefineNode() => AddOutput<float>("Out");

    // ── IShaderNode ────────────────────────────────────────────────────────
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
        => PropNodeUtil.NormaliseName(Name);

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

/// <summary>
/// Material-bindable boolean toggle. Prowl uses a <c>Bool</c> property type;
/// the GLSL uniform is <c>bool _Name</c>. The output wire is a <c>float</c>
/// (0.0 or 1.0) rather than a bare GLSL bool, avoiding implicit-conversion pitfalls in expressions.
/// </summary>
public sealed class BoolPropertyNode : Node, IShaderGraphNode, IShaderProperty, IShaderNode
{
    public string Name  = "_Toggle";
    public string Label = "Toggle";
    public bool   Value = false;
    public bool   ExposedToInspector = true;

    public override string Title    => $"Bool \u00b7 {Label}";
    public override string Category => "Properties";
    public override System.Drawing.Color AccentColor => PropNodeUtil.PropertyAccent;

    // ── IShaderProperty ────────────────────────────────────────────────────
    string     IShaderProperty.PropertyName  => PropNodeUtil.NormaliseName(Name);
    string     IShaderProperty.DisplayName   => string.IsNullOrEmpty(Label) ? Name : Label;
    bool       IShaderProperty.Exposed       => ExposedToInspector;
    ShaderType IShaderProperty.PropertyType  => ShaderType.Bool;
    string     IShaderProperty.DefaultLiteral => Value ? "1" : "0";

    // ── Node ───────────────────────────────────────────────────────────────
    protected override void DefineNode() => AddOutput<float>("Out");

    // ── IShaderNode ────────────────────────────────────────────────────────
    // Emit float(uniform) so the output wire is a scalar 0/1, compatible with
    // all arithmetic downstream — avoids forcing explicit bool→float casts.
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
        => $"float({PropNodeUtil.NormaliseName(Name)})";

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Float;
}

/// <summary>
/// Compile-time switch: selects between two inputs A (Off) and B (On) based on a
/// material-bindable Bool uniform. Emits <c>mix(a, b, float(_Switch))</c> which
/// collapses to <c>a</c> or <c>b</c> depending on the uniform's value at draw time.
/// The output type is the wider of the two inputs (dynamic, via ShaderGenContext).
/// </summary>
public sealed class SwitchPropertyNode : Node, IShaderGraphNode, IShaderProperty, IShaderNode
{
    public string Name  = "_Switch";
    public string Label = "Switch";
    public bool   DefaultOn = false;
    public bool   ExposedToInspector = true;

    public override string Title    => $"Switch \u00b7 {Label}";
    public override string Category => "Properties";
    public override System.Drawing.Color AccentColor => PropNodeUtil.PropertyAccent;

    // ── IShaderProperty ────────────────────────────────────────────────────
    string     IShaderProperty.PropertyName  => PropNodeUtil.NormaliseName(Name);
    string     IShaderProperty.DisplayName   => string.IsNullOrEmpty(Label) ? Name : Label;
    bool       IShaderProperty.Exposed       => ExposedToInspector;
    ShaderType IShaderProperty.PropertyType  => ShaderType.Bool;
    string     IShaderProperty.DefaultLiteral => DefaultOn ? "1" : "0";

    // ── Node ───────────────────────────────────────────────────────────────
    protected override void DefineNode()
    {
        AddInput<float>("A", 0f, required: true, tooltip: "Value when switch is Off");
        AddInput<float>("B", 1f, required: true, tooltip: "Value when switch is On");
        AddOutput<float>("Out");
    }

    // ── IShaderNode ────────────────────────────────────────────────────────
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        // Resolve the dominant type across both inputs for type promotion.
        var inA  = GetInput("A")!;
        var inB  = GetInput("B")!;
        var typeA = ctx.GetSourceType(inA);
        var typeB = ctx.GetSourceType(inB);
        var unified = ShaderTypeUtil.Channels(typeA) >= ShaderTypeUtil.Channels(typeB) ? typeA : typeB;

        var exprA = ctx.EvaluateInputAs(inA, unified);
        var exprB = ctx.EvaluateInputAs(inB, unified);
        var sw    = PropNodeUtil.NormaliseName(Name);
        return $"mix({exprA}, {exprB}, float({sw}))";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
    {
        var inA = GetInput("A")!;
        var inB = GetInput("B")!;
        var typeA = ctx.GetSourceType(inA);
        var typeB = ctx.GetSourceType(inB);
        return ShaderTypeUtil.Channels(typeA) >= ShaderTypeUtil.Channels(typeB) ? typeA : typeB;
    }
}

/// <summary>
/// Material-bindable 4x4 matrix property. Emits <c>uniform mat4 _Name;</c>.
/// The Properties{} keyword is "Matrix" (added to ShaderTypeUtil.ToPropertyKeyword).
/// FLAG: Prowl's shader parser may not yet have a default-literal format for Matrix
/// properties. This node emits an empty default literal ("") which the compiler will
/// omit — investigate ShaderParser.ParseProperty to confirm or add support.
/// </summary>
public sealed class Matrix4x4PropertyNode : Node, IShaderGraphNode, IShaderProperty, IShaderNode
{
    public string Name  = "_Matrix";
    public string Label = "Matrix";
    public bool   ExposedToInspector = true;

    // 16 flat fields (row-major) — Echo only serialises public fields.
    public float M00 = 1f; public float M01 = 0f; public float M02 = 0f; public float M03 = 0f;
    public float M10 = 0f; public float M11 = 1f; public float M12 = 0f; public float M13 = 0f;
    public float M20 = 0f; public float M21 = 0f; public float M22 = 1f; public float M23 = 0f;
    public float M30 = 0f; public float M31 = 0f; public float M32 = 0f; public float M33 = 1f;

    public override string Title    => $"Matrix 4x4 \u00b7 {Label}";
    public override string Category => "Properties";
    public override System.Drawing.Color AccentColor => PropNodeUtil.PropertyAccent;

    // ── IShaderProperty ────────────────────────────────────────────────────
    string     IShaderProperty.PropertyName  => PropNodeUtil.NormaliseName(Name);
    string     IShaderProperty.DisplayName   => string.IsNullOrEmpty(Label) ? Name : Label;
    bool       IShaderProperty.Exposed       => ExposedToInspector;
    ShaderType IShaderProperty.PropertyType  => ShaderType.Mat4;
    // Matrix properties have no parseable default syntax — emit empty to let the
    // compiler skip the `= default` clause. ShaderParser then initialises the
    // runtime value to Float4x4.Identity, matching the authored Value field's own
    // default if the user hasn't changed it from C# at runtime.
    string     IShaderProperty.DefaultLiteral => "";

    // ── Node ───────────────────────────────────────────────────────────────
    protected override void DefineNode() => AddOutput<Float4x4>("Out");

    // ── IShaderNode ────────────────────────────────────────────────────────
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
        => PropNodeUtil.NormaliseName(Name);

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Mat4;
}

/// <summary>
/// Material-bindable colour property. Emits <c>uniform vec4 _Name;</c> with keyword
/// "Color" so the material inspector shows a colour picker.
/// See ColorConstantNode for the non-material-bound variant.
/// </summary>
public sealed class ColorPropertyNode : Node, IShaderGraphNode, IShaderProperty, IShaderNode
{
    public string Name  = "_Color";
    public string Label = "Color";
    public Color  Value = new Color(1f, 1f, 1f, 1f);
    public bool   ExposedToInspector = true;

    public override string Title    => $"Color \u00b7 {Label}";
    public override string Category => "Properties";
    public override System.Drawing.Color AccentColor => PropNodeUtil.PropertyAccent;

    // ── IShaderProperty ────────────────────────────────────────────────────
    string     IShaderProperty.PropertyName  => PropNodeUtil.NormaliseName(Name);
    string     IShaderProperty.DisplayName   => string.IsNullOrEmpty(Label) ? Name : Label;
    bool       IShaderProperty.Exposed       => ExposedToInspector;
    ShaderType IShaderProperty.PropertyType  => ShaderType.Color;
    string     IShaderProperty.DefaultLiteral
        => $"({PropNodeUtil.F(Value.R)}, {PropNodeUtil.F(Value.G)}, {PropNodeUtil.F(Value.B)}, {PropNodeUtil.F(Value.A)})";

    // ── Node ───────────────────────────────────────────────────────────────
    protected override void DefineNode()
    {
        AddOutput<Color>("RGBA");
        AddOutput<Float3>("RGB");
        AddOutput<float>("R");
        AddOutput<float>("G");
        AddOutput<float>("B");
        AddOutput<float>("A");
    }

    // ── IShaderNode ────────────────────────────────────────────────────────
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var name = PropNodeUtil.NormaliseName(Name);
        return p.Name switch
        {
            "RGB"  => $"{name}.rgb",
            "R"    => $"{name}.r",
            "G"    => $"{name}.g",
            "B"    => $"{name}.b",
            "A"    => $"{name}.a",
            _      => name,   // "RGBA" or default → full vec4
        };
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => p.Name switch
    {
        "RGB"                        => ShaderType.Vec3,
        "R" or "G" or "B" or "A"    => ShaderType.Float,
        _                            => ShaderType.Color,
    };
}

// ═════════════════════════════════════════════════════════════════════════════
// ── TEXTURE / SAMPLER NODES ──────────────────────────────────────────────────
// ═════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Material-bindable Texture2D property (sampler2D uniform). By itself this node
/// emits only the uniform declaration and the Properties{} entry; it does not sample.
/// Wire its output into a <see cref="Tex2DSampleNode"/> to sample pixels.
/// </summary>
public sealed class Texture2DPropertyNode : Node, IShaderGraphNode, IShaderProperty, IShaderNode
{
    public string Name    = "_MainTex";
    public string Label   = "Main Texture";
    public string Default = "white";    // "white" | "black" | "gray" | "bump"
    public bool   ExposedToInspector = true;

    public override string Title    => $"Texture 2D \u00b7 {Label}";
    public override string Category => "Properties";
    public override System.Drawing.Color AccentColor => PropNodeUtil.PropertyAccent;

    // ── IShaderProperty ────────────────────────────────────────────────────
    string     IShaderProperty.PropertyName  => PropNodeUtil.NormaliseName(Name);
    string     IShaderProperty.DisplayName   => string.IsNullOrEmpty(Label) ? Name : Label;
    bool       IShaderProperty.Exposed       => ExposedToInspector;
    ShaderType IShaderProperty.PropertyType  => ShaderType.Sampler2D;
    string     IShaderProperty.DefaultLiteral => $"\"{Default}\"";

    // ── Node ───────────────────────────────────────────────────────────────
    protected override void DefineNode()
        => AddOutput<Resources.Texture2D>("Sampler",
               tooltip: "Pass to a Tex2DSampleNode to sample pixels.");

    // ── IShaderNode ────────────────────────────────────────────────────────
    // The output of a property texture node is just the uniform name — the
    // Tex2DSampleNode calls texture(name, uv) around it.
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
        => PropNodeUtil.NormaliseName(Name);

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Sampler2D;
}

/// <summary>
/// Non-inspector Texture2D (sampler2D uniform still emitted but hidden from the
/// material inspector). Useful for graph-internal textures set from C# via
/// <c>Material.SetTexture</c>.
/// </summary>
public sealed class Texture2DAssetNode : Node, IShaderGraphNode, IShaderProperty, IShaderNode
{
    public string Name    = "_Tex";
    public string Label   = "Texture";
    public string Default = "white";

    public override string Title    => $"Texture 2D Asset \u00b7 {Label}";
    public override string Category => "Properties";
    public override System.Drawing.Color AccentColor => PropNodeUtil.PropertyAccent;

    // ── IShaderProperty — not exposed to the inspector ─────────────────────
    string     IShaderProperty.PropertyName  => PropNodeUtil.NormaliseName(Name);
    string     IShaderProperty.DisplayName   => string.IsNullOrEmpty(Label) ? Name : Label;
    bool       IShaderProperty.Exposed       => false;   // hidden from material inspector
    ShaderType IShaderProperty.PropertyType  => ShaderType.Sampler2D;
    string     IShaderProperty.DefaultLiteral => $"\"{Default}\"";

    // ── Node ───────────────────────────────────────────────────────────────
    protected override void DefineNode()
        => AddOutput<Resources.Texture2D>("Sampler",
               tooltip: "Pass to a Tex2DSampleNode to sample pixels.");

    // ── IShaderNode ────────────────────────────────────────────────────────
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
        => PropNodeUtil.NormaliseName(Name);

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Sampler2D;
}

/// <summary>
/// Samples a Texture2D given a sampler and UV. When UV is unconnected the node
/// falls back to the vertex interpolated <c>texCoord0</c> varying (pushed to the
/// context's Varyings set). Supports an optional MIP bias input.
/// Outputs: RGBA (full vec4), RGB (vec3), and individual R/G/B/A channels.
/// </summary>
public sealed class Tex2DSampleNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title    => "Tex2D Sample";
    public override string Category => "Texture";
    public override System.Drawing.Color AccentColor =>
        System.Drawing.Color.FromArgb(255, 60, 170, 130);

    protected override void DefineNode()
    {
        AddInput<Resources.Texture2D>("Sampler", required: true,
            tooltip: "Connect a Texture2DPropertyNode or Texture2DAssetNode.");
        AddInput<Float2>("UV", Float2.Zero,
            tooltip: "Texture coordinates. Defaults to texCoord0 when unconnected.");
        AddInput<float>("MIP", 0f,
            tooltip: "Optional mip-level bias. Triggers textureLod when connected.");

        AddOutput<Color>("RGBA");
        AddOutput<Float3>("RGB");
        AddOutput<float>("R");
        AddOutput<float>("G");
        AddOutput<float>("B");
        AddOutput<float>("A");
    }

    // A stable per-node temp name: derived from the node's stable Guid so that
    // every output port of the same node reuses the same vec4 temp variable.
    // We use a short hex suffix (8 chars) to keep generated names readable.
    private string SampleTempName()
        => "_tex2d_" + Id.ToString("N").Substring(0, 8);

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var samplerPort = GetInput("Sampler")!;
        var uvPort      = GetInput("UV")!;
        var mipPort     = GetInput("MIP")!;

        // ── Stable temp name ─────────────────────────────────────────────
        // Use a node-ID-based name so every output port of this node shares
        // the same vec4 temp and the texture() call is emitted exactly once.
        var tmp = SampleTempName();

        // Emit the texture() / textureLod() call exactly once per compile, no matter
        // how many output ports are hooked up. EmitOnce keys the dedup on a stable name.
        ctx.EmitOnce("tex2d:" + tmp, () =>
        {
            var samplerExpr = ctx.EvaluateInput(samplerPort);

            string uvExpr;
            if (ctx.IsConnected(uvPort))
                uvExpr = ctx.EvaluateInput(uvPort);
            else
            {
                // Fall back to texCoord0 interpolated from the vertex stage.
                ctx.Varyings.Add(("texCoord0", "vec2"));
                uvExpr = "texCoord0";
            }

            string sampleExpr = ctx.IsConnected(mipPort)
                ? $"textureLod({samplerExpr}, {uvExpr}, {ctx.EvaluateInput(mipPort)})"
                : $"texture({samplerExpr}, {uvExpr})";

            ctx.BodyPrelude.AppendLine($"    vec4 {tmp} = {sampleExpr};");
        });

        // ── Per-output swizzle ────────────────────────────────────────────
        return p.Name switch
        {
            "RGB" => $"{tmp}.rgb",
            "R"   => $"{tmp}.r",
            "G"   => $"{tmp}.g",
            "B"   => $"{tmp}.b",
            "A"   => $"{tmp}.a",
            _     => tmp,   // "RGBA"
        };
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => p.Name switch
    {
        "RGB"                        => ShaderType.Vec3,
        "R" or "G" or "B" or "A"    => ShaderType.Float,
        _                            => ShaderType.Color,   // "RGBA"
    };
}

/// <summary>
/// Material-bindable cubemap property and sampler combined. Emits a
/// <c>uniform samplerCube _Name;</c> (mapped to Prowl's Texture3D C# type for the
/// closest cubemap stand-in). Samples via <c>texture(sampler, dir)</c> or
/// <c>textureLod(sampler, dir, mip)</c> when MIP is connected.
/// Outputs: RGB (vec3), R, G, B, A (float).
/// </summary>
public sealed class CubemapSampleNode : Node, IShaderGraphNode, IShaderProperty, IShaderNode
{
    public string Name    = "_Cubemap";
    public string Label   = "Cubemap";
    public string Default = "white";
    public bool   ExposedToInspector = true;

    public override string Title    => $"Cubemap \u00b7 {Label}";
    public override string Category => "Texture";
    public override System.Drawing.Color AccentColor =>
        System.Drawing.Color.FromArgb(255, 60, 170, 130);

    // ── IShaderProperty ────────────────────────────────────────────────────
    string     IShaderProperty.PropertyName  => PropNodeUtil.NormaliseName(Name);
    string     IShaderProperty.DisplayName   => string.IsNullOrEmpty(Label) ? Name : Label;
    bool       IShaderProperty.Exposed       => ExposedToInspector;
    ShaderType IShaderProperty.PropertyType  => ShaderType.SamplerCube;
    string     IShaderProperty.DefaultLiteral => $"\"{Default}\"";

    // ── Node ───────────────────────────────────────────────────────────────
    protected override void DefineNode()
    {
        AddInput<Float3>("DIR", Float3.Zero,
            tooltip: "Sample direction (world-space). Defaults to vec3(0,0,1) when unconnected.");
        AddInput<float>("MIP", 0f,
            tooltip: "Optional mip level. Triggers textureLod when connected.");

        AddOutput<Float3>("RGB");
        AddOutput<float>("R");
        AddOutput<float>("G");
        AddOutput<float>("B");
        AddOutput<float>("A");
    }

    private string SampleTempName()
        => "_cube_" + Id.ToString("N").Substring(0, 8);

    // ── IShaderNode ────────────────────────────────────────────────────────
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        var dirPort = GetInput("DIR")!;
        var mipPort = GetInput("MIP")!;

        var uniformName = PropNodeUtil.NormaliseName(Name);

        // The uniform is declared by CollectProperties for exposed properties,
        // but also push it here so non-exposed variants still work.
        ctx.Uniforms.Add($"uniform samplerCube {uniformName};");

        var tmp = SampleTempName();
        ctx.EmitOnce("cube:" + tmp, () =>
        {
            var dirExpr = ctx.IsConnected(dirPort)
                ? ctx.EvaluateInput(dirPort)
                : "vec3(0.0, 0.0, 1.0)";

            // GLSL texture / textureLod on samplerCube.
            string sampleExpr = ctx.IsConnected(mipPort)
                ? $"textureLod({uniformName}, {dirExpr}, {ctx.EvaluateInput(mipPort)})"
                : $"texture({uniformName}, {dirExpr})";

            ctx.BodyPrelude.AppendLine($"    vec4 {tmp} = {sampleExpr};");
        });

        return p.Name switch
        {
            "R" => $"{tmp}.r",
            "G" => $"{tmp}.g",
            "B" => $"{tmp}.b",
            "A" => $"{tmp}.a",
            _   => $"{tmp}.rgb",   // "RGB"
        };
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => p.Name switch
    {
        "R" or "G" or "B" or "A" => ShaderType.Float,
        _                         => ShaderType.Vec3,   // "RGB"
    };
}
