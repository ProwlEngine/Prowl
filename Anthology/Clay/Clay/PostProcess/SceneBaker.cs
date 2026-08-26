// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Converts a post-processed <see cref="IntermediateScene"/> into the immutable public
/// <see cref="Model"/>.
/// </summary>
internal static class SceneBaker
{
    public static Model Bake(IntermediateScene scene, ImportContext context)
    {
        AssignNodeIndices(scene.Nodes);

        // Skin bake produces (Skin[], inverseBindPose array per mesh).
        var bakedSkins = BakeSkins(scene);

        // Bake meshes (need to know skin assignment per mesh to populate BindPoses + BoneWeights).
        var meshSkinIndex = BuildMeshToSkinMap(scene, bakedSkins);
        var bakedMeshes = new List<Mesh>(scene.Meshes.Count);
        for (int i = 0; i < scene.Meshes.Count; i++)
            bakedMeshes.Add(BakeMesh(scene.Meshes[i], meshSkinIndex[i], bakedSkins, context));

        var bakedMaterials = new List<Material>(scene.Materials.Count);
        foreach (var im in scene.Materials)
            bakedMaterials.Add(BakeMaterial(im));

        var bakedTextures = new List<Texture>(scene.Textures.Count);
        foreach (var it in scene.Textures)
            bakedTextures.Add(BakeTexture(it));

        var bakedCameras = new List<Camera>(scene.Cameras.Count);
        foreach (var c in scene.Cameras)
            bakedCameras.Add(BakeCamera(c));

        var bakedLights = new List<Light>(scene.Lights.Count);
        foreach (var l in scene.Lights)
            bakedLights.Add(BakeLight(l));

        var bakedNodes = new ModelNode[scene.Nodes.Count];
        ModelNode? bakedRoot = null;
        for (int i = 0; i < scene.Nodes.Count; i++)
        {
            var src = scene.Nodes[i];
            var local = Float4x4.CreateTRS(src.LocalPosition, src.LocalRotation, src.LocalScale);
            var world = src.Parent is { } parent && parent.BakeIndex >= 0
                ? bakedNodes[parent.BakeIndex].WorldMatrix * local
                : local;

            var node = new ModelNode
            {
                Index = i,
                Name = src.Name,
                LocalPosition = src.LocalPosition,
                LocalRotation = src.LocalRotation,
                LocalScale = src.LocalScale,
                LocalMatrix = local,
                WorldMatrix = world,
                MeshIndex = src.MeshIndex,
                SkinIndex = src.SkinIndex,
                CameraIndex = src.CameraIndex,
                LightIndex = src.LightIndex,
                Metadata = src.Metadata.Count == 0
                    ? new Dictionary<string, object?>()
                    : new Dictionary<string, object?>(src.Metadata),
            };
            bakedNodes[i] = node;
            if (src.Parent is null)
                bakedRoot = node;
        }

        for (int i = 0; i < scene.Nodes.Count; i++)
        {
            var src = scene.Nodes[i];
            var dst = bakedNodes[i];
            if (src.Parent is { BakeIndex: var pi } && pi >= 0)
                dst.Parent = bakedNodes[pi];

            if (src.Children.Count == 0)
            {
                dst.Children = Array.Empty<ModelNode>();
            }
            else
            {
                var arr = new ModelNode[src.Children.Count];
                for (int c = 0; c < arr.Length; c++)
                    arr[c] = bakedNodes[src.Children[c].BakeIndex];
                dst.Children = arr;
            }
        }

        // Skins reference baked node indices; finalize them after the node bake.
        var publicSkins = new List<Skin>(bakedSkins.Count);
        foreach (var s in bakedSkins)
        {
            int[] boneIndices = new int[s.IntermediateSkin.BoneNodes.Count];
            for (int b = 0; b < boneIndices.Length; b++)
                boneIndices[b] = s.IntermediateSkin.BoneNodes[b].BakeIndex;

            publicSkins.Add(new Skin
            {
                Name = s.IntermediateSkin.Name,
                RootNodeIndex = s.IntermediateSkin.RootNode?.BakeIndex ?? -1,
                BoneNodeIndices = boneIndices,
                InverseBindPoses = s.IntermediateSkin.InverseBindPoses.ToArray(),
            });
        }

        var bakedAnimations = new List<AnimationClip>(scene.Animations.Count);
        foreach (var src in scene.Animations)
            bakedAnimations.Add(BakeAnimation(src, bakedNodes, context));

        var metadata = new ModelMetadata
        {
            Format = scene.Format,
            FormatVersion = scene.FormatVersion,
            Generator = scene.Generator,
            Copyright = scene.Copyright,
            RawExtensions = scene.RawExtensions.Count == 0
                ? new Dictionary<string, System.Text.Json.JsonElement>()
                : new Dictionary<string, System.Text.Json.JsonElement>(scene.RawExtensions),
            Extras = scene.Extras.Count == 0
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(scene.Extras),
        };

        return new Model
        {
            SourcePath = context.SourcePath,
            Metadata = metadata,
            Root = bakedRoot ?? throw new ImportException("Scene has no root node.", context.SourcePath, context.Format),
            Nodes = bakedNodes,
            Meshes = bakedMeshes,
            Materials = bakedMaterials,
            Textures = bakedTextures,
            Cameras = bakedCameras,
            Lights = bakedLights,
            Skins = publicSkins,
            AnimationClips = bakedAnimations,
            Log = context.Log,
        };
    }

    private sealed class BakedSkin
    {
        public required IntermediateSkin IntermediateSkin { get; init; }
        public required int Index { get; init; }
    }

    private static List<BakedSkin> BakeSkins(IntermediateScene scene)
    {
        var list = new List<BakedSkin>(scene.Skins.Count);
        for (int i = 0; i < scene.Skins.Count; i++)
            list.Add(new BakedSkin { IntermediateSkin = scene.Skins[i], Index = i });
        return list;
    }

    /// <summary>
    /// For each IntermediateMesh, decide which Skin (if any) supplies its BindPoses array.
    /// We use the first node that references the mesh with a non-negative SkinIndex.
    /// </summary>
    private static int[] BuildMeshToSkinMap(IntermediateScene scene, List<BakedSkin> skins)
    {
        var result = new int[scene.Meshes.Count];
        Array.Fill(result, -1);
        if (skins.Count == 0) return result;

        foreach (var node in scene.Nodes)
        {
            if (node.MeshIndex < 0 || node.SkinIndex < 0) continue;
            if (node.MeshIndex >= result.Length) continue;
            if (result[node.MeshIndex] == -1)
                result[node.MeshIndex] = node.SkinIndex;
        }
        return result;
    }

    private static void AssignNodeIndices(List<IntermediateNode> nodes)
    {
        for (int i = 0; i < nodes.Count; i++)
            nodes[i].BakeIndex = i;
    }

    private static Mesh BakeMesh(IntermediateMesh src, int skinIndex, List<BakedSkin> skins, ImportContext context)
    {
        int vertexCount = src.Positions.Count;

        bool hasPoints = (src.PrimitiveKinds & PrimitiveKind.Point) != 0;
        bool hasLines = (src.PrimitiveKinds & PrimitiveKind.Line) != 0;
        bool hasTris = (src.PrimitiveKinds & PrimitiveKind.Triangle) != 0;
        bool hasPolys = (src.PrimitiveKinds & PrimitiveKind.Polygon) != 0;
        if (hasPolys) hasTris = true;

        var indicesList = new List<uint>(EstimateIndexCount(src));
        var submeshes = new List<SubMesh>(3);
        AppendSubMeshes(src, indicesList, submeshes, PrimitiveTopology.Triangles, hasTris, vertexCount);
        AppendSubMeshes(src, indicesList, submeshes, PrimitiveTopology.Lines, hasLines, vertexCount);
        AppendSubMeshes(src, indicesList, submeshes, PrimitiveTopology.Points, hasPoints, vertexCount);

        uint[] indices = indicesList.ToArray();
        bool has32 = vertexCount > ushort.MaxValue;

        BoneWeight[]? boneWeights = BakeBoneWeights(src, context);
        Float4x4[]? bindPoses = null;
        if (skinIndex >= 0 && skinIndex < skins.Count)
            bindPoses = skins[skinIndex].IntermediateSkin.InverseBindPoses.ToArray();

        BlendShape[] blendShapes = BakeBlendShapes(src);

        Bounds bounds = ComputeBounds(src.Positions);

        var mesh = new Mesh
        {
            Name = src.Name,
            Vertices = src.Positions.ToArray(),
            Normals = src.Normals?.ToArray(),
            Tangents = src.Tangents?.ToArray(),
            Colors = src.Colors0?.ToArray(),
            UVs = src.UVs.Select(u => u?.ToArray()).ToArray(),
            SubMeshes = submeshes.ToArray(),
            Indices = indices,
            Bounds = bounds,
            Has32BitIndices = has32,
            BoneWeights = boneWeights,
            BindPoses = bindPoses,
            BlendShapes = blendShapes,
        };

        for (int s = 0; s < mesh.SubMeshes.Length; s++)
        {
            var sm = mesh.SubMeshes[s];
            Bounds sb = Bounds.Empty;
            for (int i = 0; i < sm.IndexCount; i++)
                sb.Encapsulate(mesh.Vertices[(int)indices[sm.IndexStart + i] + sm.BaseVertex]);
            mesh.SubMeshes[s] = new SubMesh
            {
                Topology = sm.Topology,
                IndexStart = sm.IndexStart,
                IndexCount = sm.IndexCount,
                BaseVertex = sm.BaseVertex,
                MaterialIndex = sm.MaterialIndex,
                Bounds = sb,
            };
        }

        return mesh;
    }

    /// <summary>
    /// Reduces each vertex's influences to the fixed-four <see cref="BoneWeight"/> layout, keeping the
    /// strongest and renormalising them.
    /// </summary>
    /// <remarks>
    /// This used to take the first four in file order and leave the weights as they were. A vertex
    /// with eight influences summing to 1 then reached the GPU summing to whatever those four
    /// happened to be, and under linear blend skinning a vertex whose weights sum to less than 1
    /// collapses toward the origin, so meshes visibly deflated.
    /// </remarks>
    private static BoneWeight[]? BakeBoneWeights(IntermediateMesh src, ImportContext context)
    {
        if (src.VertexJoints is null || src.VertexWeights is null)
            return null;

        int n = src.Positions.Count;
        int influences = src.MaxInfluencesPerVertex;
        if (influences <= 0)
            return null;

        int expected = n * influences;
        if (src.VertexJoints.Length < expected || src.VertexWeights.Length < expected)
            throw new ImportException(
                $"Mesh '{src.Name}' declares {influences} bone influences for {n} vertices, "
                + $"which needs {expected} entries but only {Math.Min(src.VertexJoints.Length, src.VertexWeights.Length)} are present.");

        if (influences > BoneWeight.MaxInfluences)
            context.Log.Warning(
                $"Mesh '{src.Name}' has {influences} bone influences per vertex; keeping the strongest "
                + $"{BoneWeight.MaxInfluences}. Run the LimitBoneWeights step to control how this is reduced.",
                "SceneBaker");

        var result = new BoneWeight[n];
        Span<int> keptJoints = stackalloc int[BoneWeight.MaxInfluences];
        Span<float> keptWeights = stackalloc float[BoneWeight.MaxInfluences];

        for (int v = 0; v < n; v++)
        {
            int b = v * influences;
            keptJoints.Clear();
            keptWeights.Clear();

            SelectStrongest(src.VertexJoints, src.VertexWeights, b, influences, keptJoints, keptWeights);

            // Only when influences were dropped, so a mesh that already fits keeps the weights the
            // file authored rather than having the bake quietly rescale them.
            if (influences > BoneWeight.MaxInfluences)
                Renormalise(keptWeights);

            result[v] = new BoneWeight
            {
                Index0 = keptJoints[0], Weight0 = keptWeights[0],
                Index1 = keptJoints[1], Weight1 = keptWeights[1],
                Index2 = keptJoints[2], Weight2 = keptWeights[2],
                Index3 = keptJoints[3], Weight3 = keptWeights[3],
            };
        }
        return result;
    }

    /// <summary>Fills the kept spans with the highest-weighted influences, strongest first.</summary>
    private static void SelectStrongest(
        int[] joints, float[] weights, int start, int count,
        Span<int> keptJoints, Span<float> keptWeights)
    {
        int last = keptWeights.Length - 1;
        for (int k = 0; k < count; k++)
        {
            float w = weights[start + k];
            if (w <= keptWeights[last]) continue;

            int slot = last;
            while (slot > 0 && keptWeights[slot - 1] < w)
                slot--;
            for (int m = last; m > slot; m--)
            {
                keptWeights[m] = keptWeights[m - 1];
                keptJoints[m] = keptJoints[m - 1];
            }
            keptWeights[slot] = w;
            keptJoints[slot] = joints[start + k];
        }
    }

    private static void Renormalise(Span<float> weights)
    {
        float sum = 0f;
        for (int i = 0; i < weights.Length; i++)
            sum += weights[i];
        // A vertex with no influence at all has nothing to scale; inventing one would bind it to
        // whichever bone happens to be index 0.
        if (sum <= 1e-6f) return;
        for (int i = 0; i < weights.Length; i++)
            weights[i] /= sum;
    }

    private static BlendShape[] BakeBlendShapes(IntermediateMesh src)
    {
        if (src.BlendShapes.Count == 0)
            return Array.Empty<BlendShape>();

        var result = new BlendShape[src.BlendShapes.Count];
        for (int i = 0; i < src.BlendShapes.Count; i++)
        {
            var sib = src.BlendShapes[i];
            var frames = new BlendShapeFrame[sib.Frames.Count];
            for (int f = 0; f < sib.Frames.Count; f++)
            {
                var srcFrame = sib.Frames[f];
                frames[f] = new BlendShapeFrame
                {
                    Weight = srcFrame.Weight,
                    DeltaVertices = srcFrame.DeltaPositions,
                    DeltaNormals = srcFrame.DeltaNormals,
                    DeltaTangents = srcFrame.DeltaTangents,
                };
            }
            result[i] = new BlendShape
            {
                Name = sib.Name,
                Frames = frames,
            };
        }
        return result;
    }

    private static AnimationClip BakeAnimation(IntermediateAnimation src, ModelNode[] bakedNodes, ImportContext ctx)
    {
        var bindings = new List<AnimationBinding>(src.Bindings.Count);
        for (int i = 0; i < src.Bindings.Count; i++)
        {
            var b = src.Bindings[i];
            int nodeIdx = b.TargetNode?.BakeIndex ?? -1;
            if (nodeIdx < 0)
                ctx.Log.Warning(
                    $"Animation '{src.Name}': binding {i} has no resolved target node; using -1.",
                    nameof(SceneBaker));

            bindings.Add(new AnimationBinding
            {
                NodeIndex = nodeIdx,
                Property = b.Property,
                SubIndex = b.SubIndex,
                Curve = BuildCurve(b),
            });
        }

        // Measured from the authored curves, before the backfill runs. A backfilled channel is one
        // key holding forever, so its time is arbitrary; letting it into the range would drag the
        // start of every late-starting clip back to zero.
        float start = float.PositiveInfinity;
        float end = float.NegativeInfinity;
        foreach (var binding in bindings)
        {
            if (binding.Curve.Count == 0) continue;
            start = MathF.Min(start, binding.Curve.StartTime);
            end = MathF.Max(end, binding.Curve.EndTime);
        }
        if (float.IsInfinity(start)) { start = 0f; end = 0f; }

        // Placed at the start so the synthesized key lines up with the authored ones rather than
        // sitting outside the clip.
        BackfillMissingTRSBindings(bindings, bakedNodes, start);

        return new AnimationClip
        {
            Name = src.Name,
            StartTime = start,
            EndTime = end,
            Bindings = bindings.ToArray(),
        };
    }

    /// <summary>
    /// Turns a binding's flat key data into a curve. Cubic sampler output is interleaved as
    /// in-tangent, value, out-tangent per key, which the curve knows how to unpack, so the tangents
    /// survive instead of being thrown away and re-derived.
    /// </summary>
    private static AnimationCurve BuildCurve(IntermediateAnimationBinding b)
    {
        float[] times = b.Times.ToArray();
        float[] values = b.Values.ToArray();

        AnimationCurve curve = b.Interpolation == CurveInterpolation.CubicSpline
            ? AnimationCurve.FromGltfCubicSpline(b.Dimension, times, values)
            : AnimationCurve.FromPacked(b.Dimension, times, values, b.Interpolation);

        // A rotation curve interpolated component-wise across a sign flip sweeps the long way round.
        // Slerp handles that itself, so this only matters for the cubic case, but it costs nothing.
        if (b.Property == AnimatedProperty.Rotation && b.Dimension == 4)
            curve.EnsureQuaternionContinuity();

        return curve;
    }

    /// <summary>
    /// For every node that has at least one Position/Rotation/Scale binding in this clip, ensure
    /// all three of P/R/S exist by synthesizing constant single-key bindings at the node's bind
    /// pose Lcl T/R/S for any channel the source didn't author. Mixamo-style FBX rigs are the
    /// canonical case: they animate only rotation for non-root bones, leaving consumers that
    /// unconditionally drive bone.LocalPosition every frame (e.g. Prowl's AnimationComponent)
    /// to snap those bones to origin. Backfilling at bake time means every Clay consumer gets
    /// a complete 9-channel-per-bone clip without having to special-case missing channels.
    /// </summary>
    private static void BackfillMissingTRSBindings(List<AnimationBinding> bindings, ModelNode[] bakedNodes, float atTime)
    {
        // Bit flags per node: 1=has Position, 2=has Rotation, 4=has Scale. Limited to SubIndex==0
        // (TRS only has one slot; SubIndex is used by blend-shape-weight bindings).
        var present = new Dictionary<int, int>();
        foreach (var b in bindings)
        {
            if (b.SubIndex != 0) continue;
            if (b.NodeIndex < 0) continue;
            int bit = b.Property switch
            {
                AnimatedProperty.Position => 1,
                AnimatedProperty.Rotation => 2,
                AnimatedProperty.Scale => 4,
                _ => 0,
            };
            if (bit == 0) continue;
            present.TryGetValue(b.NodeIndex, out int mask);
            present[b.NodeIndex] = mask | bit;
        }

        foreach (var kv in present)
        {
            int nodeIdx = kv.Key;
            int mask = kv.Value;
            if (mask == 0b111) continue; // all three already present
            if ((uint)nodeIdx >= (uint)bakedNodes.Length) continue;
            var n = bakedNodes[nodeIdx];

            if ((mask & 1) == 0)
                bindings.Add(MakeConstantBinding(atTime, nodeIdx, AnimatedProperty.Position, 3, n.LocalPosition.X, n.LocalPosition.Y, n.LocalPosition.Z));
            if ((mask & 2) == 0)
                bindings.Add(MakeConstantBinding(atTime, nodeIdx, AnimatedProperty.Rotation, 4, n.LocalRotation.X, n.LocalRotation.Y, n.LocalRotation.Z, n.LocalRotation.W));
            if ((mask & 4) == 0)
                bindings.Add(MakeConstantBinding(atTime, nodeIdx, AnimatedProperty.Scale, 3, n.LocalScale.X, n.LocalScale.Y, n.LocalScale.Z));
        }
    }

    private static AnimationBinding MakeConstantBinding(float atTime, int nodeIndex, AnimatedProperty prop, int dim, params float[] values)
    {
        return new AnimationBinding
        {
            NodeIndex = nodeIndex,
            Property = prop,
            SubIndex = 0,
            Curve = AnimationCurve.FromPacked(dim, new[] { atTime }, values, CurveInterpolation.Linear),
        };
    }

    /// <summary>
    /// Emits one <see cref="SubMesh"/> per material within a topology, which is what makes a mesh
    /// carrying several materials draw correctly: each range is one draw call with one material.
    /// Materials keep the order they are first met in the face list, so a merged glTF mesh's
    /// sub-meshes come out in its primitive order.
    /// </summary>
    private static void AppendSubMeshes(
        IntermediateMesh src,
        List<uint> indicesList,
        List<SubMesh> submeshes,
        PrimitiveTopology topology,
        bool include,
        int vertexCount)
    {
        if (!include) return;

        int indicesPerFace = topology switch
        {
            PrimitiveTopology.Triangles => 3,
            PrimitiveTopology.Lines => 2,
            PrimitiveTopology.Points => 1,
            _ => 3,
        };

        var materialOrder = new List<int>();
        foreach (var face in src.Faces)
        {
            if (face.Indices.Length != indicesPerFace) continue;
            int material = src.MaterialForFace(face);
            if (!materialOrder.Contains(material)) materialOrder.Add(material);
        }

        foreach (int material in materialOrder)
        {
            int start = indicesList.Count;
            foreach (var face in src.Faces)
            {
                if (face.Indices.Length != indicesPerFace) continue;
                if (src.MaterialForFace(face) != material) continue;
                for (int k = 0; k < face.Indices.Length; k++)
                {
                    int index = face.Indices[k];
                    // Checked here rather than left to the bounds pass below, which would fault on it
                    // with nothing to say about which mesh the bad index came from.
                    if ((uint)index >= (uint)vertexCount)
                        throw new ImportException(
                            $"Mesh '{src.Name}' has index {index} but only {vertexCount} vertices.");
                    indicesList.Add((uint)index);
                }
            }

            int count = indicesList.Count - start;
            if (count == 0) continue;

            submeshes.Add(new SubMesh
            {
                Topology = topology,
                IndexStart = start,
                IndexCount = count,
                BaseVertex = 0,
                MaterialIndex = material,
                Bounds = Bounds.Empty,
            });
        }
    }

    private static int EstimateIndexCount(IntermediateMesh src)
    {
        int sum = 0;
        foreach (var face in src.Faces)
            sum += face.Indices.Length;
        return sum;
    }

    private static Bounds ComputeBounds(List<Float3> positions)
    {
        Bounds b = Bounds.Empty;
        for (int i = 0; i < positions.Count; i++)
            b.Encapsulate(positions[i]);
        return b;
    }

    private static Material BakeMaterial(IntermediateMaterial src) => new()
    {
        Name = src.Name,
        AlphaMode = src.AlphaMode,
        AlphaCutoff = src.AlphaCutoff,
        DoubleSided = src.DoubleSided,
        Unlit = src.Unlit,
        BaseColor = src.BaseColor,
        BaseColorTexture = CopySlot(src.BaseColorTexture),
        Metallic = src.Metallic,
        Roughness = src.Roughness,
        MetallicRoughnessTexture = CopySlot(src.MetallicRoughnessTexture),
        NormalTexture = CopySlot(src.NormalTexture),
        NormalScale = src.NormalScale,
        OcclusionTexture = CopySlot(src.OcclusionTexture),
        OcclusionStrength = src.OcclusionStrength,
        EmissiveFactor = src.EmissiveFactor,
        EmissiveTexture = CopySlot(src.EmissiveTexture),
        EmissiveStrength = src.EmissiveStrength,
        Clearcoat = src.Clearcoat,
        Sheen = src.Sheen,
        Transmission = src.Transmission,
        Volume = src.Volume,
        Ior = src.Ior,
        Specular = src.Specular,
        SpecularGlossiness = src.SpecularGlossiness,
        RawExtensions = src.RawExtensions.Count == 0
            ? new Dictionary<string, System.Text.Json.JsonElement>()
            : new Dictionary<string, System.Text.Json.JsonElement>(src.RawExtensions),
    };

    private static MaterialTextureSlot? CopySlot(IntermediateTextureSlot? s) =>
        s is null ? null : new MaterialTextureSlot
        {
            TextureIndex = s.TextureIndex,
            UVChannel = s.UVChannel,
            Offset = s.Offset,
            Scale = s.Scale,
            Rotation = s.Rotation,
        };

    private static Camera BakeCamera(IntermediateCamera src) => new()
    {
        Name = src.Name,
        Projection = src.Projection,
        VerticalFovRadians = src.VerticalFovRadians,
        AspectRatio = src.AspectRatio,
        OrthographicHalfWidth = src.OrthographicHalfWidth,
        OrthographicHalfHeight = src.OrthographicHalfHeight,
        NearPlane = src.NearPlane,
        FarPlane = src.FarPlane,
    };

    private static Light BakeLight(IntermediateLight src) => new()
    {
        Name = src.Name,
        Type = src.Type,
        Color = src.Color,
        Intensity = src.Intensity,
        Range = src.Range,
        InnerConeAngleRadians = src.InnerConeAngleRadians,
        OuterConeAngleRadians = src.OuterConeAngleRadians,
    };

    private static Texture BakeTexture(IntermediateTexture src) => new()
    {
        Name = src.Name,
        SourcePath = src.SourcePath,
        EncodedBytes = src.EncodedBytes,
        MimeType = src.MimeType,
        Sampler = new TextureSampler
        {
            WrapU = src.Sampler.WrapU,
            WrapV = src.Sampler.WrapV,
            MinFilter = src.Sampler.MinFilter,
            MagFilter = src.Sampler.MagFilter,
            GenerateMipmaps = src.Sampler.GenerateMipmaps,
        },
    };

}
