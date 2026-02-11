// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Silk.NET.Assimp;

using Prowl.Runtime.Resources;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;
using Scene = Silk.NET.Assimp.Scene;
using ImageMagick;

namespace Prowl.Runtime.AssetImporting;

public struct ModelImporterSettings
{
    public bool GenerateNormals = true;
    public bool GenerateSmoothNormals = false;
    public bool CalculateTangentSpace = true;
    public bool MakeLeftHanded = true;
    public bool FlipUVs = false;
    //public bool CullEmpty = false;
    public bool OptimizeGraph = false;
    public bool OptimizeMeshes = false;
    public bool FlipWindingOrder = true;
    public bool WeldVertices = false;
    public bool InvertNormals = false;
    public bool GlobalScale = false;

    public float UnitScale = 1.0f;

    public ModelImporterSettings() { }
}

public class ModelImporter
{
    private readonly Assimp _assimp;

    public ModelImporter()
    {
        _assimp = Assimp.GetApi();
    }

    private void Failed(string reason)
    {
        Debug.LogError($"Failed to Import Model: {reason}");
        throw new Exception(reason);
    }

    public Model Import(FileInfo assetPath, ModelImporterSettings? settings = null) =>
        ImportFromFile(assetPath.FullName, assetPath.Directory, assetPath.Extension, settings);

    public Model Import(Stream stream, string virtualPath, ModelImporterSettings? settings = null) =>
        ImportFromStream(stream, virtualPath, null, Path.GetExtension(virtualPath), settings);

    private unsafe Model ImportFromFile(string filePath, DirectoryInfo? parentDir, string extension, ModelImporterSettings? settings = null)
    {
        // new settings if null
        settings ??= new ModelImporterSettings();

        PostProcessSteps steps = GetPostProcessSteps(settings.Value);
        Scene* scene = _assimp.ImportFile(filePath, (uint)steps);

        try
        {
            if (scene == null) Failed("Assimp returned null object.");

            if (scene->MNumMeshes == 0) Failed("Model has no Meshes.");

            float scale = GetScale(settings.Value, extension);

            return BuildModel(scene, filePath, parentDir, scale, settings.Value);
        }
        finally
        {
            if (scene != null)
                _assimp.ReleaseImport(scene);
        }
    }

    private unsafe Model ImportFromStream(Stream stream, string virtualPath, DirectoryInfo? parentDir, string extension, ModelImporterSettings? settings = null)
    {
        // Use provided settings or defaults (no settings file loading for streams)
        settings ??= new ModelImporterSettings();

        // Read stream into byte array
        byte[] buffer;
        if (stream is MemoryStream ms)
        {
            buffer = ms.ToArray();
        }
        else
        {
            using (var memStream = new MemoryStream())
            {
                stream.CopyTo(memStream);
                buffer = memStream.ToArray();
            }
        }

        PostProcessSteps steps = GetPostProcessSteps(settings.Value);

        Scene* scene;
        fixed (byte* pBuffer = buffer)
        {
            scene = _assimp.ImportFileFromMemory(pBuffer, (uint)buffer.Length, (uint)steps, extension);
        }

        try
        {
            if (scene == null) Failed("Assimp returned null object.");

            if (scene->MNumMeshes == 0) Failed("Model has no Meshes.");

            float scale = GetScale(settings.Value, extension);

            return BuildModel(scene, virtualPath, parentDir, scale, settings.Value);
        }
        finally
        {
            if (scene != null)
                _assimp.ReleaseImport(scene);
        }
    }

    private PostProcessSteps GetPostProcessSteps(ModelImporterSettings settings)
    {
        PostProcessSteps steps = PostProcessSteps.LimitBoneWeights | PostProcessSteps.GenerateUVCoords | PostProcessSteps.RemoveRedundantMaterials;
        steps |= PostProcessSteps.Triangulate;
        if (settings.GenerateNormals && settings.GenerateSmoothNormals) steps |= PostProcessSteps.GenerateSmoothNormals;
        else if (settings.GenerateNormals) steps |= PostProcessSteps.GenerateNormals;
        if (settings.CalculateTangentSpace) steps |= PostProcessSteps.CalculateTangentSpace;
        if (settings.MakeLeftHanded) steps |= PostProcessSteps.MakeLeftHanded;
        if (settings.FlipUVs) steps |= PostProcessSteps.FlipUVs;
        if (settings.OptimizeGraph) steps |= PostProcessSteps.OptimizeGraph;
        if (settings.OptimizeMeshes) steps |= PostProcessSteps.OptimizeMeshes;
        if (settings.FlipWindingOrder) steps |= PostProcessSteps.FlipWindingOrder;
        if (settings.WeldVertices) steps |= PostProcessSteps.JoinIdenticalVertices;
        // GlobalScale is not available in Silk.NET.Assimp
        return steps;
    }

    private float GetScale(ModelImporterSettings settings, string extension)
    {
        return 1f;
        float scale = settings.UnitScale;
        // FBX's are usually in cm, so scale them to meters
        if (extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase))
            scale *= 0.01f;
        return scale;
    }

    private unsafe Model BuildModel(Scene* scene, string assetPath, DirectoryInfo? parentDir, float scale, ModelImporterSettings settings)
    {
        var model = new Model(Path.GetFileNameWithoutExtension(assetPath));
        model.UnitScale = settings.UnitScale;

        // Load materials and meshes into the model
        if (scene->MNumMaterials > 0)
            LoadMaterials(scene, parentDir, model.Materials);

        if (scene->MNumMeshes > 0)
            LoadMeshes(assetPath, settings, scene, scale, model.Materials, model.Meshes);

        // Build skeleton with hierarchy and mesh attachments
        model.Skeleton = BuildSkeleton(scene, model.Meshes, scale);

        // Animations
        if (scene->MNumAnimations > 0)
            LoadAnimations(scene, scale, model.Animations);

        // Set skeleton reference in all animations and rebuild bone mapping
        if (model.Skeleton.IsValid())
        {
            foreach (AnimationClip animation in model.Animations)
            {
                animation.Skeleton = model.Skeleton;
                animation.RebuildBoneMapping();
            }
        }

        return model;
    }

    private unsafe void LoadMaterials(Scene* scene, DirectoryInfo? parentDir, List<Material> mats)
    {
        for (uint i = 0; i < scene->MNumMaterials; i++)
        {
            Silk.NET.Assimp.Material* m = scene->MMaterials[i];
            Material mat = new(Shader.LoadDefault(DefaultShader.Standard));
            string? name = null;

            // Get material name
            AssimpString matName;
            if (_assimp.GetMaterialString(m, Assimp.MatkeyName, 0, 0, &matName) == Return.Success)
            {
                name = matName.AsString;
            }

            // Albedo
            System.Numerics.Vector4 diffuseColor;
            if (_assimp.GetMaterialColor(m, Assimp.MatkeyColorDiffuse, 0, 0, &diffuseColor) == Return.Success)
                mat.SetColor("_MainColor", new Color(diffuseColor.X, diffuseColor.Y, diffuseColor.Z, diffuseColor.W));
            else
                mat.SetColor("_MainColor", Color.White);

            // Emissive Color
            System.Numerics.Vector4 emissiveColor;
            if (_assimp.GetMaterialColor(m, Assimp.MatkeyColorEmissive, 0, 0, &emissiveColor) == Return.Success)
            {
                mat.SetFloat("_EmissionIntensity", 1f);
                mat.SetColor("_EmissiveColor", new Color(emissiveColor.X, emissiveColor.Y, emissiveColor.Z, emissiveColor.W));
            }
            else
            {
                mat.SetFloat("_EmissionIntensity", 0f);
                mat.SetColor("_EmissiveColor", Color.Black);
            }

            // Texture
            AssimpString texPath = default;
            if (_assimp.GetMaterialTexture(m, Silk.NET.Assimp.TextureType.Diffuse, 0, &texPath, null, null, null, null, null, null) == Return.Success)
            {
                string texPathStr = texPath.AsString;
                name ??= "Mat_" + Path.GetFileNameWithoutExtension(texPathStr);
                if (FindTextureFromPath(texPathStr, parentDir, out FileInfo? file))
                    LoadTextureIntoMesh("_MainTex", file, mat);
                else
                    mat.SetTexture("_MainTex", Texture2D.LoadDefault(DefaultTexture.Grid));
            }
            else
                mat.SetTexture("_MainTex", Texture2D.LoadDefault(DefaultTexture.Grid));

            // Normal Texture
            texPath = default;
            if (_assimp.GetMaterialTexture(m, Silk.NET.Assimp.TextureType.Normals, 0, &texPath, null, null, null, null, null, null) == Return.Success)
            {
                string texPathStr = texPath.AsString;
                name ??= "Mat_" + Path.GetFileNameWithoutExtension(texPathStr);
                if (FindTextureFromPath(texPathStr, parentDir, out FileInfo? file))
                    LoadTextureIntoMesh("_NormalTex", file, mat);
                else
                    mat.SetTexture("_NormalTex", Texture2D.LoadDefault(DefaultTexture.Normal));
            }
            else
                mat.SetTexture("_NormalTex", Texture2D.LoadDefault(DefaultTexture.Normal));

            //AO, Roughness, Metallic Texture Attempt 1
            texPath = default;
            if (_assimp.GetMaterialTexture(m, Silk.NET.Assimp.TextureType.Unknown, 0, &texPath, null, null, null, null, null, null) == Return.Success)
            {
                string texPathStr = texPath.AsString;
                name ??= "Mat_" + Path.GetFileNameWithoutExtension(texPathStr);
                if (FindTextureFromPath(texPathStr, parentDir, out FileInfo? file))
                    LoadTextureIntoMesh("_SurfaceTex", file, mat);
                else
                    mat.SetTexture("_SurfaceTex", Texture2D.LoadDefault(DefaultTexture.Surface));
            }
            else
                mat.SetTexture("_SurfaceTex", Texture2D.LoadDefault(DefaultTexture.Surface));

            //AO, Roughness, Metallic Texture Attempt 2
            texPath = default;
            if (_assimp.GetMaterialTexture(m, Silk.NET.Assimp.TextureType.Specular, 0, &texPath, null, null, null, null, null, null) == Return.Success)
            {
                string texPathStr = texPath.AsString;
                name ??= "Mat_" + Path.GetFileNameWithoutExtension(texPathStr);
                if (FindTextureFromPath(texPathStr, parentDir, out FileInfo? file))
                    LoadTextureIntoMesh("_SurfaceTex", file, mat);
                else
                    mat.SetTexture("_SurfaceTex", Texture2D.LoadDefault(DefaultTexture.Surface));
            }

            // Emissive Texture
            texPath = default;
            if (_assimp.GetMaterialTexture(m, Silk.NET.Assimp.TextureType.Emissive, 0, &texPath, null, null, null, null, null, null) == Return.Success)
            {
                string texPathStr = texPath.AsString;
                name ??= "Mat_" + Path.GetFileNameWithoutExtension(texPathStr);
                if (FindTextureFromPath(texPathStr, parentDir, out FileInfo? file))
                {
                    mat.SetFloat("_EmissionIntensity", 1f);
                    LoadTextureIntoMesh("_EmissionTex", file, mat);
                }
                else
                    mat.SetTexture("_EmissionTex", Texture2D.LoadDefault(DefaultTexture.Emission));
            }
            else
                mat.SetTexture("_EmissionTex", Texture2D.LoadDefault(DefaultTexture.Emission));

            name ??= "StandardMat";
            mat.Name = name;
            mats.Add(mat);
        }
    }

    private unsafe void LoadMeshes(string assetPath, ModelImporterSettings settings, Scene* scene, float scale, List<Material> mats, List<ModelMesh> meshMats)
    {
        for (uint meshIndex = 0; meshIndex < scene->MNumMeshes; meshIndex++)
        {
            Silk.NET.Assimp.Mesh* m = scene->MMeshes[meshIndex];

            if ((m->MPrimitiveTypes & (uint)PrimitiveType.Triangle) == 0)
            {
                Debug.Log($"{Path.GetFileName(assetPath)} 's mesh '{m->MName.AsString}' is not of Triangle Primitive, Skipping...");
                continue;
            }

            Mesh mesh = new();
            mesh.Name = m->MName.AsString;
            int vertexCount = (int)m->MNumVertices;
            mesh.IndexFormat = vertexCount >= ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;

            // Vertices
            Float3[] vertices = new Float3[vertexCount];
            for (int i = 0; i < vertices.Length; i++)
            {
                System.Numerics.Vector3 v = m->MVertices[i];
                vertices[i] = new Float3(v.X, v.Y, v.Z) * (float)scale;
            }
            mesh.Vertices = vertices;

            // Normals
            if (m->MNormals != null)
            {
                Float3[] normals = new Float3[vertexCount];
                for (int i = 0; i < normals.Length; i++)
                {
                    System.Numerics.Vector3 n = m->MNormals[i];
                    normals[i] = new Float3(n.X, n.Y, n.Z);
                    if (settings.InvertNormals)
                        normals[i] = -normals[i];
                }
                mesh.Normals = normals;
            }

            // Tangents
            if (m->MTangents != null)
            {
                Float3[] tangents = new Float3[vertexCount];
                for (int i = 0; i < tangents.Length; i++)
                {
                    System.Numerics.Vector3 t = m->MTangents[i];
                    tangents[i] = new Float3(t.X, t.Y, t.Z);
                }
                mesh.Tangents = tangents;
            }

            // UV channel 0
            if (m->MTextureCoords.Element0 != null)
            {
                Float2[] texCoords1 = new Float2[vertexCount];
                for (int i = 0; i < texCoords1.Length; i++)
                {
                    System.Numerics.Vector3 uv = m->MTextureCoords.Element0[i];
                    texCoords1[i] = new Float2(uv.X, uv.Y);
                }
                mesh.UV = texCoords1;
            }

            // UV channel 1
            if (m->MTextureCoords.Element1 != null)
            {
                Float2[] texCoords2 = new Float2[vertexCount];
                for (int i = 0; i < texCoords2.Length; i++)
                {
                    System.Numerics.Vector3 uv = m->MTextureCoords.Element1[i];
                    texCoords2[i] = new Float2(uv.X, uv.Y);
                }
                mesh.UV2 = texCoords2;
            }

            // Vertex colors
            if (m->MColors.Element0 != null)
            {
                Color[] colors = new Color[vertexCount];
                for (int i = 0; i < colors.Length; i++)
                {
                    System.Numerics.Vector4 c = m->MColors.Element0[i];
                    colors[i] = new Color(c.X, c.Y, c.Z, c.W);
                }
                mesh.Colors = colors;
            }

            // Indices
            uint[] indices = new uint[m->MNumFaces * 3];
            int indexOffset = 0;
            for (uint i = 0; i < m->MNumFaces; i++)
            {
                Face face = m->MFaces[i];
                if (face.MNumIndices == 3)
                {
                    indices[indexOffset++] = face.MIndices[0];
                    indices[indexOffset++] = face.MIndices[1];
                    indices[indexOffset++] = face.MIndices[2];
                }
            }
            mesh.Indices = indices;
            mesh.RecalculateBounds();

            // Bones
            if (m->MNumBones > 0)
            {
                mesh.bindPoses = new Float4x4[m->MNumBones];
                mesh.boneNames = new string[m->MNumBones];
                mesh.BoneIndices = new Float4[vertexCount];
                mesh.BoneWeights = new Float4[vertexCount];

                for (uint i = 0; i < m->MNumBones; i++)
                {
                    Bone* bone = m->MBones[i];

                    // Store bone name
                    mesh.boneNames[i] = bone->MName.AsString;

                    System.Numerics.Matrix4x4 offsetMatrix = bone->MOffsetMatrix;
                    Float4x4 bindPose = new(
                        offsetMatrix.M11, offsetMatrix.M12, offsetMatrix.M13, offsetMatrix.M14,
                        offsetMatrix.M21, offsetMatrix.M22, offsetMatrix.M23, offsetMatrix.M24,
                        offsetMatrix.M31, offsetMatrix.M32, offsetMatrix.M33, offsetMatrix.M34,
                        offsetMatrix.M41, offsetMatrix.M42, offsetMatrix.M43, offsetMatrix.M44
                    );

                    bindPose.Translation *= (float)scale;

                    mesh.bindPoses[i] = bindPose;

                    if (bone->MNumWeights == 0) continue;
                    byte boneIndex = (byte)(i + 1);

                    // foreach weight
                    for (uint j = 0; j < bone->MNumWeights; j++)
                    {
                        VertexWeight weight = bone->MWeights[j];
                        uint vertexId = weight.MVertexId;
                        float weightValue = weight.MWeight;

                        Float4 b = mesh.BoneIndices[vertexId];
                        Float4 w = mesh.BoneWeights[vertexId];
                        if (b.X == 0 || weightValue > w.X)
                        {
                            b.X = boneIndex;
                            w.X = weightValue;
                        }
                        else if (b.Y == 0 || weightValue > w.Y)
                        {
                            b.Y = boneIndex;
                            w.Y = weightValue;
                        }
                        else if (b.Z == 0 || weightValue > w.Z)
                        {
                            b.Z = boneIndex;
                            w.Z = weightValue;
                        }
                        else if (b.W == 0 || weightValue > w.W)
                        {
                            b.W = boneIndex;
                            w.W = weightValue;
                        }
                        else
                        {
                            Debug.LogWarning($"Vertex {vertexId} has more than 4 bone weights, Skipping...");
                        }
                        mesh.BoneIndices[vertexId] = b;
                        mesh.BoneWeights[vertexId] = w;
                    }
                }

                for (int i = 0; i < vertices.Length; i++)
                {
                    Float4 w = mesh.BoneWeights[i];
                    float totalWeight = w.X + w.Y + w.Z + w.W;
                    if (totalWeight == 0) continue;
                    w.X /= totalWeight;
                    w.Y /= totalWeight;
                    w.Z /= totalWeight;
                    w.W /= totalWeight;
                    mesh.BoneWeights[i] = w;
                }
            }

            meshMats.Add(new ModelMesh(m->MName.AsString, mesh, mats[(int)m->MMaterialIndex], m->MNumBones > 0));
        }
    }

    private static unsafe void LoadAnimations(Scene* scene, float scale, List<AnimationClip> animations)
    {
        for (uint animIndex = 0; animIndex < scene->MNumAnimations; animIndex++)
        {
            Animation* anim = scene->MAnimations[animIndex];

            // Create Animation
            AnimationClip animation = new();
            animation.Name = anim->MName.AsString;
            animation.Duration = (float)(anim->MDuration / (anim->MTicksPerSecond != 0 ? anim->MTicksPerSecond : 25.0f));
            animation.TicksPerSecond = (float)anim->MTicksPerSecond;
            animation.DurationInTicks = (float)anim->MDuration;

            for (uint chanIndex = 0; chanIndex < anim->MNumChannels; chanIndex++)
            {
                NodeAnim* channel = anim->MChannels[chanIndex];
                Silk.NET.Assimp.Node* boneNode = FindNode(scene->MRootNode, channel->MNodeName.AsString);

                var animBone = new AnimationClip.AnimBone();
                animBone.BoneName = boneNode != null ? boneNode->MName.AsString : channel->MNodeName.AsString;

                if (channel->MNumPositionKeys > 0)
                {
                    var xCurve = new AnimationCurve();
                    var yCurve = new AnimationCurve();
                    var zCurve = new AnimationCurve();

                    xCurve.Keys.Clear();
                    yCurve.Keys.Clear();
                    zCurve.Keys.Clear();

                    for (uint i = 0; i < channel->MNumPositionKeys; i++)
                    {
                        VectorKey posKey = channel->MPositionKeys[i];
                        float time = (float)(posKey.MTime / anim->MDuration) * animation.Duration;
                        xCurve.Keys.Add(new KeyFrame(time, posKey.MValue.X * scale));
                        yCurve.Keys.Add(new KeyFrame(time, posKey.MValue.Y * scale));
                        zCurve.Keys.Add(new KeyFrame(time, posKey.MValue.Z * scale));
                    }
                    animBone.PosX = xCurve;
                    animBone.PosY = yCurve;
                    animBone.PosZ = zCurve;
                }

                if (channel->MNumRotationKeys > 0)
                {
                    var xCurve = new AnimationCurve();
                    var yCurve = new AnimationCurve();
                    var zCurve = new AnimationCurve();
                    var wCurve = new AnimationCurve();

                    xCurve.Keys.Clear();
                    yCurve.Keys.Clear();
                    zCurve.Keys.Clear();
                    wCurve.Keys.Clear();

                    for (uint i = 0; i < channel->MNumRotationKeys; i++)
                    {
                        QuatKey rotKey = channel->MRotationKeys[i];
                        float time = (float)(rotKey.MTime / anim->MDuration) * animation.Duration;
                        xCurve.Keys.Add(new KeyFrame(time, rotKey.MValue.X));
                        yCurve.Keys.Add(new KeyFrame(time, rotKey.MValue.Y));
                        zCurve.Keys.Add(new KeyFrame(time, rotKey.MValue.Z));
                        wCurve.Keys.Add(new KeyFrame(time, rotKey.MValue.W));
                    }
                    animBone.RotX = xCurve;
                    animBone.RotY = yCurve;
                    animBone.RotZ = zCurve;
                    animBone.RotW = wCurve;
                }

                if (channel->MNumScalingKeys > 0)
                {
                    var xCurve = new AnimationCurve();
                    var yCurve = new AnimationCurve();
                    var zCurve = new AnimationCurve();

                    xCurve.Keys.Clear();
                    yCurve.Keys.Clear();
                    zCurve.Keys.Clear();

                    for (uint i = 0; i < channel->MNumScalingKeys; i++)
                    {
                        VectorKey scaleKey = channel->MScalingKeys[i];
                        float time = (float)(scaleKey.MTime / anim->MDuration) * animation.Duration;
                        xCurve.Keys.Add(new KeyFrame(time, scaleKey.MValue.X));
                        yCurve.Keys.Add(new KeyFrame(time, scaleKey.MValue.Y));
                        zCurve.Keys.Add(new KeyFrame(time, scaleKey.MValue.Z));
                    }
                    animBone.ScaleX = xCurve;
                    animBone.ScaleY = yCurve;
                    animBone.ScaleZ = zCurve;
                }

                animation.AddBone(animBone);
            }

            animation.EnsureQuaternionContinuity();
            animations.Add(animation);
        }
    }

    private static unsafe Silk.NET.Assimp.Node* FindNode(Silk.NET.Assimp.Node* node, string name)
    {
        if (node == null) return null;
        if (node->MName.AsString == name) return node;

        for (uint i = 0; i < node->MNumChildren; i++)
        {
            var found = FindNode(node->MChildren[i], name);
            if (found != null) return found;
        }

        return null;
    }

    private static unsafe Skeleton BuildSkeleton(Scene* scene, List<ModelMesh> meshes, float scale)
    {
        // Collect offset matrices from meshes (for bones with weights)
        Dictionary<string, Float4x4> boneOffsetMatrices = new();

        foreach (ModelMesh modelMesh in meshes)
        {
            if (modelMesh.HasBones && modelMesh.Mesh.boneNames != null && modelMesh.Mesh.bindPoses != null)
            {
                for (int i = 0; i < modelMesh.Mesh.boneNames.Length; i++)
                {
                    string boneName = modelMesh.Mesh.boneNames[i];
                    if (!boneOffsetMatrices.ContainsKey(boneName))
                    {
                        boneOffsetMatrices[boneName] = modelMesh.Mesh.bindPoses[i];
                    }
                }
            }
        }

        // Always create a skeleton (even for non-animated models)
        Skeleton skeleton = new();
        skeleton.Name = "Skeleton";

        Dictionary<string, int> boneNameToID = new();
        int boneID = 0;

        // Recursively add all nodes in the hierarchy as bones
        void ProcessNode(Silk.NET.Assimp.Node* node)
        {
            if (node == null)
                return;

            string nodeName = node->MName.AsString;

            // Create bone for this node
            var bone = new Skeleton.Bone(boneID, nodeName);

            // Use offset matrix if this node has weights, otherwise use identity
            bone.OffsetMatrix = boneOffsetMatrices.TryGetValue(nodeName, out Float4x4 offsetMatrix)
                ? offsetMatrix
                : Float4x4.Identity;

            // Extract bind pose transform from the node
            System.Numerics.Matrix4x4 nodeTransform = node->MTransformation;
            System.Numerics.Matrix4x4.Decompose(nodeTransform, out System.Numerics.Vector3 scale_vec, out System.Numerics.Quaternion rotation, out System.Numerics.Vector3 position);

            bone.BindPosition = new Float3(position.X, position.Y, position.Z) * scale;
            var euler = Quaternion.ToEuler(new(rotation.X, rotation.Y, rotation.Z, rotation.W));
            bone.BindRotation = Quaternion.FromEuler(euler.X, -euler.Y, euler.Z);
            bone.BindScale = new Float3(scale_vec.X, scale_vec.Y, scale_vec.Z);

            // Set parent relationship (if not root)
            if (node->MParent != null && node->MParent != scene->MRootNode)
            {
                string parentName = node->MParent->MName.AsString;
                if (boneNameToID.TryGetValue(parentName, out int parentID))
                {
                    bone.ParentID = parentID;
                }
            }

            // Attach meshes to this bone
            if (node->MNumMeshes > 0)
            {
                for (uint i = 0; i < node->MNumMeshes; i++)
                {
                    bone.MeshIndices.Add((int)node->MMeshes[i]);
                }
                if (node->MNumMeshes == 1)
                    bone.MeshIndex = (int)node->MMeshes[0];
            }

            boneNameToID[nodeName] = boneID;
            skeleton.AddBone(bone);
            boneID++;

            // Process all children
            for (uint i = 0; i < node->MNumChildren; i++)
            {
                ProcessNode(node->MChildren[i]);
            }
        }

        // Start from root node's children (skip the root itself)
        for (uint i = 0; i < scene->MRootNode->MNumChildren; i++)
        {
            ProcessNode(scene->MRootNode->MChildren[i]);
        }

        return skeleton;
    }

    private bool FindTextureFromPath(string filePath, DirectoryInfo parentDir, out FileInfo file)
    {
        // If the filePath is stored in the model relative to the file this will exist
        file = new FileInfo(Path.Combine(parentDir.FullName, filePath));
        if (System.IO.File.Exists(file.FullName)) return true;
        // If not the filePath is probably a Full path, so lets loop over each node in the path starting from the end
        // so first check if the File name exists inside parentDir, if so return, if not then check the file with its parent exists so like
        // if the file is at C:\Users\Me\Documents\MyModel\Textures\MyTexture.png
        // we first check if Path.Combine(parentDir, MyTexture.png) exists, if not we check if Path.Combine(parentDir, Textures\MyTexture.png) exists and so on
        string[] nodes = filePath.Split(Path.DirectorySeparatorChar);
        for (int i = nodes.Length - 1; i >= 0; i--)
        {
            string path = Path.Combine(parentDir.FullName, string.Join(Path.DirectorySeparatorChar, nodes.Skip(i)));
            file = new FileInfo(path);
            if (file.Exists) return true;
        }
        // If we get here we have failed to find the texture
        return false;
    }

    private static void LoadTextureIntoMesh(string name, FileInfo file, Material mat)
    {
        mat.SetTexture(name, Texture2D.LoadFromFile(file.FullName, true));
    }
}
