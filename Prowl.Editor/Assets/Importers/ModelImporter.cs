using Assimp;
using Prowl.Runtime;
using Prowl.Runtime.Assets;
using Prowl.Runtime.Components;
using Prowl.Runtime.Resources;
using Prowl.Runtime.Utils;
using HexaEngine.ImGuiNET;
using System;
using System.Numerics;
using System.Xml.Linq;
using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;

namespace Prowl.Editor.Assets
{

    [Importer("ModelIcon.png", typeof(GameObject), ".obj", ".blend", ".dae", ".fbx", ".gltf", ".ply", ".pmx", ".stl")]
    public class ModelImporter : ScriptedImporter
    {
        public static readonly string[] Supported = { ".obj", ".blend", ".dae", ".fbx", ".gltf", ".ply", ".pmx", ".stl" };

        public bool GenerateNormals = true;
        public bool GenerateSmoothNormals = false;
        public bool CalculateTangentSpace = true;
        public bool Triangulate = true;
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

            try
            {

                using (var importer = new AssimpContext())
                {
                    importer.SetConfig(new Assimp.Configs.VertexBoneWeightLimitConfig(4));
                    var steps = PostProcessSteps.LimitBoneWeights | PostProcessSteps.GenerateUVCoords;
                    if (GenerateNormals && GenerateSmoothNormals) steps |= PostProcessSteps.GenerateSmoothNormals;
                    else if (GenerateNormals) steps |= PostProcessSteps.GenerateNormals;
                    if (CalculateTangentSpace) steps |= PostProcessSteps.CalculateTangentSpace;
                    if (Triangulate) steps |= PostProcessSteps.Triangulate;
                    if (MakeLeftHanded) steps |= PostProcessSteps.MakeLeftHanded;
                    if (FlipUVs) steps |= PostProcessSteps.FlipUVs;
                    if (OptimizeMeshes) steps |= PostProcessSteps.OptimizeMeshes;
                    if (FlipWindingOrder) steps |= PostProcessSteps.FlipWindingOrder;
                    if (WeldVertices) steps |= PostProcessSteps.JoinIdenticalVertices;
                    if (GlobalScale) steps |= PostProcessSteps.GlobalScale;
                    var scene = importer.ImportFile(assetPath.FullName, steps);
                    if (scene == null) Failed("Assimp returned null object.");

                    DirectoryInfo? parentDir = assetPath.Directory;

                    if (!scene.HasMeshes) Failed("Model has no Meshes.");

                    List<Material> mats = new();
                    if (scene.HasMaterials)
                        foreach (var m in scene.Materials)
                        {
                            Material mat = new Material(Shader.Find("Defaults/Standard.shader"));
                            ctx.AddSubObject(mat);

                            // Albedo
                            if (m.HasColorDiffuse)
                                mat.SetColor("_MainColor", new Color(m.ColorDiffuse.R, m.ColorDiffuse.G, m.ColorDiffuse.B, m.ColorDiffuse.A));
                            else
                                mat.SetColor("_MainColor", Color.white);

                            // Texture
                            if (m.HasTextureDiffuse)
                            {
                                var file = new FileInfo(Path.Combine(parentDir.FullName, m.TextureDiffuse.FilePath));
                                if(file.Exists)
                                    LoadTextureIntoMesh("_MainTex", ctx, file, mat);
                                else
                                    mat.SetTexture("_MainTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/grid.png")));
                            }
                            else
                                mat.SetTexture("_MainTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/grid.png")));

                            // Normal Texture
                            if (m.HasTextureNormal)
                            {
                                var file = new FileInfo(Path.Combine(parentDir.FullName, m.TextureNormal.FilePath));
                                if (file.Exists)
                                    LoadTextureIntoMesh("_NormalTex", ctx, file, mat);
                                else
                                    mat.SetTexture("_NormalTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_normal.png")));
                            }
                            else
                                mat.SetTexture("_NormalTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_normal.png")));

                            // Roughness Texture
                            if (m.GetMaterialTexture(TextureType.Roughness, 0, out var roughnessSlot))
                            {
                                var file = new FileInfo(Path.Combine(parentDir.FullName, roughnessSlot.FilePath));
                                if (file.Exists)
                                    LoadTextureIntoMesh("_RoughnessTex", ctx, file, mat);
                                else
                                    mat.SetTexture("_RoughnessTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_roughness.png")));
                            }
                            else
                                mat.SetTexture("_RoughnessTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_roughness.png")));

                            // Shininess Texture
                            if (m.GetMaterialTexture(TextureType.Shininess, 0, out var shininessSlot))
                            {
                                var file = new FileInfo(Path.Combine(parentDir.FullName, shininessSlot.FilePath));
                                if (file.Exists)
                                    LoadTextureIntoMesh("_RoughnessTex", ctx, file, mat);
                                else
                                    mat.SetTexture("_RoughnessTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_roughness.png")));
                            }
                            else
                                mat.SetTexture("_RoughnessTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_roughness.png")));

                            // Metallic Texture
                            if (m.GetMaterialTexture(TextureType.Metalness, 0, out var metalnessSlot))
                            {
                                var file = new FileInfo(Path.Combine(parentDir.FullName, metalnessSlot.FilePath));
                                if (file.Exists)
                                    LoadTextureIntoMesh("_MetallicTex", ctx, file, mat);
                                else
                                    mat.SetTexture("_MetallicTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_emission.png")));
                            }
                            else
                                mat.SetTexture("_MetallicTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_emission.png")));

                            // Specular Texture (As Metallic)
                            if (m.GetMaterialTexture(TextureType.Specular, 0, out var specularSlot))
                            {
                                var file = new FileInfo(Path.Combine(parentDir.FullName, specularSlot.FilePath));
                                if (file.Exists)
                                    LoadTextureIntoMesh("_MetallicTex", ctx, file, mat);
                                else
                                    mat.SetTexture("_MetallicTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_emission.png")));
                            }
                            else
                                mat.SetTexture("_MetallicTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_emission.png")));

                            // Emissive Texture
                            if (m.HasTextureEmissive)
                            {
                                var file = new FileInfo(Path.Combine(parentDir.FullName, m.TextureEmissive.FilePath));
                                if (file.Exists)
                                    LoadTextureIntoMesh("_EmissionTex", ctx, file, mat);
                                else
                                    mat.SetTexture("_EmissionTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_emission.png")));
                            }
                            else
                                mat.SetTexture("_EmissionTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_emission.png")));

                            // Ambient Occlusion Texture
                            if (m.HasTextureAmbientOcclusion)
                            {
                                var file = new FileInfo(Path.Combine(parentDir.FullName, m.TextureAmbientOcclusion.FilePath));
                                if (file.Exists)
                                    LoadTextureIntoMesh("_OcclusionTex", ctx, file, mat);
                                else
                                    mat.SetTexture("_OcclusionTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_ao.png")));
                            }
                            else
                                mat.SetTexture("_OcclusionTex", new AssetRef<Texture2D>(AssetDatabase.GUIDFromAssetPath("Defaults/default_ao.png")));

                            mats.Add(mat);
                        }

                    List<MeshMaterialBinding> meshMats = new List<MeshMaterialBinding>();
                    if (scene.HasMeshes)
                        foreach (var m in scene.Meshes)
                        {
                            List<float> verts = new List<float>();
                            List<float> norms = new List<float>();
                            List<float> uvs = new List<float>();
                            List<float> uvs2 = new List<float>();
                            List<ushort> triangles = new List<ushort>();

                            // Vertices
                            if (m.HasVertices)
                                foreach (var v in m.Vertices)
                                {
                                    verts.Add(-v.X);
                                    verts.Add(v.Y);
                                    verts.Add(v.Z);
                                }

                            // Normals
                            if (m.HasNormals)
                                foreach (var n in m.Normals)
                                {
                                    if (InvertNormals)
                                    {
                                        norms.Add(n.X);
                                        norms.Add(-n.Y);
                                        norms.Add(-n.Z);
                                    }
                                    else
                                    {
                                        norms.Add(-n.X);
                                        norms.Add(n.Y);
                                        norms.Add(n.Z);
                                    }
                                }

                            // Triangles
                            if (m.HasFaces)
                                foreach (var f in m.Faces)
                                {
                                    // Ignore degenerate faces
                                    if (f.IndexCount == 1 || f.IndexCount == 2)
                                        continue;

                                    for (int i = 0; i < (f.IndexCount - 2); i++)
                                    {
                                        triangles.Add((ushort)f.Indices[i + 2]);
                                        triangles.Add((ushort)f.Indices[i + 1]);
                                        triangles.Add((ushort)f.Indices[0]);
                                    }
                                }

                            // Uv (texture coordinate) 
                            if (m.HasTextureCoords(0))
                                foreach (var uv in m.TextureCoordinateChannels[0])
                                {
                                    uvs.Add(uv.X); uvs.Add(uv.Y);
                                }

                            // Uv2 (texture coordinate) 
                            if (m.HasTextureCoords(1))
                                foreach (var uv in m.TextureCoordinateChannels[1])
                                {
                                    uvs2.Add(uv.X); uvs2.Add(uv.Y);
                                }

                            Mesh mesh = new();
                            mesh.vertices = verts.ToArray();
                            mesh.normals = norms.ToArray();
                            mesh.triangles = triangles.ToArray();
                            mesh.texcoords = uvs.ToArray();
                            mesh.texcoords2 = uvs2.ToArray();
                            ctx.AddSubObject(mesh);

                            meshMats.Add(new MeshMaterialBinding(m.Name, mesh, mats[m.MaterialIndex]));
                        }

                    GameObject rootNode = NodeToGameObject(scene.RootNode, meshMats);
                    rootNode.Scale = Vector3.One * UnitScale;
                    ctx.SetMainObject(rootNode);

                    ImGuiNotify.InsertNotification("Model Imported.", new(0.75f, 0.35f, 0.20f, 1.00f), assetPath.FullName);
                }
            }
            catch (Exception e)
            {
                ImGuiNotify.InsertNotification("Failed to Import Model.", new(0.8f, 0.1f, 0.1f, 1), "Reason: " + e.Message);
            }
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
                // Ok so the texture isnt loaded, lets make sure it exists
                if (!file.Exists)
                    throw new FileNotFoundException($"Texture file for model was not found!", file.FullName);

                // Ok so we dont have it in the asset database but the file does infact exist
                // so lets load it in as a sub asset to this object
                Texture2D tex = new Texture2D(file.FullName);
                ctx.AddSubObject(tex);
                mat.SetTexture(name, new AssetRef<Texture2D>(guid));
            }
        }

        // Create GameObjects from nodes
        GameObject NodeToGameObject(Node node, in List<MeshMaterialBinding> meshMats)
        {
            GameObject uOb = GameObject.CreateSilently();
            uOb.Name = node.Name;

            // Set Mesh
            if (node.HasMeshes)
                foreach (var mIdx in node.MeshIndices)
                {
                    var uMeshAndMat = meshMats[mIdx];
                    GameObject uSubOb = GameObject.CreateSilently();
                    uSubOb.Name = uMeshAndMat.MeshName;
                    uSubOb.AddComponent<MeshRenderer>();
                    uSubOb.GetComponent<MeshRenderer>().Mesh = uMeshAndMat.Mesh;
                    uSubOb.GetComponent<MeshRenderer>().Material = uMeshAndMat.Material;
                    uSubOb.SetParent(uOb);
                }

            // Transform
            node.Transform.Decompose(out var aScale, out var aQuat, out var aTranslation);

            uOb.Scale = new Vector3(aScale.X, aScale.Y, aScale.Z);
            uOb.Position = new Vector3(aTranslation.X, aTranslation.Y, aTranslation.Z);
            uOb.Orientation = new System.Numerics.Quaternion(aQuat.X, aQuat.Y, aQuat.Z, aQuat.W);

            if (node.HasChildren) foreach (var cn in node.Children) NodeToGameObject(cn, meshMats).SetParent(uOb);

            return uOb;
        }


        class MeshMaterialBinding
        {
            private string meshName;
            private Mesh mesh;
            private Material material;

            private MeshMaterialBinding() { }
            public MeshMaterialBinding(string meshName, Mesh mesh, Material material)
            {
                this.meshName = meshName;
                this.mesh = mesh;
                this.material = material;
            }

            public Mesh Mesh { get => mesh; }
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
            ImGui.Checkbox("Triangulate", ref importer.Triangulate);
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
