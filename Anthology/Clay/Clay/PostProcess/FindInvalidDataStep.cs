using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Scans every per-vertex stream and animation curve for NaN or infinity, plus collapses
/// consecutive identical keyframes on animation curves.
/// </summary>
/// <remarks>
/// Invalid floats are replaced with safe defaults (zero for position deltas, identity for
/// normals/tangents, etc.) and a warning is logged.
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
            droppedKeys += SanitizeAnimation(anim, context);

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
                if (!Finite(normals[i]) || Magnitude(normals[i]) < 1e-12f)
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
        _ = ctx;
        return fixedCount;
    }

    private static int SanitizeAnimation(IntermediateAnimation anim, ImportContext ctx)
    {
        int collapsed = 0;
        foreach (var b in anim.Bindings)
        {
            int valuesPerKey = b.Interpolation == AnimationInterpolation.CubicSpline
                ? b.Dimension * 3
                : b.Dimension;
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
    private static float Magnitude(Float3 v) => MathF.Sqrt(v.X * v.X + v.Y * v.Y + v.Z * v.Z);
}
