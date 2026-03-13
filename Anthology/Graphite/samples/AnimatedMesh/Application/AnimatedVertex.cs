using System.Numerics;

namespace AnimatedMesh
{
    public unsafe struct AnimatedVertex
    {
        public Vector3 Position;
        public Vector2 UV;
        public Vector4 BoneWeights;
        public UInt4 BoneIndices;

        public void AddBone(uint id, float weight)
        {
            if (BoneWeights.X == 0)
            {
                BoneWeights.X = weight;
                BoneIndices.X = id;
            }
            else if (BoneWeights.Y == 0)
            {
                BoneWeights.Y = weight;
                BoneIndices.Y = id;
            }
            else if (BoneWeights.Z == 0)
            {
                BoneWeights.Z = weight;
                BoneIndices.Z = id;
            }
            else if (BoneWeights.W == 0)
            {
                BoneWeights.W = weight;
                BoneIndices.W = id;
            }
        }
    }

    public struct UInt4
    {
        public uint X, Y, Z, W;
    }
}
