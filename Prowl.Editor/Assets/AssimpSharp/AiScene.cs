using System;
using System.Collections.Generic;
using System.Numerics;

namespace AssimpSharp
{
    public class AiNode
    {
        public string Name { get; set; } = "";
        public Matrix4x4 Transform { get; set; } = Matrix4x4.Identity;
        public AiNode Parent { get; set; }
        public int NumChildren { get; set; }
        public List<AiNode> Children { get; set; } = new List<AiNode>();
        public int NumMeshes { get; set; }
        public int[] Meshes { get; set; } = Array.Empty<int>();
        public List<AiMetadata> MetaData { get; set; }

        public bool HasChildren => Children.Count > 0;
        public bool HasMeshes => Meshes.Length > 0;

        public AiNode FindNode(string name)
        {
            if (this.Name == name) return this;
            return Children.FirstOrDefault(child => child.FindNode(name) != null);
        }
    }

    public class AiMetadata
    {
        // TODO: Implement
    }

    public class AiScene
    {
        public const int AI_SCENE_FLAGS_INCOMPLETE = 0x1;
        public const int AI_SCENE_FLAGS_VALIDATED = 0x2;
        public const int AI_SCENE_FLAGS_VALIDATION_WARNING = 0x4;
        public const int AI_SCENE_FLAGS_NON_VERBOSE_FORMAT = 0x8;
        public const int AI_SCENE_FLAGS_TERRAIN = 0x10;
        public const int AI_SCENE_FLAGS_ALLOW_SHARED = 0x20;

        public int Flags { get; set; }
        public AiNode RootNode { get; set; }
        public int NumMeshes { get; set; }
        public List<AiMesh> Meshes { get; set; } = [];
        public int NumMaterials { get; set; }
        public List<AiMaterial> Materials { get; set; } = [];
        public int NumAnimations { get; set; }
        public List<AiAnimation> Animations { get; set; } = [];
        public int NumTextures { get; set; }
        public List<string> Textures { get; set; } = [];
        public int NumLights { get; set; }
        public List<AiLight> Lights { get; set; } = [];
        public int NumCameras { get; set; }
        public List<AiCamera> Cameras { get; set; } = [];

        public bool HasMeshes => Meshes.Count > 0;
        public bool HasMaterials => Materials.Count > 0;
        public bool HasLights => Lights.Count > 0;
        public bool HasTextures => Textures.Count > 0;
        public bool HasCameras => Cameras.Count > 0;
        public bool HasAnimations => Animations.Count > 0;
    }
}
