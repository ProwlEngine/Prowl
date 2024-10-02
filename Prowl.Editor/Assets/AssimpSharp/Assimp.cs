using System;
using System.Numerics;

namespace AssimpSharp
{
    public static class AssimpExtensions
    {
        public static void Decompose(this Matrix4x4 matrix, out Vector3 scaling, out Quaternion rotation, out Vector3 position)
        {
            Matrix4x4.Decompose(matrix, out scaling, out rotation, out position);
        }

        public static Vector3 Transform(this Matrix4x4 matrix, Vector3 vector)
        {
            return Vector3.Transform(vector, matrix);
        }

        public static bool IsBlack(this Vector3 vector)
        {
            const float epsilon = 10e-3f;
            return Math.Abs(vector.X) < epsilon && Math.Abs(vector.Y) < epsilon && Math.Abs(vector.Z) < epsilon;
        }
    }

    public static class ASSIMP
    {
        public static class BUILD
        {
            public static bool DEBUG = true;

            public static class NO
            {
                public static bool VALIDATEDS_PROCESS = true;
                public static bool X_IMPORTER = false;
                public static bool OBJ_IMPORTER = false;
                public static bool AMF_IMPORTER = false;
                public static bool _3DS_IMPORTER = false;
                public static bool MD3_IMPORTER = false;
                public static bool MD2_IMPORTER = false;
                public static bool PLY_IMPORTER = false;
                public static bool MDL_IMPORTER = false;
                public static bool ASE_IMPORTER = false;
                public static bool HMP_IMPORTER = false;
                public static bool SMD_IMPORTER = false;
                public static bool MDC_IMPORTER = false;
                public static bool MD5_IMPORTER = false;
                public static bool STL_IMPORTER = false;
                public static bool LWO_IMPORTER = false;
                public static bool DXF_IMPORTER = false;
                public static bool NFF_IMPORTER = false;
                public static bool RAW_IMPORTER = false;
                public static bool SIB_IMPORTER = false;
                public static bool OFF_IMPORTER = false;
                public static bool AC_IMPORTER = false;
                public static bool BVH_IMPORTER = false;
                public static bool IRRMESH_IMPORTER = false;
                public static bool IRR_IMPORTER = false;
                public static bool Q3D_IMPORTER = false;
                public static bool B3D_IMPORTER = false;
                public static bool COLLADA_IMPORTER = false;
                public static bool TERRAGEN_IMPORTER = false;
                public static bool CSM_IMPORTER = false;
                public static bool _3D_IMPORTER = false;
                public static bool LWS_IMPORTER = false;
                public static bool OGRE_IMPORTER = false;
                public static bool OPENGEX_IMPORTER = false;
                public static bool MS3D_IMPORTER = false;
                public static bool COB_IMPORTER = false;
                public static bool BLEND_IMPORTER = false;
                public static bool Q3BSP_IMPORTER = false;
                public static bool NDO_IMPORTER = false;
                public static bool IFC_IMPORTER = false;
                public static bool XGL_IMPORTER = false;
                public static bool FBX_IMPORTER = false;
                public static bool ASSBIN_IMPORTER = false;
                public static bool GLTF_IMPORTER = false;
                public static bool C4D_IMPORTER = false;
                public static bool _3MF_IMPORTER = false;
                public static bool X3D_IMPORTER = false;
            }
        }
    }
}
