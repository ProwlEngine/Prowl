using System.Text.Json;
using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.Formats.Gltf;

/// <summary>
/// Maps glTF mesh primitives into <see cref="IntermediateMesh"/>es. Each primitive becomes a
/// standalone <see cref="IntermediateMesh"/> so the importer keeps a one-material-per-mesh
/// contract (the post-process pipeline can re-merge later if requested).
/// </summary>
internal static class GltfMeshMapper
{
    public sealed class Result
    {
        /// <summary>For each glTF mesh, the contiguous IntermediateScene mesh range and the
        /// per-primitive skin index (-1 when the primitive is unskinned).</summary>
        public List<(int First, int Count)> MeshIndexRanges { get; } = new();

        /// <summary>Morph target names, populated from <c>mesh.extras.targetNames</c> when present.</summary>
        public List<string[]?> MorphTargetNames { get; } = new();
    }

    public static Result MapAll(GltfDom dom, GltfAccessorReader reader, IntermediateScene scene, ImportContext ctx)
    {
        var result = new Result();
        if (dom.Meshes is null)
            return result;

        for (int mi = 0; mi < dom.Meshes.Length; mi++)
        {
            var srcMesh = dom.Meshes[mi];
            int firstIndex = scene.Meshes.Count;

            string[]? targetNames = ResolveTargetNames(srcMesh);
            result.MorphTargetNames.Add(targetNames);

            for (int pi = 0; pi < srcMesh.Primitives.Length; pi++)
            {
                var prim = srcMesh.Primitives[pi];
                var im = MapPrimitive(srcMesh.Name ?? $"Mesh_{mi}", pi, prim, reader, ctx, targetNames);
                scene.Meshes.Add(im);
            }
            result.MeshIndexRanges.Add((firstIndex, srcMesh.Primitives.Length));
        }

        return result;
    }

    private static string[]? ResolveTargetNames(GltfMesh src)
    {
        if (src.Extras is not { } extras || extras.ValueKind != JsonValueKind.Object)
            return null;
        if (!extras.TryGetProperty("targetNames", out JsonElement names) || names.ValueKind != JsonValueKind.Array)
            return null;
        var arr = new string[names.GetArrayLength()];
        for (int i = 0; i < arr.Length; i++)
            arr[i] = names[i].GetString() ?? $"Target{i}";
        return arr;
    }

    private static IntermediateMesh MapPrimitive(
        string parentName,
        int primitiveIndex,
        GltfPrimitive prim,
        GltfAccessorReader reader,
        ImportContext ctx,
        string[]? targetNames)
    {
        var mesh = new IntermediateMesh
        {
            Name = $"{parentName}/prim{primitiveIndex}",
            MaterialIndex = prim.Material ?? -1,
        };

        if (!prim.Attributes.TryGetValue("POSITION", out int posIdx))
            throw new ImportException($"glTF primitive in '{parentName}' has no POSITION attribute.");
        mesh.Positions.AddRange(reader.ReadVec3(posIdx));
        int vertexCount = mesh.Positions.Count;

        if (prim.Attributes.TryGetValue("NORMAL", out int normIdx))
            mesh.Normals = new List<Float3>(reader.ReadVec3(normIdx));

        if (prim.Attributes.TryGetValue("TANGENT", out int tanIdx))
            mesh.Tangents = new List<Float4>(reader.ReadVec4(tanIdx));

        if (prim.Attributes.TryGetValue("COLOR_0", out int colIdx))
            mesh.Colors0 = new List<Color>(reader.ReadColor(colIdx));

        for (int uv = 0; uv < Mesh.MaxUVChannels; uv++)
        {
            if (prim.Attributes.TryGetValue($"TEXCOORD_{uv}", out int uvIdx))
                mesh.UVs[uv] = new List<Float2>(reader.ReadVec2(uvIdx));
        }

        ReadJointsAndWeights(prim, reader, mesh, vertexCount, ctx, parentName);
        ReadMorphTargets(prim, reader, mesh, vertexCount, ctx, parentName, targetNames);

        uint[] indices;
        if (prim.Indices is { } indicesAccessor)
            indices = reader.ReadUInts(indicesAccessor);
        else
        {
            indices = new uint[vertexCount];
            for (int i = 0; i < indices.Length; i++)
                indices[i] = (uint)i;
        }

        BuildFaces(prim.Mode, indices, mesh, ctx, parentName);
        return mesh;
    }

    private static void ReadJointsAndWeights(
        GltfPrimitive prim,
        GltfAccessorReader reader,
        IntermediateMesh mesh,
        int vertexCount,
        ImportContext ctx,
        string parentName)
    {
        int setCount = 0;
        while (prim.Attributes.ContainsKey($"JOINTS_{setCount}"))
            setCount++;

        if (setCount == 0)
            return;

        int weightSetCount = 0;
        while (prim.Attributes.ContainsKey($"WEIGHTS_{weightSetCount}"))
            weightSetCount++;

        if (weightSetCount != setCount)
        {
            ctx.Log.Warning(
                $"{parentName}: JOINTS set count ({setCount}) does not match WEIGHTS set count ({weightSetCount}); skipping bone data.",
                "GltfMeshMapper");
            return;
        }

        int influencesPerVertex = setCount * 4;
        int[] joints = new int[vertexCount * influencesPerVertex];
        float[] weights = new float[vertexCount * influencesPerVertex];

        for (int s = 0; s < setCount; s++)
        {
            int jointAcc = prim.Attributes[$"JOINTS_{s}"];
            int weightAcc = prim.Attributes[$"WEIGHTS_{s}"];

            // Joints are unsigned-byte or unsigned-short scalar-of-VEC4 (so 4 components).
            var jointAccessor = reader.Get(jointAcc);
            if (jointAccessor.Type != "VEC4")
            {
                ctx.Log.Warning($"{parentName}: JOINTS_{s} is type {jointAccessor.Type}, expected VEC4.", "GltfMeshMapper");
                return;
            }
            var weightsRaw = reader.ReadVec4(weightAcc);

            // We need joint indices as ints; ReadVec4 returns floats which carry the integer
            // value correctly for both unsigned-byte and unsigned-short (and float) joints.
            var jointsAsFloat = reader.ReadVec4(jointAcc);
            for (int v = 0; v < vertexCount; v++)
            {
                int baseDst = v * influencesPerVertex + s * 4;
                joints[baseDst + 0] = (int)jointsAsFloat[v].X;
                joints[baseDst + 1] = (int)jointsAsFloat[v].Y;
                joints[baseDst + 2] = (int)jointsAsFloat[v].Z;
                joints[baseDst + 3] = (int)jointsAsFloat[v].W;
                weights[baseDst + 0] = weightsRaw[v].X;
                weights[baseDst + 1] = weightsRaw[v].Y;
                weights[baseDst + 2] = weightsRaw[v].Z;
                weights[baseDst + 3] = weightsRaw[v].W;
            }
        }

        mesh.VertexJoints = joints;
        mesh.VertexWeights = weights;
        mesh.MaxInfluencesPerVertex = influencesPerVertex;
    }

    private static void ReadMorphTargets(
        GltfPrimitive prim,
        GltfAccessorReader reader,
        IntermediateMesh mesh,
        int vertexCount,
        ImportContext ctx,
        string parentName,
        string[]? targetNames)
    {
        if (prim.Targets is null || prim.Targets.Length == 0)
            return;

        for (int ti = 0; ti < prim.Targets.Length; ti++)
        {
            var target = prim.Targets[ti];
            Float3[]? deltaPos = null;
            Float3[]? deltaNormals = null;
            Float3[]? deltaTangents = null;

            if (target.TryGetValue("POSITION", out int posAcc))
                deltaPos = reader.ReadVec3(posAcc);
            if (target.TryGetValue("NORMAL", out int normAcc))
                deltaNormals = reader.ReadVec3(normAcc);
            if (target.TryGetValue("TANGENT", out int tanAcc))
                deltaTangents = reader.ReadVec3(tanAcc);

            // Ensure the delta arrays match the vertex count - a sparse position accessor will
            // already have been expanded by the accessor reader.
            if (deltaPos is null)
            {
                ctx.Log.Warning($"{parentName}: morph target {ti} has no POSITION; skipping.", "GltfMeshMapper");
                continue;
            }
            if (deltaPos.Length != vertexCount)
            {
                ctx.Log.Warning(
                    $"{parentName}: morph target {ti} has {deltaPos.Length} positions but mesh has {vertexCount} vertices; skipping.",
                    "GltfMeshMapper");
                continue;
            }

            string name = targetNames is { } names && ti < names.Length
                ? names[ti]
                : $"Target_{ti}";

            var blendShape = new IntermediateBlendShape { Name = name };
            blendShape.Frames.Add(new IntermediateBlendShapeFrame
            {
                Weight = 100f,
                DeltaPositions = deltaPos,
                DeltaNormals = deltaNormals,
                DeltaTangents = deltaTangents,
            });
            mesh.BlendShapes.Add(blendShape);
        }
    }

    private static void BuildFaces(int mode, uint[] indices, IntermediateMesh mesh, ImportContext ctx, string parentName)
    {
        switch (mode)
        {
            case GltfPrimitiveMode.Points:
                mesh.PrimitiveKinds |= PrimitiveKind.Point;
                for (int i = 0; i < indices.Length; i++)
                    mesh.Faces.Add(new IntermediateFace(new[] { (int)indices[i] }));
                break;

            case GltfPrimitiveMode.Lines:
                mesh.PrimitiveKinds |= PrimitiveKind.Line;
                if ((indices.Length & 1) != 0)
                    ctx.Log.Warning($"{parentName}: LINES primitive has odd index count; trailing index dropped.", "GltfMeshMapper");
                for (int i = 0; i + 1 < indices.Length; i += 2)
                    mesh.Faces.Add(new IntermediateFace(new[] { (int)indices[i], (int)indices[i + 1] }));
                break;

            case GltfPrimitiveMode.LineLoop:
                mesh.PrimitiveKinds |= PrimitiveKind.Line;
                for (int i = 0; i < indices.Length; i++)
                {
                    int a = (int)indices[i];
                    int b = (int)indices[(i + 1) % indices.Length];
                    mesh.Faces.Add(new IntermediateFace(new[] { a, b }));
                }
                break;

            case GltfPrimitiveMode.LineStrip:
                mesh.PrimitiveKinds |= PrimitiveKind.Line;
                for (int i = 0; i + 1 < indices.Length; i++)
                    mesh.Faces.Add(new IntermediateFace(new[] { (int)indices[i], (int)indices[i + 1] }));
                break;

            case GltfPrimitiveMode.Triangles:
                mesh.PrimitiveKinds |= PrimitiveKind.Triangle;
                int triCount = indices.Length / 3;
                if (indices.Length % 3 != 0)
                    ctx.Log.Warning($"{parentName}: TRIANGLES primitive has {indices.Length} indices (not a multiple of 3); trailing indices dropped.", "GltfMeshMapper");
                for (int i = 0; i < triCount; i++)
                {
                    mesh.Faces.Add(new IntermediateFace(new[]
                    {
                        (int)indices[i * 3 + 0],
                        (int)indices[i * 3 + 1],
                        (int)indices[i * 3 + 2],
                    }));
                }
                break;

            case GltfPrimitiveMode.TriangleStrip:
                mesh.PrimitiveKinds |= PrimitiveKind.Triangle;
                for (int i = 0; i + 2 < indices.Length; i++)
                {
                    int a = (int)indices[i];
                    int b = (int)indices[i + 1];
                    int c = (int)indices[i + 2];
                    if ((i & 1) == 0)
                        mesh.Faces.Add(new IntermediateFace(new[] { a, b, c }));
                    else
                        mesh.Faces.Add(new IntermediateFace(new[] { b, a, c }));
                }
                break;

            case GltfPrimitiveMode.TriangleFan:
                mesh.PrimitiveKinds |= PrimitiveKind.Triangle;
                if (indices.Length >= 3)
                {
                    int center = (int)indices[0];
                    for (int i = 1; i + 1 < indices.Length; i++)
                    {
                        mesh.Faces.Add(new IntermediateFace(new[]
                        {
                            center,
                            (int)indices[i],
                            (int)indices[i + 1],
                        }));
                    }
                }
                break;

            default:
                throw new ImportException($"Unsupported glTF primitive mode {mode}.");
        }
    }
}
