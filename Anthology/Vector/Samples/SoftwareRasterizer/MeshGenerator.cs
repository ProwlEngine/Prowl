// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;

using Float3 = Prowl.Vector.Float3;
using VertexBuffer = SoftwareRasterizer.Rasterizer.VertexBuffer;

namespace SoftwareRasterizer;

public static class MeshGenerator
    {
        public class MeshData
        {
            public float[] Positions { get; set; }
            public float[] Normals { get; set; }
            public int[] Indices { get; set; }

            public VertexBuffer VertexBuffer { get; set; }

            public void CreateVAttributes()
            {
                Float3[] positions = new Float3[Positions.Length / 3];
                Float3[] normals = new Float3[Normals.Length / 3];

                for (int i = 0; i < positions.Length; i++)
                {
                    positions[i] = new Float3(Positions[i * 3], Positions[i * 3 + 1], Positions[i * 3 + 2]);
                    normals[i] = new Float3(Normals[i * 3], Normals[i * 3 + 1], Normals[i * 3 + 2]);
                }

                // Create vertex buffer
                VertexBuffer = new VertexBuffer(
                    [positions, normals],
                    positions.Length,
                    Indices
                    );
            }
        }

        public static MeshData GenerateSphereMesh(float radius, int latitudeBands, int longitudeBands)
        {
            var positions = new List<float>();
            var normals = new List<float>();
            var indices = new List<int>();

            // Generate vertices and normals
            for (int lat = 0; lat <= latitudeBands; lat++)
            {
                float theta = lat * (float)Maths.PI / latitudeBands;
                float sinTheta = Maths.Sin(theta);
                float cosTheta = Maths.Cos(theta);

                for (int lon = 0; lon <= longitudeBands; lon++)
                {
                    float phi = lon * 2 * (float)Maths.PI / longitudeBands;
                    float sinPhi = Maths.Sin(phi);
                    float cosPhi = Maths.Cos(phi);

                    float x = cosPhi * sinTheta;
                    float y = cosTheta;
                    float z = sinPhi * sinTheta;

                    positions.AddRange(new[] { radius * x * 0.5f, radius * y * 0.5f, radius * z * 0.5f });
                    normals.AddRange(new[] { x, y, z });
                }
            }

            // Generate indices
            for (int lat = 0; lat < latitudeBands; lat++)
            {
                for (int lon = 0; lon < longitudeBands; lon++)
                {
                    int first = (lat * (longitudeBands + 1) + lon);
                    int second = (first + longitudeBands + 1);

                    indices.AddRange(new int[] { first, second, (first + 1) });
                    indices.AddRange(new int[] { second, (second + 1), (first + 1) });
                }
            }

            return new MeshData
            {
                Positions = positions.ToArray(),
                Normals = normals.ToArray(),
                Indices = indices.ToArray()
            };
        }

        public static MeshData GenerateCubeMesh(float size = 1f)
        {
            float h = size / 2;
            float[] v = new float[]
            {
            -h, -h, h,  h, -h, h,  h, h, h,  -h, h, h,  // front
            -h, -h, -h, -h, h, -h, h, h, -h, h, -h, -h, // back
            -h, h, -h,  -h, h, h,  h, h, h,  h, h, -h,  // top
            -h, -h, -h, h, -h, -h, h, -h, h, -h, -h, h, // bottom
            h, -h, -h,  h, h, -h,  h, h, h,  h, -h, h,  // right
            -h, -h, -h, -h, -h, h, -h, h, h, -h, h, -h  // left
            };

            float[] n = new float[]
            {
            0, 0, 1,  0, 0, 1,  0, 0, 1,  0, 0, 1,
            0, 0, -1, 0, 0, -1, 0, 0, -1, 0, 0, -1,
            0, 1, 0,  0, 1, 0,  0, 1, 0,  0, 1, 0,
            0, -1, 0, 0, -1, 0, 0, -1, 0, 0, -1, 0,
            1, 0, 0,  1, 0, 0,  1, 0, 0,  1, 0, 0,
            -1, 0, 0, -1, 0, 0, -1, 0, 0, -1, 0, 0
            };

            int[] i = new int[]
            {
            0, 2, 1, 0, 3, 2, 4, 6, 5, 4, 7, 6, 8, 10, 9, 8, 11, 10,
            12, 14, 13, 12, 15, 14, 16, 18, 17, 16, 19, 18, 20, 22, 21, 20, 23, 22
            };

            return new MeshData
            {
                Positions = v,
                Normals = n,
                Indices = i
            };
        }
    }
