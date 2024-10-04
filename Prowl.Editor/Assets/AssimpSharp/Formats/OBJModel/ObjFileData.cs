using System;
using System.Collections.Generic;
using System.Numerics;

namespace AssimpSharp.Formats.Obj
{
    public class Face
    {
        public AiPrimitiveType PrimitiveType { get; set; } = AiPrimitiveType.Polygon;
        public List<int> Vertices { get; set; } = new List<int>();
        public List<int> Normals { get; set; } = new List<int>();
        public List<int> TextureCoords { get; set; } = new List<int>();
        public ObjMaterial Material { get; set; }
    }

    public class Object
    {
        public string ObjName { get; set; } = "";
        public Matrix4x4 Transformation { get; set; } = Matrix4x4.Identity;
        public List<Object> SubObjects { get; set; } = new List<Object>();
        public List<int> Meshes { get; set; } = new List<int>();

        public enum Type { Obj, Group }
    }

    public class ObjMaterial
    {
        public string MaterialName { get; set; }
        public List<ObjTexture> Textures { get; set; } = new List<ObjTexture>();
        public Vector3 Ambient { get; set; } = Vector3.Zero;
        public Vector3 Diffuse { get; set; } = new Vector3(0.6f);
        public Vector3 Specular { get; set; } = Vector3.Zero;
        public Vector3 Emissive { get; set; } = Vector3.Zero;
        public float Alpha { get; set; } = 1.0f;
        public float Shininess { get; set; } = 0.0f;
        public int IlluminationModel { get; set; } = 1;
        public float Ior { get; set; } = 1.0f;
        public Vector3 Transparent { get; set; } = Vector3.One;

        public class ObjTexture
        {
            public string Name { get; set; }
            public Type TextureType { get; set; }
            public bool Clamp { get; set; } = false;

            public enum Type
            {
                Diffuse,
                Specular,
                Ambient,
                Emissive,
                Bump,
                Normal,
                ReflectionSphere,
                ReflectionCubeTop,
                ReflectionCubeBottom,
                ReflectionCubeFront,
                ReflectionCubeBack,
                ReflectionCubeLeft,
                ReflectionCubeRight,
                Specularity,
                Opacity,
                Displacement
            }
        }
    }

    public class ObjMesh
    {
        public const int NoMaterial = -1;

        public string Name { get; set; }
        public List<Face> Faces { get; set; } = new List<Face>();
        public ObjMaterial Material { get; set; }
        public int NumIndices { get; set; } = 0;
        public int[] UVCoordinates { get; set; } = new int[Constants.AI_MAX_NUMBER_OF_TEXTURECOORDS];
        public int MaterialIndex { get; set; } = NoMaterial;
        public bool HasNormals { get; set; } = false;
        public bool HasVertexColors { get; set; } = true;
    }

    public class Model
    {
        public string ModelName { get; set; } = "";
        public List<Object> Objects { get; set; } = new List<Object>();
        public Object Current { get; set; }
        public ObjMaterial CurrentMaterial { get; set; }
        public ObjMaterial DefaultMaterial { get; set; }
        public List<string> MaterialLib { get; set; } = new List<string>();
        public List<Vector3> Vertices { get; set; } = new List<Vector3>();
        public List<Vector3> Normals { get; set; } = new List<Vector3>();
        public List<Vector3> VertexColors { get; set; } = new List<Vector3>();
        public Dictionary<string, List<int>> Groups { get; set; } = new Dictionary<string, List<int>>();
        public List<int> GroupFaceIDs { get; set; } = new List<int>();
        public string ActiveGroup { get; set; } = "";
        public List<List<float>> TextureCoord { get; set; } = new List<List<float>>();
        public ObjMesh CurrentMesh { get; set; }
        public List<ObjMesh> Meshes { get; set; } = new List<ObjMesh>();
        public Dictionary<string, ObjMaterial> MaterialMap { get; set; } = new Dictionary<string, ObjMaterial>();
    }
}
