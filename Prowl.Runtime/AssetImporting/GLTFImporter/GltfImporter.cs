// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;

namespace Prowl.Runtime.AssetImporting.Gltf;

public class GltfImporter
{
    // Quaternion from rotation matrix columns (Shoemake algorithm)
    static Quaternion QuaternionFromAxes(Float3 col0, Float3 col1, Float3 col2)
    {
        float m00 = col0.X, m01 = col1.X, m02 = col2.X;
        float m10 = col0.Y, m11 = col1.Y, m12 = col2.Y;
        float m20 = col0.Z, m21 = col1.Z, m22 = col2.Z;
        float trace = m00 + m11 + m22;
        Quaternion q;
        if (trace > 0)
        {
            float s = 0.5f / MathF.Sqrt(trace + 1.0f);
            q = new Quaternion((m21 - m12) * s, (m02 - m20) * s, (m10 - m01) * s, 0.25f / s);
        }
        else if (m00 > m11 && m00 > m22)
        {
            float s = 2.0f * MathF.Sqrt(1.0f + m00 - m11 - m22);
            q = new Quaternion(0.25f * s, (m01 + m10) / s, (m02 + m20) / s, (m21 - m12) / s);
        }
        else if (m11 > m22)
        {
            float s = 2.0f * MathF.Sqrt(1.0f + m11 - m00 - m22);
            q = new Quaternion((m01 + m10) / s, 0.25f * s, (m12 + m21) / s, (m02 - m20) / s);
        }
        else
        {
            float s = 2.0f * MathF.Sqrt(1.0f + m22 - m00 - m11);
            q = new Quaternion((m02 + m20) / s, (m12 + m21) / s, 0.25f * s, (m10 - m01) / s);
        }
        return Quaternion.NormalizeSafe(q);
    }

    // GLTF is right-handed Y-up; Prowl is left-handed Y-up → negate Z
    static Float3 ConvertPos(float[] v) => new(v[0], v[1], -v[2]);
    static Float3 ConvertPos(Float3 v) => new(v.X, v.Y, -v.Z);
    static Float3 ConvertNormal(Float3 v) => new(v.X, v.Y, -v.Z);
    static Float3 ConvertTangent(Float4 v) => new(v.X, v.Y, -v.Z);
    static float ConvertTangentW(Float4 v) => -v.W; // flip handedness
    static Quaternion ConvertRot(float[] q) => new(q[0], q[1], -q[2], -q[3]);
    static Quaternion ConvertRot(Quaternion q) => new(q.X, q.Y, -q.Z, -q.W);

    static Float4x4 ConvertMatrix(Float4x4 m)
    {
        // Negate Z column (col 2) and Z row (row 2) to convert RH→LH
        // Float4x4 uses [col, row] indexer and column-vector constructor
        var result = m;
        // Negate column 2 (Z column)
        result[2, 0] = -result[2, 0];
        result[2, 1] = -result[2, 1];
        // result[2,2] stays (double negate)
        result[2, 3] = -result[2, 3];
        // Negate row 2 (Z row)
        result[0, 2] = -result[0, 2];
        result[1, 2] = -result[1, 2];
        // result[2,2] already handled
        result[3, 2] = -result[3, 2];
        return result;
    }

    public Model Import(FileInfo assetPath, ModelImporterSettings? settings = null)
    {
        var gltf = GltfFile.Load(assetPath.FullName);
        return Build(gltf, assetPath.DirectoryName ?? "", settings ?? new ModelImporterSettings());
    }

    public Model Import(Stream stream, string virtualPath, ModelImporterSettings? settings = null)
    {
        string ext = Path.GetExtension(virtualPath).ToLowerInvariant();
        bool isGlb = ext == ".glb";
        string basePath = Path.GetDirectoryName(virtualPath) ?? "";
        var gltf = GltfFile.Load(stream, basePath, isGlb);
        return Build(gltf, basePath, settings ?? new ModelImporterSettings());
    }

    private Model Build(GltfFile gltf, string basePath, ModelImporterSettings settings)
    {
        var root = gltf.Root;
        float scale = settings.UnitScale;

        // Load textures (cached by image index)
        var textures = LoadTextures(gltf, basePath);

        // Load materials
        var materials = LoadMaterials(root, textures);

        // Load meshes — each GLTF primitive becomes one Prowl Mesh
        // Track mapping: gltfMeshIndex → list of prowl mesh indices
        var allMeshes = new List<ModelMesh>();
        var meshMapping = new Dictionary<int, List<int>>();
        LoadMeshes(gltf, root, materials, allMeshes, meshMapping, scale, settings);

        // Build skeleton from node hierarchy
        var skeleton = BuildSkeleton(gltf, root, meshMapping, scale);

        // Populate bind poses on skinned meshes
        PopulateBindPoses(gltf, root, skeleton, allMeshes, meshMapping);

        // Load animations
        var animations = LoadAnimations(gltf, root, scale);

        // Assemble model
        var model = new Model(Path.GetFileName(basePath));
        model.UnitScale = settings.UnitScale;
        model.Materials = materials.Select(m => new AssetRef<Material>(m)).ToList();
        model.Meshes = allMeshes;
        model.Skeleton = skeleton;
        model.Animations = animations;

        if (skeleton.IsValid())
        {
            foreach (var clip in model.Animations)
            {
                clip.Skeleton = skeleton;
                clip.RebuildBoneMapping();
            }
        }

        return model;
    }

    // ================================================================
    //  Textures
    // ================================================================

    private Dictionary<int, Texture2D> LoadTextures(GltfFile gltf, string basePath)
    {
        var cache = new Dictionary<int, Texture2D>();
        if (gltf.Root.Images == null) return cache;

        for (int i = 0; i < gltf.Root.Images.Count; i++)
        {
            var image = gltf.Root.Images[i];
            try
            {
                if (image.BufferView.HasValue)
                {
                    // Embedded image in buffer
                    var bv = gltf.Root.BufferViews[image.BufferView.Value];
                    var buffer = gltf.Buffers[bv.Buffer];
                    int offset = bv.ByteOffset;
                    int length = bv.ByteLength;
                    using var ms = new MemoryStream(buffer, offset, length);
                    cache[i] = Texture2D.LoadFromStream(ms, true);
                }
                else if (!string.IsNullOrEmpty(image.Uri))
                {
                    if (image.Uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                    {
                        // Base64 data URI
                        int commaIdx = image.Uri.IndexOf(',');
                        if (commaIdx >= 0)
                        {
                            byte[] data = Convert.FromBase64String(image.Uri[(commaIdx + 1)..]);
                            using var ms = new MemoryStream(data);
                            cache[i] = Texture2D.LoadFromStream(ms, true);
                        }
                    }
                    else
                    {
                        // External file
                        string path = Path.Combine(basePath, Uri.UnescapeDataString(image.Uri));
                        if (File.Exists(path))
                            cache[i] = Texture2D.LoadFromFile(path, true);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[GLTF] Failed to load image {i}: {ex.Message}");
            }
        }

        return cache;
    }

    private Texture2D? ResolveTexture(GltfRoot root, Dictionary<int, Texture2D> texCache, int? textureIndex)
    {
        if (!textureIndex.HasValue || root.Textures == null) return null;
        var tex = root.Textures[textureIndex.Value];
        if (tex.Source.HasValue && texCache.TryGetValue(tex.Source.Value, out var t))
            return t;
        return null;
    }

    // ================================================================
    //  Materials
    // ================================================================

    private List<Material> LoadMaterials(GltfRoot root, Dictionary<int, Texture2D> texCache)
    {
        var result = new List<Material>();
        if (root.Materials == null) return result;

        foreach (var gmat in root.Materials)
        {
            var mat = new Material(Shader.LoadDefault(DefaultShader.Standard));
            mat.Name = gmat.Name ?? "Material";

            var pbr = gmat.PbrMetallicRoughness;
            if (pbr != null)
            {
                // Base color
                if (pbr.BaseColorFactor != null && pbr.BaseColorFactor.Length >= 4)
                    mat.SetColor("_MainColor", new Color(pbr.BaseColorFactor[0], pbr.BaseColorFactor[1], pbr.BaseColorFactor[2], pbr.BaseColorFactor[3]));
                else
                    mat.SetColor("_MainColor", Color.White);

                // Base color texture
                var bcTex = ResolveTexture(root, texCache, pbr.BaseColorTexture?.Index);
                mat.SetTexture("_MainTex", bcTex ?? Texture2D.LoadDefault(DefaultTexture.Grid));

                // Metallic / Roughness
                mat.SetFloat("_Metallic", pbr.MetallicFactor ?? 1.0f);
                mat.SetFloat("_Roughness", pbr.RoughnessFactor ?? 1.0f);

                // MetallicRoughness texture → _SurfaceTex (GLTF: R=occlusion, G=roughness, B=metallic)
                var mrTex = ResolveTexture(root, texCache, pbr.MetallicRoughnessTexture?.Index);
                mat.SetTexture("_SurfaceTex", mrTex ?? Texture2D.LoadDefault(DefaultTexture.Surface));
            }
            else
            {
                mat.SetColor("_MainColor", Color.White);
                mat.SetTexture("_MainTex", Texture2D.LoadDefault(DefaultTexture.Grid));
                mat.SetTexture("_SurfaceTex", Texture2D.LoadDefault(DefaultTexture.Surface));
            }

            // Normal texture
            var normalTex = ResolveTexture(root, texCache, gmat.NormalTexture?.Index);
            mat.SetTexture("_NormalTex", normalTex ?? Texture2D.LoadDefault(DefaultTexture.Normal));

            // Emission
            var emTex = ResolveTexture(root, texCache, gmat.EmissiveTexture?.Index);
            mat.SetTexture("_EmissionTex", emTex ?? Texture2D.LoadDefault(DefaultTexture.Emission));

            if (gmat.EmissiveFactor != null && gmat.EmissiveFactor.Length >= 3)
            {
                float intensity = MathF.Max(gmat.EmissiveFactor[0], MathF.Max(gmat.EmissiveFactor[1], gmat.EmissiveFactor[2]));
                mat.SetFloat("_EmissionIntensity", intensity > 0 ? 1f : 0f);
                mat.SetColor("_EmissiveColor", new Color(gmat.EmissiveFactor[0], gmat.EmissiveFactor[1], gmat.EmissiveFactor[2], 1f));
            }
            else
            {
                mat.SetFloat("_EmissionIntensity", 0f);
                mat.SetColor("_EmissiveColor", Color.Black);
            }

            result.Add(mat);
        }

        return result;
    }

    // ================================================================
    //  Meshes
    // ================================================================

    private void LoadMeshes(GltfFile gltf, GltfRoot root, List<Material> materials,
        List<ModelMesh> allMeshes, Dictionary<int, List<int>> meshMapping,
        float scale, ModelImporterSettings settings)
    {
        if (root.Meshes == null) return;

        for (int mi = 0; mi < root.Meshes.Count; mi++)
        {
            var gmesh = root.Meshes[mi];
            var prowlIndices = new List<int>();
            meshMapping[mi] = prowlIndices;

            for (int pi = 0; pi < gmesh.Primitives.Count; pi++)
            {
                var prim = gmesh.Primitives[pi];
                var mesh = new Mesh();
                string meshName = gmesh.Name ?? $"Mesh_{mi}";
                if (gmesh.Primitives.Count > 1) meshName += $"_{pi}";
                mesh.Name = meshName;

                // Topology
                mesh.MeshTopology = (prim.Mode ?? 4) switch
                {
                    0 => Topology.Points,
                    1 => Topology.Lines,
                    2 => Topology.LineLoop,
                    3 => Topology.LineStrip,
                    4 => Topology.Triangles,
                    5 => Topology.TriangleStrip,
                    6 => Topology.TriangleFan,
                    _ => Topology.Triangles,
                };

                // Vertices (POSITION) — required
                if (prim.Attributes.TryGetValue("POSITION", out int posIdx))
                {
                    var raw = GltfDataReader.ReadVec3(gltf, posIdx);
                    mesh.Vertices = new Float3[raw.Length];
                    for (int i = 0; i < raw.Length; i++)
                        mesh.Vertices[i] = ConvertPos(raw[i]) * scale;
                }
                else
                {
                    Debug.LogWarning($"[GLTF] Mesh {meshName} has no POSITION attribute, skipping.");
                    continue;
                }

                // Normals
                if (prim.Attributes.TryGetValue("NORMAL", out int normIdx))
                {
                    var raw = GltfDataReader.ReadVec3(gltf, normIdx);
                    mesh.Normals = new Float3[raw.Length];
                    for (int i = 0; i < raw.Length; i++)
                        mesh.Normals[i] = ConvertNormal(raw[i]);
                }
                else if (settings.GenerateNormals)
                {
                    // Will generate after indices are loaded
                }

                // Tangents (GLTF stores as Vec4 with W=handedness)
                if (prim.Attributes.TryGetValue("TANGENT", out int tanIdx))
                {
                    var raw = GltfDataReader.ReadVec4(gltf, tanIdx);
                    mesh.Tangents = new Float3[raw.Length];
                    for (int i = 0; i < raw.Length; i++)
                        mesh.Tangents[i] = ConvertTangent(raw[i]);
                }

                // UV0
                if (prim.Attributes.TryGetValue("TEXCOORD_0", out int uv0Idx))
                {
                    var raw = GltfDataReader.ReadVec2(gltf, uv0Idx);
                    if (settings.FlipUVs)
                        for (int i = 0; i < raw.Length; i++)
                            raw[i] = new Float2(raw[i].X, 1f - raw[i].Y);
                    mesh.UV = raw;
                }

                // UV1
                if (prim.Attributes.TryGetValue("TEXCOORD_1", out int uv1Idx))
                {
                    var raw = GltfDataReader.ReadVec2(gltf, uv1Idx);
                    if (settings.FlipUVs)
                        for (int i = 0; i < raw.Length; i++)
                            raw[i] = new Float2(raw[i].X, 1f - raw[i].Y);
                    mesh.UV2 = raw;
                }

                // Colors
                if (prim.Attributes.TryGetValue("COLOR_0", out int colIdx))
                    mesh.Colors = GltfDataReader.ReadColors(gltf, colIdx);

                // Bone indices and weights
                bool hasBones = false;
                if (prim.Attributes.TryGetValue("JOINTS_0", out int jointsIdx) &&
                    prim.Attributes.TryGetValue("WEIGHTS_0", out int weightsIdx))
                {
                    var joints = GltfDataReader.ReadVec4(gltf, jointsIdx);
                    var weights = GltfDataReader.ReadVec4(gltf, weightsIdx);

                    // GLTF joints are 0-based into skin.joints[]. Prowl uses 0="no bone", so +1.
                    mesh.BoneIndices = new Float4[joints.Length];
                    for (int i = 0; i < joints.Length; i++)
                        mesh.BoneIndices[i] = new Float4(joints[i].X + 1, joints[i].Y + 1, joints[i].Z + 1, joints[i].W + 1);

                    mesh.BoneWeights = weights;
                    hasBones = true;
                }

                // IndexFormat must be set BEFORE Indices (setter clears indices)
                mesh.IndexFormat = mesh.Vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;

                // Indices
                if (prim.Indices.HasValue)
                {
                    mesh.Indices = GltfDataReader.ReadIndices(gltf, prim.Indices.Value);

                    // Reverse triangle winding for RH→LH conversion
                    if (mesh.MeshTopology == Topology.Triangles)
                    {
                        for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
                            (mesh.Indices[i + 1], mesh.Indices[i + 2]) = (mesh.Indices[i + 2], mesh.Indices[i + 1]);
                    }
                }
                else
                {
                    // Non-indexed: generate sequential indices
                    mesh.Indices = new uint[mesh.Vertices.Length];
                    for (uint i = 0; i < mesh.Vertices.Length; i++)
                        mesh.Indices[i] = i;
                }

                // Generate normals if missing
                if (mesh.Normals == null && settings.GenerateNormals)
                    GenerateNormals(mesh, settings.GenerateSmoothNormals);

                // Generate tangents if missing
                if (mesh.Tangents == null && settings.CalculateTangentSpace && mesh.Normals != null && mesh.UV != null)
                    GenerateTangents(mesh);

                mesh.RecalculateBounds();

                // Determine material
                int prowlIdx = allMeshes.Count;
                prowlIndices.Add(prowlIdx);

                Material? meshMat = null;
                if (prim.Material.HasValue && prim.Material.Value < materials.Count)
                    meshMat = materials[prim.Material.Value];

                allMeshes.Add(new ModelMesh(meshName, mesh, meshMat, hasBones));
            }
        }
    }

    // ================================================================
    //  Skeleton
    // ================================================================

    private Skeleton BuildSkeleton(GltfFile gltf, GltfRoot root,
        Dictionary<int, List<int>> meshMapping, float scale)
    {
        if (root.Nodes == null || root.Nodes.Count == 0)
            return null;

        var skeleton = new Skeleton();
        var nodeIdToBoneId = new Dictionary<int, int>();

        // Walk all nodes depth-first to build bones
        void WalkNode(int nodeIdx, int parentBoneId)
        {
            var node = root.Nodes[nodeIdx];
            int boneId = skeleton.Bones.Count;
            nodeIdToBoneId[nodeIdx] = boneId;

            // Extract transform
            Float3 pos;
            Quaternion rot;
            Float3 scl;

            if (node.Matrix != null && node.Matrix.Length == 16)
            {
                // GLTF stores column-major: [c0r0, c0r1, c0r2, c0r3, c1r0, ...]
                // Float4x4 constructor takes (col0, col1, col2, col3)
                var m = node.Matrix;
                var mat = new Float4x4(
                    new Float4(m[0], m[1], m[2], m[3]),
                    new Float4(m[4], m[5], m[6], m[7]),
                    new Float4(m[8], m[9], m[10], m[11]),
                    new Float4(m[12], m[13], m[14], m[15]));

                mat = ConvertMatrix(mat);

                // Manual TRS decomposition
                // Translation is column 3
                var dTrans = new Float3(mat[3, 0], mat[3, 1], mat[3, 2]);
                // Scale is the length of each column
                var col0 = new Float3(mat[0, 0], mat[0, 1], mat[0, 2]);
                var col1 = new Float3(mat[1, 0], mat[1, 1], mat[1, 2]);
                var col2 = new Float3(mat[2, 0], mat[2, 1], mat[2, 2]);
                var dScale = new Float3(Float3.Length(col0), Float3.Length(col1), Float3.Length(col2));
                // Rotation from normalized columns
                if (dScale.X > 1e-6f) col0 /= dScale.X;
                if (dScale.Y > 1e-6f) col1 /= dScale.Y;
                if (dScale.Z > 1e-6f) col2 /= dScale.Z;
                var dRot = QuaternionFromAxes(col0, col1, col2);

                pos = dTrans * scale;
                rot = dRot;
                scl = dScale;
            }
            else
            {
                pos = node.Translation != null && node.Translation.Length >= 3
                    ? ConvertPos(node.Translation) * scale
                    : Float3.Zero;
                rot = node.Rotation != null && node.Rotation.Length >= 4
                    ? ConvertRot(node.Rotation)
                    : Quaternion.Identity;
                scl = node.Scale != null && node.Scale.Length >= 3
                    ? new Float3(node.Scale[0], node.Scale[1], node.Scale[2])
                    : Float3.One;
            }

            var bone = new Skeleton.Bone(boneId, node.Name ?? $"Node_{nodeIdx}");
            bone.ParentID = parentBoneId;
            bone.BindPosition = pos;
            bone.BindRotation = rot;
            bone.BindScale = scl;

            // Mesh attachments
            if (node.Mesh.HasValue && meshMapping.TryGetValue(node.Mesh.Value, out var prowlMeshIds))
            {
                bone.MeshIndex = prowlMeshIds.Count > 0 ? prowlMeshIds[0] : null;
                bone.MeshIndices = prowlMeshIds;
            }

            skeleton.AddBone(bone);

            // Recurse children
            if (node.Children != null)
            {
                foreach (int childIdx in node.Children)
                    WalkNode(childIdx, boneId);
            }
        }

        // Start from the default scene's root nodes, or all root nodes
        var sceneNodes = new List<int>();
        if (root.Scene.HasValue && root.Scenes != null && root.Scene.Value < root.Scenes.Count)
            sceneNodes.AddRange(root.Scenes[root.Scene.Value].Nodes ?? []);
        else if (root.Scenes != null && root.Scenes.Count > 0)
            sceneNodes.AddRange(root.Scenes[0].Nodes ?? []);
        else
        {
            // No scenes — walk all nodes that aren't children of other nodes
            var childSet = new HashSet<int>();
            for (int i = 0; i < root.Nodes.Count; i++)
                if (root.Nodes[i].Children != null)
                    foreach (var c in root.Nodes[i].Children)
                        childSet.Add(c);
            for (int i = 0; i < root.Nodes.Count; i++)
                if (!childSet.Contains(i))
                    sceneNodes.Add(i);
        }

        foreach (int nodeIdx in sceneNodes)
            WalkNode(nodeIdx, -1);

        // Apply inverse bind matrices from skins
        if (root.Skins != null)
        {
            foreach (var skin in root.Skins)
            {
                Float4x4[]? ibms = null;
                if (skin.InverseBindMatrices.HasValue)
                {
                    ibms = GltfDataReader.ReadMat4(gltf, skin.InverseBindMatrices.Value);
                    for (int i = 0; i < ibms.Length; i++)
                        ibms[i] = ConvertMatrix(ibms[i]);
                }

                for (int j = 0; j < skin.Joints.Count; j++)
                {
                    int nodeIdx = skin.Joints[j];
                    if (nodeIdToBoneId.TryGetValue(nodeIdx, out int boneId))
                    {
                        if (ibms != null && j < ibms.Length)
                            skeleton.Bones[boneId].OffsetMatrix = ibms[j];
                    }
                }
            }
        }

        return skeleton;
    }

    private void PopulateBindPoses(GltfFile gltf, GltfRoot root, Skeleton skeleton,
        List<ModelMesh> allMeshes, Dictionary<int, List<int>> meshMapping)
    {
        if (root.Skins == null || skeleton == null || root.Nodes == null || root.Meshes == null) return;

        // For each skin, populate bindPoses and boneNames on the meshes that use it
        for (int si = 0; si < root.Skins.Count; si++)
        {
            var skin = root.Skins[si];

            for (int ni = 0; ni < root.Nodes.Count; ni++)
            {
                var node = root.Nodes[ni];
                if (node.Skin != si || !node.Mesh.HasValue) continue;

                // Use meshMapping to find the correct Prowl mesh indices
                if (!meshMapping.TryGetValue(node.Mesh.Value, out var prowlMeshIds)) continue;

                foreach (int prowlIdx in prowlMeshIds)
                {
                    if (prowlIdx >= allMeshes.Count) continue;

                    var modelMesh = allMeshes[prowlIdx];
                    var mesh = modelMesh.Mesh.Res;
                    if (mesh == null) continue;

                    // Set bind poses and bone names
                    mesh.bindPoses = new Float4x4[skin.Joints.Count];
                    mesh.boneNames = new string[skin.Joints.Count];

                    for (int j = 0; j < skin.Joints.Count; j++)
                    {
                        int jointNodeIdx = skin.Joints[j];
                        var jointNode = root.Nodes[jointNodeIdx];
                        mesh.boneNames[j] = jointNode.Name ?? $"Node_{jointNodeIdx}";

                        var bone = skeleton.GetBone(mesh.boneNames[j]);
                        mesh.bindPoses[j] = bone?.OffsetMatrix ?? Float4x4.Identity;
                    }
                }
            }
        }
    }

    // ================================================================
    //  Animations
    // ================================================================

    private List<AnimationClip> LoadAnimations(GltfFile gltf, GltfRoot root, float scale)
    {
        var result = new List<AnimationClip>();
        if (root.Animations == null) return result;

        foreach (var ganim in root.Animations)
        {
            var clip = new AnimationClip();
            clip.Name = ganim.Name ?? $"Animation_{result.Count}";

            float maxTime = 0f;
            var boneMap = new Dictionary<int, AnimationClip.AnimBone>(); // nodeIndex → AnimationClip.AnimBone

            foreach (var channel in ganim.Channels)
            {
                if (!channel.Target.Node.HasValue) continue;
                int nodeIdx = channel.Target.Node.Value;

                if (!boneMap.TryGetValue(nodeIdx, out var animBone))
                {
                    string nodeName = root.Nodes != null && nodeIdx < root.Nodes.Count
                        ? (root.Nodes[nodeIdx].Name ?? $"Node_{nodeIdx}")
                        : $"Node_{nodeIdx}";
                    animBone = new AnimationClip.AnimBone { BoneName = nodeName };
                    boneMap[nodeIdx] = animBone;
                }

                var sampler = ganim.Samplers[channel.Sampler];
                var times = GltfDataReader.ReadScalars(gltf, sampler.Input);

                if (times.Length > 0)
                    maxTime = MathF.Max(maxTime, times[^1]);

                switch (channel.Target.Path)
                {
                    case "translation":
                    {
                        var values = GltfDataReader.ReadVec3(gltf, sampler.Output);
                        animBone.PosX ??= new AnimationCurve(new List<KeyFrame>());
                        animBone.PosY ??= new AnimationCurve(new List<KeyFrame>());
                        animBone.PosZ ??= new AnimationCurve(new List<KeyFrame>());
                        for (int i = 0; i < Math.Min(times.Length, values.Length); i++)
                        {
                            var v = ConvertPos(values[i]) * scale;
                            animBone.PosX.Keys.Add(new KeyFrame(times[i], v.X));
                            animBone.PosY.Keys.Add(new KeyFrame(times[i], v.Y));
                            animBone.PosZ.Keys.Add(new KeyFrame(times[i], v.Z));
                        }
                        break;
                    }
                    case "rotation":
                    {
                        var values = GltfDataReader.ReadVec4(gltf, sampler.Output);
                        animBone.RotX ??= new AnimationCurve(new List<KeyFrame>());
                        animBone.RotY ??= new AnimationCurve(new List<KeyFrame>());
                        animBone.RotZ ??= new AnimationCurve(new List<KeyFrame>());
                        animBone.RotW ??= new AnimationCurve(new List<KeyFrame>());
                        for (int i = 0; i < Math.Min(times.Length, values.Length); i++)
                        {
                            // GLTF quaternion: [x, y, z, w]
                            var q = ConvertRot(new Quaternion(values[i].X, values[i].Y, values[i].Z, values[i].W));
                            animBone.RotX.Keys.Add(new KeyFrame(times[i], q.X));
                            animBone.RotY.Keys.Add(new KeyFrame(times[i], q.Y));
                            animBone.RotZ.Keys.Add(new KeyFrame(times[i], q.Z));
                            animBone.RotW.Keys.Add(new KeyFrame(times[i], q.W));
                        }
                        break;
                    }
                    case "scale":
                    {
                        var values = GltfDataReader.ReadVec3(gltf, sampler.Output);
                        animBone.ScaleX ??= new AnimationCurve(new List<KeyFrame>());
                        animBone.ScaleY ??= new AnimationCurve(new List<KeyFrame>());
                        animBone.ScaleZ ??= new AnimationCurve(new List<KeyFrame>());
                        for (int i = 0; i < Math.Min(times.Length, values.Length); i++)
                        {
                            // Scale is unchanged for handedness conversion
                            animBone.ScaleX.Keys.Add(new KeyFrame(times[i], values[i].X));
                            animBone.ScaleY.Keys.Add(new KeyFrame(times[i], values[i].Y));
                            animBone.ScaleZ.Keys.Add(new KeyFrame(times[i], values[i].Z));
                        }
                        break;
                    }
                }
            }

            clip.Duration = maxTime;
            clip.TicksPerSecond = 1.0f; // GLTF uses seconds directly
            clip.DurationInTicks = maxTime;

            foreach (var animBone in boneMap.Values)
                clip.AddBone(animBone);

            clip.EnsureQuaternionContinuity();
            result.Add(clip);
        }

        return result;
    }

    // ================================================================
    //  Normal / Tangent Generation
    // ================================================================

    private static void GenerateNormals(Mesh mesh, bool smooth)
    {
        mesh.Normals = new Float3[mesh.Vertices.Length];

        if (smooth)
        {
            // Accumulate face normals per vertex
            for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
            {
                int i0 = (int)mesh.Indices[i], i1 = (int)mesh.Indices[i + 1], i2 = (int)mesh.Indices[i + 2];
                var e1 = mesh.Vertices[i1] - mesh.Vertices[i0];
                var e2 = mesh.Vertices[i2] - mesh.Vertices[i0];
                var fn = Float3.Cross(e1, e2);
                mesh.Normals[i0] += fn;
                mesh.Normals[i1] += fn;
                mesh.Normals[i2] += fn;
            }
            for (int i = 0; i < mesh.Normals.Length; i++)
                mesh.Normals[i] = Float3.Normalize(mesh.Normals[i]);
        }
        else
        {
            // Flat normals: same normal for all vertices of each face
            for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
            {
                int i0 = (int)mesh.Indices[i], i1 = (int)mesh.Indices[i + 1], i2 = (int)mesh.Indices[i + 2];
                var e1 = mesh.Vertices[i1] - mesh.Vertices[i0];
                var e2 = mesh.Vertices[i2] - mesh.Vertices[i0];
                var fn = Float3.Normalize(Float3.Cross(e1, e2));
                mesh.Normals[i0] = fn;
                mesh.Normals[i1] = fn;
                mesh.Normals[i2] = fn;
            }
        }
    }

    private static void GenerateTangents(Mesh mesh)
    {
        // Mikktspace-like tangent generation
        var tangents = new Float3[mesh.Vertices.Length];
        var bitangents = new Float3[mesh.Vertices.Length];

        for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
        {
            int i0 = (int)mesh.Indices[i], i1 = (int)mesh.Indices[i + 1], i2 = (int)mesh.Indices[i + 2];

            var v0 = mesh.Vertices[i0]; var v1 = mesh.Vertices[i1]; var v2 = mesh.Vertices[i2];
            var uv0 = mesh.UV[i0]; var uv1 = mesh.UV[i1]; var uv2 = mesh.UV[i2];

            var dv1 = v1 - v0; var dv2 = v2 - v0;
            var duv1 = uv1 - uv0; var duv2 = uv2 - uv0;

            float r = duv1.X * duv2.Y - duv1.Y * duv2.X;
            if (MathF.Abs(r) < 1e-10f) continue;
            r = 1.0f / r;

            var t = (dv1 * duv2.Y - dv2 * duv1.Y) * r;
            var b = (dv2 * duv1.X - dv1 * duv2.X) * r;

            tangents[i0] += t; tangents[i1] += t; tangents[i2] += t;
            bitangents[i0] += b; bitangents[i1] += b; bitangents[i2] += b;
        }

        mesh.Tangents = new Float3[mesh.Vertices.Length];
        for (int i = 0; i < mesh.Vertices.Length; i++)
        {
            var n = mesh.Normals[i];
            var t = tangents[i];
            // Gram-Schmidt orthogonalize
            mesh.Tangents[i] = Float3.Normalize(t - n * Float3.Dot(n, t));
        }
    }
}
