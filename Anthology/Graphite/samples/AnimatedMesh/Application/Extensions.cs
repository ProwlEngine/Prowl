using System.Numerics;
using Silk.NET.Assimp;

namespace AnimatedMesh
{
    internal static class Extensions
    {
        /// <summary>
        /// Transposes a matrix from Assimp's column-vector convention to System.Numerics' row-vector convention.
        /// Assimp stores translation in (M14, M24, M34); System.Numerics expects it in (M41, M42, M43).
        /// </summary>
        public static Matrix4x4 ToSystemMatrix(this Matrix4x4 assimpMatrix)
        {
            return Matrix4x4.Transpose(assimpMatrix);
        }

        public static Quaternion ToSystemQuaternion(this AssimpQuaternion q)
        {
            return new Quaternion(q.X, q.Y, q.Z, q.W);
        }
    }
}
