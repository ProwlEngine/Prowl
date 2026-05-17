// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;


namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

// -----------------------------------------------------------------------------
// Shared accent colour for all UV-operation nodes
// -----------------------------------------------------------------------------

internal static class UVAccents
{
    /// <summary>Lime green visually distinct from math (gold) and trig (teal).</summary>
    public static readonly System.Drawing.Color UV = System.Drawing.Color.FromArgb(255, 180, 220, 100);
}

// =============================================================================
// PANNER
// Standard panning implementation for UV scrolling.
//
// Original formula (Evaluate):
//   output = uv + dist * float2(speedU, speedV)
// where "Dist" defaults to _Time.y (i.e. elapsed seconds) when unconnected.
//
// Prowl GLSL mapping:
//   output = uv + dist * vec2(speedU, speedV)
// _Time.y == elapsed seconds in Prowl's GlobalUniforms (ShaderVariables.glsl).
// =============================================================================

/// <summary>
/// Scrolls UV coordinates over time (or by a supplied distance scalar).
/// </summary>
/// <remarks>
/// Speed is a per-node field (U/V float pair) controlling scroll rate.
/// The Distance input accepts any scalar; when unconnected it falls back to
/// _Time.y so the surface animates automatically.
/// UV defaults to texCoord0 when unconnected, providing a sensible fallback.
/// </remarks>
public sealed class PannerNode : Node, IShaderNode, IShaderGraphNode
{
    // -- Per-node settings (serialised by Echo as public fields) --------------

    /// <summary>Scroll speed along the U axis (world-time units per second).</summary>
    public float SpeedU = 1f;

    /// <summary>Scroll speed along the V axis (world-time units per second).</summary>
    public float SpeedV = 0f;

    // -- Node identity ---------------------------------------------------------

    public override string Title => "Panner";
    public override string Category => "UV";
    public override System.Drawing.Color AccentColor => UVAccents.UV;

    // -- Port layout -----------------------------------------------------------

    protected override void DefineNode()
    {
        AddInput<Float2>("UV",   Float2.Zero);   // optional defaults to texCoord0
        AddInput<float>("Dist",  0f);            // optional defaults to _Time.y
        AddOutput<Float2>("Out");
    }

    // -- GLSL emission ---------------------------------------------------------

    string IShaderNode.Evaluate(Port p, ShaderStage stage, ShaderGenContext ctx)
    {
        // ShaderVariables.glsl declares vec4 _Time inside GlobalUniforms.
        // Fragment.glsl already includes ShaderVariables, but we push it
        // explicitly so standalone vertex-only graphs also compile.
        ctx.Includes.Add("ShaderVariables");

        // UV input: fall back to texCoord0 varying when nothing is wired,
        // providing a sensible default when unconnected.
        string uv;
        if (ctx.IsConnected(GetInput("UV")!))
        {
            uv = ctx.EvaluateInputAs(GetInput("UV")!, ShaderType.Vec2);
        }
        else
        {
            uv = "texCoord0";
            ctx.Varyings.Add(("texCoord0", "vec2"));
        }

        // Dist input: fall back to _Time.y (elapsed seconds) when nothing is wired,
        // giving automatic animation when unconnected.
        string dist;
        if (ctx.IsConnected(GetInput("Dist")!))
            dist = ctx.EvaluateInputAs(GetInput("Dist")!, ShaderType.Float);
        else
            dist = "_Time.y";

        // Formula: uv + dist * float2(speedU, speedV)
        string su = ShaderGenContext.Fmt(SpeedU);
        string sv = ShaderGenContext.Fmt(SpeedV);
        return $"({uv} + {dist} * vec2({su}, {sv}))";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec2;
}

// =============================================================================
// PARALLAX
// Cheap offset parallax (per-fragment UV shift). For true silhouette-preserving
// parallax, use ParallaxOcclusionNode below.
//
// Formula: output = depth * (height - refHeight) * viewDir.xy + uv
// The tangent-space view direction is computed automatically from the vertex
// varyings via Lighting.glsl's GetTangentViewDir helper the user doesn't have
// to build the TBN themselves.
// =============================================================================

/// <summary>
/// Offsets UV coordinates by a height-driven amount along the tangent-space view
/// direction produces cheap faux-depth on flat surfaces. Uses Prowl's
/// <c>GetTangentViewDir</c> helper so no manual TBN construction is needed.
/// For a real ray-marched parallax use <see cref="ParallaxOcclusionNode"/>.
/// </summary>
public sealed class ParallaxNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Parallax";
    public override string Category => "UV";
    public override System.Drawing.Color AccentColor => UVAccents.UV;

    protected override void DefineNode()
    {
        AddInput<Float2>("UV",       Float2.Zero); // optional defaults to texCoord0
        AddInput<float>("Height",    0f,  required: true);
        AddInput<float>("Depth",     0.05f);       // displacement amount
        AddInput<float>("RefHeight", 0.5f);        // reference height (subtract, so height=0.5 means no shift)
        AddOutput<Float2>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage stage, ShaderGenContext ctx)
    {
        // GetTangentViewDir reads worldPos / vNormal / vTangent / vBitangent
        // varyings populated by the fragment stage. Vertex use returns the raw
        // UV unmodified (no view direction available there).
        if (ctx.RequireFragmentStage(Id, Title))
        {
            return ctx.IsConnected(GetInput("UV")!)
                ? ctx.EvaluateInputAs(GetInput("UV")!, ShaderType.Vec2)
                : "vec2(0.0)";
        }

        ctx.Includes.Add("Lighting");
        ctx.Varyings.Add(("worldPos",   "vec3"));
        ctx.Varyings.Add(("vNormal",    "vec3"));
        ctx.Varyings.Add(("vTangent",   "vec3"));
        ctx.Varyings.Add(("vBitangent", "vec3"));

        // UV: fall back to texCoord0 when unconnected. The varying name matches
        // VertexAttributes.glsl's standard UV0 emission; if that convention ever
        // changes, this fallback (and every UV-defaulting node) needs to follow.
        string uv;
        if (ctx.IsConnected(GetInput("UV")!))
            uv = ctx.EvaluateInputAs(GetInput("UV")!, ShaderType.Vec2);
        else
        {
            uv = "texCoord0";
            ctx.Varyings.Add(("texCoord0", "vec2"));
        }

        string hei  = ctx.EvaluateInputAs(GetInput("Height")!,    ShaderType.Float);
        string dep  = ctx.EvaluateInputAs(GetInput("Depth")!,     ShaderType.Float);
        string refH = ctx.EvaluateInputAs(GetInput("RefHeight")!, ShaderType.Float);

        return $"({dep} * ({hei} - {refH}) * GetTangentViewDir(worldPos, vNormal, vTangent, vBitangent).xy + {uv})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec2;
}

// =============================================================================
// ROTATOR
// Standard UV rotation implementation.
//
// Original formula (Evaluate):
//   ang  = Ang input (defaults to _Time.y)
//   spd  = Spd input (defaults to 1.0)
//   piv  = Piv input (defaults to float2(0.5, 0.5))
//   cosV = cos(spd * ang)
//   sinV = sin(spd * ang)
//   output = mul(uv - piv, float2x2(cosV,-sinV, sinV,cosV)) + piv
//
// GLSL mat2 constructor is column-major, so the rotation matrix
//   [ cos  -sin ]
//   [ sin   cos ]
// becomes mat2(cos, sin, -sin, cos) in GLSL.
// Equivalently we can write:
//   mat2(cosV, sinV, -sinV, cosV) * (uv - piv) + piv
// (matrix * column-vector is the GLSL idiom).
// =============================================================================

/// <summary>
/// Rotates UV coordinates around a pivot point by a given angle (in radians).
/// </summary>
/// <remarks>
/// When Angle is unconnected the node defaults to _Time.y, giving automatic
/// rotation when unconnected.
/// Speed scales the angle, defaulting to 1.0.
/// Pivot defaults to (0.5, 0.5) the centre of a normalised UV tile.
///
/// GLSL note: mat2 is stored column-major. The rotation is expressed as a
/// left-multiply of a column-vector: mat2(c,s,-s,c) * v.
/// </remarks>
public sealed class RotatorNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Rotator";
    public override string Category => "UV";
    public override System.Drawing.Color AccentColor => UVAccents.UV;

    protected override void DefineNode()
    {
        AddInput<Float2>("UV",    Float2.Zero,               required: true);
        AddInput<Float2>("Pivot", new Float2(0.5f, 0.5f));  // default pivot at center
        AddInput<float>("Angle",  0f);                       // optional defaults to _Time.y
        AddInput<float>("Speed",  1f);                       // default speed 1.0
        AddOutput<Float2>("Out");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage stage, ShaderGenContext ctx)
    {
        ctx.Includes.Add("ShaderVariables");

        string uv  = ctx.EvaluateInputAs(GetInput("UV")!,    ShaderType.Vec2);
        string piv = ctx.EvaluateInputAs(GetInput("Pivot")!, ShaderType.Vec2);
        string spd = ctx.EvaluateInputAs(GetInput("Speed")!, ShaderType.Float);

        // Angle defaults to _Time.y for automatic rotation.
        string ang;
        if (ctx.IsConnected(GetInput("Angle")!))
            ang = ctx.EvaluateInputAs(GetInput("Angle")!, ShaderType.Float);
        else
            ang = "_Time.y";

        // Emit angle and trig into temp vars to avoid duplicating the sin/cos
        // computation when the expression is referenced more than once downstream.
        string localAng = ctx.FreshLocal("_rotAng");
        string localSpd = ctx.FreshLocal("_rotSpd");
        string localC   = ctx.FreshLocal("_rotC");
        string localS   = ctx.FreshLocal("_rotS");

        ctx.BodyPrelude.AppendLine($"float {localSpd} = {spd};");
        ctx.BodyPrelude.AppendLine($"float {localAng} = {localSpd} * {ang};");
        ctx.BodyPrelude.AppendLine($"float {localC} = cos({localAng});");
        ctx.BodyPrelude.AppendLine($"float {localS} = sin({localAng});");

        // GLSL mat2 is column-major: mat2(col0.x, col0.y, col1.x, col1.y)
        // Rotation matrix (column-major):
        //   col0 = (cos,  sin)
        //   col1 = (-sin, cos)
        // So: mat2(c, s, -s, c) * v  ==  rotate v by angle
        string mat = $"mat2({localC}, {localS}, -{localS}, {localC})";
        return $"({mat} * ({uv} - {piv}) + {piv})";
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec2;
}

// =============================================================================
// UV TILE
// Standard UV tiling for sprite sheets and flipbooks.
//
// Original formula (GetPreDefineRows + Evaluate):
//   tileCountRecip = float2(1,1) / float2(width, height)
//   tileY          = floor(tile * tileCountRecip.x)          // row index
//   tileX          = tile - width * tileY                    // column index
//   output         = (uv + float2(tileX, tileY)) * tileCountRecip
//
// The Tile index is 0-based, row-major (left-to-right then top-to-bottom)
// matching a sprite-sheet layout.
// =============================================================================

/// <summary>
/// Selects a single cell from a sprite-sheet / flipbook texture by remapping
/// UV coordinates to the cell at a given 0-based tile index. Doubles as a
/// time-driven flipbook when the optional <c>Time</c> input is wired the node
/// then drives the index from <c>Time * FrameRate</c> and exposes a
/// <c>NextOut</c> + <c>Blend</c> pair so downstream samplers can cross-fade.
/// </summary>
/// <remarks>
/// Tiles are laid out row-major (left to right, top to bottom) and indexed
/// from 0. <c>Width</c> / <c>Height</c> are columns / rows in the sheet. <c>UV</c>
/// defaults to <c>texCoord0</c> when unconnected.
///
/// <para><b>Static use</b> (<c>Time</c> unconnected): the explicit <c>Tile</c>
/// input picks the cell. <c>NextOut</c> mirrors <c>Out</c> and <c>Blend</c> is 0,
/// so a downstream cross-fade collapses to a no-op.</para>
///
/// <para><b>Animated use</b> (<c>Time</c> connected, e.g. wire a Time node into
/// it): the index becomes <c>Time * FrameRate</c>, optionally wrapped via
/// <c>Loop</c>. <c>Blend</c> is the fractional progress to the next frame, and
/// <c>NextOut</c> is the UV for that next frame so:
/// <code>color = mix(texture(t, Out), texture(t, NextOut), Blend);</code>
/// gives smooth flipbook cross-fades.</para>
/// </remarks>
public sealed class UVTileNode : Node, IShaderNode, IShaderGraphNode
{
    /// <summary>Frames per second when <c>Time</c> is wired. Ignored otherwise.</summary>
    public float FrameRate = 12f;

    /// <summary>When true (default) the time-driven index wraps via <c>mod</c> at
    /// <c>Width*Height</c>; when false it clamps at the last frame.</summary>
    public bool Loop = true;

    public override string Title => "UV Tile";
    public override string Category => "UV";
    public override System.Drawing.Color AccentColor => UVAccents.UV;

    protected override void DefineNode()
    {
        AddInput<Float2>("UV",     Float2.Zero); // optional defaults to texCoord0
        AddInput<float>("Width",   4f, required: true);   // number of columns
        AddInput<float>("Height",  4f, required: true);   // number of rows
        AddInput<float>("Tile",    0f, required: true,
            tooltip: "Static tile index. Ignored when Time is wired.");
        AddInput<float>("Time",    0f,
            tooltip: "When connected, drives the tile index via Time * FrameRate. Wire _Time.y for auto-play.");
        AddOutput<Float2>("Out",
            tooltip: "UV for the current frame.");
        AddOutput<Float2>("NextOut",
            tooltip: "UV for the next frame. Equals Out when Time is unwired.");
        AddOutput<float>("Blend",
            tooltip: "Fractional progress to NextOut [0, 1). Always 0 when Time is unwired.");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage stage, ShaderGenContext ctx)
    {
        // All three outputs share the same tile-math preamble. Emit-once keyed on
        // the node id so wiring multiple outputs doesn't duplicate the temps.
        string baseUV   = $"_uvt_uv_{Id:N}";
        string recip    = $"_uvt_r_{Id:N}";
        string total    = $"_uvt_n_{Id:N}";
        string idx      = $"_uvt_i_{Id:N}";
        string nextIdx  = $"_uvt_in_{Id:N}";
        string blend    = $"_uvt_b_{Id:N}";

        ctx.EmitOnce("uvtile:" + idx, () =>
        {
            // UV fallback to texCoord0 when nothing wired.
            string uv;
            if (ctx.IsConnected(GetInput("UV")!))
                uv = ctx.EvaluateInputAs(GetInput("UV")!, ShaderType.Vec2);
            else
            {
                uv = "texCoord0";
                ctx.Varyings.Add(("texCoord0", "vec2"));
            }

            string wdt  = ctx.EvaluateInputAs(GetInput("Width")!,  ShaderType.Float);
            string hgt  = ctx.EvaluateInputAs(GetInput("Height")!, ShaderType.Float);

            ctx.BodyPrelude.AppendLine($"    vec2 {baseUV} = {uv};");
            ctx.BodyPrelude.AppendLine($"    vec2 {recip}  = vec2(1.0) / vec2({wdt}, {hgt});");
            ctx.BodyPrelude.AppendLine($"    float {total} = {wdt} * {hgt};");

            if (ctx.IsConnected(GetInput("Time")!))
            {
                // Time-driven: index = Time * FrameRate, optionally wrapped.
                // Frame's fractional part is the cross-fade alpha to the next index.
                string timeExpr = ctx.EvaluateInputAs(GetInput("Time")!, ShaderType.Float);
                string fr = ShaderGenContext.Fmt(FrameRate);
                string frameLocal = $"_uvt_f_{Id:N}";
                ctx.BodyPrelude.AppendLine($"    float {frameLocal} = {timeExpr} * {fr};");

                if (Loop)
                {
                    ctx.BodyPrelude.AppendLine($"    float {idx}     = mod(floor({frameLocal}),       {total});");
                    ctx.BodyPrelude.AppendLine($"    float {nextIdx} = mod(floor({frameLocal}) + 1.0, {total});");
                }
                else
                {
                    // Clamp to the last valid index and freeze blend at 0 once we
                    // hit the end so the surface doesn't snap back to frame 0.
                    ctx.BodyPrelude.AppendLine($"    float {idx}     = clamp(floor({frameLocal}),       0.0, {total} - 1.0);");
                    ctx.BodyPrelude.AppendLine($"    float {nextIdx} = clamp(floor({frameLocal}) + 1.0, 0.0, {total} - 1.0);");
                }
                ctx.BodyPrelude.AppendLine($"    float {blend} = fract({frameLocal}) * step({idx} + 0.5, {nextIdx});");
            }
            else
            {
                // Static: the user-supplied Tile picks one frame. NextOut mirrors
                // Out and Blend = 0 so any downstream cross-fade collapses cleanly.
                string tile = ctx.EvaluateInputAs(GetInput("Tile")!, ShaderType.Float);
                ctx.BodyPrelude.AppendLine($"    float {idx}     = {tile};");
                ctx.BodyPrelude.AppendLine($"    float {nextIdx} = {tile};");
                ctx.BodyPrelude.AppendLine($"    float {blend}   = 0.0;");
            }
        });

        // Per-output expression: convert the linear index back to (col, row) and
        // remap the UV into that cell. Matches the original formulation exactly:
        //   tileY = floor(idx / width); tileX = idx - width * tileY
        //   out   = (uv + vec2(tileX, tileY)) * recip
        string CellExpr(string cellIdx)
        {
            string wdt = $"(1.0 / {recip}.x)"; // width recovered from recip; avoids re-eval of Width.
            return $"(({baseUV} + vec2({cellIdx} - {wdt} * floor({cellIdx} * {recip}.x), floor({cellIdx} * {recip}.x))) * {recip})";
        }

        return p.Name switch
        {
            "NextOut" => CellExpr(nextIdx),
            "Blend"   => blend,
            _         => CellExpr(idx),
        };
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => p.Name switch
    {
        "Blend" => ShaderType.Float,
        _       => ShaderType.Vec2,
    };
}

// =============================================================================
// PARALLAX OCCLUSION MAPPING
// Real POM using Prowl's ParallaxOcclusionMapping helper (PBR.glsl) ray-march
// + secant refinement. Samples a height texture along the tangent-space view
// direction and returns the displaced UV. Much more expensive but far more
// accurate than the simple ParallaxNode above use this when you need
// silhouette-preserving depth on close-up surfaces.
// =============================================================================

/// <summary>
/// Height-texture-driven parallax occlusion mapping. Returns the displaced UV
/// you should feed into downstream texture samplers (albedo, normal, etc.).
/// Height is sampled from the Height Texture's G channel.
/// </summary>
/// <remarks>
/// Builds the tangent-space view direction via the compiler's shared TBN helper
/// (same basis the PBR path uses) so results stay consistent with Prowl's
/// built-in surface shader. Steps is an integer more steps = crisper silhouette,
/// linearly more expensive. 16 is a good baseline.
/// </remarks>
public sealed class ParallaxOcclusionNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Parallax Occlusion";
    public override string Category => "UV";
    public override System.Drawing.Color AccentColor => UVAccents.UV;

    protected override void DefineNode()
    {
        AddInput<Resources.Texture2D>("Height Tex", required: true,
            tooltip: "Height map sampled on the G channel.");
        AddInput<Prowl.Vector.Float2>("UV", Prowl.Vector.Float2.Zero,
            tooltip: "Defaults to texCoord0 when unconnected.");
        AddInput<float>("Scale", 0.05f,
            tooltip: "Height scale how deep the displacement goes.");
        AddInput<int>("Steps", 16,
            tooltip: "Linear ray-march step count. 8 = cheap/blocky, 32 = crisp.");
        AddOutput<Prowl.Vector.Float2>("Out",
            tooltip: "Displaced UV. Feed into your sampler nodes' UV inputs.");
    }

    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        // Lighting.glsl exposes GetTangentViewDir which builds the tangent-space view
        // direction from the vertex varyings matches what the built-in Standard
        // shader feeds to ParallaxOcclusionMapping.
        ctx.Includes.Add("PBR");
        ctx.Includes.Add("Lighting");
        ctx.Varyings.Add(("worldPos",   "vec3"));
        ctx.Varyings.Add(("vNormal",    "vec3"));
        ctx.Varyings.Add(("vTangent",   "vec3"));
        ctx.Varyings.Add(("vBitangent", "vec3"));

        // UV falls back to texCoord0 when unconnected.
        string uv;
        if (ctx.IsConnected(GetInput("UV")!))
            uv = ctx.EvaluateInputAs(GetInput("UV")!, ShaderType.Vec2);
        else
        {
            uv = "texCoord0";
            ctx.Varyings.Add(("texCoord0", "vec2"));
        }

        var heightTex = ctx.EvaluateInput(GetInput("Height Tex")!);
        var scale     = ctx.EvaluateInputAs(GetInput("Scale")!, ShaderType.Float);
        var steps     = ctx.EvaluateInputAs(GetInput("Steps")!, ShaderType.Int);

        // Cache the displaced UV once a graph typically feeds this into
        // several sampler UV pins and we only want one ray-march per fragment.
        var local = $"_pomUV{Id:N}";
        ctx.EmitOnce("pom:" + local, () =>
        {
            ctx.BodyPrelude.AppendLine(
                $"    vec2 {local} = ParallaxOcclusionMapping({heightTex}, {uv}, GetTangentViewDir(worldPos, vNormal, vTangent, vBitangent), {scale}, {steps});");
        });
        return local;
    }

    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => ShaderType.Vec2;
}
