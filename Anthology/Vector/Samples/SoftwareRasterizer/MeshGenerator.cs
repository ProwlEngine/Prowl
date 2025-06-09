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
        int triangleCount = indices.Count / 3;
        Console.WriteLine($"Sphere Mesh: {positions.Count / 3} vertices, {triangleCount} triangles");
        // Create and return the mesh data


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

    public static MeshData GenerateCorridorMesh(float corridorWidth = 2f, float corridorHeight = 3f)
    {
        var positions = new List<float>();
        var normals = new List<float>();
        var indices = new List<int>();

        float w = corridorWidth / 2;
        float h = corridorHeight;

        // Define corridor path points (x, z coordinates)
        var pathPoints = new[]
        {
        new Float2(-8, 0), new Float2(-4, 0), new Float2(-4, 4),
        new Float2(0, 4), new Float2(0, -4), new Float2(4, -4),
        new Float2(4, 0), new Float2(8, 0)
    };

        // Calculate perpendiculars for each segment
        var perpendiculars = new Float2[pathPoints.Length - 1];
        for (int i = 0; i < pathPoints.Length - 1; i++)
        {
            var direction = Maths.Normalize(new Float2(pathPoints[i + 1].X - pathPoints[i].X, pathPoints[i + 1].Y - pathPoints[i].Y));
            perpendiculars[i] = new Float2(-direction.Y, direction.X) * w;
        }

        // Calculate join points for each corner
        var leftPoints = new Float2[pathPoints.Length];
        var rightPoints = new Float2[pathPoints.Length];

        // First point
        leftPoints[0] = new Float2(pathPoints[0].X - perpendiculars[0].X, pathPoints[0].Y - perpendiculars[0].Y);
        rightPoints[0] = new Float2(pathPoints[0].X + perpendiculars[0].X, pathPoints[0].Y + perpendiculars[0].Y);

        // Middle points (intersections)
        for (int i = 1; i < pathPoints.Length - 1; i++)
        {
            var prevPerp = perpendiculars[i - 1];
            var nextPerp = perpendiculars[i];

            // Calculate intersection of the two corridor edges
            leftPoints[i] = LineIntersection(
                pathPoints[i] - prevPerp, pathPoints[i - 1] - prevPerp,
                pathPoints[i] - nextPerp, pathPoints[i + 1] - nextPerp
            );

            rightPoints[i] = LineIntersection(
                pathPoints[i] + prevPerp, pathPoints[i - 1] + prevPerp,
                pathPoints[i] + nextPerp, pathPoints[i + 1] + nextPerp
            );
        }

        // Last point
        int lastIdx = pathPoints.Length - 1;
        leftPoints[lastIdx] = new Float2(pathPoints[lastIdx].X - perpendiculars[lastIdx - 1].X, pathPoints[lastIdx].Y - perpendiculars[lastIdx - 1].Y);
        rightPoints[lastIdx] = new Float2(pathPoints[lastIdx].X + perpendiculars[lastIdx - 1].X, pathPoints[lastIdx].Y + perpendiculars[lastIdx - 1].Y);

        int vertexIndex = 0;

        // Generate corridor segments using the calculated join points
        for (int i = 0; i < pathPoints.Length - 1; i++)
        {
            var startLeft = leftPoints[i];
            var startRight = rightPoints[i];
            var endLeft = leftPoints[i + 1];
            var endRight = rightPoints[i + 1];

            // Floor (counter-clockwise when viewed from above)
            positions.AddRange(new[] { startLeft.X, 0, startLeft.Y });
            positions.AddRange(new[] { endLeft.X, 0, endLeft.Y });
            positions.AddRange(new[] { endRight.X, 0, endRight.Y });
            positions.AddRange(new[] { startRight.X, 0, startRight.Y });

            // Floor normals (up)
            for (int j = 0; j < 4; j++) normals.AddRange(new[] { 0f, 1f, 0f });

            // Floor indices
            indices.AddRange(new[] {
            vertexIndex, vertexIndex + 1, vertexIndex + 2,
            vertexIndex, vertexIndex + 2, vertexIndex + 3
        });

            // Ceiling (counter-clockwise when viewed from below)
            positions.AddRange(new[] { startLeft.X, h, startLeft.Y });
            positions.AddRange(new[] { startRight.X, h, startRight.Y });
            positions.AddRange(new[] { endRight.X, h, endRight.Y });
            positions.AddRange(new[] { endLeft.X, h, endLeft.Y });

            // Ceiling normals (down)
            for (int j = 0; j < 4; j++) normals.AddRange(new[] { 0f, -1f, 0f });

            // Ceiling indices
            indices.AddRange(new[] {
            vertexIndex + 4, vertexIndex + 5, vertexIndex + 6,
            vertexIndex + 4, vertexIndex + 6, vertexIndex + 7
        });

            // Left wall
            positions.AddRange(new[] { startLeft.X, 0, startLeft.Y });
            positions.AddRange(new[] { startLeft.X, h, startLeft.Y });
            positions.AddRange(new[] { endLeft.X, h, endLeft.Y });
            positions.AddRange(new[] { endLeft.X, 0, endLeft.Y });

            // Left wall normal (outward)
            var wallDir = new Float2(endLeft.X - startLeft.X, endLeft.Y - startLeft.Y);
            var leftNormal = new Float3(-wallDir.Y, 0f, wallDir.X);
            leftNormal = Maths.Normalize(leftNormal);
            for (int j = 0; j < 4; j++) normals.AddRange(new[] { leftNormal.X, leftNormal.Y, leftNormal.Z });

            // Left wall indices
            indices.AddRange(new[] {
            vertexIndex + 8, vertexIndex + 9, vertexIndex + 10,
            vertexIndex + 8, vertexIndex + 10, vertexIndex + 11
        });

            // Right wall
            positions.AddRange(new[] { endRight.X, 0, endRight.Y });
            positions.AddRange(new[] { endRight.X, h, endRight.Y });
            positions.AddRange(new[] { startRight.X, h, startRight.Y });
            positions.AddRange(new[] { startRight.X, 0, startRight.Y });

            // Right wall normal (outward)
            var rightNormal = new Float3(wallDir.Y, 0f, -wallDir.X);
            rightNormal = Maths.Normalize(rightNormal);
            for (int j = 0; j < 4; j++) normals.AddRange(new[] { rightNormal.X, rightNormal.Y, rightNormal.Z });

            // Right wall indices
            indices.AddRange(new[] {
            vertexIndex + 12, vertexIndex + 13, vertexIndex + 14,
            vertexIndex + 12, vertexIndex + 14, vertexIndex + 15
        });

            vertexIndex += 16;
        }

        return new MeshData
        {
            Positions = positions.ToArray(),
            Normals = normals.ToArray(),
            Indices = indices.ToArray()
        };
    }

    // Helper function to find intersection of two lines
    private static Float2 LineIntersection(Float2 p1, Float2 p2, Float2 p3, Float2 p4)
    {
        float denom = (p1.X - p2.X) * (p3.Y - p4.Y) - (p1.Y - p2.Y) * (p3.X - p4.X);
        if (Math.Abs(denom) < 1e-10) // Lines are parallel
        {
            return new Float2((p1.X + p3.X) * 0.5f, (p1.Y + p3.Y) * 0.5f); // Return midpoint
        }

        float t = ((p1.X - p3.X) * (p3.Y - p4.Y) - (p1.Y - p3.Y) * (p3.X - p4.X)) / denom;
        return new Float2(
            p1.X + t * (p2.X - p1.X),
            p1.Y + t * (p2.Y - p1.Y)
        );
    }

    public static MeshData GenerateQuadMesh(float width = 1f, float height = 1f)
    {
        float w = width / 2;
        float h = height / 2;
        return new MeshData
        {
            Positions = new float[]
            {
                -w, -h, 0, w, -h, 0, w, h, 0, -w, h, 0
            },
            Normals = new float[]
            {
                0, 0, 1, 0, 0, 1, 0, 0, 1, 0, 0, 1
            },
            Indices = new int[]
            {
                0, 1, 2,
                0, 2, 3
            }
        };
    }
}
