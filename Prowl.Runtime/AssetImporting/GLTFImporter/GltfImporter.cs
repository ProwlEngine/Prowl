// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;

namespace Prowl.Runtime.AssetImporting.Gltf;

public class GltfImporter
{
    // ================================================================
    //  Coordinate Conversion Helpers (RH Y-up → LH Y-up: negate Z)
    // ================================================================

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

    static Float3 ConvertPos(float[] v) => new(v[0], v[1], -v[2]);
    static Float3 ConvertPos(Float3 v) => new(v.X, v.Y, -v.Z);
    static Float3 ConvertNormal(Float3 v) => new(v.X, v.Y, -v.Z);
    static Float3 ConvertTangent(Float4 v) => new(v.X, v.Y, -v.Z);
    static float ConvertTangentW(Float4 v) => -v.W;
    static Quaternion ConvertRot(float[] q) => new(q[0], q[1], -q[2], -q[3]);
    static Quaternion ConvertRot(Quaternion q) => new(q.X, q.Y, -q.Z, -q.W);

    static Float4x4 ConvertMatrix(Float4x4 m)
    {
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

    // ================================================================
    //  TRS Decomposition from GLTF node
    // ================================================================

    private static void DecomposeNodeTRS(GltfNode node, float scale,
        out Float3 pos, out Quaternion rot, out Float3 scl)
    {
        if (node.Matrix != null && node.Matrix.Length == 16)
        {
            var m = node.Matrix;
            var mat = new Float4x4(
                new Float4(m[0], m[1], m[2], m[3]),
                new Float4(m[4], m[5], m[6], m[7]),
                new Float4(m[8], m[9], m[10], m[11]),
                new Float4(m[12], m[13], m[14], m[15]));

            mat = ConvertMatrix(mat);

            var dTrans = new Float3(mat[3, 0], mat[3, 1], mat[3, 2]);
            var col0 = new Float3(mat[0, 0], mat[0, 1], mat[0, 2]);
            var col1 = new Float3(mat[1, 0], mat[1, 1], mat[1, 2]);
            var col2 = new Float3(mat[2, 0], mat[2, 1], mat[2, 2]);
            var dScale = new Float3(Float3.Length(col0), Float3.Length(col1), Float3.Length(col2));
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
    }

    // ================================================================
    //  Public Entry Points
    // ================================================================

    public ModelImportResult Import(FileInfo assetPath, ModelImporterSettings? settings = null)
    {
        var gltf = GltfFile.Load(assetPath.FullName);
        return Build(gltf, assetPath.DirectoryName ?? "", settings ?? new ModelImporterSettings());
    }

    public ModelImportResult Import(Stream stream, string virtualPath, ModelImporterSettings? settings = null)
    {
        string ext = Path.GetExtension(virtualPath).ToLowerInvariant();
        bool isGlb = ext == ".glb";
        string basePath = Path.GetDirectoryName(virtualPath) ?? "";
        var gltf = GltfFile.Load(stream, basePath, isGlb);
        return Build(gltf, basePath, settings ?? new ModelImporterSettings());
    }

    // ================================================================
    //  Build — main pipeline
    // ================================================================

    private ModelImportResult Build(GltfFile gltf, string basePath, ModelImporterSettings settings)
    {
        var root = gltf.Root;
        float scale = settings.UnitScale;

        // 1. Load textures
        var textures = LoadTextures(gltf, basePath);

        // 2. Load materials
        var materials = LoadMaterials(root, textures);

        // 3. Build combined meshes (one Prowl Mesh per GLTF mesh, with submeshes per primitive)
        //    Also track per-primitive material index.
        var meshes = new List<Mesh>();                         // index = gltfMeshIndex
        var meshMaterials = new List<List<Material?>>();        // per mesh, per submesh material
        var meshIsSkinned = new List<bool>();                   // whether mesh has bone data
        BuildMeshes(gltf, root, materials, meshes, meshMaterials, meshIsSkinned, scale, settings);

        // 4. Build skin data (joint GOs, bind poses, bone names)
        //    skinJointNodeIndices[skinIdx] = list of node indices for joints
        var skinJointNodeIndices = new Dictionary<int, List<int>>();
        var skinRootNode = new Dictionary<int, int?>();
        var skinIBMs = new Dictionary<int, Float4x4[]>();
        if (root.Skins != null)
        {
            for (int si = 0; si < root.Skins.Count; si++)
            {
                var skin = root.Skins[si];
                skinJointNodeIndices[si] = skin.Joints;
                skinRootNode[si] = skin.Skeleton;

                Float4x4[]? ibms = null;
                if (skin.InverseBindMatrices.HasValue)
                {
                    ibms = GltfDataReader.ReadMat4(gltf, skin.InverseBindMatrices.Value);
                    for (int i = 0; i < ibms.Length; i++)
                        ibms[i] = ConvertMatrix(ibms[i]);
                }
                skinIBMs[si] = ibms ?? Array.Empty<Float4x4>();
            }
        }

        // 5. Build the GameObject hierarchy
        var nodeGOs = new Dictionary<int, GameObject>();
        var usedNames = new HashSet<string>();

        // Determine scene root nodes
        var sceneNodes = GetSceneRootNodes(root);

        string modelName = Path.GetFileNameWithoutExtension(basePath);
        if (string.IsNullOrEmpty(modelName)) modelName = "Model";

        // Create root GO
        var rootGO = new GameObject(modelName);

        // Walk GLTF nodes and create child GOs
        void WalkNode(int nodeIdx, GameObject parent)
        {
            var node = root.Nodes[nodeIdx];

            // Ensure unique name
            string goName = node.Name ?? $"Node_{nodeIdx}";
            if (!usedNames.Add(goName))
            {
                goName = $"{goName}_{nodeIdx}";
                usedNames.Add(goName);
            }

            var go = new GameObject(goName);

            // Set local TRS
            DecomposeNodeTRS(node, scale, out Float3 pos, out Quaternion rot, out Float3 scl);
            go.Transform.LocalPosition = pos;
            go.Transform.LocalRotation = rot;
            go.Transform.LocalScale = scl;

            go.SetParent(parent, worldPositionStays: false);
            nodeGOs[nodeIdx] = go;

            // Recurse children
            if (node.Children != null)
            {
                foreach (int childIdx in node.Children)
                    WalkNode(childIdx, go);
            }
        }

        foreach (int nodeIdx in sceneNodes)
            WalkNode(nodeIdx, rootGO);

        // 6. Attach mesh components to nodes
        if (root.Nodes != null)
        {
            for (int ni = 0; ni < root.Nodes.Count; ni++)
            {
                var node = root.Nodes[ni];
                if (!node.Mesh.HasValue) continue;
                int meshIdx = node.Mesh.Value;
                if (meshIdx >= meshes.Count) continue;
                if (!nodeGOs.TryGetValue(ni, out var go)) continue;

                var mesh = meshes[meshIdx];
                var mats = meshMaterials[meshIdx];
                var matRefs = mats.Select(m => new AssetRef<Material>(m)).ToList();

                if (node.Skin.HasValue && skinJointNodeIndices.ContainsKey(node.Skin.Value))
                {
                    // Skinned mesh
                    int skinIdx = node.Skin.Value;
                    var jointNodes = skinJointNodeIndices[skinIdx];
                    var ibms = skinIBMs[skinIdx];

                    // Populate mesh bind poses and bone paths (relative to hierarchy root)
                    mesh.BindPoses = new Float4x4[jointNodes.Count];
                    mesh.BoneNames = new string[jointNodes.Count];
                    for (int j = 0; j < jointNodes.Count; j++)
                    {
                        int jointNodeIdx = jointNodes[j];
                        mesh.BoneNames[j] = nodeGOs.TryGetValue(jointNodeIdx, out var jointGO)
                            ? Transform.GetRelativePath(jointGO.Transform, rootGO.Transform)
                            : (root.Nodes[jointNodeIdx].Name ?? $"Node_{jointNodeIdx}");
                        mesh.BindPoses[j] = (j < ibms.Length) ? ibms[j] : Float4x4.Identity;
                    }

                    var smr = go.AddComponent<SkinnedMeshRenderer>();
                    smr.SharedMesh = new AssetRef<Mesh>(mesh);
                    smr.Materials = matRefs;

                    // Resolve bone transforms
                    var boneTransforms = new Transform[jointNodes.Count];
                    for (int j = 0; j < jointNodes.Count; j++)
                    {
                        if (nodeGOs.TryGetValue(jointNodes[j], out var boneGO))
                            boneTransforms[j] = boneGO.Transform;
                    }

                    // Resolve root bone
                    Transform? rootBoneTransform = null;
                    int? rootBoneNodeIdx = skinRootNode.GetValueOrDefault(skinIdx);
                    if (rootBoneNodeIdx.HasValue && nodeGOs.TryGetValue(rootBoneNodeIdx.Value, out var rootBoneGO))
                        rootBoneTransform = rootBoneGO.Transform;
                    else if (jointNodes.Count > 0 && nodeGOs.TryGetValue(jointNodes[0], out var firstJointGO))
                        rootBoneTransform = firstJointGO.Transform;

                    // SetBones computes paths relative to hierarchy root
                    smr.SetBones(boneTransforms, rootBoneTransform);
                }
                else
                {
                    // Static mesh
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.Mesh = new AssetRef<Mesh>(mesh);
                    mr.Materials = matRefs;
                }
            }
        }

        // 7. Load animations (pass nodeGOs + rootGO so bone paths can be computed)
        var animations = LoadAnimations(gltf, root, scale, nodeGOs, rootGO);

        // 8. Attach AnimationComponent to root if there are clips
        if (animations.Count > 0)
        {
            var anim = rootGO.AddComponent<AnimationComponent>();
            anim.DefaultClip = new AssetRef<AnimationClip>(animations[0]);
            anim.Clips = animations.Select(c => new AssetRef<AnimationClip>(c)).ToList();
        }

        // 9. Return all live objects — the editor importer handles asset DB registration
        return new ModelImportResult
        {
            RootGO = rootGO,
            Meshes = meshes,
            Materials = materials,
            Animations = animations,
        };
    }

    // ================================================================
    //  Scene Root Nodes
    // ================================================================

    private List<int> GetSceneRootNodes(GltfRoot root)
    {
        var sceneNodes = new List<int>();

        if (root.Nodes == null || root.Nodes.Count == 0)
            return sceneNodes;

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

        return sceneNodes;
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
                if (pbr.BaseColorFactor != null && pbr.BaseColorFactor.Length >= 4)
                    mat.SetColor("_MainColor", new Color(pbr.BaseColorFactor[0], pbr.BaseColorFactor[1], pbr.BaseColorFactor[2], pbr.BaseColorFactor[3]));
                else
                    mat.SetColor("_MainColor", Color.White);

                var bcTex = ResolveTexture(root, texCache, pbr.BaseColorTexture?.Index);
                mat.SetTexture("_MainTex", bcTex ?? Texture2D.LoadDefault(DefaultTexture.Grid));

                mat.SetFloat("_Metallic", pbr.MetallicFactor ?? 1.0f);
                mat.SetFloat("_Roughness", pbr.RoughnessFactor ?? 1.0f);

                var mrTex = ResolveTexture(root, texCache, pbr.MetallicRoughnessTexture?.Index);
                mat.SetTexture("_SurfaceTex", mrTex ?? Texture2D.LoadDefault(DefaultTexture.Surface));
            }
            else
            {
                mat.SetColor("_MainColor", Color.White);
                mat.SetTexture("_MainTex", Texture2D.LoadDefault(DefaultTexture.Grid));
                mat.SetTexture("_SurfaceTex", Texture2D.LoadDefault(DefaultTexture.Surface));
            }

            var normalTex = ResolveTexture(root, texCache, gmat.NormalTexture?.Index);
            mat.SetTexture("_NormalTex", normalTex ?? Texture2D.LoadDefault(DefaultTexture.Normal));

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
    //  Meshes — one Prowl Mesh per GLTF mesh, with submeshes per primitive
    // ================================================================

    private void BuildMeshes(GltfFile gltf, GltfRoot root, List<Material> materials,
        List<Mesh> meshes, List<List<Material?>> meshMaterials, List<bool> meshIsSkinned,
        float scale, ModelImporterSettings settings)
    {
        if (root.Meshes == null) return;

        for (int mi = 0; mi < root.Meshes.Count; mi++)
        {
            var gmesh = root.Meshes[mi];
            string meshName = gmesh.Name ?? $"Mesh_{mi}";

            // Accumulate all primitives into one combined mesh
            var allVertices = new List<Float3>();
            var allNormals = new List<Float3>();
            var allTangents = new List<Float3>();
            var allUV = new List<Float2>();
            var allUV2 = new List<Float2>();
            var allColors = new List<Color>();
            var allBoneIndices = new List<Float4>();
            var allBoneWeights = new List<Float4>();
            var allIndices = new List<uint>();
            var subMeshes = new List<SubMeshDescriptor>();
            var primMaterials = new List<Material?>();
            bool hasBones = false;
            bool hasNormals = true;
            bool hasTangents = true;
            bool hasUV = true;
            bool hasUV2 = true;
            bool hasColors = true;

            for (int pi = 0; pi < gmesh.Primitives.Count; pi++)
            {
                var prim = gmesh.Primitives[pi];
                int vertexOffset = allVertices.Count;
                int indexStart = allIndices.Count;

                // --- Vertices (POSITION) ---
                Float3[] primVerts;
                if (prim.Attributes.TryGetValue("POSITION", out int posIdx))
                {
                    var raw = GltfDataReader.ReadVec3(gltf, posIdx);
                    primVerts = new Float3[raw.Length];
                    for (int i = 0; i < raw.Length; i++)
                        primVerts[i] = ConvertPos(raw[i]) * scale;
                }
                else
                {
                    Debug.LogWarning($"[GLTF] Mesh {meshName} primitive {pi} has no POSITION attribute, skipping.");
                    continue;
                }

                int primVertCount = primVerts.Length;
                allVertices.AddRange(primVerts);

                // --- Normals ---
                if (prim.Attributes.TryGetValue("NORMAL", out int normIdx))
                {
                    var raw = GltfDataReader.ReadVec3(gltf, normIdx);
                    var normals = new Float3[raw.Length];
                    for (int i = 0; i < raw.Length; i++)
                        normals[i] = ConvertNormal(raw[i]);
                    allNormals.AddRange(normals);
                }
                else
                {
                    hasNormals = false;
                    // Add placeholders to keep arrays aligned
                    for (int i = 0; i < primVertCount; i++)
                        allNormals.Add(Float3.Zero);
                }

                // --- Tangents ---
                if (prim.Attributes.TryGetValue("TANGENT", out int tanIdx))
                {
                    var raw = GltfDataReader.ReadVec4(gltf, tanIdx);
                    var tangents = new Float3[raw.Length];
                    for (int i = 0; i < raw.Length; i++)
                        tangents[i] = ConvertTangent(raw[i]);
                    allTangents.AddRange(tangents);
                }
                else
                {
                    hasTangents = false;
                    for (int i = 0; i < primVertCount; i++)
                        allTangents.Add(Float3.Zero);
                }

                // --- UV0 ---
                if (prim.Attributes.TryGetValue("TEXCOORD_0", out int uv0Idx))
                {
                    var raw = GltfDataReader.ReadVec2(gltf, uv0Idx);
                    if (settings.FlipUVs)
                        for (int i = 0; i < raw.Length; i++)
                            raw[i] = new Float2(raw[i].X, 1f - raw[i].Y);
                    allUV.AddRange(raw);
                }
                else
                {
                    hasUV = false;
                    for (int i = 0; i < primVertCount; i++)
                        allUV.Add(Float2.Zero);
                }

                // --- UV1 ---
                if (prim.Attributes.TryGetValue("TEXCOORD_1", out int uv1Idx))
                {
                    var raw = GltfDataReader.ReadVec2(gltf, uv1Idx);
                    if (settings.FlipUVs)
                        for (int i = 0; i < raw.Length; i++)
                            raw[i] = new Float2(raw[i].X, 1f - raw[i].Y);
                    allUV2.AddRange(raw);
                }
                else
                {
                    hasUV2 = false;
                    for (int i = 0; i < primVertCount; i++)
                        allUV2.Add(Float2.Zero);
                }

                // --- Colors ---
                if (prim.Attributes.TryGetValue("COLOR_0", out int colIdx))
                {
                    allColors.AddRange(GltfDataReader.ReadColors(gltf, colIdx));
                }
                else
                {
                    hasColors = false;
                    for (int i = 0; i < primVertCount; i++)
                        allColors.Add(Color.White);
                }

                // --- Bone indices and weights ---
                if (prim.Attributes.TryGetValue("JOINTS_0", out int jointsIdx) &&
                    prim.Attributes.TryGetValue("WEIGHTS_0", out int weightsIdx))
                {
                    var jointsAccessor = gltf.Root.Accessors[jointsIdx];
                    bool savedJointsNorm = jointsAccessor.Normalized ?? false;
                    jointsAccessor.Normalized = false;
                    var joints = GltfDataReader.ReadVec4(gltf, jointsIdx);
                    jointsAccessor.Normalized = savedJointsNorm;

                    var weightsAccessor = gltf.Root.Accessors[weightsIdx];
                    bool savedWeightsNorm = weightsAccessor.Normalized ?? false;
                    if (weightsAccessor.ComponentType != 5126)
                        weightsAccessor.Normalized = true;
                    var weights = GltfDataReader.ReadVec4(gltf, weightsIdx);
                    weightsAccessor.Normalized = savedWeightsNorm;

                    // GLTF joints are 0-based into skin.joints[]. Prowl uses 0="no bone", so +1.
                    for (int i = 0; i < joints.Length; i++)
                        allBoneIndices.Add(new Float4(joints[i].X + 1, joints[i].Y + 1, joints[i].Z + 1, joints[i].W + 1));

                    // Normalize weights
                    for (int i = 0; i < weights.Length; i++)
                    {
                        float sum = weights[i].X + weights[i].Y + weights[i].Z + weights[i].W;
                        if (sum > 0.0001f && MathF.Abs(sum - 1.0f) > 0.001f)
                            weights[i] /= sum;
                    }
                    allBoneWeights.AddRange(weights);
                    hasBones = true;
                }
                else
                {
                    // No bones for this primitive — add zero-weight entries
                    for (int i = 0; i < primVertCount; i++)
                    {
                        allBoneIndices.Add(Float4.Zero);
                        allBoneWeights.Add(Float4.Zero);
                    }
                }

                // --- Indices ---
                uint[] primIndices;
                if (prim.Indices.HasValue)
                {
                    primIndices = GltfDataReader.ReadIndices(gltf, prim.Indices.Value);

                    // Reverse triangle winding for RH->LH
                    int mode = prim.Mode ?? 4;
                    if (mode == 4) // Triangles
                    {
                        for (int i = 0; i + 2 < primIndices.Length; i += 3)
                            (primIndices[i + 1], primIndices[i + 2]) = (primIndices[i + 2], primIndices[i + 1]);
                    }
                }
                else
                {
                    primIndices = new uint[primVertCount];
                    for (uint i = 0; i < primVertCount; i++)
                        primIndices[i] = i;
                }

                // Offset indices by the vertex offset of this primitive
                for (int i = 0; i < primIndices.Length; i++)
                    primIndices[i] += (uint)vertexOffset;

                allIndices.AddRange(primIndices);

                // Record submesh
                subMeshes.Add(new SubMeshDescriptor(indexStart, primIndices.Length, Topology.Triangles));

                // Material for this submesh
                Material? primMat = null;
                if (prim.Material.HasValue && prim.Material.Value < materials.Count)
                    primMat = materials[prim.Material.Value];
                primMaterials.Add(primMat);
            }

            if (allVertices.Count == 0)
            {
                // All primitives were skipped — add a placeholder
                meshes.Add(new Mesh { Name = meshName });
                meshMaterials.Add(new List<Material?>());
                meshIsSkinned.Add(false);
                continue;
            }

            // Assemble the combined Prowl Mesh
            var mesh = new Mesh();
            mesh.Name = meshName;
            mesh.MeshTopology = Topology.Triangles;
            mesh.Vertices = allVertices.ToArray();

            if (hasNormals)
                mesh.Normals = allNormals.ToArray();
            if (hasTangents)
                mesh.Tangents = allTangents.ToArray();
            if (hasUV)
                mesh.UV = allUV.ToArray();
            if (hasUV2)
                mesh.UV2 = allUV2.ToArray();
            if (hasColors)
                mesh.Colors = allColors.ToArray();

            if (hasBones)
            {
                mesh.BoneIndices = allBoneIndices.ToArray();
                mesh.BoneWeights = allBoneWeights.ToArray();
            }

            mesh.IndexFormat = allVertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.Indices = allIndices.ToArray();

            // Generate normals if missing
            if (!hasNormals && settings.GenerateNormals)
                GenerateNormals(mesh, settings.GenerateSmoothNormals);

            // Generate tangents if missing
            if (!hasTangents && settings.CalculateTangentSpace && mesh.Normals != null && mesh.UV != null)
                GenerateTangents(mesh);

            // Set submeshes
            if (subMeshes.Count > 1)
            {
                mesh.SetSubMeshCount(subMeshes.Count);
                for (int s = 0; s < subMeshes.Count; s++)
                    mesh.SetSubMesh(s, subMeshes[s]);
            }

            mesh.RecalculateBounds();

            meshes.Add(mesh);
            meshMaterials.Add(primMaterials);
            meshIsSkinned.Add(hasBones);
        }
    }

    // ================================================================
    //  Animations
    // ================================================================

    private List<AnimationClip> LoadAnimations(GltfFile gltf, GltfRoot root, float scale,
        Dictionary<int, GameObject> nodeGOs, GameObject rootGO)
    {
        var result = new List<AnimationClip>();
        if (root.Animations == null) return result;

        // Build node index → relative path map (paths relative to rootGO, excluding root name)
        var nodePathMap = new Dictionary<int, string>();
        foreach (var (nodeIdx, go) in nodeGOs)
            nodePathMap[nodeIdx] = Transform.GetRelativePath(go.Transform, rootGO.Transform);

        foreach (var ganim in root.Animations)
        {
            var clip = new AnimationClip();
            clip.Name = ganim.Name ?? $"Animation_{result.Count}";

            float maxTime = 0f;
            var boneMap = new Dictionary<int, AnimationClip.AnimBone>();

            foreach (var channel in ganim.Channels)
            {
                if (!channel.Target.Node.HasValue) continue;
                int nodeIdx = channel.Target.Node.Value;

                if (!boneMap.TryGetValue(nodeIdx, out var animBone))
                {
                    // Use path from rootGO as the bone name
                    string bonePath = nodePathMap.TryGetValue(nodeIdx, out var p)
                        ? p
                        : (root.Nodes != null && nodeIdx < root.Nodes.Count
                            ? (root.Nodes[nodeIdx].Name ?? $"Node_{nodeIdx}")
                            : $"Node_{nodeIdx}");
                    animBone = new AnimationClip.AnimBone { BoneName = bonePath };
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
                            animBone.ScaleX.Keys.Add(new KeyFrame(times[i], values[i].X));
                            animBone.ScaleY.Keys.Add(new KeyFrame(times[i], values[i].Y));
                            animBone.ScaleZ.Keys.Add(new KeyFrame(times[i], values[i].Z));
                        }
                        break;
                    }
                }
            }

            clip.Duration = maxTime;
            clip.TicksPerSecond = 1.0f;
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
            var orthogonalized = t - n * Float3.Dot(n, t);
            float lenSq = Float3.Dot(orthogonalized, orthogonalized);
            mesh.Tangents[i] = lenSq > 1e-8f ? orthogonalized / MathF.Sqrt(lenSq) : Float3.UnitX;
        }
    }
}
