using System.Numerics;

namespace Prowl.Unwrapper
{
    public struct Vertex
    {
        public Vector3 position;
    }

    public struct UvRect
    {
        public float left;
        public float top;
        public float width;
        public float height;

        public readonly Vector2 Position => new(left, top);
        public readonly Vector2 Size => new(width, height);
    }

    public struct Face
    {
        public int[] indices;

        public Face() => indices = new int[3];
    }

    public struct TextureCoord
    {
        public float[] uv;
        public TextureCoord() => uv = new float[2];
        public TextureCoord(float u, float v) => uv = [u, v];
    }

    public struct FaceTextureCoords
    {
        public TextureCoord[] coords;
        public FaceTextureCoords() => coords = new TextureCoord[3];
    }

    public class UVMesh
    {
        public List<Vertex> vertices = [];
        public List<Face> faces = [];
        public List<Vector3> faceNormals = [];
        public List<int> facePartitions = [];
    }
}