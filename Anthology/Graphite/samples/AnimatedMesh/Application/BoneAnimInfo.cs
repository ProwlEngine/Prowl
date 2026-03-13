using System.Numerics;
using System.Runtime.CompilerServices;

namespace AnimatedMesh
{
    public unsafe struct BoneAnimInfo
    {
        public Matrix4x4[] BonesTransformations;

        public Blittable GetBlittable()
        {
            Blittable b;
            fixed (Matrix4x4* ptr = BonesTransformations)
            {
                Unsafe.CopyBlock(&b, ptr, 64 * 64);
            }

            return b;
        }

        public struct Blittable
        {
            public fixed float BoneData[16 * 64];
        }

        internal static BoneAnimInfo New()
        {
            return new BoneAnimInfo() { BonesTransformations = new Matrix4x4[64] };
        }
    }
}
