using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.Formats.Gltf;

/// <summary>
/// Maps glTF <c>animations</c> into <see cref="IntermediateAnimation"/>s.
/// </summary>
/// <remarks>
/// Each glTF channel becomes one <see cref="IntermediateAnimationBinding"/>. The sampler's
/// interpolation type drives the binding's interpolation; for CUBICSPLINE the output accessor
/// contains 3x the per-key values (in-tangent, value, out-tangent) and we preserve that layout.
/// </remarks>
internal static class GltfAnimationMapper
{
    public static void MapAll(
        GltfDom dom,
        IntermediateNode[] nodes,
        GltfAccessorReader reader,
        GltfMeshMapper.Result meshMapping,
        IntermediateScene scene,
        ImportContext ctx)
    {
        if (dom.Animations is null)
            return;

        for (int a = 0; a < dom.Animations.Length; a++)
        {
            var src = dom.Animations[a];
            var anim = new IntermediateAnimation { Name = src.Name ?? $"Animation_{a}" };

            foreach (var channel in src.Channels)
            {
                if (channel.Target.Node is not { } nodeIdx ||
                    (uint)nodeIdx >= (uint)nodes.Length)
                {
                    ctx.Log.Warning($"Animation {a}: channel target node {channel.Target.Node} out of range.", "GltfAnimationMapper");
                    continue;
                }
                if ((uint)channel.Sampler >= (uint)src.Samplers.Length)
                {
                    ctx.Log.Warning($"Animation {a}: channel sampler {channel.Sampler} out of range.", "GltfAnimationMapper");
                    continue;
                }

                var sampler = src.Samplers[channel.Sampler];
                AnimationInterpolation interp = ParseInterpolation(sampler.Interpolation, ctx);

                var times = reader.ReadFloats1D(sampler.Input);
                if (times.Length == 0) continue;

                switch (channel.Target.Path)
                {
                    case "translation":
                        AddVecBinding(anim, nodes[nodeIdx], AnimatedProperty.Position, 0, interp, times, reader, sampler.Output, 3);
                        break;
                    case "rotation":
                        AddVecBinding(anim, nodes[nodeIdx], AnimatedProperty.Rotation, 0, interp, times, reader, sampler.Output, 4);
                        break;
                    case "scale":
                        AddVecBinding(anim, nodes[nodeIdx], AnimatedProperty.Scale, 0, interp, times, reader, sampler.Output, 3);
                        break;
                    case "weights":
                        AddWeightsBinding(anim, nodes, nodeIdx, meshMapping, interp, times, reader, sampler.Output, ctx);
                        break;
                    default:
                        ctx.Log.Warning(
                            $"Animation {a}: unknown target path '{channel.Target.Path}'.",
                            "GltfAnimationMapper");
                        break;
                }
            }

            // Duration = largest key time encountered across all bindings.
            float duration = 0f;
            foreach (var b in anim.Bindings)
                if (b.Times.Count > 0)
                    duration = MathF.Max(duration, b.Times[^1]);
            anim.Duration = duration;

            scene.Animations.Add(anim);
        }
    }

    private static void AddVecBinding(
        IntermediateAnimation anim,
        IntermediateNode target,
        AnimatedProperty property,
        int subIndex,
        AnimationInterpolation interp,
        float[] times,
        GltfAccessorReader reader,
        int outputAccessor,
        int components)
    {
        float[] values = reader.ReadFloats1DComponents(outputAccessor, components);
        // Sanity: cubic spline triples the values per key.
        int expected = (interp == AnimationInterpolation.CubicSpline ? 3 : 1) * times.Length * components;
        if (values.Length != expected)
            throw new ImportException(
                $"Animation output value count {values.Length} does not match expected {expected}.");

        var binding = new IntermediateAnimationBinding
        {
            TargetNode = target,
            Property = property,
            SubIndex = subIndex,
            Interpolation = interp,
            Dimension = components,
        };
        binding.Times.AddRange(times);
        binding.Values.AddRange(values);
        anim.Bindings.Add(binding);
    }

    private static void AddWeightsBinding(
        IntermediateAnimation anim,
        IntermediateNode[] nodes,
        int nodeIdx,
        GltfMeshMapper.Result meshMapping,
        AnimationInterpolation interp,
        float[] times,
        GltfAccessorReader reader,
        int outputAccessor,
        ImportContext ctx)
    {
        // The node must reference a mesh whose blend-shape count drives the weight stride.
        // We don't have the glTF mesh index on the IntermediateNode directly, so we look up
        // by stride: total values / (keys * (cubic? 3 : 1)) must equal blend shape count.
        // For simplicity, we read the raw float stream and split per-shape into separate bindings.
        float[] flatValues = reader.ReadFloats1D(outputAccessor);
        int stride = interp == AnimationInterpolation.CubicSpline ? 3 : 1;
        int totalPerKey = flatValues.Length / (times.Length * stride);
        if (totalPerKey * times.Length * stride != flatValues.Length)
        {
            ctx.Log.Warning(
                "Morph-weights animation: output values are not evenly divisible by time count; skipping.",
                "GltfAnimationMapper");
            return;
        }

        for (int shape = 0; shape < totalPerKey; shape++)
        {
            var binding = new IntermediateAnimationBinding
            {
                TargetNode = nodes[nodeIdx],
                Property = AnimatedProperty.BlendShapeWeight,
                SubIndex = shape,
                Interpolation = interp,
                Dimension = 1,
            };
            binding.Times.AddRange(times);

            // De-interleave: per-key value (cubic spline keeps in/out tangents adjacent).
            // glTF morph weights are 0-1; scale to 0-100 so blend-shape weight curves share the
            // same unit across formats (FBX DeformPercent and BlendShapeFrame.Weight are 0-100).
            float[] shapeValues = new float[times.Length * stride];
            for (int k = 0; k < times.Length; k++)
            {
                for (int t = 0; t < stride; t++)
                {
                    int srcOff = (k * stride + t) * totalPerKey + shape;
                    shapeValues[k * stride + t] = flatValues[srcOff] * 100f;
                }
            }
            binding.Values.AddRange(shapeValues);
            anim.Bindings.Add(binding);
        }
    }

    private static AnimationInterpolation ParseInterpolation(string? interp, ImportContext ctx) => interp switch
    {
        "STEP" => AnimationInterpolation.Step,
        "LINEAR" or null => AnimationInterpolation.Linear,
        "CUBICSPLINE" => AnimationInterpolation.CubicSpline,
        _ => Warn(interp, ctx),
    };

    private static AnimationInterpolation Warn(string? interp, ImportContext ctx)
    {
        ctx.Log.Warning($"Unknown animation interpolation '{interp}'; using LINEAR.", "GltfAnimationMapper");
        return AnimationInterpolation.Linear;
    }
}
