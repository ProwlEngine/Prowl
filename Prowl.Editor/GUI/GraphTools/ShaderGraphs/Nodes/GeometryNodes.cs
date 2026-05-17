// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

namespace Prowl.Runtime.GraphTools.ShaderGraphs.Nodes;

// -----------------------------------------------------------------------------
// Color accent for all geometry data nodes
// -----------------------------------------------------------------------------

internal static class GeometryAccents
{
    public static readonly System.Drawing.Color Geometry = System.Drawing.Color.FromArgb(255, 140, 110, 60); /* dark amber */
}

// =============================================================================
// TexCoordNode
// Outputs the mesh UV coordinates. Channel selects UV0 or UV1.
// Only UV0 is a compiler-built varying (texCoord0); UV1 is passed through as
// vertexTexCoord1 from VertexAttributes.glsl. Both are vec2.
// =============================================================================

public sealed class TexCoordNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "UV Coord";
    public override string Category => "Geometry";
    public override System.Drawing.Color AccentColor => GeometryAccents.Geometry;

    /// <summary>Which UV channel to sample 0 = primary, 1 = secondary.</summary>
    public int Channel = 0; // Echo requires public fields for persistence

    protected override void DefineNode()
    {
        AddOutput<Float2>("UV");
        AddOutput<float>("U");
        AddOutput<float>("V");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        // In vertex stage, read the raw vertex attribute directly the varying
        // hasn't been written yet. In fragment, read the interpolated varying.
        string src;
        if (stage == ShaderStage.Vertex)
        {
            ctx.Includes.Add("VertexAttributes");
            src = Channel == 0 ? "vertexTexCoord0" : "vertexTexCoord1";
        }
        else if (Channel == 0)
        {
            ctx.Varyings.Add(("texCoord0", "vec2"));
            src = "texCoord0";
        }
        else
        {
            ctx.Varyings.Add(("texCoord1", "vec2"));
            src = "texCoord1";
        }

        return outputPort.Name switch
        {
            "U" => $"{src}.x",
            "V" => $"{src}.y",
            _   => src,
        };
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx)
        => outputPort.Name == "UV" ? ShaderType.Vec2 : ShaderType.Float;
}

// =============================================================================
// VertexColorNode
// Per-vertex color interpolated across the triangle (vColor, vec4).
// Outputs RGBA (vec4) and individual float channels.
// =============================================================================

public sealed class VertexColorNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Vertex Color";
    public override string Category => "Geometry";
    public override System.Drawing.Color AccentColor => GeometryAccents.Geometry;

    protected override void DefineNode()
    {
        AddOutput<Float4>("RGBA");
        AddOutput<Float3>("RGB");
        AddOutput<float>("R");
        AddOutput<float>("G");
        AddOutput<float>("B");
        AddOutput<float>("A");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        // Varying in fragment, direct attribute (with instance tint) in vertex.
        string src;
        if (stage == ShaderStage.Vertex)
        {
            ctx.Includes.Add("VertexAttributes");
            src = "GetInstanceColor()";
        }
        else
        {
            ctx.Varyings.Add(("vColor", "vec4"));
            src = "vColor";
        }
        return outputPort.Name switch
        {
            "RGB" => $"{src}.rgb",
            "R"   => $"{src}.r",
            "G"   => $"{src}.g",
            "B"   => $"{src}.b",
            "A"   => $"{src}.a",
            _     => src,
        };
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx)
        => outputPort.Name switch
        {
            "RGBA" => ShaderType.Vec4,
            "RGB"  => ShaderType.Vec3,
            _      => ShaderType.Float,
        };
}

// =============================================================================
// NormalDirectionNode
// Outputs the surface normal in world, object, or tangent space.
//   World  (default) : vNormal  (normalized world-space varying)
//   Tangent          : vec3(0,0,1)  (tangent-space representation of the world
//                      normal is always +Z by construction of the TBN basis)
//   Object           : inverse-transposes the model matrix to preserve normal
//                      orthogonality under non-uniform scale. `transpose(mat3(
//                      prowl_WorldToObject))` is equivalent to the inverse-transpose
//                      of ObjectToWorld, which is the mathematically-correct
//                      normal transform.
// =============================================================================

public enum NormalSpace { World = 0, Tangent = 1, Object = 2 }

public sealed class NormalDirectionNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Normal Direction";
    public override string Category => "Geometry";
    public override System.Drawing.Color AccentColor => GeometryAccents.Geometry;

    /// <summary>Space the normal is expressed in.</summary>
    public NormalSpace Space = NormalSpace.World;

    protected override void DefineNode()
    {
        AddOutput<Float3>("Normal");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        switch (Space)
        {
            case NormalSpace.Tangent:
                // Tangent-space passthrough the TBN matrix is applied by the
                // surface master. Same default value SurfaceMasterNode uses for its Normal input.
                return "vec3(0.0, 0.0, 1.0)";

            case NormalSpace.Object:
                // In vertex, the attribute is already object-space. In fragment we
                // reconstruct from the world varying via the inverse-transpose.
                if (stage == ShaderStage.Vertex)
                {
                    ctx.Includes.Add("VertexAttributes");
                    return "normalize(vertexNormal)";
                }
                ctx.Varyings.Add(("vNormal", "vec3"));
                ctx.Includes.Add("ShaderVariables");
                var local = $"_objNrm{Id:N}";
                ctx.EmitOnce("objnrm:" + local, () =>
                {
                    ctx.BodyPrelude.AppendLine(
                        $"    vec3 {local} = normalize(transpose(mat3(prowl_WorldToObject)) * normalize(vNormal));");
                });
                return local;

            default: // World
                if (stage == ShaderStage.Vertex)
                {
                    ctx.Includes.Add("VertexAttributes");
                    return "TransformDirection(vertexNormal)";
                }
                ctx.Varyings.Add(("vNormal", "vec3"));
                return "vNormal";
        }
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx)
        => ShaderType.Vec3;
}

// =============================================================================
// TangentDirectionNode
// World-space tangent interpolated across the triangle (vTangent, vec3).
// =============================================================================

public sealed class TangentDirectionNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Tangent Direction";
    public override string Category => "Geometry";
    public override System.Drawing.Color AccentColor => GeometryAccents.Geometry;

    protected override void DefineNode()
    {
        AddOutput<Float3>("Tangent");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        // Varying in fragment; transform the attribute inline in vertex. The mesh
        // feature system falls tangent back to (1,0,0) when the mesh has no
        // HAS_TANGENTS VertexAttributes.glsl handles that.
        if (stage == ShaderStage.Vertex)
        {
            ctx.Includes.Add("VertexAttributes");
            return "TransformDirection(vertexTangent.xyz)";
        }
        ctx.Varyings.Add(("vTangent", "vec3"));
        return "vTangent";
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx)
        => ShaderType.Vec3;
}

// =============================================================================
// BitangentDirectionNode
// World-space bitangent (cross of world normal x tangent) (vBitangent, vec3).
// =============================================================================

public sealed class BitangentDirectionNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Bitangent Direction";
    public override string Category => "Geometry";
    public override System.Drawing.Color AccentColor => GeometryAccents.Geometry;

    protected override void DefineNode()
    {
        AddOutput<Float3>("Bitangent");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        // In fragment the compiler's vertex body hands us the already-crossed,
        // already-normalised vBitangent. In vertex we build it from vertexNormal x
        // vertexTangent, matching the same formula used later in the vertex body.
        if (stage == ShaderStage.Vertex)
        {
            ctx.Includes.Add("VertexAttributes");
            var local = $"_bitV{Id:N}";
            ctx.EmitOnce("bitV:" + local, () =>
            {
                ctx.BodyPrelude.AppendLine(
                    $"    vec3 {local} = cross(TransformDirection(vertexNormal), TransformDirection(vertexTangent.xyz));");
            });
            return local;
        }
        ctx.Varyings.Add(("vBitangent", "vec3"));
        return "vBitangent";
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx)
        => ShaderType.Vec3;
}

// =============================================================================
// WorldPositionNode
// World-space position of the current fragment (worldPos, vec3).
// Multi-output: XYZ / X / Y / Z.
// =============================================================================

public sealed class WorldPositionNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "World Position";
    public override string Category => "Geometry";
    public override System.Drawing.Color AccentColor => GeometryAccents.Geometry;

    protected override void DefineNode()
    {
        AddOutput<Float3>("XYZ");
        AddOutput<float>("X");
        AddOutput<float>("Y");
        AddOutput<float>("Z");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        // In the fragment stage we can read the interpolated varying. In the vertex
        // stage the varying hasn't been written yet (the compiler assigns it AFTER
        // Vertex Position is evaluated), so we compute the rest-vertex world position
        // inline via TransformPosition(vertexPosition). That's what authors driving
        // wind / wobble effects actually want anyway.
        string src;
        if (stage == ShaderStage.Vertex)
        {
            ctx.Includes.Add("VertexAttributes");
            var local = $"_wpV{Id:N}";
            ctx.EmitOnce("wpV:" + local, () =>
            {
                ctx.BodyPrelude.AppendLine($"    vec3 {local} = TransformPosition(vertexPosition);");
            });
            src = local;
        }
        else
        {
            ctx.Varyings.Add(("worldPos", "vec3"));
            src = "worldPos";
        }

        return outputPort.Name switch
        {
            "X" => $"{src}.x",
            "Y" => $"{src}.y",
            "Z" => $"{src}.z",
            _   => src,
        };
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx)
        => outputPort.Name == "XYZ" ? ShaderType.Vec3 : ShaderType.Float;
}

// =============================================================================
// ObjectPositionNode
// World-space position of the object's origin the translation column of the
// model matrix (prowl_ObjectToWorld[3].xyz). Requires ShaderVariables.
// Multi-output: XYZ / X / Y / Z.
// =============================================================================

public sealed class ObjectPositionNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Object Position";
    public override string Category => "Geometry";
    public override System.Drawing.Color AccentColor => GeometryAccents.Geometry;

    protected override void DefineNode()
    {
        AddOutput<Float3>("XYZ");
        AddOutput<float>("X");
        AddOutput<float>("Y");
        AddOutput<float>("Z");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        ctx.Includes.Add("ShaderVariables");
        // prowl_ObjectToWorld is a column-major mat4; column 3 holds the translation.
        return outputPort.Name switch
        {
            "X" => "prowl_ObjectToWorld[3].x",
            "Y" => "prowl_ObjectToWorld[3].y",
            "Z" => "prowl_ObjectToWorld[3].z",
            _   => "prowl_ObjectToWorld[3].xyz",
        };
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx)
        => outputPort.Name == "XYZ" ? ShaderType.Vec3 : ShaderType.Float;
}

// =============================================================================
// ObjectScaleNode
// Per-axis world-space scale derived from the column lengths of the model matrix.
// Cached into a BodyPrelude temp so the three length() calls are paid once.
// Multi-output: XYZ / X / Y / Z.
// =============================================================================

public sealed class ObjectScaleNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Object Scale";
    public override string Category => "Geometry";
    public override System.Drawing.Color AccentColor => GeometryAccents.Geometry;

    protected override void DefineNode()
    {
        AddOutput<Float3>("XYZ");
        AddOutput<float>("X");
        AddOutput<float>("Y");
        AddOutput<float>("Z");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        ctx.Includes.Add("ShaderVariables");
        var local = $"_objScale{Id:N}";
        if (!ctx.HelperFunctions.Contains(local))
        {
            ctx.HelperFunctions.Add(local);
            ctx.BodyPrelude.AppendLine(
                $"    vec3 {local} = vec3(" +
                "length(prowl_ObjectToWorld[0].xyz), " +
                "length(prowl_ObjectToWorld[1].xyz), " +
                "length(prowl_ObjectToWorld[2].xyz));");
        }
        return outputPort.Name switch
        {
            "X" => $"{local}.x",
            "Y" => $"{local}.y",
            "Z" => $"{local}.z",
            _   => local,
        };
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx)
        => outputPort.Name == "XYZ" ? ShaderType.Vec3 : ShaderType.Float;
}

// =============================================================================
// ViewDirectionNode
// World-space unit vector from the fragment toward the camera.
// = normalize(_WorldSpaceCameraPos.xyz - worldPos)
// Cached via BodyPrelude so the normalize() runs once per fragment.
// =============================================================================

public sealed class ViewDirectionNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "View Direction";
    public override string Category => "Geometry";
    public override System.Drawing.Color AccentColor => GeometryAccents.Geometry;

    protected override void DefineNode()
    {
        AddOutput<Float3>("View Dir");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        ctx.Includes.Add("Lighting");
        if (stage == ShaderStage.Vertex)
        {
            ctx.Includes.Add("VertexAttributes");
            return "GetWorldViewDir(TransformPosition(vertexPosition))";
        }
        ctx.Varyings.Add(("worldPos", "vec3"));
        return "GetWorldViewDir(worldPos)";
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx)
        => ShaderType.Vec3;
}

// =============================================================================
// ViewReflectionDirectionNode
// Reflection of the view direction about the world-space surface normal.
// = reflect(-viewDir, worldNormal)
// Both viewDir and vNormal are resolved via cache so each helper emits once.
// =============================================================================

public sealed class ViewReflectionDirectionNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "View Reflection Direction";
    public override string Category => "Geometry";
    public override System.Drawing.Color AccentColor => GeometryAccents.Geometry;

    protected override void DefineNode()
    {
        AddOutput<Float3>("Reflection");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        ctx.Includes.Add("Lighting");

        var local = $"_vdRefl{Id:N}";
        if (stage == ShaderStage.Vertex)
        {
            ctx.Includes.Add("VertexAttributes");
            ctx.EmitOnce("vdrefl:" + local, () =>
            {
                ctx.BodyPrelude.AppendLine(
                    $"    vec3 {local} = reflect(-GetWorldViewDir(TransformPosition(vertexPosition)), TransformDirection(vertexNormal));");
            });
            return local;
        }

        ctx.Varyings.Add(("worldPos", "vec3"));
        ctx.Varyings.Add(("vNormal",  "vec3"));
        ctx.EmitOnce("vdrefl:" + local, () =>
        {
            ctx.BodyPrelude.AppendLine(
                $"    vec3 {local} = reflect(-GetWorldViewDir(worldPos), normalize(vNormal));");
        });
        return local;
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx)
        => ShaderType.Vec3;
}

// =============================================================================
// ScreenPositionNode
// Fragment screen-space position, available in two modes:
//   Raw        : gl_FragCoord.xy / _ScreenParams.xy  -> [0,1] window UV
//   Normalized : NDC xy remapped to [-1,+1]
// Output is always vec2.
// =============================================================================

public enum ScreenPositionMode { Raw = 0, Normalized = 1, Tiled = 2 }

public sealed class ScreenPositionNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Screen Position";
    public override string Category => "Geometry";
    public override System.Drawing.Color AccentColor => GeometryAccents.Geometry;

    /// <summary>Raw = [0,1] window UV; Normalized = NDC [-1,+1]; Tiled = Raw with aspect-
    /// ratio corrected X (so the horizontal span matches the vertical in units) useful
    /// for distance / circular effects across non-square viewports.</summary>
    public ScreenPositionMode Mode = ScreenPositionMode.Raw;

    protected override void DefineNode()
    {
        AddOutput<Float2>("UV");
        AddOutput<float>("U");
        AddOutput<float>("V");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title)) return outputPort.Name == "UV" ? "vec2(0.0)" : "0.0";
        ctx.Includes.Add("ShaderVariables");
        var local = $"_scrPos{Id:N}";
        if (!ctx.HelperFunctions.Contains(local))
        {
            ctx.HelperFunctions.Add(local);
            if (Mode == ScreenPositionMode.Raw)
            {
                // _ScreenParams.xy = (width, height) in pixels.
                ctx.BodyPrelude.AppendLine(
                    $"    vec2 {local} = gl_FragCoord.xy / _ScreenParams.xy;");
            }
            else if (Mode == ScreenPositionMode.Normalized)
            {
                // Remap [0,1] -> [-1,+1].
                ctx.BodyPrelude.AppendLine(
                    $"    vec2 {local} = (gl_FragCoord.xy / _ScreenParams.xy) * 2.0 - 1.0;");
            }
            else // Tiled aspect-correct X so horizontal and vertical units match.
            {
                ctx.BodyPrelude.AppendLine(
                    $"    vec2 {local} = vec2((gl_FragCoord.x / _ScreenParams.y), (gl_FragCoord.y / _ScreenParams.y));");
            }
        }
        return outputPort.Name switch
        {
            "U" => $"{local}.x",
            "V" => $"{local}.y",
            _   => local,
        };
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx)
        => outputPort.Name == "UV" ? ShaderType.Vec2 : ShaderType.Float;
}

// =============================================================================
// FaceSignNode
// Returns +1.0 for front-facing fragments and -1.0 for back-facing ones, using
// GLSL's built-in gl_FrontFacing boolean. The PlusMinusOne mode outputs +1/-1;
// the OneAndZero variant outputs 1.0 / 0.0 instead.
// =============================================================================

public enum FaceSignOutputMode { PlusMinusOne = 0, OneAndZero = 1 }

public sealed class FaceSignNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Face Sign";
    public override string Category => "Geometry";
    public override System.Drawing.Color AccentColor => GeometryAccents.Geometry;

    /// <summary>PlusMinusOne = +1/-1; OneAndZero = 1/0 (front/back).</summary>
    public FaceSignOutputMode OutputMode = FaceSignOutputMode.PlusMinusOne;

    protected override void DefineNode()
    {
        AddOutput<float>("Sign");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        if (ctx.RequireFragmentStage(Id, Title)) return "1.0";
        return OutputMode == FaceSignOutputMode.PlusMinusOne
            ? "(gl_FrontFacing ? 1.0 : -1.0)"
            : "(gl_FrontFacing ? 1.0 :  0.0)";
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx)
        => ShaderType.Float;
}

// =============================================================================
// FresnelNode
// Standard Schlick/power-of-cosine Fresnel:
//   pow(1.0 - abs(dot(normal, viewDir)), exponent)
// Inputs:
//   Normal  (Vec3, optional defaults to world-space vNormal)
//   Bias    (Float, default 0.0 additive offset before pow)
//   Power   (Float, default 5.0 exponent)
// =============================================================================

public sealed class FresnelNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Fresnel";
    public override string Category => "Geometry";
    public override System.Drawing.Color AccentColor => GeometryAccents.Geometry;

    protected override void DefineNode()
    {
        AddInput<Float3>("Normal",  Float3.Zero); // unconnected -> world vNormal fallback
        AddInput<float>("Bias",    0.0f);
        AddInput<float>("Power",   5.0f);
        AddOutput<float>("Out");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        ctx.Includes.Add("Lighting");

        // View direction varying-based in fragment, inline in vertex (the worldPos
        // varying isn't populated yet when vertex-offset subtrees evaluate).
        string vdExpr;
        if (stage == ShaderStage.Vertex)
        {
            ctx.Includes.Add("VertexAttributes");
            vdExpr = "GetWorldViewDir(TransformPosition(vertexPosition))";
        }
        else
        {
            ctx.Varyings.Add(("worldPos", "vec3"));
            vdExpr = "GetWorldViewDir(worldPos)";
        }

        // Normal: use connected input; fall back to the appropriate-stage world normal.
        string normalExpr;
        var normalPort = GetInput("Normal")!;
        if (ctx.IsConnected(normalPort))
        {
            normalExpr = $"normalize({ctx.EvaluateInputAs(normalPort, ShaderType.Vec3)})";
        }
        else if (stage == ShaderStage.Vertex)
        {
            ctx.Includes.Add("VertexAttributes");
            normalExpr = "TransformDirection(vertexNormal)";
        }
        else
        {
            ctx.Varyings.Add(("vNormal", "vec3"));
            normalExpr = "normalize(vNormal)";
        }

        string biasExpr  = ctx.EvaluateInput(GetInput("Bias")!);
        string powerExpr = ctx.EvaluateInput(GetInput("Power")!);

        // Bias shifts the base term before the exponent is applied.
        return $"({biasExpr} + pow(1.0 - abs(dot({normalExpr}, {vdExpr})), {powerExpr}))";
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx)
        => ShaderType.Float;
}

// =============================================================================
// DepthNode
// Eye-space (linear) depth: world-space distance from the camera to the fragment.
// Uses length(_WorldSpaceCameraPos.xyz - worldPos) which is the distance in view
// space for a perspective camera. Requires ShaderVariables.
// =============================================================================

public sealed class DepthNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Depth";
    public override string Category => "Geometry";
    public override System.Drawing.Color AccentColor => GeometryAccents.Geometry;

    protected override void DefineNode()
    {
        AddOutput<float>("Depth");
    }

    string IShaderNode.Evaluate(Port outputPort, ShaderStage stage, ShaderGenContext ctx)
    {
        ctx.Includes.Add("ShaderVariables");
        // Compute world pos inline in vertex stage the varying isn't written yet.
        string posExpr;
        if (stage == ShaderStage.Vertex)
        {
            ctx.Includes.Add("VertexAttributes");
            posExpr = "TransformPosition(vertexPosition)";
        }
        else
        {
            ctx.Varyings.Add(("worldPos", "vec3"));
            posExpr = "worldPos";
        }
        var local = $"_depth{Id:N}";
        ctx.EmitOnce("depth:" + local, () =>
        {
            ctx.BodyPrelude.AppendLine(
                $"    float {local} = length(_WorldSpaceCameraPos.xyz - {posExpr});");
        });
        return local;
    }

    ShaderType IShaderNode.GetOutputType(Port outputPort, ShaderGenContext ctx)
        => ShaderType.Float;
}

// =============================================================================
// InstanceColorNode
//
// Exposes the per-instance tint used by GPU-instanced renderers. When
// GPU_INSTANCING is defined, this is vertexColor * instanceColor (from
// VertexAttributes.glsl's GetInstanceColor()). When not instancing, it falls
// back to plain vertexColor, matching the varying the vertex stage already
// emits as vColor.
// =============================================================================

public sealed class InstanceColorNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Instance Color";
    public override string Category => "Geometry";
    public override System.Drawing.Color AccentColor => GeometryAccents.Geometry;
    protected override void DefineNode()
    {
        AddOutput<Color>("RGBA");
        AddOutput<Float3>("RGB");
        AddOutput<float>("R"); AddOutput<float>("G"); AddOutput<float>("B"); AddOutput<float>("A");
    }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        // Varying in fragment; direct attribute call in vertex (varying hasn't been
        // written yet when Vertex Position offsets evaluate).
        string src;
        if (s == ShaderStage.Vertex)
        {
            ctx.Includes.Add("VertexAttributes");
            src = "GetInstanceColor()";
        }
        else
        {
            ctx.Varyings.Add(("vColor", "vec4"));
            src = "vColor";
        }
        return p.Name switch
        {
            "R"    => $"{src}.r", "G" => $"{src}.g", "B" => $"{src}.b", "A" => $"{src}.a",
            "RGB"  => $"{src}.rgb",
            _      => src,
        };
    }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx) => p.Name switch
    {
        "RGBA" => ShaderType.Color,
        "RGB"  => ShaderType.Vec3,
        _      => ShaderType.Float,
    };
}

// =============================================================================
// InstanceCustomDataNode
//
// Per-instance vec4 payload authors can use for anything (timing offsets, wind
// strength, sprite frame, etc.). Only meaningful under GPU_INSTANCING; otherwise
// reads vec4(0) see VertexAttributes.glsl::GetInstanceCustomData.
// =============================================================================

public sealed class InstanceCustomDataNode : Node, IShaderNode, IShaderGraphNode
{
    public override string Title => "Instance Custom Data";
    public override string Category => "Geometry";
    public override System.Drawing.Color AccentColor => GeometryAccents.Geometry;
    protected override void DefineNode()
    {
        AddOutput<Float4>("XYZW");
        AddOutput<float>("X"); AddOutput<float>("Y"); AddOutput<float>("Z"); AddOutput<float>("W");
    }
    string IShaderNode.Evaluate(Port p, ShaderStage s, ShaderGenContext ctx)
    {
        // Varying in fragment (captured once by the vertex body), direct attribute
        // call in vertex stage when the varying hasn't been assigned yet.
        string src;
        if (s == ShaderStage.Vertex)
        {
            ctx.Includes.Add("VertexAttributes");
            src = "GetInstanceCustomData()";
        }
        else
        {
            ctx.Varyings.Add(("vInstanceData", "vec4"));
            src = "vInstanceData";
        }
        return p.Name switch
        {
            "X" => $"{src}.x", "Y" => $"{src}.y",
            "Z" => $"{src}.z", "W" => $"{src}.w",
            _   => src,
        };
    }
    ShaderType IShaderNode.GetOutputType(Port p, ShaderGenContext ctx)
        => p.Name == "XYZW" ? ShaderType.Vec4 : ShaderType.Float;
}
