// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

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
        /// <summary>For each glTF mesh, the index of the single IntermediateScene mesh it produced,
        /// or -1 when it had no primitives.</summary>
        public List<int> MeshIndex { get; } = new();

        /// <summary>Morph target names, populated from <c>mesh.extras.targetNames</c> when present.</summary>
        public List<string[]?> MorphTargetNames { get; } = new();
    }

    /// <summary>
    /// One glTF mesh becomes one <see cref="IntermediateMesh"/>, with its primitives merged and each
    /// one's material recorded per face.
    /// </summary>
    /// <remarks>
    /// A glTF primitive is a material batch, not an object: a single authored model with three
    /// material slots exports as one mesh of three primitives. Mapping each to its own mesh turned
    /// that into three sibling objects in the scene, which is not what the artist built, costs a
    /// draw call setup and a transform each, and left morph-target weights reaching only the first
    /// of them. Merged here, the sub-mesh split that <see cref="Mesh.SubMeshes"/> has always
    /// described falls out of the material assignment at bake time.
    /// </remarks>
    public static Result MapAll(GltfDom dom, GltfAccessorReader reader, IntermediateScene scene, ImportContext ctx)
    {
        var result = new Result();
        if (dom.Meshes is null)
            return result;

        for (int mi = 0; mi < dom.Meshes.Length; mi++)
        {
            var srcMesh = dom.Meshes[mi];
            string name = srcMesh.Name ?? $"Mesh_{mi}";

            string[]? targetNames = ResolveTargetNames(srcMesh);
            result.MorphTargetNames.Add(targetNames);

            if (srcMesh.Primitives.Length == 0)
            {
                result.MeshIndex.Add(-1);
                continue;
            }

            var primitives = new List<IntermediateMesh>(srcMesh.Primitives.Length);
            for (int pi = 0; pi < srcMesh.Primitives.Length; pi++)
                primitives.Add(MapPrimitive(name, pi, srcMesh.Primitives[pi], reader, ctx, targetNames));

            result.MeshIndex.Add(scene.Meshes.Count);
            scene.Meshes.Add(primitives.Count == 1
                ? Rename(primitives[0], name)
                : MergePrimitives(primitives, name, ctx));
        }

        return result;
    }

    private static IntermediateMesh Rename(IntermediateMesh mesh, string name)
    {
        mesh.Name = name;
        return mesh;
    }

    /// <summary>
    /// Concatenates primitives into one mesh. Attribute streams are unioned: a primitive missing an
    /// attribute another one has is padded with that attribute's neutral value, since a single
    /// vertex buffer cannot have a stream present for only some of its vertices.
    /// </summary>
    private static IntermediateMesh MergePrimitives(List<IntermediateMesh> parts, string name, ImportContext ctx)
    {
        int totalVertices = 0;
        foreach (var p in parts) totalVertices += p.Positions.Count;

        bool anyNormals = parts.Exists(p => p.Normals is not null);
        bool anyTangents = parts.Exists(p => p.Tangents is not null);
        bool anyColors = parts.Exists(p => p.Colors0 is not null);
        bool anySkin = parts.Exists(p => p.VertexJoints is not null);

        var uvUsed = new bool[Mesh.MaxUVChannels];
        for (int uv = 0; uv < Mesh.MaxUVChannels; uv++)
            uvUsed[uv] = parts.Exists(p => p.UVs[uv] is not null);

        int influences = 4;
        foreach (var p in parts)
            if (p.VertexJoints is not null) influences = Math.Max(influences, p.MaxInfluencesPerVertex);

        // Every primitive of a mesh must expose the same morph targets, so the count is the max and
        // a primitive short of one contributes zero deltas for it.
        int targetCount = 0;
        foreach (var p in parts) targetCount = Math.Max(targetCount, p.BlendShapes.Count);

        var merged = new IntermediateMesh
        {
            Name = name,
            MaterialIndex = -1,
            MaxInfluencesPerVertex = influences,
        };

        if (anyNormals) merged.Normals = new List<Float3>(totalVertices);
        if (anyTangents) merged.Tangents = new List<Float4>(totalVertices);
        if (anyColors) merged.Colors0 = new List<Color>(totalVertices);
        for (int uv = 0; uv < Mesh.MaxUVChannels; uv++)
            if (uvUsed[uv]) merged.UVs[uv] = new List<Float2>(totalVertices);

        int[]? joints = anySkin ? new int[totalVertices * influences] : null;
        float[]? weights = anySkin ? new float[totalVertices * influences] : null;

        var shapeDeltas = new Float3[targetCount][];
        var shapeNormals = new Float3[targetCount][];
        var shapeTangents = new Float3[targetCount][];
        var shapeNames = new string[targetCount];
        var shapeHasNormals = new bool[targetCount];
        var shapeHasTangents = new bool[targetCount];

        for (int t = 0; t < targetCount; t++)
        {
            shapeDeltas[t] = new Float3[totalVertices];
            foreach (var p in parts)
            {
                if (t >= p.BlendShapes.Count || p.BlendShapes[t].Frames.Count == 0) continue;
                shapeNames[t] ??= p.BlendShapes[t].Name;
                var frame = p.BlendShapes[t].Frames[0];
                shapeHasNormals[t] |= frame.DeltaNormals is not null;
                shapeHasTangents[t] |= frame.DeltaTangents is not null;
            }
            if (shapeHasNormals[t]) shapeNormals[t] = new Float3[totalVertices];
            if (shapeHasTangents[t]) shapeTangents[t] = new Float3[totalVertices];
        }

        int offset = 0;
        foreach (var part in parts)
        {
            int count = part.Positions.Count;
            merged.Positions.AddRange(part.Positions);

            AppendOrPad(merged.Normals, part.Normals, count, new Float3(0f, 1f, 0f));
            AppendOrPad(merged.Tangents, part.Tangents, count, new Float4(1f, 0f, 0f, 1f));
            AppendOrPad(merged.Colors0, part.Colors0, count, new Color(1f, 1f, 1f, 1f));
            for (int uv = 0; uv < Mesh.MaxUVChannels; uv++)
                AppendOrPad(merged.UVs[uv], part.UVs[uv], count, Float2.Zero);

            if (joints is not null && weights is not null && part.VertexJoints is { } pj && part.VertexWeights is { } pw)
            {
                int partInfluences = part.MaxInfluencesPerVertex;
                for (int v = 0; v < count; v++)
                {
                    int src = v * partInfluences;
                    int dst = (offset + v) * influences;
                    for (int k = 0; k < Math.Min(partInfluences, influences); k++)
                    {
                        joints[dst + k] = pj[src + k];
                        weights[dst + k] = pw[src + k];
                    }
                }
            }

            for (int t = 0; t < targetCount && t < part.BlendShapes.Count; t++)
            {
                if (part.BlendShapes[t].Frames.Count == 0) continue;
                var frame = part.BlendShapes[t].Frames[0];
                Array.Copy(frame.DeltaPositions, 0, shapeDeltas[t], offset, Math.Min(frame.DeltaPositions.Length, count));
                if (shapeNormals[t] is not null && frame.DeltaNormals is { } dn)
                    Array.Copy(dn, 0, shapeNormals[t], offset, Math.Min(dn.Length, count));
                if (shapeTangents[t] is not null && frame.DeltaTangents is { } dt)
                    Array.Copy(dt, 0, shapeTangents[t], offset, Math.Min(dt.Length, count));
            }

            foreach (var face in part.Faces)
            {
                var shifted = new int[face.Indices.Length];
                for (int k = 0; k < shifted.Length; k++)
                    shifted[k] = face.Indices[k] + offset;

                // The primitive's material becomes this face's, which is what makes the merged mesh
                // resolve back into one sub-mesh per material at bake.
                merged.Faces.Add(new IntermediateFace(shifted, part.MaterialIndex));
            }

            merged.PrimitiveKinds |= part.PrimitiveKinds;
            offset += count;
        }

        merged.VertexJoints = joints;
        merged.VertexWeights = weights;

        for (int t = 0; t < targetCount; t++)
        {
            var shape = new IntermediateBlendShape { Name = shapeNames[t] ?? $"Target_{t}" };
            shape.Frames.Add(new IntermediateBlendShapeFrame
            {
                Weight = 100f,
                DeltaPositions = shapeDeltas[t],
                DeltaNormals = shapeNormals[t],
                DeltaTangents = shapeTangents[t],
            });
            merged.BlendShapes.Add(shape);
        }

        ctx.Log.Info(
            $"Merged {parts.Count} primitives of mesh '{name}' into one mesh with per-face materials.",
            "GltfMeshMapper");

        return merged;
    }

    private static void AppendOrPad<T>(List<T>? target, List<T>? source, int count, T pad)
    {
        if (target is null) return;
        if (source is not null) { target.AddRange(source); return; }
        for (int i = 0; i < count; i++) target.Add(pad);
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

        // An accessor with neither a bufferView nor a sparse overlay is legal and reads as all
        // zeros, but for POSITION that means every vertex on the origin, which is never what was
        // meant. It is what an undecoded compression extension leaves behind.
        var posAccessor = reader.Get(posIdx);
        if (posAccessor.BufferView is null && posAccessor.Sparse is null)
        {
            ctx.Log.Warning(
                $"{parentName}: the POSITION accessor has no bufferView and no sparse data, so every vertex " +
                "reads as the origin. The mesh will be a single point.",
                "GltfMeshMapper");
        }

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
