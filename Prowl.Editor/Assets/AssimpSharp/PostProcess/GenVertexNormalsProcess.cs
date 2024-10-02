using System.Numerics;

namespace AssimpSharp
{
    public class GenVertexNormalsProcess : BaseProcess
    {
        public const float Deg2Rad = 6.28318530717959f / 360f;

        private float configMaxAngle;
        private bool force;
        private bool flippedWindingOrder;
        private bool leftHanded;

        public GenVertexNormalsProcess()
        {
            configMaxAngle = 175f * Deg2Rad;
        }

        public override bool IsActive(int f)
        {
            AiPostProcessSteps flags = (AiPostProcessSteps)f;
            force = flags.HasFlag(AiPostProcessSteps.ForceGenNormals);
            flippedWindingOrder = flags.HasFlag(AiPostProcessSteps.FlipWindingOrder);
            leftHanded = flags.HasFlag(AiPostProcessSteps.MakeLeftHanded);
            return flags.HasFlag(AiPostProcessSteps.GenSmoothNormals);
        }

        public override void SetupProperties(Importer importer)
        {
            configMaxAngle = importer.GetFloatProperty(PropertyRepository.AI_CONFIG_PP_GSN_MAX_SMOOTHING_ANGLE, 175.0f);
            configMaxAngle = Math.Max(Math.Min(configMaxAngle, 175.0f), 0.0f) * Deg2Rad;
        }

        public override void Execute(AiScene scene)
        {
            Console.WriteLine("GenVertexNormalsProcess begin");

            bool hasNormals = false;
            for (int a = 0; a < scene.NumMeshes; ++a)
            {
                if (GenMeshVertexNormals(scene.Meshes[a], a))
                    hasNormals = true;
            }

            if (hasNormals)
            {
                Console.WriteLine("GenVertexNormalsProcess finished. Vertex normals have been calculated");
            }
            else
            {
                Console.WriteLine("GenVertexNormalsProcess finished. Normals are already there");
            }
        }

        private bool GenMeshVertexNormals(AiMesh mesh, int meshIndex)
        {
            if (mesh.HasNormals)
            {
                if (!force)
                {
                    return false;
                }
                mesh.Normals = null;
            }

            if (!mesh.PrimitiveType.HasFlag(AiPrimitiveType.Triangle | AiPrimitiveType.Polygon))
            {
                Console.WriteLine("Normal vectors are undefined for line and point meshes");
                return false;
            }

            mesh.Normals = new List<Vector3>(new Vector3[mesh.VertexCount]);

            for (int a = 0; a < mesh.NumFaces; a++)
            {
                AiFace face = mesh.Faces[a];
                if (face.Count < 3)
                {
                    for (int i = 0; i < face.Count; ++i)
                    {
                        mesh.Normals[face[i]] = new Vector3(float.NaN);
                    }
                    continue;
                }

                Vector3 pV1 = mesh.Vertices[face[0]];
                Vector3 pV2 = mesh.Vertices[face[1]];
                Vector3 pV3 = mesh.Vertices[face[face.Count - 1]];

                if (flippedWindingOrder != leftHanded)
                {
                    (pV2, pV3) = (pV3, pV2);
                }

                Vector3 vNor = Vector3.Normalize(Vector3.Cross(pV2 - pV1, pV3 - pV1));

                for (int i = 0; i < face.Count; ++i)
                {
                    mesh.Normals[face[i]] = vNor;
                }
            }

            SpatialSort vertexFinder = new SpatialSort();
            float posEpsilon = 1e-5f;

            if (Shared != null)
            {
                // Assuming Shared.GetProperty is implemented elsewhere
                // var avf = Shared.GetProperty<List<(SpatialSort, float)>>(AI_SPP_SPATIAL_SORT);
                // if (avf != null && meshIndex < avf.Count)
                // {
                //     (vertexFinder, posEpsilon) = avf[meshIndex];
                // }
            }

            if (vertexFinder == null)
            {
                vertexFinder = new SpatialSort();
                vertexFinder.Fill(mesh.Vertices);
                posEpsilon = ComputePositionEpsilon(mesh);
            }

            List<int> verticesFound = new List<int>();
            Vector3[] pcNew = new Vector3[mesh.VertexCount];

            if (configMaxAngle >= 175f * Deg2Rad)
            {
                bool[] abHad = new bool[mesh.VertexCount];
                for (int i = 0; i < mesh.VertexCount; ++i)
                {
                    if (abHad[i]) continue;

                    vertexFinder.FindPositions(mesh.Vertices[i], posEpsilon, verticesFound);

                    Vector3 pcNor = Vector3.Zero;
                    foreach (int idx in verticesFound)
                    {
                        Vector3 v = mesh.Normals[idx];
                        if (!float.IsNaN(v.X)) pcNor += v;
                    }
                    pcNor = Vector3.Normalize(pcNor);

                    foreach (int idx in verticesFound)
                    {
                        pcNew[idx] = pcNor;
                        abHad[idx] = true;
                    }
                }
            }
            else
            {
                float fLimit = (float)Math.Cos(configMaxAngle);
                for (int i = 0; i < mesh.VertexCount; ++i)
                {
                    vertexFinder.FindPositions(mesh.Vertices[i], posEpsilon, verticesFound);

                    Vector3 vr = mesh.Normals[i];

                    Vector3 pcNor = Vector3.Zero;
                    foreach (int idx in verticesFound)
                    {
                        Vector3 v = mesh.Normals[idx];

                        if (!float.IsNaN(v.X) && (idx == i || Vector3.Dot(v, vr) >= fLimit))
                            pcNor += v;
                    }
                    pcNew[i] = Vector3.Normalize(pcNor);
                }
            }

            mesh.Normals = pcNew.ToList();

            return true;
        }

        private float ComputePositionEpsilon(AiMesh mesh)
        {
            // Implement this method based on your needs
            return 1e-5f;
        }
    }

    public class SpatialSort
    {
        // Implement this class based on your needs
        public void Fill(List<Vector3> vertices) { }
        public void FindPositions(Vector3 vertex, float epsilon, List<int> result) { }
    }
}
