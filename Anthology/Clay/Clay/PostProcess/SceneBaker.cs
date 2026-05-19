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
            bakedMeshes.Add(BakeMesh(scene.Meshes[i], meshSkinIndex[i], bakedSkins));

        var bakedMaterials = new List<Material>(scene.Materials.Count);
        foreach (var im in scene.Materials)
            bakedMaterials.Add(BakeMaterial(im));

        var bakedTextures = new List<Texture>(scene.Textures.Count);
        foreach (var it in scene.Textures)
            bakedTextures.Add(BakeTexture(it));

        var bakedNodes = new ModelNode[scene.Nodes.Count];
        ModelNode? bakedRoot = null;
        for (int i = 0; i < scene.Nodes.Count; i++)
        {
            var src = scene.Nodes[i];
            var local = SceneBakerHelpers.ComposeTRS(src.LocalPosition, src.LocalRotation, src.LocalScale);
            var world = src.Parent is { } parent && parent.BakeIndex >= 0
                ? SceneBakerHelpers.Mul(bakedNodes[parent.BakeIndex].WorldMatrix, local)
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

    private static Mesh BakeMesh(IntermediateMesh src, int skinIndex, List<BakedSkin> skins)
    {
        int vertexCount = src.Positions.Count;

        bool hasPoints = (src.PrimitiveKinds & PrimitiveKind.Point) != 0;
        bool hasLines = (src.PrimitiveKinds & PrimitiveKind.Line) != 0;
        bool hasTris = (src.PrimitiveKinds & PrimitiveKind.Triangle) != 0;
        bool hasPolys = (src.PrimitiveKinds & PrimitiveKind.Polygon) != 0;
        if (hasPolys) hasTris = true;

        var indicesList = new List<uint>(EstimateIndexCount(src));
        var submeshes = new List<SubMesh>(3);
        AppendSubMesh(src, indicesList, submeshes, PrimitiveTopology.Triangles, hasTris);
        AppendSubMesh(src, indicesList, submeshes, PrimitiveTopology.Lines, hasLines);
        AppendSubMesh(src, indicesList, submeshes, PrimitiveTopology.Points, hasPoints);

        uint[] indices = indicesList.ToArray();
        bool has32 = vertexCount > ushort.MaxValue;

        BoneWeight[]? boneWeights = BakeBoneWeights(src);
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

    private static BoneWeight[]? BakeBoneWeights(IntermediateMesh src)
    {
        if (src.VertexJoints is null || src.VertexWeights is null)
            return null;

        int n = src.Positions.Count;
        int influences = src.MaxInfluencesPerVertex;
        // The public BoneWeight is fixed-4. If influences > 4 here, LimitBoneWeights wasn't run;
        // we still produce a result by truncating.
        int copyCount = Math.Min(4, influences);

        var result = new BoneWeight[n];
        for (int v = 0; v < n; v++)
        {
            int b = v * influences;
            var bw = new BoneWeight();
            if (copyCount >= 1) { bw.Index0 = src.VertexJoints[b + 0]; bw.Weight0 = src.VertexWeights[b + 0]; }
            if (copyCount >= 2) { bw.Index1 = src.VertexJoints[b + 1]; bw.Weight1 = src.VertexWeights[b + 1]; }
            if (copyCount >= 3) { bw.Index2 = src.VertexJoints[b + 2]; bw.Weight2 = src.VertexWeights[b + 2]; }
            if (copyCount >= 4) { bw.Index3 = src.VertexJoints[b + 3]; bw.Weight3 = src.VertexWeights[b + 3]; }
            result[v] = bw;
        }
        return result;
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
                Curve = new AnimationCurve
                {
                    Interpolation = b.Interpolation,
                    Dimension = b.Dimension,
                    Times = b.Times.ToArray(),
                    Values = b.Values.ToArray(),
                },
            });
        }

        BackfillMissingTRSBindings(bindings, bakedNodes);

        return new AnimationClip
        {
            Name = src.Name,
            Duration = src.Duration,
            Bindings = bindings.ToArray(),
        };
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
    private static void BackfillMissingTRSBindings(List<AnimationBinding> bindings, ModelNode[] bakedNodes)
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
                bindings.Add(MakeConstantBinding(nodeIdx, AnimatedProperty.Position, 3, n.LocalPosition.X, n.LocalPosition.Y, n.LocalPosition.Z));
            if ((mask & 2) == 0)
                bindings.Add(MakeConstantBinding(nodeIdx, AnimatedProperty.Rotation, 4, n.LocalRotation.X, n.LocalRotation.Y, n.LocalRotation.Z, n.LocalRotation.W));
            if ((mask & 4) == 0)
                bindings.Add(MakeConstantBinding(nodeIdx, AnimatedProperty.Scale, 3, n.LocalScale.X, n.LocalScale.Y, n.LocalScale.Z));
        }
    }

    private static AnimationBinding MakeConstantBinding(int nodeIndex, AnimatedProperty prop, int dim, params float[] values)
    {
        return new AnimationBinding
        {
            NodeIndex = nodeIndex,
            Property = prop,
            SubIndex = 0,
            Curve = new AnimationCurve
            {
                Interpolation = AnimationInterpolation.Linear,
                Dimension = dim,
                Times = new[] { 0f },
                Values = values,
            },
        };
    }

    private static void AppendSubMesh(
        IntermediateMesh src,
        List<uint> indicesList,
        List<SubMesh> submeshes,
        PrimitiveTopology topology,
        bool include)
    {
        if (!include) return;

        int indicesPerFace = topology switch
        {
            PrimitiveTopology.Triangles => 3,
            PrimitiveTopology.Lines => 2,
            PrimitiveTopology.Points => 1,
            _ => 3,
        };

        int start = indicesList.Count;
        foreach (var face in src.Faces)
        {
            if (face.Indices.Length != indicesPerFace) continue;
            for (int k = 0; k < face.Indices.Length; k++)
                indicesList.Add((uint)face.Indices[k]);
        }
        int count = indicesList.Count - start;
        if (count == 0) return;

        submeshes.Add(new SubMesh
        {
            Topology = topology,
            IndexStart = start,
            IndexCount = count,
            BaseVertex = 0,
            MaterialIndex = src.MaterialIndex,
            Bounds = Bounds.Empty,
        });
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
