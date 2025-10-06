using System.Collections.Generic;

namespace Prowl.Runtime.Resources
{
    public class Model : EngineObject
    {
        public string Name { get; set; }
        public ModelNode RootNode { get; set; }
        public List<AssetRef<Material>> Materials { get; set; } = new();
        public List<ModelMesh> Meshes { get; set; } = new();
        public float UnitScale { get; set; } = 1.0f;

        public Model(string name)
        {
            Name = name;
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
        public AssetRef<Mesh> Mesh { get; set; }
        public AssetRef<Material> Material { get; set; }
        public bool HasBones { get; set; }

        public ModelMesh(string name, AssetRef<Mesh> mesh, AssetRef<Material> material, bool hasBones = false)
        {
            Name = name;
            Mesh = mesh;
            Material = material;
            HasBones = hasBones;
        }
    }
}
