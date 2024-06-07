using Assimp;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Utils;
using static Prowl.Runtime.AnimationClip;
using Material = Prowl.Runtime.Material;
using Mesh = Prowl.Runtime.Mesh;
using Node = Assimp.Node;
using Texture2D = Prowl.Runtime.Texture2D;

namespace Prowl.Editor.Assets
{

    [Importer("ModelIcon.png", typeof(GameObject), ".obj", ".blend", ".dae", ".fbx", ".gltf", ".ply", ".pmx", ".stl")]
    public class ModelImporter : ScriptedImporter
    {
        public static readonly string[] Supported = { ".obj", ".blend", ".dae", ".fbx", ".gltf", ".ply", ".pmx", ".stl" };

        public bool GenerateColliders = false;
        public bool GenerateNormals = true;
        public bool GenerateSmoothNormals = false;
        public bool CalculateTangentSpace = true;
        public bool MakeLeftHanded = true;
        public bool FlipUVs = false;
        public bool CullEmpty = false;
        public bool OptimizeGraph = false;
        public bool OptimizeMeshes = false;
        public bool FlipWindingOrder = true;
        public bool WeldVertices = false;
        public bool InvertNormals = false;
        public bool GlobalScale = false;

        public float UnitScale = 1.0f;

        void Failed(string reason)
        {
            Debug.LogError("Failed to Import Model. Reason: " + reason);
            throw new Exception(reason);
        }

        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            // Just confirm the format, We should have todo this but technically someone could call ImportTexture manually skipping the existing format check
            if (!Supported.Contains(assetPath.Extension))
                Failed("Format Not Supported: " + assetPath.Extension);

            using (var importer = new AssimpContext())
            {
                importer.SetConfig(new Assimp.Configs.VertexBoneWeightLimitConfig(4));
                var steps = PostProcessSteps.LimitBoneWeights | PostProcessSteps.GenerateUVCoords;
                steps |= PostProcessSteps.Triangulate;
                if (GenerateNormals && GenerateSmoothNormals) steps |= PostProcessSteps.GenerateSmoothNormals;
                else if (GenerateNormals) steps |= PostProcessSteps.GenerateNormals;
                if (CalculateTangentSpace) steps |= PostProcessSteps.CalculateTangentSpace;
                if (MakeLeftHanded) steps |= PostProcessSteps.MakeLeftHanded;
                if (FlipUVs) steps |= PostProcessSteps.FlipUVs;
                if (OptimizeGraph) steps |= PostProcessSteps.OptimizeGraph;
                if (OptimizeMeshes) steps |= PostProcessSteps.OptimizeMeshes;
                if (FlipWindingOrder) steps |= PostProcessSteps.FlipWindingOrder;
                if (WeldVertices) steps |= PostProcessSteps.JoinIdenticalVertices;
                if (GlobalScale) steps |= PostProcessSteps.GlobalScale;
                var scene = importer.ImportFile(assetPath.FullName, steps);
                if (scene == null) Failed("Assimp returned null object.");

                DirectoryInfo? parentDir = assetPath.Directory;

                if (!scene.HasMeshes) Failed("Model has no Meshes.");

                double scale = UnitScale;

                // FBX's are usually in cm, so scale them to meters
                if (assetPath.Extension.Equals(".fbx", StringComparison.OrdinalIgnoreCase))
                    scale *= 0.01;

                // Create the object tree, We need to do this first so we can get the bone names
                List<(GameObject, Node)> GOs = [];
                GetNodes(Path.GetFileNameWithoutExtension(assetPath.Name), scene.RootNode, ref GOs, scale);

                //if (scene.HasTextures) {
                //    // Embedded textures, Extract them first
                //    foreach (var t in scene.Textures) {
                //        if (t.IsCompressed) {
                //            // Export it as whatever format it already is to a file
                //            var format = ImageMagick.MagickFormat.Png;
                //            switch (t.CompressedFormatHint) {
                //                case "png":
                //                    format = ImageMagick.MagickFormat.Png;
                //                    break;
                //                case "tga":
                //                    format = ImageMagick.MagickFormat.Tga;
                //                    break;
                //                case "dds":
                //                    format = ImageMagick.MagickFormat.Dds;
                //                    break;
                //                case "jpg":
                //                    format = ImageMagick.MagickFormat.Jpg;
                //                    break;
                //                case "bmp":
                //                    format = ImageMagick.MagickFormat.Bmp;
                //                    break;
                //                default:
                //                    Debug.LogWarning($"Unknown texture format '{t.CompressedFormatHint}'");
                //                    break;
                //            }
                //            ImageMagick.MagickImage img = new ImageMagick.MagickImage(t.CompressedData, new ImageMagick.MagickReadSettings() { Format = format });
                //            var file = new FileInfo(Path.Combine(subAssetPath.FullName, $"{t.Filename}.{t.CompressedFormatHint}"));
                //            img.Write(file.FullName, format);
                //            AssetDatabase.Refresh(file);
                //            //AssetDatabase.LastLoadedAssetID; the textures guid
                //        } else {
                //            // Export it as a png
                //            byte[] data = new byte[t.NonCompressedData.Length * 4];
                //            for (int i = 0; i < t.NonCompressedData.Length; i++) {
                //                data[i * 4 + 0] = t.NonCompressedData[i].R;
                //                data[i * 4 + 1] = t.NonCompressedData[i].G;
                //                data[i * 4 + 2] = t.NonCompressedData[i].B;
                //                data[i * 4 + 3] = t.NonCompressedData[i].A;
                //            }
                //
                //            ImageMagick.MagickImage img = new ImageMagick.MagickImage(data);
                //            var file = new FileInfo(Path.Combine(subAssetPath.FullName, $"{t.Filename}.png"));
                //            img.Write(file.FullName, ImageMagick.MagickFormat.Png);
                //            AssetDatabase.Refresh(file);
                //            //AssetDatabase.LastLoadedAssetID; the textures guid
                //        }
                //    }
                //}

                List<AssetRef<Material>> mats = new();
                if (scene.HasMaterials)
                    LoadMaterials(ctx, scene, parentDir, mats);

                // Animations
                List<AssetRef<AnimationClip>> anims = [];
                if (scene.HasAnimations)
                    anims = LoadAnimations(ctx, scene, scale);

                List<MeshMaterialBinding> meshMats = new List<MeshMaterialBinding>();
                if (scene.HasMeshes)
                    LoadMeshes(ctx, assetPath, scene, scale, mats, meshMats);

                // Create Meshes
                foreach (var goNode in GOs)
                {
                    var node = goNode.Item2;
                    var go = goNode.Item1;
                    // Set Mesh
                    if (node.HasMeshes)
                    {
                        if (node.MeshIndices.Count == 1)
                        {
                            var uMeshAndMat = meshMats[node.MeshIndices[0]];
                            AddMeshComponent(GOs, go, uMeshAndMat);
                        }
                        else
                        {
                            foreach (var mIdx in node.MeshIndices)
                            {
                                var uMeshAndMat = meshMats[mIdx];
                                GameObject uSubOb = GameObject.CreateSilently();
                                //uSubOb.AddComponent<Transform>();
                                uSubOb.Name = uMeshAndMat.MeshName;
                                AddMeshComponent(GOs, uSubOb, uMeshAndMat);
                                uSubOb.SetParent(go, false);
                            }
                        }
                    }
                }

                GameObject rootNode = GOs[0].Item1;
                if (UnitScale != 1f)
                    rootNode.Transform.localScale = Vector3.one * UnitScale;

                // Add Animation Component with all the animations assigned
                if (anims.Count > 0)
                {
                    var anim = rootNode.AddComponent<Runtime.Animation>();
                    foreach (var a in anims)
                        anim.Clips.Add(a);
                    anim.DefaultClip = anims[0];
                }

                if (CullEmpty)
                {
                    // Remove Empty GameObjects
                    List<(GameObject, Node)> GOsToRemove = [];
                    foreach (var go in GOs)
                    {
                        if (go.Item1.GetComponentsInChildren<MonoBehaviour>().Count() == 0)
                            GOsToRemove.Add(go);
                    }
                    foreach (var go in GOsToRemove)
                    {
                        if (!go.Item1.IsDestroyed)
                            go.Item1.DestroyImmediate();
                        GOs.Remove(go);
                    }
                }

                ctx.SetMainObject(rootNode);
            }

            void AddMeshComponent(List<(GameObject, Node)> GOs, GameObject go, MeshMaterialBinding uMeshAndMat)
            {
                if (uMeshAndMat.AMesh.HasBones)
                {
                    var mr = go.AddComponent<SkinnedMeshRenderer>();
                    mr.Mesh = uMeshAndMat.Mesh;
                    mr.Material = uMeshAndMat.Material;
                    mr.Bones = new Transform[uMeshAndMat.AMesh.Bones.Count];
                    for (int i = 0; i < uMeshAndMat.AMesh.Bones.Count; i++)
                        mr.Bones[i] = GOs[0].Item1.Transform.DeepFind(uMeshAndMat.AMesh.Bones[i].Name)!.gameObject.Transform;
                }
                else
                {
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.Mesh = uMeshAndMat.Mesh;
                    mr.Material = uMeshAndMat.Material;
                }

                if (GenerateColliders)
                {
                    var mc = go.AddComponent<MeshCollider>();
                    mc.mesh = uMeshAndMat.Mesh;
                }
            }
        }

        private void LoadMaterials(SerializedAsset ctx, Assimp.Scene? scene, DirectoryInfo? parentDir, List<AssetRef<Material>> mats)
        {
            foreach (var m in scene.Materials)
            {
                Material mat = new Material(Shader.Find("Defaults/Standard.shader"));
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
                        LoadTextureIntoMesh("_MainTex", ctx, file, mat);
                    else
                        mat.SetTexture("_MainTex", new AssetRef<Texture2D>(AssetDatabase.GuidFromRelativePath("Defaults/grid.png")));
                }
                else
                    mat.SetTexture("_MainTex", new AssetRef<Texture2D>(AssetDatabase.GuidFromRelativePath("Defaults/grid.png")));

                // Normal Texture
                if (m.HasTextureNormal)
                {
                    name ??= "Mat_" + Path.GetFileNameWithoutExtension(m.TextureNormal.FilePath);
                    if (FindTextureFromPath(m.TextureNormal.FilePath, parentDir, out var file))
                        LoadTextureIntoMesh("_NormalTex", ctx, file, mat);
                    else
                        mat.SetTexture("_NormalTex", new AssetRef<Texture2D>(AssetDatabase.GuidFromRelativePath("Defaults/default_normal.png")));
                }
                else
                    mat.SetTexture("_NormalTex", new AssetRef<Texture2D>(AssetDatabase.GuidFromRelativePath("Defaults/default_normal.png")));

                //AO, Roughness, Metallic Texture
                if (m.GetMaterialTexture(TextureType.Unknown, 0, out var surface))
                {
                    name ??= "Mat_" + Path.GetFileNameWithoutExtension(surface.FilePath);
                    if (FindTextureFromPath(surface.FilePath, parentDir, out var file))
                        LoadTextureIntoMesh("_SurfaceTex", ctx, file, mat);
                    else
                        mat.SetTexture("_SurfaceTex", new AssetRef<Texture2D>(AssetDatabase.GuidFromRelativePath("Defaults/default_surface.png")));
                }
                else
                    mat.SetTexture("_SurfaceTex", new AssetRef<Texture2D>(AssetDatabase.GuidFromRelativePath("Defaults/default_surface.png")));

                // Emissive Texture
                if (m.HasTextureEmissive)
                {
                    name ??= "Mat_" + Path.GetFileNameWithoutExtension(m.TextureEmissive.FilePath);
                    if (FindTextureFromPath(m.TextureEmissive.FilePath, parentDir, out var file))
                    {
                        mat.SetFloat("_EmissionIntensity", 1f);
                        LoadTextureIntoMesh("_EmissionTex", ctx, file, mat);
                    }
                    else
                        mat.SetTexture("_EmissionTex", new AssetRef<Texture2D>(AssetDatabase.GuidFromRelativePath("Defaults/default_emission.png")));
                }
                else
                    mat.SetTexture("_EmissionTex", new AssetRef<Texture2D>(AssetDatabase.GuidFromRelativePath("Defaults/default_emission.png")));

                name ??= "StandardMat";
                mat.Name = name;
                mats.Add(ctx.AddSubObject(mat));
            }
        }

        private void LoadMeshes(SerializedAsset ctx, FileInfo assetPath, Assimp.Scene? scene, double scale, List<AssetRef<Material>> mats, List<MeshMaterialBinding> meshMats)
        {
            foreach (var m in scene.Meshes)
            {
                if (m.PrimitiveType != PrimitiveType.Triangle)
                {
                    Debug.Log($"{assetPath.Name} 's mesh '{m.Name}' is not of Triangle Primitive, Skipping...");
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
                        if(InvertNormals)
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


                meshMats.Add(new MeshMaterialBinding(m.Name, m, ctx.AddSubObject(mesh), mats[m.MaterialIndex]));
            }
        }

        private static List<AssetRef<AnimationClip>> LoadAnimations(SerializedAsset ctx, Assimp.Scene? scene, double scale)
        {
            List<AssetRef<AnimationClip>> anims = [];
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

                    var animBone = new AnimBone();
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
                anims.Add(ctx.AddSubObject(animation));
            }

            return anims;
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

        private static void LoadTextureIntoMesh(string name, SerializedAsset ctx, FileInfo file, Material mat)
        {
            if (AssetDatabase.TryGetGuid(file, out var guid))
            {
                // We have this texture as an asset, Juse use the asset we dont need to load it
                mat.SetTexture(name, new AssetRef<Texture2D>(guid));
            }
            else
            {
#warning TODO: Handle importing external textures
                Debug.LogError($"Failed to load texture for model at path '{file.FullName}'");
                //// Ok so the texture isnt loaded, lets make sure it exists
                //if (!file.Exists)
                //    throw new FileNotFoundException($"Texture file for model was not found!", file.FullName);
                //
                //// Ok so we dont have it in the asset database but the file does infact exist
                //// so lets load it in as a sub asset to this object
                //Texture2D tex = new Texture2D(file.FullName);
                //ctx.AddSubObject(tex);
                //mat.SetTexture(name, new AssetRef<Texture2D>(guid));
            }
        }

        GameObject GetNodes(string? name, Node node, ref List<(GameObject, Node)> GOs, double scale)
        {
            GameObject uOb = GameObject.CreateSilently();
            GOs.Add((uOb, node));
            uOb.Name = name ?? node.Name;

            if (node.HasChildren)
                foreach (var cn in node.Children)
                {
                    var go = GetNodes(null, cn, ref GOs, scale);
                    go.SetParent(uOb, false);
                }

            // Transform
            var t = node.Transform;
            t.Decompose(out var aSca, out var aRot, out var aPos);

            uOb.Transform.localPosition = new Vector3(aPos.X, aPos.Y, aPos.Z) * scale;
            uOb.Transform.localRotation = new Runtime.Quaternion(aRot.X, aRot.Y, aRot.Z, aRot.W);
            uOb.Transform.localScale = new Vector3(aSca.X, aSca.Y, aSca.Z);

            return uOb;
        }

        class MeshMaterialBinding
        {
            private string meshName;
            private AssetRef<Mesh> mesh;
            private Assimp.Mesh aMesh;
            private AssetRef<Material> material;

            private MeshMaterialBinding() { }
            public MeshMaterialBinding(string meshName, Assimp.Mesh aMesh, AssetRef<Mesh> mesh, AssetRef<Material> material)
            {
                this.meshName = meshName;
                this.mesh = mesh;
                this.aMesh = aMesh;
                this.material = material;
            }

            public AssetRef<Mesh> Mesh { get => mesh; }
            public Assimp.Mesh AMesh { get => aMesh; }
            public AssetRef<Material> Material { get => material; }
            public string MeshName { get => meshName; }
        }
    }

    [CustomEditor(typeof(ModelImporter))]
    public class ModelEditor : ScriptedEditor
    {

        int selectedAnim = 0;
        int selectedAnimBone = 0;

        int selectedTab = 0;

        public override void OnInspectorGUI()
        {
            var importer = (ModelImporter)(target as MetaFile).importer;
            var serialized = AssetDatabase.LoadAsset((target as MetaFile).AssetPath);

            g.CurrentNode.Layout(Runtime.GUI.LayoutType.Column);
            g.CurrentNode.ScaleChildren();

            using (g.Node("Tabs").Width(Size.Percentage(1f)).MaxHeight(GuiStyle.ItemHeight).Layout(LayoutType.Row).ScaleChildren().Enter())
            {
                if(EditorGUI.QuickButton("Meshes"))
                    selectedTab = 0;
                if(EditorGUI.QuickButton("Materials"))
                    selectedTab = 1;
                if(EditorGUI.QuickButton("Scene"))
                    selectedTab = 2;
                if(EditorGUI.QuickButton("Animations"))
                    selectedTab = 3;
            }


            using (g.Node("Content").Width(Size.Percentage(1f)).MarginTop(5).Layout(LayoutType.Column).Enter())
            {
                switch (selectedTab)
                {
                    case 0:
                        Meshes(importer, serialized);
                        break;
                    case 1:
                        Materials(importer, serialized);
                        break;
                    case 2:
                        Scene(importer, serialized);
                        break;
                    case 3:
                        Animations(importer, serialized);
                        break;
                }

                if (EditorGUI.QuickButton("Save"))
                {
                    (target as MetaFile).Save();
                    AssetDatabase.Reimport((target as MetaFile).AssetPath);
                }

                g.ScrollV();
            }

        }

        private void Meshes(ModelImporter importer, SerializedAsset? serialized)
        {
            EditorGUI.DrawProperty(0, "Merge Objects", ref importer.OptimizeGraph);
            EditorGUI.DrawProperty(1, "Generate Mesh Colliders", ref importer.GenerateColliders);
            EditorGUI.DrawProperty(2, "Generate Normals", ref importer.GenerateNormals);
            if (importer.GenerateNormals)
                EditorGUI.DrawProperty(3, "Generate Smooth Normals", ref importer.GenerateSmoothNormals);
            EditorGUI.DrawProperty(4, "Calculate Tangent Space", ref importer.CalculateTangentSpace);
            EditorGUI.DrawProperty(5, "Make Left Handed", ref importer.MakeLeftHanded);
            EditorGUI.DrawProperty(6, "Flip UVs", ref importer.FlipUVs);
            EditorGUI.DrawProperty(7, "Optimize Meshes", ref importer.OptimizeMeshes);
            EditorGUI.DrawProperty(8, "Flip Winding Order", ref importer.FlipWindingOrder);
            EditorGUI.DrawProperty(9, "Weld Vertices", ref importer.WeldVertices);
            EditorGUI.DrawProperty(10, "Invert Normals", ref importer.InvertNormals);
            EditorGUI.DrawProperty(11, "GlobalScale", ref importer.GlobalScale);
            EditorGUI.DrawProperty(12, "UnitScale", ref importer.UnitScale);

            var meshes = serialized.SubAssets.Where(x => x is Mesh);
            g.TextNode("mCount", $"Mesh Count: {meshes.Count()}").ExpandWidth().Height(GuiStyle.ItemHeight);
            g.TextNode("vCount", $"Vertex Count: {meshes.Sum(x => (x as Mesh).VertexCount)}").ExpandWidth().Height(GuiStyle.ItemHeight);
            g.TextNode("tCount", $"Triangle Count: {meshes.Sum(x => (x as Mesh).IndexCount / 3)}").ExpandWidth().Height(GuiStyle.ItemHeight);
            g.TextNode("bCount", $"Bone Count: {meshes.Sum(x => (x as Mesh).BoneIndices?.Length ?? 0)}").ExpandWidth().Height(GuiStyle.ItemHeight);

            //#warning TODO: Support for Exporting sub assets
            //#warning TODO: Support for editing Model specific data like Animation data
        }

        private void Scene(ModelImporter importer, SerializedAsset? serialized)
        {
            // Draw the Scene Graph
            GameObject root = serialized.Main as GameObject;

            g.TextNode("goCount", $"GameObject Count: {CountNodes(root)}").ExpandWidth().Height(GuiStyle.ItemHeight);
            EditorGUI.DrawProperty(0, "Merge Objects", ref importer.OptimizeGraph);
            EditorGUI.DrawProperty(1, "Cull Empty Objects", ref importer.CullEmpty);

            // TODO: Draw Scene Graph as Tree similarly to Hierarchy

        }

        private int CountNodes(GameObject go)
        {
            int count = 1;
            foreach (var child in go.children)
                count += CountNodes(child);
            return count;
        }

        private void Materials(ModelImporter importer, SerializedAsset? serialized)
        {
            var materials = serialized.SubAssets.Where(x => x is Material);

            g.TextNode("mCount", $"Material Count: {materials.Count()}").ExpandWidth().Height(GuiStyle.ItemHeight);

            using (g.Node("MaterialList").ExpandWidth().FitContentHeight().Layout(LayoutType.Column).Enter())
            {
                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.WindowBackground * 0.8f, 10);
                for (int i=0; i<materials.Count(); i++)
                {
                    g.TextNode("mat" + i, materials.ElementAt(i).Name).ExpandWidth().Height(GuiStyle.ItemHeight);
                }
            }

        }

        private void Animations(ModelImporter importer, SerializedAsset? serialized)
        {
            var animations = serialized.SubAssets.Where(x => x is AnimationClip);

            g.TextNode("aCount", $"Animation Count: {animations.Count()}").ExpandWidth().Height(GuiStyle.ItemHeight);

            if (animations.Count() <= 0) return;

            using (g.Node("AnimationList").Padding(10).ExpandWidth().MaxHeight(300).Clip().FitContentHeight().Layout(LayoutType.Column).Enter())
            {
                g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.WindowBackground * 0.8f, 10);
                for (int i = 0; i < animations.Count(); i++)
                {
                    if (EditorGUI.QuickButton(i + ": " +  animations.ElementAt(i).Name))
                    {
                        selectedAnim = i + 1;
                    }
                }
                g.ScrollV();
            }

            if (selectedAnim > 0 && selectedAnim <= animations.Count())
            {
                var anim = animations.ElementAt(selectedAnim - 1) as AnimationClip;
                g.TextNode("aName", $"Name: {anim.Name}").ExpandWidth().Height(GuiStyle.ItemHeight);
                g.TextNode("aDuration", $"Duration: {anim.Duration}").ExpandWidth().Height(GuiStyle.ItemHeight);
                g.TextNode("aTPS", $"Ticks Per Second: {anim.TicksPerSecond}").ExpandWidth().Height(GuiStyle.ItemHeight);
                g.TextNode("aDIT", $"Duration In Ticks: {anim.DurationInTicks}").ExpandWidth().Height(GuiStyle.ItemHeight);
                g.TextNode("aBoneCount", $"Bone Count: {anim.Bones.Count}").ExpandWidth().Height(GuiStyle.ItemHeight);

                if (anim.Bones.Count <= 0) return;

                using (g.Node("BoneList").Padding(10).ExpandWidth().MaxHeight(300).Clip().FitContentHeight().Layout(LayoutType.Column).Enter())
                {
                    g.Draw2D.DrawRectFilled(g.CurrentNode.LayoutData.Rect, GuiStyle.WindowBackground * 0.8f, 10);
                    for (int i = 0; i < anim.Bones.Count; i++)
                    {
                        if (EditorGUI.QuickButton(i + ": " + anim.Bones[i].BoneName))
                        {
                            selectedAnimBone = i;
                        }
                    }
                    g.ScrollV();
                }

                if (selectedAnimBone > 0 && selectedAnimBone <= anim.Bones.Count)
                {
                    var bone = anim.Bones[selectedAnimBone - 1];
                    g.TextNode("bName", $"Bone Name: {bone.BoneName}").ExpandWidth().Height(GuiStyle.ItemHeight);
                    g.TextNode("bPosKeys", $"Position Keys: {bone.PosX.Keys.Count}").ExpandWidth().Height(GuiStyle.ItemHeight);
                    g.TextNode("bRotKeys", $"Rotation Keys: {bone.RotX.Keys.Count}").ExpandWidth().Height(GuiStyle.ItemHeight);
                    g.TextNode("bScaleKeys", $"Scale Keys: {bone.ScaleX.Keys.Count}").ExpandWidth().Height(GuiStyle.ItemHeight);
                }
            }

        }
    }
}
