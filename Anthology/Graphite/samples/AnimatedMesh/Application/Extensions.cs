using System.Numerics;
using System.Runtime.CompilerServices;

namespace AnimatedMesh
{
    internal unsafe static class Extensions
    {
        public static Matrix4x4 ToSystemMatrix(this Assimp.Matrix4x4 mat)
        {
            return Unsafe.Read<Matrix4x4>(&mat);
        }

        public static Matrix4x4 ToSystemMatrixTransposed(this Assimp.Matrix4x4 mat)
        {
            return Matrix4x4.Transpose(Unsafe.Read<Matrix4x4>(&mat));
        }


        public static Quaternion ToSystemQuaternion(this Assimp.Quaternion quat)
        {
            return new Quaternion(quat.X, quat.Y, quat.Z, quat.W);
        }

        public static Vector3 ToSystemVector3(this Assimp.Vector3D v3)
        {
            return new Vector3(v3.X, v3.Y, v3.Z);
        }
    }
}
