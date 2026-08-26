// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Scans every per-vertex stream and animation curve for NaN or infinity, plus collapses
/// consecutive identical keyframes on animation curves.
/// </summary>
/// <remarks>
/// Invalid floats are replaced with a default the data can hold: zero for positions and morph
/// deltas, up for a normal, one for a scale, identity for a rotation. Keys whose time is not finite
/// are dropped, since there is nowhere sensible to put them. Everything replaced is counted and
/// warned about once at the end.
/// </remarks>
internal sealed class FindInvalidDataStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.FindInvalidData;
    public string Name => "FindInvalidData";

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        int fixedFloats = 0;
        int droppedKeys = 0;

        foreach (var mesh in scene.Meshes)
            fixedFloats += SanitizeMesh(mesh, context);

        foreach (var anim in scene.Animations)
        {
            foreach (var binding in anim.Bindings)
            {
                droppedKeys += DropKeysWithInvalidTimes(binding);
                fixedFloats += SanitizeCurveValues(binding);
            }
            droppedKeys += SanitizeAnimation(anim, context);
        }

        if (fixedFloats > 0)
            context.Log.Warning(
                $"Replaced {fixedFloats} non-finite float(s) across mesh data.", Name);
        if (droppedKeys > 0)
            context.Log.Info($"Collapsed {droppedKeys} redundant animation key(s).", Name);
    }

    private static int SanitizeMesh(IntermediateMesh mesh, ImportContext ctx)
    {
        int fixedCount = 0;
        for (int i = 0; i < mesh.Positions.Count; i++)
        {
            var p = mesh.Positions[i];
            if (!Finite(p))
            {
                mesh.Positions[i] = Float3.Zero;
                fixedCount += 3;
            }
        }

        if (mesh.Normals is { } normals)
        {
            for (int i = 0; i < normals.Count; i++)
            {
                if (!Finite(normals[i]) || Float3.Length(normals[i]) < 1e-12f)
                {
                    normals[i] = new Float3(0f, 1f, 0f);
                    fixedCount += 3;
                }
            }
        }

        if (mesh.Tangents is { } tangents)
        {
            for (int i = 0; i < tangents.Count; i++)
            {
                var t = tangents[i];
                if (!Finite(t))
                {
                    tangents[i] = new Float4(1f, 0f, 0f, 1f);
                    fixedCount += 4;
                }
            }
        }

        if (mesh.Colors0 is { } colors)
        {
            for (int i = 0; i < colors.Count; i++)
            {
                var c = colors[i];
                if (!Finite(c))
                {
                    colors[i] = new Color(1f, 1f, 1f, 1f);
                    fixedCount += 4;
                }
            }
        }

        for (int uv = 0; uv < Mesh.MaxUVChannels; uv++)
        {
            if (mesh.UVs[uv] is { } list)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var u = list[i];
                    if (!Finite(u))
                    {
                        list[i] = Float2.Zero;
                        fixedCount += 2;
                    }
                }
            }
        }

        // A non-finite weight poisons the normalisation of its whole vertex, taking the influences
        // that were fine with it.
        if (mesh.VertexWeights is { } weights)
        {
            for (int i = 0; i < weights.Length; i++)
            {
                if (!float.IsFinite(weights[i]))
                {
                    weights[i] = 0f;
                    fixedCount++;
                }
            }
        }

        foreach (var shape in mesh.BlendShapes)
        {
            foreach (var frame in shape.Frames)
            {
                fixedCount += SanitizeDeltas(frame.DeltaPositions);
                if (frame.DeltaNormals is { } dn) fixedCount += SanitizeDeltas(dn);
                if (frame.DeltaTangents is { } dt) fixedCount += SanitizeDeltas(dt);
            }
        }

        _ = ctx;
        return fixedCount;
    }

    /// <summary>Zeroes non-finite morph deltas, which is the same as that vertex not moving.</summary>
    private static int SanitizeDeltas(Float3[] deltas)
    {
        int fixedCount = 0;
        for (int i = 0; i < deltas.Length; i++)
        {
            if (!Finite(deltas[i]))
            {
                deltas[i] = Float3.Zero;
                fixedCount += 3;
            }
        }
        return fixedCount;
    }

    /// <summary>Values per key, which cubic spline triples for its in and out tangents.</summary>
    private static int ValuesPerKey(IntermediateAnimationBinding b) =>
        b.Interpolation == CurveInterpolation.CubicSpline ? b.Dimension * 3 : b.Dimension;

    /// <summary>
    /// Removes keys whose time is not finite.
    /// </summary>
    /// <remarks>
    /// A key at a NaN time cannot be repaired, only placed somewhere arbitrary, and it poisons every
    /// comparison the curve makes to find the segment a sample falls in.
    /// </remarks>
    private static int DropKeysWithInvalidTimes(IntermediateAnimationBinding b)
    {
        int stride = ValuesPerKey(b);
        if (stride <= 0 || b.Values.Count != b.Times.Count * stride)
            return 0;

        int write = 0;
        for (int k = 0; k < b.Times.Count; k++)
        {
            if (!float.IsFinite(b.Times[k])) continue;

            if (write != k)
            {
                b.Times[write] = b.Times[k];
                for (int v = 0; v < stride; v++)
                    b.Values[write * stride + v] = b.Values[k * stride + v];
            }
            write++;
        }

        int dropped = b.Times.Count - write;
        if (dropped > 0)
        {
            b.Times.RemoveRange(write, dropped);
            b.Values.RemoveRange(write * stride, b.Values.Count - write * stride);
        }
        return dropped;
    }

    /// <summary>
    /// Replaces non-finite curve values with something the property can hold.
    /// </summary>
    /// <remarks>
    /// The default has to suit the property: zero is a reasonable position or morph weight and a
    /// harmless tangent, but a zero scale collapses the node and a zero quaternion has no rotation
    /// to normalise back to.
    /// </remarks>
    private static int SanitizeCurveValues(IntermediateAnimationBinding b)
    {
        int stride = ValuesPerKey(b);
        if (stride <= 0 || b.Values.Count != b.Times.Count * stride)
            return 0;

        bool cubic = b.Interpolation == CurveInterpolation.CubicSpline;
        int fixedCount = 0;

        for (int i = 0; i < b.Values.Count; i++)
        {
            if (float.IsFinite(b.Values[i])) continue;

            int slot = i % stride;
            // The cubic layout is in-tangent, value, out-tangent; only the middle third is the value.
            bool isValue = !cubic || (slot >= b.Dimension && slot < b.Dimension * 2);
            int component = slot % b.Dimension;

            b.Values[i] = isValue ? DefaultComponent(b.Property, component) : 0f;
            fixedCount++;
        }
        return fixedCount;
    }

    private static float DefaultComponent(AnimatedProperty property, int component) => property switch
    {
        AnimatedProperty.Scale => 1f,
        // Identity quaternion, whose only non-zero component is W.
        AnimatedProperty.Rotation => component == 3 ? 1f : 0f,
        _ => 0f,
    };

    private static int SanitizeAnimation(IntermediateAnimation anim, ImportContext ctx)
    {
        int collapsed = 0;
        foreach (var b in anim.Bindings)
        {
            int valuesPerKey = ValuesPerKey(b);
            if (b.Times.Count < 3 || b.Values.Count != b.Times.Count * valuesPerKey)
                continue;

            // Collapse runs of three or more identical adjacent keys to a single boundary pair.
            int writeKey = 0;
            for (int k = 0; k < b.Times.Count; k++)
            {
                bool sameAsLeft = k > 0 && KeyEquals(b, k, k - 1, valuesPerKey);
                bool sameAsRight = k < b.Times.Count - 1 && KeyEquals(b, k, k + 1, valuesPerKey);
                if (sameAsLeft && sameAsRight)
                {
                    collapsed++;
                    continue;
                }
                if (writeKey != k)
                {
                    b.Times[writeKey] = b.Times[k];
                    for (int v = 0; v < valuesPerKey; v++)
                        b.Values[writeKey * valuesPerKey + v] = b.Values[k * valuesPerKey + v];
                }
                writeKey++;
            }
            if (writeKey < b.Times.Count)
            {
                b.Times.RemoveRange(writeKey, b.Times.Count - writeKey);
                b.Values.RemoveRange(writeKey * valuesPerKey, b.Values.Count - writeKey * valuesPerKey);
            }
        }
        _ = ctx;
        return collapsed;
    }

    private static bool KeyEquals(IntermediateAnimationBinding b, int a, int c, int stride)
    {
        for (int v = 0; v < stride; v++)
        {
            if (b.Values[a * stride + v] != b.Values[c * stride + v])
                return false;
        }
        return true;
    }

    private static bool Finite(Float2 v) => float.IsFinite(v.X) && float.IsFinite(v.Y);
    private static bool Finite(Float3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    private static bool Finite(Float4 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z) && float.IsFinite(v.W);
    private static bool Finite(Color c) => float.IsFinite(c.R) && float.IsFinite(c.G) && float.IsFinite(c.B) && float.IsFinite(c.A);
}
