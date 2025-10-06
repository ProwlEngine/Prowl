using System;
using System.Collections.Generic;
using System.IO;

namespace Prowl.Runtime.Resources
{
    public class Model : EngineObject
    {
        public string Name { get; set; }
        public ModelNode RootNode { get; set; }
        public List<Material> Materials { get; set; } = new();
        public List<ModelMesh> Meshes { get; set; } = new();
        public float UnitScale { get; set; } = 1.0f;

        public Model(string name)
        {
            Name = name;
        }

        /// <summary>
        /// Loads a model from a file (.obj, .fbx, .gltf, etc.)
        /// </summary>
        public static Model LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"Model file not found: {filePath}");

            var importer = new AssetImporting.ModelImporter();
            var model = importer.Import(new FileInfo(filePath));
            model.AssetPath = filePath;
            return model;
        }

        /// <summary>
        /// Loads a model from a stream
        /// </summary>
        public static Model LoadFromStream(Stream stream, string virtualPath)
        {
            var importer = new AssetImporting.ModelImporter();
            var model = importer.Import(stream, virtualPath);
            model.AssetPath = virtualPath;
            return model;
        }

        /// <summary>
        /// Loads a default embedded model
        /// </summary>
        public static Model LoadDefault(DefaultModel model)
        {
            string fileName = model switch
            {
                DefaultModel.Cube => "Cube.obj",
                DefaultModel.Sphere => "Sphere.obj",
                DefaultModel.Cylinder => "Cylinder.obj",
                DefaultModel.Plane => "Plane.obj",
                DefaultModel.SkyDome => "SkyDome.obj",
                DefaultModel.UnitCube => "1mcube.obj",
                _ => throw new ArgumentException($"Unknown default model: {model}")
            };

            string resourcePath = $"Assets/Defaults/{fileName}";
            using (var stream = EmbeddedResources.GetStream(resourcePath))
            {
                var importer = new AssetImporting.ModelImporter();
                var result = importer.Import(stream, resourcePath);
                result.AssetPath = $"$Default:{model}";
                return result;
            }
        }
    }

    public class ModelNode
    {
        public string Name { get; set; }
        public Vector3 LocalPosition { get; set; }
        public Quaternion LocalRotation { get; set; }
        public Vector3 LocalScale { get; set; } = Vector3.one;
        public List<ModelNode> Children { get; set; } = new();

        // For single mesh per node
        public int? MeshIndex { get; set; }

        // For multiple meshes per node
        public List<int> MeshIndices { get; set; } = new();

        public ModelNode(string name)
        {
            Name = name;
        }
    }

    public class ModelMesh
    {
        public string Name { get; set; }
        public Mesh Mesh { get; set; }
        public Material Material { get; set; }
        public bool HasBones { get; set; }

        public ModelMesh(string name, Mesh mesh, Material material, bool hasBones = false)
        {
            Name = name;
            Mesh = mesh;
            Material = material;
            HasBones = hasBones;
        }
    }
}
