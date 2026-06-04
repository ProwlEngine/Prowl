using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.Formats.Fbx;

/// <summary>
/// Maps FBX <c>Deformer::BlendShape</c> / <c>Deformer::BlendShapeChannel</c> /
/// <c>Geometry::Shape</c> chains to <see cref="IntermediateBlendShape"/>s on the affected meshes.
/// </summary>
/// <remarks>
/// Object graph:
/// <code>
///   BlendShape (OO src to a Geometry::Mesh)
///     -&gt; BlendShapeChannel (OO src to BlendShape) - one per named morph
///       -&gt; ShapeGeometry (Geometry::Shape, OO src to channel) - the actual deltas
/// </code>
/// Each Shape carries sparse <c>Indexes</c> + <c>Vertices</c>: only vertices that move are listed.
/// We scatter the deltas through the FBX -&gt; intermediate vertex expansion the same way the skin
/// mapper does, so morph deltas survive the per-polygon-vertex unpack.
/// </remarks>
internal static class FbxBlendShapeMapper
{
    /// <summary>
    /// Resolves a <c>BlendShapeChannel</c> deformer to the geometry it animates and the channel's
    /// position within that geometry's blend-shape list. Used by the animation mapper to turn a
    /// <c>DeformPercent</c> curve into a <see cref="AnimatedProperty.BlendShapeWeight"/> binding.
    /// </summary>
    internal readonly record struct ChannelTarget(long GeometryId, int BlendShapeIndex);

    public static Dictionary<long, ChannelTarget> MapAll(FbxDocument doc, FbxMeshMapper.MeshMapping meshMapping, IntermediateScene scene, ImportContext ctx)
    {
        // channelId -> (geometry, index of this channel's blend shape within the geometry).
        var channelTargets = new Dictionary<long, ChannelTarget>();
        var perGeometryCount = new Dictionary<long, int>();

        foreach (var bs in doc.Objects.Values)
        {
            if (bs.ObjectType != "Deformer" || bs.Subtype != "BlendShape") continue;

            // BlendShape attaches to a Geometry::Mesh via OO src.
            FbxObject? geometry = null;
            if (doc.ConnectionsBySource.TryGetValue(bs.Id, out var bsOut))
            {
                foreach (var c in bsOut)
                {
                    if (c.Type != "OO") continue;
                    if (!doc.Objects.TryGetValue(c.Destination, out var dst)) continue;
                    if (dst.ObjectType == "Geometry" && dst.Subtype == "Mesh") { geometry = dst; break; }
                }
            }
            if (geometry is null) continue;
            if (!meshMapping.GeometryToMeshes.TryGetValue(geometry.Id, out var geoMapping)) continue;

            // For each BlendShape, walk its child Channels.
            if (!doc.ConnectionsByDestination.TryGetValue(bs.Id, out var bsIn)) continue;
            foreach (var c in bsIn)
            {
                if (c.Type != "OO") continue;
                if (!doc.Objects.TryGetValue(c.Source, out var channel)) continue;
                if (channel.ObjectType != "Deformer" || channel.Subtype != "BlendShapeChannel") continue;

                // The channel's index must match the order blend shapes are appended to the mesh,
                // so it's only advanced when MapChannel actually produces a blend shape.
                if (MapChannel(channel, doc, geoMapping, scene, ctx))
                {
                    perGeometryCount.TryGetValue(geometry.Id, out int idx);
                    channelTargets[channel.Id] = new ChannelTarget(geometry.Id, idx);
                    perGeometryCount[geometry.Id] = idx + 1;
                }
            }
        }

        return channelTargets;
    }

    private static bool MapChannel(FbxObject channel, FbxDocument doc, FbxMeshMapper.GeometryMapping geoMapping, IntermediateScene scene, ImportContext ctx)
    {
        // Each channel -> one or more ShapeGeometry. Most files have exactly one shape per channel
        // (binary morph: 0% rest, 100% shape). Multi-shape channels are progressive morphs - we
        // emit one BlendShape per IntermediateMesh with one frame per shape.
        var shapes = new List<FbxObject>();
        if (doc.ConnectionsByDestination.TryGetValue(channel.Id, out var inConns))
        {
            foreach (var c in inConns)
            {
                if (c.Type != "OO") continue;
                if (!doc.Objects.TryGetValue(c.Source, out var src)) continue;
                if (src.ObjectType == "Geometry" && src.Subtype == "Shape")
                    shapes.Add(src);
            }
        }
        if (shapes.Count == 0) return false;

        // Optional FullWeights: parallel to shapes, gives the activation weight (0-100) per shape.
        double[]? fullWeights = channel.Node.FindChild("FullWeights")?.Properties.ElementAtOrDefault(0)?.AsDoubleArray();

        // Create one IntermediateBlendShape per affected IntermediateMesh.
        var blendShapes = new IntermediateBlendShape[geoMapping.MeshCount];
        for (int m = 0; m < geoMapping.MeshCount; m++)
        {
            blendShapes[m] = new IntermediateBlendShape
            {
                Name = string.IsNullOrEmpty(channel.Name) ? $"BlendShape_{channel.Id}" : channel.Name,
            };
        }

        // Each shape contributes one frame across all affected meshes.
        for (int si = 0; si < shapes.Count; si++)
        {
            var shape = shapes[si];
            int[] sparseIndexes = shape.Node.FindChild("Indexes")?.Properties.ElementAtOrDefault(0)?.AsIntArray()
                                ?? Array.Empty<int>();
            double[] sparseVerts = shape.Node.FindChild("Vertices")?.Properties.ElementAtOrDefault(0)?.AsDoubleArray()
                                ?? Array.Empty<double>();

            if (sparseVerts.Length != sparseIndexes.Length * 3)
            {
                ctx.Log.Warning(
                    $"BlendShape '{shape.Name}': Vertices length {sparseVerts.Length} not 3x Indexes length {sparseIndexes.Length}; skipping shape.",
                    "FbxBlendShapeMapper");
                continue;
            }

            // Build dense delta arrays for every affected IntermediateMesh.
            var meshDeltas = new Float3[geoMapping.MeshCount][];
            for (int m = 0; m < geoMapping.MeshCount; m++)
                meshDeltas[m] = new Float3[scene.Meshes[geoMapping.FirstMeshIndex + m].Positions.Count];

            // Optional normal deltas.
            double[]? normalDeltasRaw = shape.Node.FindChild("Normals")?.Properties.ElementAtOrDefault(0)?.AsDoubleArray();
            bool hasNormalDeltas = normalDeltasRaw is not null && normalDeltasRaw.Length == sparseVerts.Length;
            var meshNormalDeltas = hasNormalDeltas
                ? Enumerable.Range(0, geoMapping.MeshCount).Select(m => new Float3[scene.Meshes[geoMapping.FirstMeshIndex + m].Positions.Count]).ToArray()
                : null;

            for (int i = 0; i < sparseIndexes.Length; i++)
            {
                int fbxV = sparseIndexes[i];
                if ((uint)fbxV >= (uint)(geoMapping.Starts.Length - 1)) continue;
                Float3 d = new Float3((float)sparseVerts[i * 3], (float)sparseVerts[i * 3 + 1], (float)sparseVerts[i * 3 + 2]);
                Float3 dn = hasNormalDeltas
                    ? new Float3((float)normalDeltasRaw![i * 3], (float)normalDeltasRaw[i * 3 + 1], (float)normalDeltasRaw[i * 3 + 2])
                    : default;

                int start = geoMapping.Starts[fbxV];
                int end = geoMapping.Starts[fbxV + 1];
                for (int e = start; e < end; e++)
                {
                    int meshIdx = geoMapping.MeshIndices[e];
                    int vIdx = geoMapping.VertexIndices[e];
                    int localMesh = meshIdx - geoMapping.FirstMeshIndex;
                    if ((uint)localMesh >= (uint)geoMapping.MeshCount) continue;
                    meshDeltas[localMesh][vIdx] = d;
                    if (meshNormalDeltas is not null) meshNormalDeltas[localMesh][vIdx] = dn;
                }
            }

            float weight = fullWeights is { Length: var fwLen } && si < fwLen ? (float)fullWeights[si] : 100f;
            for (int m = 0; m < geoMapping.MeshCount; m++)
            {
                blendShapes[m].Frames.Add(new IntermediateBlendShapeFrame
                {
                    Weight = weight,
                    DeltaPositions = meshDeltas[m],
                    DeltaNormals = meshNormalDeltas?[m],
                });
            }
        }

        // Attach the blend shapes to every affected mesh.
        bool added = false;
        for (int m = 0; m < geoMapping.MeshCount; m++)
        {
            if (blendShapes[m].Frames.Count > 0)
            {
                scene.Meshes[geoMapping.FirstMeshIndex + m].BlendShapes.Add(blendShapes[m]);
                added = true;
            }
        }
        return added;
    }
}
