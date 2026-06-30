using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.Formats.Fbx;

/// <summary>
/// Maps FBX <c>AnimationStack</c> / <c>AnimationLayer</c> / <c>AnimationCurveNode</c> /
/// <c>AnimationCurve</c> objects to <see cref="IntermediateAnimation"/>s.
/// </summary>
/// <remarks>
/// FBX animation graph:
/// <code>
///   AnimationStack (= "clip")
///     -&gt; AnimationLayer (one or more, usually one)
///       -&gt; AnimationCurveNode (one per target-property: Lcl Translation, Lcl Rotation,
///                                Lcl Scaling, DeformPercent for blend-shape weights)
///         -&gt; AnimationCurve (one per scalar channel: X, Y, Z)
/// </code>
/// Curve nodes connect to their target <c>Model</c> via OP with the property name being the FBX
/// channel name (e.g. "Lcl Translation"). Curves carry <c>KeyTime</c> (int64 in KTime ticks,
/// 1/46186158000 of a second) and <c>KeyValueFloat</c> (parallel float values).
/// </remarks>
internal static class FbxAnimationMapper
{
    /// <summary>FBX time unit: 1 second = 46186158000 KTime ticks.</summary>
    private const double KTimePerSecond = 46186158000.0;

    public static void MapAll(FbxDocument doc, FbxModelMapper.ModelMapping modelMapping, IntermediateScene scene, ImportContext ctx,
        Dictionary<long, FbxBlendShapeMapper.ChannelTarget> blendShapeChannels)
    {
        // Walk every AnimationStack. Each becomes one IntermediateAnimation.
        foreach (var stack in doc.Objects.Values)
        {
            if (stack.ObjectType != "AnimationStack") continue;
            var anim = BuildClip(stack, doc, modelMapping, blendShapeChannels, ctx);
            if (anim is null || anim.Bindings.Count == 0) continue;
            scene.Animations.Add(anim);
        }
    }

    private static IntermediateAnimation? BuildClip(FbxObject stack, FbxDocument doc, FbxModelMapper.ModelMapping modelMapping,
        Dictionary<long, FbxBlendShapeMapper.ChannelTarget> blendShapeChannels, ImportContext ctx)
    {
        var clip = new IntermediateAnimation { Name = string.IsNullOrEmpty(stack.Name) ? $"Take_{stack.Id}" : stack.Name };

        // Stack -> Layers (Layer is a destination of an OO connection from the Stack).
        var layers = new List<FbxObject>();
        if (doc.ConnectionsByDestination.TryGetValue(stack.Id, out var stackIn))
        {
            foreach (var c in stackIn)
            {
                if (c.Type != "OO") continue;
                if (!doc.Objects.TryGetValue(c.Source, out var src)) continue;
                if (src.ObjectType == "AnimationLayer")
                    layers.Add(src);
            }
        }
        if (layers.Count == 0) return null;

        // For each layer, walk every AnimationCurveNode connected.
        foreach (var layer in layers)
        {
            if (!doc.ConnectionsByDestination.TryGetValue(layer.Id, out var layerIn)) continue;
            foreach (var c in layerIn)
            {
                if (c.Type != "OO") continue;
                if (!doc.Objects.TryGetValue(c.Source, out var src)) continue;
                if (src.ObjectType != "AnimationCurveNode") continue;
                BuildBindingsFromCurveNode(src, doc, modelMapping, blendShapeChannels, clip, ctx);
            }
        }

        // Duration: max time across all bindings.
        float maxT = 0f;
        foreach (var b in clip.Bindings)
            if (b.Times.Count > 0)
                maxT = MathF.Max(maxT, b.Times[^1]);
        clip.Duration = maxT;
        return clip;
    }

    private static void BuildBindingsFromCurveNode(
        FbxObject curveNode, FbxDocument doc, FbxModelMapper.ModelMapping modelMapping,
        Dictionary<long, FbxBlendShapeMapper.ChannelTarget> blendShapeChannels,
        IntermediateAnimation clip, ImportContext ctx)
    {
        // The curve node connects to its target via OP. The target is either a Model (transform
        // channels) or a BlendShapeChannel deformer (DeformPercent = blend-shape weight).
        string property = string.Empty;
        IntermediateNode? targetNode = null;
        long targetFbxId = 0;
        FbxObject? targetObj = null;
        if (doc.ConnectionsBySource.TryGetValue(curveNode.Id, out var curveOut))
        {
            foreach (var c in curveOut)
            {
                if (c.Type != "OP") continue;
                if (!doc.Objects.TryGetValue(c.Destination, out var dst)) continue;

                if (dst.ObjectType == "Model" && modelMapping.NodesByFbxId.TryGetValue(dst.Id, out targetNode))
                {
                    property = c.Property;
                    targetFbxId = dst.Id;
                    targetObj = dst;
                    break;
                }
                if (dst.ObjectType == "Deformer" && dst.Subtype == "BlendShapeChannel")
                {
                    property = c.Property;
                    targetObj = dst;
                    break;
                }
            }
        }
        if (targetObj is null) return;

        // Blend-shape weight channel (DeformPercent on a BlendShapeChannel).
        if (targetObj.ObjectType == "Deformer")
        {
            EmitBlendShapeWeightBinding(targetObj, curveNode, doc, modelMapping, blendShapeChannels, clip);
            return;
        }

        if (targetNode is null || property.Length == 0) return;

        AnimatedProperty prowlProp = property switch
        {
            "Lcl Translation" => AnimatedProperty.Position,
            "Lcl Rotation" => AnimatedProperty.Rotation,
            "Lcl Scaling" => AnimatedProperty.Scale,
            _ => default, // unmapped
        };
        if (property != "Lcl Translation" && property != "Lcl Rotation" && property != "Lcl Scaling")
            return; // unsupported Model property

        // Collect the (X/Y/Z) child curves via OP (Curve -> CurveNode with property "d|X" etc.).
        var curveX = ResolveScalarCurve(curveNode, doc, "d|X");
        var curveY = ResolveScalarCurve(curveNode, doc, "d|Y");
        var curveZ = ResolveScalarCurve(curveNode, doc, "d|Z");
        if (curveX is null && curveY is null && curveZ is null) return;

        // For rotation we sample Euler -> quaternion at the union of key times. For position
        // and scale we keep them as separate per-axis curves and resample on the merged time
        // axis (the IntermediateAnimationCurve representation is per-axis values flat-packed).
        if (prowlProp == AnimatedProperty.Rotation)
        {
            // Pre/Post rotation: my FbxModelMapper baked these into the node's bind-time
            // LocalRotation. Animation curves only carry the middle "R" of the FBX rotation
            // chain, so each sample must be wrapped as (Pre * sampled * inverse(Post)) for the
            // runtime LocalRotation to evaluate the same composed transform.
            modelMapping.RotationOffsets.TryGetValue(targetFbxId, out var pp);
            EmitRotationBinding(targetNode, curveX, curveY, curveZ, pp.Pre, pp.Post, clip, ctx);
        }
        else
        {
            EmitVec3Binding(targetNode, prowlProp, curveX, curveY, curveZ, clip);
        }
    }

    /// <summary>Resolves the AnimationCurve attached to a CurveNode under a given property name.</summary>
    private static ScalarCurve? ResolveScalarCurve(FbxObject curveNode, FbxDocument doc, string propertyName)
    {
        if (!doc.ConnectionsByDestination.TryGetValue(curveNode.Id, out var inConns)) return null;
        foreach (var c in inConns)
        {
            if (c.Type != "OP" || c.Property != propertyName) continue;
            if (!doc.Objects.TryGetValue(c.Source, out var src)) continue;
            if (src.ObjectType != "AnimationCurve") continue;
            return ReadScalarCurve(src);
        }
        return null;
    }

    /// <summary>Resolves the first AnimationCurve attached to a CurveNode, regardless of property name.</summary>
    private static ScalarCurve? ResolveFirstScalarCurve(FbxObject curveNode, FbxDocument doc)
    {
        if (!doc.ConnectionsByDestination.TryGetValue(curveNode.Id, out var inConns)) return null;
        foreach (var c in inConns)
        {
            if (c.Type != "OP") continue;
            if (!doc.Objects.TryGetValue(c.Source, out var src)) continue;
            if (src.ObjectType != "AnimationCurve") continue;
            var curve = ReadScalarCurve(src);
            if (curve is not null) return curve;
        }
        return null;
    }

    private static ScalarCurve? ReadScalarCurve(FbxObject animCurve)
    {
        var keyTimeNode = animCurve.Node.FindChild("KeyTime");
        var keyValueNode = animCurve.Node.FindChild("KeyValueFloat");
        if (keyTimeNode is null || keyValueNode is null) return null;
        if (keyTimeNode.Properties.Count == 0 || keyValueNode.Properties.Count == 0) return null;

        // KeyTime is i64 KTime ticks; KeyValueFloat is f32 per key.
        long[] times = keyTimeNode.Properties[0].LongArrayValue ?? Array.Empty<long>();
        float[] values = keyValueNode.Properties[0].FloatArrayValue ?? keyValueNode.Properties[0].AsFloatArray();
        if (times.Length == 0 || values.Length == 0) return null;
        int len = Math.Min(times.Length, values.Length);
        float[] timesS = new float[len];
        for (int i = 0; i < len; i++) timesS[i] = (float)(times[i] / KTimePerSecond);
        float[] vals = new float[len];
        Array.Copy(values, vals, len);
        return new ScalarCurve(timesS, vals);
    }

    /// <summary>
    /// Emits a <see cref="AnimatedProperty.BlendShapeWeight"/> binding from a DeformPercent curve on a
    /// BlendShapeChannel. FBX DeformPercent is already 0-100, matching the blend-shape frame weights.
    /// </summary>
    private static void EmitBlendShapeWeightBinding(
        FbxObject channel, FbxObject curveNode, FbxDocument doc, FbxModelMapper.ModelMapping modelMapping,
        Dictionary<long, FbxBlendShapeMapper.ChannelTarget> blendShapeChannels, IntermediateAnimation clip)
    {
        if (!blendShapeChannels.TryGetValue(channel.Id, out var target)) return;

        IntermediateNode? node = ResolveNodeForGeometry(target.GeometryId, doc, modelMapping);
        if (node is null) return;

        ScalarCurve? curve = ResolveScalarCurve(curveNode, doc, "d|DeformPercent") ?? ResolveFirstScalarCurve(curveNode, doc);
        if (curve is null || curve.Times.Length == 0) return;

        var binding = new IntermediateAnimationBinding
        {
            TargetNode = node,
            Property = AnimatedProperty.BlendShapeWeight,
            SubIndex = target.BlendShapeIndex,
            Interpolation = AnimationInterpolation.Linear,
            Dimension = 1,
        };
        binding.Times.AddRange(curve.Times);
        binding.Values.AddRange(curve.Values);
        clip.Bindings.Add(binding);
    }

    /// <summary>Finds the model node that references a geometry (Geometry --OO--&gt; Model).</summary>
    private static IntermediateNode? ResolveNodeForGeometry(long geometryId, FbxDocument doc, FbxModelMapper.ModelMapping modelMapping)
    {
        if (!doc.ConnectionsBySource.TryGetValue(geometryId, out var outConns)) return null;
        foreach (var c in outConns)
        {
            if (c.Type != "OO") continue;
            if (!doc.Objects.TryGetValue(c.Destination, out var dst)) continue;
            if (dst.ObjectType != "Model") continue;
            if (modelMapping.NodesByFbxId.TryGetValue(dst.Id, out var node)) return node;
        }
        return null;
    }

    private static void EmitVec3Binding(
        IntermediateNode target, AnimatedProperty property,
        ScalarCurve? curveX, ScalarCurve? curveY, ScalarCurve? curveZ,
        IntermediateAnimation clip)
    {
        // Merge the three axes onto a shared time axis. Sample each curve at the union.
        float[] mergedTimes = MergeTimes(curveX, curveY, curveZ);
        if (mergedTimes.Length == 0) return;

        Float3 defaultValue = property == AnimatedProperty.Scale ? new Prowl.Vector.Float3(1f, 1f, 1f) : Prowl.Vector.Float3.Zero;

        var binding = new IntermediateAnimationBinding
        {
            TargetNode = target,
            Property = property,
            Interpolation = AnimationInterpolation.Linear,
            Dimension = 3,
        };
        binding.Times.AddRange(mergedTimes);
        binding.Values.Capacity = mergedTimes.Length * 3;
        for (int i = 0; i < mergedTimes.Length; i++)
        {
            float t = mergedTimes[i];
            float x = curveX?.Sample(t) ?? defaultValue.X;
            float y = curveY?.Sample(t) ?? defaultValue.Y;
            float z = curveZ?.Sample(t) ?? defaultValue.Z;
            binding.Values.Add(x); binding.Values.Add(y); binding.Values.Add(z);
        }
        clip.Bindings.Add(binding);
    }

    private static void EmitRotationBinding(
        IntermediateNode target,
        ScalarCurve? curveX, ScalarCurve? curveY, ScalarCurve? curveZ,
        Quaternion preRotation, Quaternion postRotation,
        IntermediateAnimation clip, ImportContext ctx)
    {
        // FBX stores per-axis Euler degrees on Lcl Rotation. The full FBX rotation chain is
        // (PreRotation * Lcl Rotation * inverse(PostRotation)); the curve only animates the
        // middle term. We compose around it at each sample so the runtime's LocalRotation matches
        // the bake-time composition (which already included Pre/Post).
        float[] mergedTimes = MergeTimes(curveX, curveY, curveZ);
        if (mergedTimes.Length == 0) return;

        bool needPre = !IsIdentity(preRotation);
        bool needPost = !IsIdentity(postRotation);
        Quaternion postInverse = needPost ? new Quaternion(-postRotation.X, -postRotation.Y, -postRotation.Z, postRotation.W) : Quaternion.Identity;

        var binding = new IntermediateAnimationBinding
        {
            TargetNode = target,
            Property = AnimatedProperty.Rotation,
            Interpolation = AnimationInterpolation.Linear,
            Dimension = 4,
        };
        binding.Times.AddRange(mergedTimes);
        binding.Values.Capacity = mergedTimes.Length * 4;
        for (int i = 0; i < mergedTimes.Length; i++)
        {
            float t = mergedTimes[i];
            float rx = (curveX?.Sample(t) ?? 0f) * MathF.PI / 180f;
            float ry = (curveY?.Sample(t) ?? 0f) * MathF.PI / 180f;
            float rz = (curveZ?.Sample(t) ?? 0f) * MathF.PI / 180f;
            var q = EulerToQuat(rx, ry, rz);
            if (needPre) q = MulQuat(preRotation, q);
            if (needPost) q = MulQuat(q, postInverse);
            binding.Values.Add(q.X); binding.Values.Add(q.Y); binding.Values.Add(q.Z); binding.Values.Add(q.W);
        }
        clip.Bindings.Add(binding);
        _ = ctx;
    }

    private static bool IsIdentity(Quaternion q) =>
        MathF.Abs(q.X) < 1e-7f && MathF.Abs(q.Y) < 1e-7f && MathF.Abs(q.Z) < 1e-7f && MathF.Abs(q.W - 1f) < 1e-7f;

    private static Quaternion MulQuat(Quaternion a, Quaternion b) => new(
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);

    private static Prowl.Vector.Quaternion EulerToQuat(float rx, float ry, float rz)
    {
        float cx = MathF.Cos(rx * 0.5f), sx = MathF.Sin(rx * 0.5f);
        float cy = MathF.Cos(ry * 0.5f), sy = MathF.Sin(ry * 0.5f);
        float cz = MathF.Cos(rz * 0.5f), sz = MathF.Sin(rz * 0.5f);
        // Rz * Ry * Rx (XYZ Euler extrinsic).
        return new Prowl.Vector.Quaternion(
            sx * cy * cz - cx * sy * sz,
            cx * sy * cz + sx * cy * sz,
            cx * cy * sz - sx * sy * cz,
            cx * cy * cz + sx * sy * sz);
    }

    private static float[] MergeTimes(ScalarCurve? a, ScalarCurve? b, ScalarCurve? c)
    {
        var set = new SortedSet<float>();
        if (a is not null) foreach (var t in a.Times) set.Add(t);
        if (b is not null) foreach (var t in b.Times) set.Add(t);
        if (c is not null) foreach (var t in c.Times) set.Add(t);
        float[] result = new float[set.Count];
        set.CopyTo(result);
        return result;
    }

    private sealed class ScalarCurve
    {
        public float[] Times;
        public float[] Values;
        public ScalarCurve(float[] times, float[] values) { Times = times; Values = values; }

        /// <summary>Linear sample with clamp-to-edge outside [Times[0], Times[^1]].</summary>
        public float Sample(float t)
        {
            if (Times.Length == 0) return 0f;
            if (t <= Times[0]) return Values[0];
            if (t >= Times[^1]) return Values[^1];

            int lo = 0, hi = Times.Length - 1;
            while (lo + 1 < hi)
            {
                int mid = (lo + hi) >> 1;
                if (Times[mid] <= t) lo = mid;
                else hi = mid;
            }
            float t0 = Times[lo], t1 = Times[lo + 1];
            float u = (t1 - t0) > 1e-12f ? (t - t0) / (t1 - t0) : 0f;
            return Values[lo] + (Values[lo + 1] - Values[lo]) * u;
        }
    }
}
