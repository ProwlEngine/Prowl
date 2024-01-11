using Assimp;
using HexaEngine.ImGuiNET;
using Prowl.Runtime;
using Prowl.Runtime.Assets;
using Prowl.Runtime.Utils;
using Silk.NET.Input;
using System;
using System.ComponentModel.Design;
using System.Linq;
using System.Xml.Linq;
using static Prowl.Runtime.Mesh;
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

        public bool GenerateNormals = true;
        public bool GenerateSmoothNormals = false;
        public bool CalculateTangentSpace = true;
        public bool MakeLeftHanded = true;
        public bool FlipUVs = false;
        public bool OptimizeMeshes = false;
        public bool FlipWindingOrder = false;
        public bool WeldVertices = false;
        public bool InvertNormals = false;
        public bool GlobalScale = false;

        public float UnitScale = 1.0f;

        void Failed(string reason)
        {
            ImGuiNotify.InsertNotification("Failed to Import Model.", new(0.8f, 0.1f, 0.1f, 1f), reason);
            throw new Exception(reason);
        }

        public override void Import(SerializedAsset ctx, FileInfo assetPath)
        {
            // Just confirm the format, We should have todo this but technically someone could call ImportTexture manually skipping the existing format check
            if (!Supported.Contains(assetPath.Extension))
                Failed("Format Not Supported: " + assetPath.Extension);

            using (var importer = new AssimpContext()) {
                importer.SetConfig(new Assimp.Configs.VertexBoneWeightLimitConfig(4));
                var steps = PostProcessSteps.LimitBoneWeights | PostProcessSteps.GenerateUVCoords;
                steps |= PostProcessSteps.Triangulate;
                if (GenerateNormals && GenerateSmoothNormals) steps |= PostProcessSteps.GenerateSmoothNormals;
                else if (GenerateNormals) steps |= PostProcessSteps.GenerateNormals;
                if (CalculateTangentSpace) steps |= PostProcessSteps.CalculateTangentSpace;
                if (MakeLeftHanded) steps |= PostProcessSteps.MakeLeftHanded;
                if (FlipUVs) steps |= PostProcessSteps.FlipUVs;
                if (OptimizeMeshes) steps |= PostProcessSteps.OptimizeMeshes;
                if (FlipWindingOrder) steps |= PostProcessSteps.FlipWindingOrder;
                if (WeldVertices) steps |= PostProcessSteps.JoinIdenticalVertices;
                if (GlobalScale) steps |= PostProcessSteps.GlobalScale;
                var scene = importer.ImportFile(assetPath.FullName, steps);
                if (scene == null) Failed("Assimp returned null object.");

                DirectoryInfo? parentDir = assetPath.Directory;
                // SubAssetDataPath is a folder next to the asset file with the same name and a _Data added to the end
                DirectoryInfo subAssetPath = new DirectoryInfo(Path.Combine(assetPath.Directory.FullName, Path.GetFileNameWithoutExtension(assetPath.Name) + "_Data"));
                subAssetPath.Create();

                if (!scene.HasMeshes) Failed("Model has no Meshes.");

                // Create the object tree, We need to do this first so we can get the bone names
                List<(GameObject, Node)> GOs = [];
                Dictionary<string, int> nameToIndex = [];
                GetNodes(scene.RootNode, ref GOs, ref nameToIndex);

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

                List<Material> mats = new();
                if (scene.HasMaterials)
                    foreach (var m in scene.Materials) {
                        Material mat = new Material(Shader.Find("Defaults/Standard.shader"));
                        string? name = m.HasName ? m.Name : null;

                        // Albedo
                        if (m.HasColorDiffuse)
                            mat.SetColor("_MainColor", new Color(m.ColorDiffuse.R, m.ColorDiffuse.G, m.ColorDiffuse.B, m.ColorDiffuse.A));
                        else
                            mat.SetColor("_MainColor", Color.white);

                        // Emissive Color
                        if (m.HasColorEmissive) {
                            mat.SetFloat("_EmissionIntensity", 1f);
                            mat.SetColor("_EmissiveColor", new Color(m.ColorEmissive.R, m.ColorEmissive.G, m.ColorEmissive.B, m.ColorEmissive.A));
                        } else {

                            mat.SetFloat("_EmissionIntensity", 0f);
                            mat.SetColor("_EmissiveColor", Color.black);
                        }

                        // Texture
                        if (m.HasTextureDiffuse) {
                            name ??= Path.GetFileNameWithoutExtension(m.TextureDiffuse.FilePath);
                            if (FindTextureFromPath(m.TextureDiffuse.FilePath, parentDir, out var file))
                                LoadTextureIntoMesh("_MainTex", ctx, file, mat);
                            else
                                mat.SetTexture("_MainTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/grid.png")));
                        } else
                            mat.SetTexture("_MainTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/grid.png")));

                        // Normal Texture
                        if (m.HasTextureNormal)
                        {
                            name ??= Path.GetFileNameWithoutExtension(m.TextureNormal.FilePath);
                            if (FindTextureFromPath(m.TextureNormal.FilePath, parentDir, out var file))
                                LoadTextureIntoMesh("_NormalTex", ctx, file, mat);
                            else
                                mat.SetTexture("_NormalTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_normal.png")));
                        } else
                            mat.SetTexture("_NormalTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_normal.png")));

                        //AO, Roughness, Metallic Texture
                        if (m.GetMaterialTexture(TextureType.Unknown, 0, out var surface)) {
                            name ??= Path.GetFileNameWithoutExtension(surface.FilePath);
                            if (FindTextureFromPath(surface.FilePath, parentDir, out var file))
                                LoadTextureIntoMesh("_SurfaceTex", ctx, file, mat);
                            else
                                mat.SetTexture("_SurfaceTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_surface.png")));
                        } else
                            mat.SetTexture("_SurfaceTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_surface.png")));

                        // Emissive Texture
                        if (m.HasTextureEmissive) {
                            name ??= Path.GetFileNameWithoutExtension(m.TextureEmissive.FilePath);
                            if (FindTextureFromPath(m.TextureEmissive.FilePath, parentDir, out var file)) {
                                mat.SetFloat("_EmissionIntensity", 1f);
                                LoadTextureIntoMesh("_EmissionTex", ctx, file, mat);
                            } else
                                mat.SetTexture("_EmissionTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_emission.png")));
                        } else
                            mat.SetTexture("_EmissionTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_emission.png")));

                        name ??= "StandardMat";
                        FileInfo matFilePath = new FileInfo(Path.Combine(subAssetPath.FullName, $"{name}.mat"));
                        // If it already exists it gets overwritten
#warning TODO: Decide, Should we overwrite, or is it better to re-use the existing one incase they modified it?
                        AssetDatabase.Remove(AssetDatabase.FileToRelative(matFilePath));
                        StringTagConverter.WriteToFile((CompoundTag)TagSerializer.Serialize(mat), matFilePath);
                        AssetDatabase.Refresh(matFilePath);
                        mat.AssetID = AssetDatabase.LastLoadedAssetID;

                        mats.Add(mat);
                    }

                List<MeshMaterialBinding> meshMats = new List<MeshMaterialBinding>();
                if (scene.HasMeshes)
                    foreach (var m in scene.Meshes) {
                        if (m.PrimitiveType != PrimitiveType.Triangle) {
                            Debug.Log($"{assetPath.Name} 's mesh '{m.Name}' is not of Triangle Primitive, Skipping...");
                            continue;
                        }

                        if (!m.HasNormals) {
                            Debug.Log($"{assetPath.Name} Does not have any normals in mesh '{m.Name}', Skipping...");
                            continue;
                        }

                        if (!m.HasTangentBasis) {
                            Debug.Log($"{assetPath.Name} Does not have any tangents in mesh '{m.Name}', Skipping...");
                            continue;
                        }

                        List<Mesh.VertexFormat.Element> elements = [
                            new Mesh.VertexFormat.Element(Mesh.VertexFormat.VertexSemantic.Position, Mesh.VertexFormat.VertexType.Float, 3),
                            new Mesh.VertexFormat.Element(Mesh.VertexFormat.VertexSemantic.TexCoord, Mesh.VertexFormat.VertexType.Float, 2),
                            new Mesh.VertexFormat.Element(Mesh.VertexFormat.VertexSemantic.Normal, Mesh.VertexFormat.VertexType.Float, 3, 0, true),
                            new Mesh.VertexFormat.Element(Mesh.VertexFormat.VertexSemantic.Color, Mesh.VertexFormat.VertexType.Float, 3),
                            new Mesh.VertexFormat.Element(Mesh.VertexFormat.VertexSemantic.Tangent, Mesh.VertexFormat.VertexType.Float, 3),
                            new Mesh.VertexFormat.Element(Mesh.VertexFormat.VertexSemantic.BoneIndex, Mesh.VertexFormat.VertexType.UnsignedByte, 4),
                            new Mesh.VertexFormat.Element(Mesh.VertexFormat.VertexSemantic.BoneWeight, Mesh.VertexFormat.VertexType.Float, 4)
                        ];
                        Mesh.VertexFormat format = new(elements.ToArray());

                        Mesh mesh = new();
                        mesh.format = format;

                        var verts = m.Vertices;
                        var norms = m.Normals;
                        var tangs = m.Tangents;
                        var texs = m.TextureCoordinateChannels[0];
                        Vertex[] vertices = new Vertex[m.VertexCount];

                        for (var i = 0; i < vertices.Length; i++) {
                            Vertex vert = new Vertex();
                            var v = verts[i]; var n = norms[i]; var t = tangs[i]; var tc = texs[i];
                            vert.Position = new Vector3(v.X, v.Y, v.Z);
                            vert.TexCoord = new Vector2(tc.X, tc.Y);
                            vert.Normal = new Vector3(n.X, n.Y, n.Z);
                            if (m.HasVertexColors(0)) {
                                var c = m.VertexColorChannels[0][i];
                                vert.Color = new Vector3(c.R, c.G, c.B);
                            } else {
                                vert.Color = Vector3.one;
                            }
                            vert.Tangent = new Vector3(t.X, t.Y, t.Z);

                            vertices[i] = vert;
                        }

                        if (m.HasBones) {
                            for (var i = 0; i < m.Bones.Count; i++) {
                                var bone = m.Bones[i];
                                if (!bone.HasVertexWeights) continue;

                                int nameIndex = nameToIndex[bone.Name];

                                var weight0 = bone.VertexWeights[0];
                                vertices[weight0.VertexID] = vertices[weight0.VertexID] with { BoneIndex0 = (byte)nameIndex, Weight0 = weight0.Weight };
                                if (bone.VertexWeightCount == 1) continue;

                                var weight1 = bone.VertexWeights[1];
                                vertices[weight1.VertexID] = vertices[weight1.VertexID] with { BoneIndex1 = (byte)nameIndex, Weight1 = weight1.Weight };
                                if (bone.VertexWeightCount == 2) continue;

                                var weight2 = bone.VertexWeights[2];
                                vertices[weight2.VertexID] = vertices[weight2.VertexID] with { BoneIndex2 = (byte)nameIndex, Weight2 = weight2.Weight };
                                if (bone.VertexWeightCount == 3) continue;

                                var weight3 = bone.VertexWeights[3];
                                vertices[weight3.VertexID] = vertices[weight3.VertexID] with { BoneIndex3 = (byte)nameIndex, Weight3 = weight3.Weight };
                            }

                            for (int i = 0; i < vertices.Length; i++) {
                                var v = vertices[i];
                                var totalWeight = v.Weight0 + v.Weight1 + v.Weight2 + v.Weight3;
                                v.Weight0 /= totalWeight;
                                v.Weight1 /= totalWeight;
                                v.Weight2 /= totalWeight;
                                v.Weight3 /= totalWeight;
                                vertices[i] = v;
                            }
                        }

                        mesh.vertices = vertices;
                        mesh.indices = m.GetShortIndices().Cast<ushort>().ToArray();

                        FileInfo meshFilePath = new FileInfo(Path.Combine(subAssetPath.FullName, $"{m.Name}.mesh"));
                        // If it already exists it gets overwritten
#warning TODO: Decide, Should we overwrite, or is it better to re-use the existing one incase they modified it?
                        AssetDatabase.Remove(AssetDatabase.FileToRelative(meshFilePath));
                        StringTagConverter.WriteToFile((CompoundTag)TagSerializer.Serialize(mesh), meshFilePath);
                        AssetDatabase.Refresh(meshFilePath);
                        mesh.AssetID = AssetDatabase.LastLoadedAssetID;

                        meshMats.Add(new MeshMaterialBinding(m.Name, m.HasBones, mesh, mats[m.MaterialIndex]));
                    }

                // Create Meshes
                foreach (var goNode in GOs) {
                    var node = goNode.Item2;
                    var go = goNode.Item1;
                    // Set Mesh
                    if (node.HasMeshes) {
                        if (node.MeshIndices.Count == 1) {
                            var uMeshAndMat = meshMats[node.MeshIndices[0]];
                            AddMeshComponent(GOs, go, uMeshAndMat);
                        } else {
                            foreach (var mIdx in node.MeshIndices) {
                                var uMeshAndMat = meshMats[mIdx];
                                GameObject uSubOb = GameObject.CreateSilently();
                                uSubOb.AddComponent<Transform>();
                                uSubOb.Name = uMeshAndMat.MeshName;
                                AddMeshComponent(GOs, uSubOb, uMeshAndMat);
                                uSubOb.SetParent(go);
                            }
                        }
                    }

                    // Transform
                    node.Transform.Decompose(out var aScale, out var aQuat, out var aTranslation);

                    go.Transform!.Scale = new Vector3(aScale.X, aScale.Y, aScale.Z);
                    go.Transform!.Position = new Vector3(aTranslation.X, aTranslation.Y, aTranslation.Z);
                    go.Transform!.Orientation = new Prowl.Runtime.Quaternion(aQuat.X, aQuat.Y, aQuat.Z, aQuat.W);
                }

                GameObject rootNode = GOs[0].Item1;
                rootNode.Transform!.Scale = Vector3.one * UnitScale;
                ctx.SetMainObject(rootNode);

                ImGuiNotify.InsertNotification("Model Imported.", new(0.75f, 0.35f, 0.20f, 1.00f), AssetDatabase.FileToRelative(assetPath));
            }

            static void AddMeshComponent(List<(GameObject, Node)> GOs, GameObject go, MeshMaterialBinding uMeshAndMat)
            {
                if (uMeshAndMat.HasBones) {
                    var mr = go.AddComponent<SkinnedMeshRenderer>();
                    mr.Mesh = uMeshAndMat.Mesh;
                    mr.Material = uMeshAndMat.Material;
                    mr.Root = GOs[0].Item1;
                } else {
                    var mr = go.AddComponent<MeshRenderer>();
                    mr.Mesh = uMeshAndMat.Mesh;
                    mr.Material = uMeshAndMat.Material;
                }
            }
        }

        private bool FindTextureFromPath(string filePath, DirectoryInfo parentDir, out FileInfo file)
        {
            // If the filePath is stored in the model relative to the file this will exist
            file = new FileInfo(Path.Combine(parentDir.FullName, filePath));
            if (file.Exists) return true;
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
            Guid guid = AssetDatabase.GUIDFromAssetPath(file);
            if (guid != Guid.Empty)
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

        GameObject GetNodes(Node node, ref List<(GameObject, Node)> GOs, ref Dictionary<string, int> nameToIndex)
        {
            GameObject uOb = GameObject.CreateSilently();
            var t = uOb.AddComponent<Transform>();
            nameToIndex.Add(node.Name, GOs.Count);
            GOs.Add((uOb, node));
            uOb.Name = node.Name;

            // Transform
            node.Transform.Decompose(out var aScale, out var aQuat, out var aTranslation);

            t.Scale = new Vector3(aScale.X, aScale.Y, aScale.Z);
            t.Position = new Vector3(aTranslation.X, aTranslation.Y, aTranslation.Z);
            t.Orientation = new Prowl.Runtime.Quaternion(aQuat.X, aQuat.Y, aQuat.Z, aQuat.W);

            if (node.HasChildren) foreach (var cn in node.Children) GetNodes(cn, ref GOs, ref nameToIndex).SetParent(uOb);

            return uOb;
        }

        class MeshMaterialBinding
        {
            private string meshName;
            private Mesh mesh;
            private bool hasBones;
            private Material material;

            private MeshMaterialBinding() { }
            public MeshMaterialBinding(string meshName, bool hasBones, Mesh mesh, Material material)
            {
                this.meshName = meshName;
                this.mesh = mesh;
                this.hasBones = hasBones;
                this.material = material;
            }

            public Mesh Mesh { get => mesh; }
            public bool HasBones { get => hasBones; }
            public Material Material { get => material; }
            public string MeshName { get => meshName; }
        }
    }

    [CustomEditor(typeof(ModelImporter))]
    public class ModelEditor : ScriptedEditor
    {
        public override void OnInspectorGUI()
        {
            var importer = (ModelImporter)(target as MetaFile).importer;

            ImGui.Checkbox("Generate Normals", ref importer.GenerateNormals);
            if(importer.GenerateNormals)
                ImGui.Checkbox("Generate Smooth Normals", ref importer.GenerateSmoothNormals);
            ImGui.Checkbox("Calculate Tangent Space", ref importer.CalculateTangentSpace);
            ImGui.Checkbox("Make Left Handed", ref importer.MakeLeftHanded);
            ImGui.Checkbox("Flip UVs", ref importer.FlipUVs);
            ImGui.Checkbox("Optimize Meshes", ref importer.OptimizeMeshes);
            ImGui.Checkbox("Flip Winding Order", ref importer.FlipWindingOrder);
            ImGui.Checkbox("Weld Vertices", ref importer.WeldVertices);
            ImGui.Checkbox("Invert Normals", ref importer.InvertNormals);
            ImGui.Checkbox("GlobalScale", ref importer.GlobalScale);
            ImGui.DragFloat("UnitScale", ref importer.UnitScale, 0.01f, 0.01f, 1000f);

#warning TODO: Support for Exporting sub assets
#warning TODO: Support for editing Model specific data like Animation data

            if (ImGui.Button("Save")) {
                (target as MetaFile).Save();
                AssetDatabase.Reimport(AssetDatabase.FileToRelative((target as MetaFile).AssetPath));
            }
        }
    }
}
