using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Assimp;

using Prowl.Echo;
using Prowl.Runtime.Resources;

using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;

namespace Prowl.Runtime.AssetImporting
{
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
        private void Failed(string reason)
        {
            Debug.LogError($"Failed to Import Model: {reason}");
            throw new Exception(reason);
        }

        public Model Import(FileInfo assetPath, ModelImporterSettings? settings = null) =>
            ImportFromFile(assetPath.FullName, assetPath.Directory, assetPath.Extension, settings);

        public Model Import(Stream stream, string virtualPath, ModelImporterSettings? settings = null) =>
            ImportFromStream(stream, virtualPath, null, Path.GetExtension(virtualPath), settings);

        private Model ImportFromFile(string filePath, DirectoryInfo? parentDir, string extension, ModelImporterSettings? settings = null)
        {
            // new settings if null
            settings ??= new ModelImporterSettings();

            using (var importer = new AssimpContext())
            {
                importer.SetConfig(new Assimp.Configs.VertexBoneWeightLimitConfig(4));
                var steps = GetPostProcessSteps(settings.Value);
                var scene = importer.ImportFile(filePath, steps);
                if (scene == null) Failed("Assimp returned null object.");

                if (!scene.HasMeshes) Failed("Model has no Meshes.");

                double scale = GetScale(settings.Value, extension);

                return BuildModel(scene, filePath, parentDir, scale, settings.Value);
            }
        }

        private Model ImportFromStream(Stream stream, string virtualPath, DirectoryInfo? parentDir, string extension, ModelImporterSettings? settings = null)
        {
            // Use provided settings or defaults (no settings file loading for streams)
            settings ??= new ModelImporterSettings();

            using (var importer = new AssimpContext())
            {
                importer.SetConfig(new Assimp.Configs.VertexBoneWeightLimitConfig(4));
                var steps = GetPostProcessSteps(settings.Value);

                // Use ImportFileFromStream for embedded resources
                var scene = importer.ImportFileFromStream(stream, steps, Path.GetExtension(virtualPath));
                if (scene == null) Failed("Assimp returned null object.");

                if (!scene.HasMeshes) Failed("Model has no Meshes.");

                double scale = GetScale(settings.Value, extension);

                return BuildModel(scene, virtualPath, parentDir, scale, settings.Value);
            }
        }

        private PostProcessSteps GetPostProcessSteps(ModelImporterSettings settings)
        {
            var steps = PostProcessSteps.LimitBoneWeights | PostProcessSteps.GenerateUVCoords | PostProcessSteps.RemoveRedundantMaterials;
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
            if (settings.GlobalScale) steps |= PostProcessSteps.GlobalScale;
            return steps;
        }

        private double GetScale(ModelImporterSettings settings, string extension)
        {
            double scale = settings.UnitScale;
            // FBX's are usually in cm, so scale them to meters
            if (extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase))
                scale *= 0.01;
            return scale;
        }

        private Model BuildModel(Assimp.Scene scene, string assetPath, DirectoryInfo? parentDir, double scale, ModelImporterSettings settings)
        {
            var model = new Model(Path.GetFileNameWithoutExtension(assetPath));
            model.UnitScale = settings.UnitScale;

            // Build the model structure
            model.RootNode = BuildModelNode(scene.RootNode, scale);

            // Load materials and meshes into the model
            if (scene.HasMaterials)
                LoadMaterials(scene, parentDir, model.Materials);

            if (scene.HasMeshes)
                LoadMeshes(assetPath, settings, scene, scale, model.Materials, model.Meshes);

            // Animations
            List<AnimationClip> anims = [];
            if (scene.HasAnimations)
                LoadAnimations(scene, scale, model.Animations);

            //if (CullEmpty)
            //{
            //    // Remove Empty GameObjects
            //    List<(MeshRenderer, Node)> GOsToRemove = [];
            //    foreach (var go in GOs)
            //    {
            //        if (go.Item1.GetEntitiesInChildren<MeshRenderer>().Count(x => x.Mesh.IsAvailable) == 0)
            //            GOsToRemove.Add(go);
            //    }
            //    foreach (var go in GOsToRemove)
            //    {
            //        if (!go.Item1.IsDestroyed)
            //            go.Item1.DestroyImmediate();
            //        GOs.Remove(go);
            //    }
            //}

            return model;
        }

        private void LoadMaterials(Assimp.Scene? scene, DirectoryInfo? parentDir, List<Material> mats)
        {
            foreach (var m in scene.Materials)
            {
                Material mat = new Material(Shader.LoadDefault(DefaultShader.Standard));
                string? name = m.HasName ? m.Name : null;

                // Albedo
                if (m.HasColorDiffuse)
                    mat.SetColor("_MainColor", new Color(m.ColorDiffuse.R, m.ColorDiffuse.G, m.ColorDiffuse.B, m.ColorDiffuse.A));
                else
                    mat.SetColor("_MainColor", Color.white);

                // Emissive Color
                if (m.HasColorEmissive)
                {
                    mat.SetFloat("_EmissionIntensity", 1f);
                    mat.SetColor("_EmissiveColor", new Color(m.ColorEmissive.R, m.ColorEmissive.G, m.ColorEmissive.B, m.ColorEmissive.A));
                }
                else
                {

                    mat.SetFloat("_EmissionIntensity", 0f);
                    mat.SetColor("_EmissiveColor", Color.black);
                }

                // Texture
                if (m.HasTextureDiffuse)
                {
                    name ??= "Mat_" + Path.GetFileNameWithoutExtension(m.TextureDiffuse.FilePath);
                    if (FindTextureFromPath(m.TextureDiffuse.FilePath, parentDir, out var file))
                        LoadTextureIntoMesh("_MainTex", file, mat);
                    else
                        mat.SetTexture("_MainTex", Texture2D.LoadDefault(DefaultTexture.Grid));
                }
                else
                    mat.SetTexture("_MainTex", Texture2D.LoadDefault(DefaultTexture.Grid));

                // Normal Texture
                if (m.HasTextureNormal)
                {
                    name ??= "Mat_" + Path.GetFileNameWithoutExtension(m.TextureNormal.FilePath);
                    if (FindTextureFromPath(m.TextureNormal.FilePath, parentDir, out var file))
                        LoadTextureIntoMesh("_NormalTex", file, mat);
                    else
                        mat.SetTexture("_NormalTex", Texture2D.LoadDefault(DefaultTexture.Normal));
                }
                else
                    mat.SetTexture("_NormalTex", Texture2D.LoadDefault(DefaultTexture.Normal));

                //AO, Roughness, Metallic Texture Attempt 1
                if (m.GetMaterialTexture(TextureType.Unknown, 0, out var surface))
                {
                    name ??= "Mat_" + Path.GetFileNameWithoutExtension(surface.FilePath);
                    if (FindTextureFromPath(surface.FilePath, parentDir, out var file))
                        LoadTextureIntoMesh("_SurfaceTex", file, mat);
                    else
                        mat.SetTexture("_SurfaceTex", Texture2D.LoadDefault(DefaultTexture.Surface));
                }
                else
                    mat.SetTexture("_SurfaceTex", Texture2D.LoadDefault(DefaultTexture.Surface));

                //AO, Roughness, Metallic Texture Attempt 2
                if (m.HasTextureSpecular)
                {
                    name ??= "Mat_" + Path.GetFileNameWithoutExtension(m.TextureSpecular.FilePath);
                    if (FindTextureFromPath(m.TextureSpecular.FilePath, parentDir, out var file))
                        LoadTextureIntoMesh("_SurfaceTex", file, mat);
                    else
                        mat.SetTexture("_SurfaceTex", Texture2D.LoadDefault(DefaultTexture.Surface));
                }
                else
                    mat.SetTexture("_SurfaceTex", Texture2D.LoadDefault(DefaultTexture.Surface));

                // Emissive Texture
                if (m.HasTextureEmissive)
                {
                    name ??= "Mat_" + Path.GetFileNameWithoutExtension(m.TextureEmissive.FilePath);
                    if (FindTextureFromPath(m.TextureEmissive.FilePath, parentDir, out var file))
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

        private void LoadMeshes(string assetPath, ModelImporterSettings settings, Assimp.Scene? scene, double scale, List<Material> mats, List<ModelMesh> meshMats)
        {
            foreach (var m in scene.Meshes)
            {
                if (m.PrimitiveType != PrimitiveType.Triangle)
                {
                    Debug.Log($"{Path.GetFileName(assetPath)} 's mesh '{m.Name}' is not of Triangle Primitive, Skipping...");
                    continue;
                }


                Mesh mesh = new();
                mesh.Name = m.Name;
                int vertexCount = m.VertexCount;
                mesh.IndexFormat = vertexCount >= ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;

                System.Numerics.Vector3[] vertices = new System.Numerics.Vector3[vertexCount];
                for (var i = 0; i < vertices.Length; i++)
                    vertices[i] = new System.Numerics.Vector3(m.Vertices[i].X, m.Vertices[i].Y, m.Vertices[i].Z) * (float)scale;
                mesh.Vertices = vertices;

                if (m.HasNormals)
                {
                    System.Numerics.Vector3[] normals = new System.Numerics.Vector3[vertexCount];
                    for (var i = 0; i < normals.Length; i++)
                    {
                        normals[i] = new System.Numerics.Vector3(m.Normals[i].X, m.Normals[i].Y, m.Normals[i].Z);
                        if (settings.InvertNormals)
                            normals[i] = -normals[i];
                    }
                    mesh.Normals = normals;
                }

                if (m.HasTangentBasis)
                {
                    System.Numerics.Vector3[] tangents = new System.Numerics.Vector3[vertexCount];
                    for (var i = 0; i < tangents.Length; i++)
                        tangents[i] = new System.Numerics.Vector3(m.Tangents[i].X, m.Tangents[i].Y, m.Tangents[i].Z);
                    mesh.Tangents = tangents;
                }

                if (m.HasTextureCoords(0))
                {
                    System.Numerics.Vector2[] texCoords1 = new System.Numerics.Vector2[vertexCount];
                    for (var i = 0; i < texCoords1.Length; i++)
                        texCoords1[i] = new System.Numerics.Vector2(m.TextureCoordinateChannels[0][i].X, m.TextureCoordinateChannels[0][i].Y);
                    mesh.UV = texCoords1;
                }

                if (m.HasTextureCoords(1))
                {
                    System.Numerics.Vector2[] texCoords2 = new System.Numerics.Vector2[vertexCount];
                    for (var i = 0; i < texCoords2.Length; i++)
                        texCoords2[i] = new System.Numerics.Vector2(m.TextureCoordinateChannels[1][i].X, m.TextureCoordinateChannels[1][i].Y);
                    mesh.UV2 = texCoords2;
                }

                if (m.HasVertexColors(0))
                {
                    Color[] colors = new Color[vertexCount];
                    for (var i = 0; i < colors.Length; i++)
                        colors[i] = new Color(m.VertexColorChannels[0][i].R, m.VertexColorChannels[0][i].G, m.VertexColorChannels[0][i].B, m.VertexColorChannels[0][i].A);
                    mesh.Colors = colors;
                }

                mesh.Indices = m.GetUnsignedIndices();

                //if(!m.HasTangentBasis)
                //    mesh.RecalculateTangents();

                mesh.RecalculateBounds();

                if (m.HasBones)
                {
                    mesh.bindPoses = new System.Numerics.Matrix4x4[m.Bones.Count];
                    mesh.BoneIndices = new System.Numerics.Vector4[vertexCount];
                    mesh.BoneWeights = new System.Numerics.Vector4[vertexCount];
                    for (var i = 0; i < m.Bones.Count; i++)
                    {
                        var bone = m.Bones[i];

                        var offsetMatrix = bone.OffsetMatrix;
                        System.Numerics.Matrix4x4 bindPose = new System.Numerics.Matrix4x4(
                            offsetMatrix.A1, offsetMatrix.B1, offsetMatrix.C1, offsetMatrix.D1,
                            offsetMatrix.A2, offsetMatrix.B2, offsetMatrix.C2, offsetMatrix.D2,
                            offsetMatrix.A3, offsetMatrix.B3, offsetMatrix.C3, offsetMatrix.D3,
                            offsetMatrix.A4, offsetMatrix.B4, offsetMatrix.C4, offsetMatrix.D4
                        );

                        // Adjust translation by scale
                        bindPose.Translation *= (float)scale;

                        mesh.bindPoses[i] = bindPose;

                        if (!bone.HasVertexWeights) continue;
                        byte boneIndex = (byte)(i + 1);

                        // foreach weight
                        for (int j = 0; j < bone.VertexWeightCount; j++)
                        {
                            var weight = bone.VertexWeights[j];
                            var b = mesh.BoneIndices[weight.VertexID];
                            var w = mesh.BoneWeights[weight.VertexID];
                            if (b.X == 0 || weight.Weight > w.X)
                            {
                                b.X = boneIndex;
                                w.X = weight.Weight;
                            }
                            else if (b.Y == 0 || weight.Weight > w.Y)
                            {
                                b.Y = boneIndex;
                                w.Y = weight.Weight;
                            }
                            else if (b.Z == 0 || weight.Weight > w.Z)
                            {
                                b.Z = boneIndex;
                                w.Z = weight.Weight;
                            }
                            else if (b.W == 0 || weight.Weight > w.W)
                            {
                                b.W = boneIndex;
                                w.W = weight.Weight;
                            }
                            else
                            {
                                Debug.LogWarning($"Vertex {weight.VertexID} has more than 4 bone weights, Skipping...");
                            }
                            mesh.BoneIndices[weight.VertexID] = b;
                            mesh.BoneWeights[weight.VertexID] = w;
                        }
                    }

                    for (int i = 0; i < vertices.Length; i++)
                    {
                        var w = mesh.BoneWeights[i];
                        var totalWeight = w.X + w.Y + w.Z + w.W;
                        if (totalWeight == 0) continue;
                        w.X /= totalWeight;
                        w.Y /= totalWeight;
                        w.Z /= totalWeight;
                        w.W /= totalWeight;
                        mesh.BoneWeights[i] = w;
                    }
                }

                meshMats.Add(new ModelMesh(m.Name, mesh, mats[m.MaterialIndex], m.HasBones));
            }
        }

        private ModelNode BuildModelNode(Node assimpNode, double scale)
        {
            var modelNode = new ModelNode(assimpNode.Name);

            // Transform
            var t = assimpNode.Transform;
            t.Decompose(out var aSca, out var aRot, out var aPos);

            modelNode.LocalPosition = new Vector3(aPos.X, aPos.Y, aPos.Z) * scale;
            modelNode.LocalRotation = new(aRot.X, aRot.Y, aRot.Z, aRot.W);
            modelNode.LocalScale = new Vector3(aSca.X, aSca.Y, aSca.Z);

            // Assign mesh indices
            if (assimpNode.HasMeshes)
            {
                modelNode.MeshIndices.AddRange(assimpNode.MeshIndices);
                if (assimpNode.MeshIndices.Count == 1)
                    modelNode.MeshIndex = assimpNode.MeshIndices[0];
            }

            // Build children
            if (assimpNode.HasChildren)
            {
                foreach (var child in assimpNode.Children)
                {
                    modelNode.Children.Add(BuildModelNode(child, scale));
                }
            }

            return modelNode;
        }

        private static void LoadAnimations(Assimp.Scene? scene, double scale, List<AnimationClip> animations)
        {
            foreach (var anim in scene.Animations)
            {
                // Create Animation
                AnimationClip animation = new AnimationClip();
                animation.Name = anim.Name;
                animation.Duration = anim.DurationInTicks / (anim.TicksPerSecond != 0 ? anim.TicksPerSecond : 25.0);
                animation.TicksPerSecond = anim.TicksPerSecond;
                animation.DurationInTicks = anim.DurationInTicks;
        
                foreach (var channel in anim.NodeAnimationChannels)
                {
                    Assimp.Node boneNode = scene.RootNode.FindNode(channel.NodeName);
        
                    var animBone = new AnimationClip.AnimBone();
                    animBone.BoneName = boneNode.Name;
        
                    // construct full path from RootNode to this bone
                    // RootNode -> Parent -> Parent -> ... -> Parent -> Bone
                    Assimp.Node target = boneNode;
                    string path = target.Name;
                    //while (target.Parent != null)
                    //{
                    //    target = target.Parent;
                    //    path = target.Name + "/" + path;
                    //    if (target.Name == scene.RootNode.Name) // TODO: Can we just do reference comparison here instead of string comparison?
                    //        break;
                    //}
        
                    if (channel.HasPositionKeys)
                    {
                        var xCurve = new AnimationCurve();
                        var yCurve = new AnimationCurve();
                        var zCurve = new AnimationCurve();
                        foreach (var posKey in channel.PositionKeys)
                        {
                            double time = (posKey.Time / anim.DurationInTicks) * animation.Duration;
                            xCurve.Keys.Add(new(time, posKey.Value.X * scale));
                            yCurve.Keys.Add(new(time, posKey.Value.Y * scale));
                            zCurve.Keys.Add(new(time, posKey.Value.Z * scale));
                        }
                        animBone.PosX = xCurve;
                        animBone.PosY = yCurve;
                        animBone.PosZ = zCurve;
                    }
        
                    if (channel.HasRotationKeys)
                    {
                        var xCurve = new AnimationCurve();
                        var yCurve = new AnimationCurve();
                        var zCurve = new AnimationCurve();
                        var wCurve = new AnimationCurve();
                        foreach (var rotKey in channel.RotationKeys)
                        {
                            double time = (rotKey.Time / anim.DurationInTicks) * animation.Duration;
                            xCurve.Keys.Add(new(time, rotKey.Value.X));
                            yCurve.Keys.Add(new(time, rotKey.Value.Y));
                            zCurve.Keys.Add(new(time, rotKey.Value.Z));
                            wCurve.Keys.Add(new(time, rotKey.Value.W));
                        }
                        animBone.RotX = xCurve;
                        animBone.RotY = yCurve;
                        animBone.RotZ = zCurve;
                        animBone.RotW = wCurve;
                    }
        
                    if (channel.HasScalingKeys)
                    {
                        var xCurve = new AnimationCurve();
                        var yCurve = new AnimationCurve();
                        var zCurve = new AnimationCurve();
                        foreach (var scaleKey in channel.ScalingKeys)
                        {
                            double time = (scaleKey.Time / anim.DurationInTicks) * animation.Duration;
                            xCurve.Keys.Add(new(time, scaleKey.Value.X));
                            yCurve.Keys.Add(new(time, scaleKey.Value.Y));
                            zCurve.Keys.Add(new(time, scaleKey.Value.Z));
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

        private bool FindTextureFromPath(string filePath, DirectoryInfo parentDir, out FileInfo file)
        {
            // If the filePath is stored in the model relative to the file this will exist
            file = new FileInfo(Path.Combine(parentDir.FullName, filePath));
            if (File.Exists(file.FullName)) return true;
            // If not the filePath is probably a Full path, so lets loop over each node in the path starting from the end
            // so first check if the File name exists inside parentDir, if so return, if not then check the file with its parent exists so like
            // if the file is at C:\Users\Me\Documents\MyModel\Textures\MyTexture.png
            // we first check if Path.Combine(parentDir, MyTexture.png) exists, if not we check if Path.Combine(parentDir, Textures\MyTexture.png) exists and so on
            var nodes = filePath.Split(Path.DirectorySeparatorChar);
            for (int i = nodes.Length - 1; i >= 0; i--)
            {
                var path = Path.Combine(parentDir.FullName, string.Join(Path.DirectorySeparatorChar, nodes.Skip(i)));
                file = new FileInfo(path);
                if (file.Exists) return true;
            }
            // If we get here we have failed to find the texture
            return false;
        }

        private static void LoadTextureIntoMesh(string name, FileInfo file, Material mat)
        {
            mat.SetTexture(name, Texture2D.LoadFromFile(file.FullName));
        }
    }
}
