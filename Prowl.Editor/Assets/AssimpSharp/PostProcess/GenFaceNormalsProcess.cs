using System;
using System.Numerics;

using Assimp;

namespace AssimpSharp
{
    public class GenFaceNormalsProcess : BaseProcess
    {
        private bool force_;
        private bool flippedWindingOrder_;
        private bool leftHanded_;

        public override bool IsActive(int f)
        {
            AiPostProcessSteps flags = (AiPostProcessSteps)f;
            force_ = flags.HasFlag(AiPostProcessSteps.ForceGenNormals);
            flippedWindingOrder_ = flags.HasFlag(AiPostProcessSteps.FlipWindingOrder);
            leftHanded_ = flags.HasFlag(AiPostProcessSteps.MakeLeftHanded);
            return flags.HasFlag(AiPostProcessSteps.GenNormals);
        }

        public override void Execute(AiScene scene)
        {
            Console.WriteLine("GenFaceNormalsProcess begin");

            bool hasNormals = false;
            for (int a = 0; a < scene.NumMeshes; a++)
            {
                if (GenMeshFaceNormals(scene.Meshes[a]))
                {
                    hasNormals = true;
                }
            }

            if (hasNormals)
            {
                Console.WriteLine("GenFaceNormalsProcess finished. Face normals have been calculated");
            }
            else
            {
                Console.WriteLine("GenFaceNormalsProcess finished. Normals are already there");
            }
        }

        private bool GenMeshFaceNormals(AiMesh mesh)
        {
            if (mesh.HasNormals)
            {
                if (force_)
                {
                    mesh.Normals = null;
                }
                else
                {
                    return false;
                }
            }

            if (!mesh.PrimitiveType.HasFlag(AiPrimitiveType.Triangle | AiPrimitiveType.Polygon))
            {
                Console.WriteLine("Normal vectors are undefined for line and point meshes");
                return false;
            }

            mesh.Normals = new List<Vector3>(mesh.VertexCount);
            float qnan = float.NaN;

            for (int a = 0; a < mesh.NumFaces; a++)
            {
                AiFace face = mesh.Faces[a];
                if (face.Count < 3)
                {
                    for (int i = 0; i < face.Count; ++i)
                    {
                        mesh.Normals[face[i]] = new Vector3(qnan);
                    }
                    continue;
                }

                Vector3 pV1 = mesh.Vertices[face[0]];
                Vector3 pV2 = mesh.Vertices[face[1]];
                Vector3 pV3 = mesh.Vertices[face[face.Count - 1]];

                if (flippedWindingOrder_ != leftHanded_)
                {
                    (pV2, pV3) = (pV3, pV2);
                }

                Vector3 vNor = Vector3.Normalize(Vector3.Cross(pV2 - pV1, pV3 - pV1));

                for (int i = 0; i < face.Count; ++i)
                {
                    mesh.Normals[face[i]] = vNor;
                }
            }

            return true;
        }
    }
}
