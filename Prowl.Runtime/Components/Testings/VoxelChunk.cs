using Prowl.Icons;
using System.Collections.Generic;
using System.Diagnostics;

namespace Prowl.Runtime.Components.Testings
{
    [AddComponentMenu($"{FontAwesome6.Dna}  Testing/{FontAwesome6.Cubes}  VoxelChunk")]
    public class VoxelChunk : MonoBehaviour
    {

        public int resolution = 32;

        private Mesh mesh;

        [GUIButton("Mesh")]
        public override void Awake()
        {
            // native arrays (Unity will auto dispose NativeArrays that are allocated in a job)
            var indices = new List<uint>(100000);
            var vertices = new List<System.Numerics.Vector3>(100000);
            var uv = new List<System.Numerics.Vector2>(100000);

            // variables
            var size = new Vector3(resolution, resolution, resolution);
            var bounds = new Bounds(size / 2, size);

            // meshing offsets
            int vertexOffset = 0;

            bool[,,] isAirCache = new bool[resolution + 2, resolution + 2, resolution + 2];

            // Populate the isAirCache array
            for (int x = 0; x < resolution; x++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    for (int z = 0; z < resolution; z++)
                    {
                        isAirCache[x + 1, y + 1, z + 1] = IsAir(x, y, z);
                    }
                }
            }


            var timer = new Stopwatch();
            timer.Start();

            for (int x = 0; x < resolution; x++)
            {
                for (int y = 0; y < resolution; y++)
                {
                    for (int z = 0; z < resolution; z++)
                    {
                        Vector3 localPosition = new Vector3(x, y, z);
                        if (!isAirCache[x + 1, y + 1, z + 1])
                        {
                            for (int side = 0; side < 6; side++)
                            {
                                Vector3 neighborBlock = Neighbors[side];
                                if (isAirCache[x + (int)neighborBlock.x + 1, y + (int)neighborBlock.y + 1, z + (int)neighborBlock.z + 1])
                                {
                                    // Quad vertices
                                    int[] sideVertices = QuadVertices[side];
                                    vertices.Add(Vertices[sideVertices[0]] + localPosition);
                                    vertices.Add(Vertices[sideVertices[1]] + localPosition);
                                    vertices.Add(Vertices[sideVertices[2]] + localPosition);
                                    vertices.Add(Vertices[sideVertices[3]] + localPosition);

                                    // Quad UVs
                                    uv.Add(TexCoords[0]);
                                    uv.Add(TexCoords[1]);
                                    uv.Add(TexCoords[2]);
                                    uv.Add(TexCoords[3]);

                                    // 0 1 2 2 1 3 <- Indice numbers
                                    indices.Add((uint)(vertexOffset + 0));
                                    indices.Add((uint)(vertexOffset + 1));
                                    indices.Add((uint)(vertexOffset + 2));
                                    indices.Add((uint)(vertexOffset + 2));
                                    indices.Add((uint)(vertexOffset + 1));
                                    indices.Add((uint)(vertexOffset + 3));

                                    vertexOffset += 4;
                                }
                            }
                        }
                    }
                }
            }

            timer.Stop();
            Debug.Log($"[Prowl] Meshing + Mesh Creation {timer.Elapsed.TotalMilliseconds.ToString("0.000")}ms");

            mesh ??= new();
            mesh.Clear();

            mesh.Vertices = vertices.ToArray();
            mesh.UV = uv.ToArray();
            mesh.Indices = indices.ToArray();


            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.Upload();

        }

        [GUIButton("Assign Mesh")]
        public void AssignMesh()
        {
            if (mesh == null)
            {
                mesh = new Mesh();
            }

            GetComponent<MeshRenderer>().Mesh = mesh;
        }

        private bool IsAir(double v1, double v2, double v3)
        {
            int x = (int)v1;
            int y = (int)v2;
            int z = (int)v3;
            FastNoiseLite noise = new FastNoiseLite();
            noise.SetNoiseType(FastNoiseLite.NoiseType.OpenSimplex2);
            noise.SetFrequency(0.1f);
            noise.SetSeed(1337);
            noise.SetFractalType(FastNoiseLite.FractalType.FBm);
            noise.SetFractalOctaves(3);
            noise.SetFractalLacunarity(2.0f);
            noise.SetFractalGain(0.5f);
            float terrainHeight = noise.GetNoise(x, z) * 8;
            return y > terrainHeight;
        }



        /// <summary>
        /// right0, left1, up2, down3, front4, back5 (same for block neighbors and grid neighbors)
        /// </summary>
        public static readonly Vector3[] Neighbors = new Vector3[6]
        {
            new Vector3(1,  0, 0),
            new Vector3(-1, 0, 0),
            new Vector3(0,  1, 0),
            new Vector3(0, -1, 0),
            new Vector3(0, 0,  1),
            new Vector3(0, 0, -1),
        };

        /// <summary>
        /// all 8 possible vertices for a bloxel
        /// </summary>
        public static readonly Vector3[] Vertices = new Vector3[8]
        {
            new Vector3(0.0f, 0.0f, 0.0f),
            new Vector3(1.0f, 0.0f, 0.0f),
            new Vector3(1.0f, 1.0f, 0.0f),
            new Vector3(0.0f, 1.0f, 0.0f),
            new Vector3(0.0f, 0.0f, 1.0f),
            new Vector3(1.0f, 0.0f, 1.0f),
            new Vector3(1.0f, 1.0f, 1.0f),
            new Vector3(0.0f, 1.0f, 1.0f),
        };

        public static readonly Vector2[] TexCoords = new Vector2[4] {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(1, 1),
            new Vector2(0, 1),
        };

        /// <summary>
        /// vertices to build a quad for each side of a bloxel
        /// </summary>
        public static readonly int[][] QuadVertices = new int[6][]
        {
            // quad order
            // right, left, up, down, front, back

            // 0 1 2 2 1 3 <- triangle numbers

            // quads
            new int[4] {1, 2, 5, 6}, // right quad
            new int[4] {4, 7, 0, 3}, // left quad

            new int[4] {3, 7, 2, 6}, // up quad
            new int[4] {1, 5, 0, 4}, // down quad

            new int[4] {5, 6, 4, 7}, // front quad
            new int[4] {0, 3, 1, 2}, // back quad
        };
    }
}