using System.Collections.Generic;
using System.Numerics;

namespace AssimpSharp
{
    public class AiFace : List<int> { }

    public struct AiVertexWeight
    {
        public int VertexId { get; set; }
        public float Weight { get; set; }
    }

    public class AiBone
    {
        public string Name { get; set; } = "";
        public int NumWeights { get; set; }
        public AiVertexWeight[] Weights { get; set; }
        public Matrix4x4 OffsetMatrix { get; set; }
    }

    [Flags]
    public enum AiPrimitiveType
    {
        Point = 0x1,
        Line = 0x2,
        Triangle = 0x4,
        Polygon = 0x8
    }

    public class AiAnimMesh : AiMesh
    {
        public float Weight { get; set; }
    }

    public enum AiMorphingMethod
    {
        VertexBlend = 0x1,
        MorphNormalized = 0x2,
        MorphRelative = 0x3
    }

    public class AiMesh
    {
        public AiPrimitiveType PrimitiveType { get; set; }
        public int VertexCount => NumVertices;
        public int NumVertices { get; set; }
        public int NumFaces { get; set; }
        public List<Vector3> Vertices { get; set; } = new List<Vector3>();
        public List<Vector3> Normals { get; set; } = new List<Vector3>();
        public List<Vector3> Tangents { get; set; } = new List<Vector3>();
        public List<Vector3> Bitangents { get; set; } = new List<Vector3>();
        public List<List<Vector4>> VertexColorChannels { get; set; } = new List<List<Vector4>>();
        public List<List<float[]>> TextureCoordinateChannels { get; set; } = new List<List<float[]>>();
        public List<AiFace> Faces { get; set; } = new List<AiFace>();
        public int NumBones { get; set; }
        public List<AiBone> Bones { get; set; } = new List<AiBone>();
        public int MaterialIndex { get; set; }
        public string Name { get; set; } = "";
        public int NumAnimMeshes { get; set; }
        public List<AiMesh> AnimMeshes { get; set; } = new List<AiMesh>();
        public int Method { get; set; }

        public bool HasPositions => NumVertices > 0;
        public bool HasFaces => NumFaces > 0;
        public bool HasNormals => Normals.Count > 0 && NumVertices > 0;
        public bool HasTangents => Tangents.Count > 0 && NumVertices > 0;
        public bool HasTangentsAndBitangents => Tangents.Count > 0 && Bitangents.Count > 0 && NumVertices > 0;
        public bool HasVertexColors(int index) => index < Constants.AI_MAX_NUMBER_OF_COLOR_SETS && VertexColorChannels.Count > index && index < VertexColorChannels.Count && NumVertices > 0;
        public bool HasTextureCoords(int index) => index < Constants.AI_MAX_NUMBER_OF_TEXTURECOORDS && TextureCoordinateChannels.Count > index && TextureCoordinateChannels[index].Count > 0 && NumVertices > 0;
        public int GetNumUVChannels => TextureCoordinateChannels.Count;
        public int GetNumColorChannels => VertexColorChannels.Count;
        public bool HasBones => Bones.Count > 0 && NumBones > 0;

        public uint[] GetUnsignedIndices()
        {
            if (HasFaces)
            {
                List<uint> indices = new List<uint>();
                foreach (AiFace face in Faces)
                {
                    if (face.Count > 0)
                    {
                        foreach (uint index in face)
                        {
                            indices.Add((uint)index);
                        }
                    }
                }

                return indices.ToArray();
            }

            return null;
        }

        public short[] GetShortIndices()
        {
            if (HasFaces)
            {
                List<short> indices = new List<short>();
                foreach (AiFace face in Faces)
                {
                    if (face.Count > 0)
                    {
                        foreach (uint index in face)
                        {
                            indices.Add((short)index);
                        }
                    }
                }

                return indices.ToArray();
            }

            return null;
        }
    }
}
