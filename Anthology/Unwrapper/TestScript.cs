//using System;
//using System.Collections.Generic;
//using UVWrapper;
//
//class Program
//{
//    static void Main()
//    {
//        Console.WriteLine("\n=== Testing Cube Unwrap ===");
//
//        // Create and verify cube mesh
//        var (vertices, faces, normals) = CreateCubeMesh();
//        VerifyMeshConnectivity(vertices, faces);
//
//        var inputMesh = new Mesh {
//            vertices = vertices,
//            faces = faces,
//            faceNormals = normals,
//            facePartitions = new List<int>(new int[faces.Count])
//        };
//
//        var unwrapper = new UvUnwrapper();
//        unwrapper.SetMesh(inputMesh);
//        unwrapper.SetTexelSize(0.1f);
//        unwrapper.Unwrap();
//
//        var faceUvs = unwrapper.GetFaceUvs();
//        var chartRects = unwrapper.GetChartRects();
//        var textureSize = unwrapper.GetTextureSize();
//
//        PrintUnwrapResults(faceUvs, chartRects, textureSize);
//    }
//
//    static (List<Vertex>, List<Face>, List<Vector3>) CreateCubeMesh()
//    {
//        // Create vertices
//        var vertices = new List<Vertex>();
//        var cubePoints = new[]
//        {
//            (-1, -1, -1), // 0: front bottom left
//            ( 1, -1, -1), // 1: front bottom right
//            ( 1,  1, -1), // 2: front top right
//            (-1,  1, -1), // 3: front top left
//            (-1, -1,  1), // 4: back bottom left
//            ( 1, -1,  1), // 5: back bottom right
//            ( 1,  1,  1), // 6: back top right
//            (-1,  1,  1)  // 7: back top left
//        };
//
//        foreach (var (x, y, z) in cubePoints)
//        {
//            vertices.Add(new Vertex {
//                position = new Vector3( x, y, z)
//            });
//        }
//
//        // Define faces with consistent winding order (counter-clockwise when viewed from outside)
//        var faceIndices = new[]
//        {
//            // Front face
//            (0, 1, 2),
//            (0, 2, 3),
//            // Right face
//            (1, 5, 6),
//            (1, 6, 2),
//            // Back face
//            (5, 4, 7),
//            (5, 7, 6),
//            // Left face
//            (4, 0, 3),
//            (4, 3, 7),
//            // Top face
//            (3, 2, 6),
//            (3, 6, 7),
//            // Bottom face
//            (4, 5, 1),
//            (4, 1, 0)
//        };
//
//        var faces = new List<Face>();
//        var normals = new List<Vector3>();
//
//        foreach (var (v1, v2, v3) in faceIndices)
//        {
//            faces.Add(new Face {
//                indices = new int[] { v1, v2, v3 }
//            });
//
//            // Calculate face normal
//            var normal = CalculateNormal(
//                vertices[v1].position,
//                vertices[v2].position,
//                vertices[v3].position
//            );
//            normals.Add(normal);
//        }
//
//        Console.WriteLine($"Created cube mesh with {vertices.Count} vertices and {faces.Count} faces");
//
//        return (vertices, faces, normals);
//    }
//
//    static void VerifyMeshConnectivity(List<Vertex> vertices, List<Face> faces)
//    {
//        Console.WriteLine("\nVerifying mesh connectivity...");
//
//        // Build edge to face map
//        var edgeToFaces = new Dictionary<(int, int), List<int>>();
//
//        for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
//        {
//            var face = faces[faceIndex];
//            for (int i = 0; i < 3; i++)
//            {
//                int j = (i + 1) % 3;
//                var edge = (Math.Min(face.indices[i], face.indices[j]),
//                          Math.Max(face.indices[i], face.indices[j]));
//
//                if (!edgeToFaces.ContainsKey(edge))
//                    edgeToFaces[edge] = new List<int>();
//                edgeToFaces[edge].Add(faceIndex);
//            }
//        }
//
//        // Check edge manifold property
//        Console.WriteLine("\nEdge analysis:");
//        int nonManifoldEdges = 0;
//        int boundaryEdges = 0;
//        foreach (var kvp in edgeToFaces)
//        {
//            if (kvp.Value.Count > 2)
//            {
//                Console.WriteLine($"Non-manifold edge {kvp.Key} connected to {kvp.Value.Count} faces");
//                nonManifoldEdges++;
//            }
//            else if (kvp.Value.Count == 1)
//            {
//                Console.WriteLine($"Boundary edge {kvp.Key} with single face {kvp.Value[0]}");
//                boundaryEdges++;
//            }
//        }
//
//        // Verify face connectivity
//        var visitedFaces = new HashSet<int>();
//        var queue = new Queue<int>();
//        queue.Enqueue(0);
//
//        while (queue.Count > 0)
//        {
//            var faceIndex = queue.Dequeue();
//            if (!visitedFaces.Add(faceIndex)) continue;
//
//            var face = faces[faceIndex];
//            for (int i = 0; i < 3; i++)
//            {
//                int j = (i + 1) % 3;
//                var edge = (Math.Min(face.indices[i], face.indices[j]),
//                          Math.Max(face.indices[i], face.indices[j]));
//
//                foreach (var adjacentFace in edgeToFaces[edge])
//                {
//                    if (!visitedFaces.Contains(adjacentFace))
//                        queue.Enqueue(adjacentFace);
//                }
//            }
//        }
//
//        Console.WriteLine($"\nMesh connectivity summary:");
//        Console.WriteLine($"Total faces: {faces.Count}");
//        Console.WriteLine($"Connected faces: {visitedFaces.Count}");
//        Console.WriteLine($"Non-manifold edges: {nonManifoldEdges}");
//        Console.WriteLine($"Boundary edges: {boundaryEdges}");
//
//        if (visitedFaces.Count != faces.Count)
//        {
//            Console.WriteLine("\nWARNING: Mesh has disconnected components!");
//            var disconnectedFaces = Enumerable.Range(0, faces.Count)
//                                            .Except(visitedFaces)
//                                            .ToList();
//            Console.WriteLine($"Disconnected faces: {string.Join(", ", disconnectedFaces)}");
//        }
//    }
//
//
//    static void PrintUnwrapResults(List<FaceTextureCoords> faceUvs, List<UvRect> chartRects, float textureSize)
//    {
//        Console.WriteLine($"Unwrap complete!");
//        Console.WriteLine($"Texture Size: {textureSize}");
//        Console.WriteLine($"Number of UV Charts: {chartRects.Count}");
//        Console.WriteLine($"Number of Face UVs: {faceUvs.Count}");
//
//        Console.WriteLine("\nChart Rectangles:");
//        for (int i = 0; i < chartRects.Count; i++)
//        {
//            var rect = chartRects[i];
//            Console.WriteLine($"Chart {i}: Position({rect.left:F3}, {rect.top:F3}) Size({rect.width:F3}, {rect.height:F3})");
//        }
//
//        Console.WriteLine("\nFirst few face UVs:");
//        for (int i = 0; i < Math.Min(5, faceUvs.Count); i++)
//        {
//            Console.WriteLine($"Face {i}:");
//            for (int j = 0; j < 3; j++)
//            {
//                Console.WriteLine($"  Vertex {j}: ({faceUvs[i].coords[j].uv[0]:F3}, {faceUvs[i].coords[j].uv[1]:F3})");
//            }
//        }
//
//        // Check for invalid UVs
//        bool hasInvalidUvs = false;
//        foreach (var faceUv in faceUvs)
//        {
//            foreach (var coord in faceUv.coords)
//            {
//                if (float.IsNaN(coord.uv[0]) || float.IsNaN(coord.uv[1]) ||
//                    float.IsInfinity(coord.uv[0]) || float.IsInfinity(coord.uv[1]))
//                {
//                    hasInvalidUvs = true;
//                    break;
//                }
//            }
//        }
//
//        Console.WriteLine($"\nUV Validation: {(hasInvalidUvs ? "Contains invalid UVs!" : "All UVs valid")}");
//    }
//
//    static Vector3 CalculateNormal(Vector3 v1, Vector3 v2, Vector3 v3)
//    {
//        // Calculate vectors from point 1 to points 2 and 3
//        var a = new Vector3 {
//            xyz = new float[]
//            {
//                v2.xyz[0] - v1.xyz[0],
//                v2.xyz[1] - v1.xyz[1],
//                v2.xyz[2] - v1.xyz[2]
//            }
//        };
//
//        var b = new Vector3 {
//            xyz = new float[]
//            {
//                v3.xyz[0] - v1.xyz[0],
//                v3.xyz[1] - v1.xyz[1],
//                v3.xyz[2] - v1.xyz[2]
//            }
//        };
//
//        // Cross product
//        var normal = Vector3.Cross(a, b);
//
//        // Normalize
//        float length = MathF.Sqrt(
//            normal.xyz[0] * normal.xyz[0] +
//            normal.xyz[1] * normal.xyz[1] +
//            normal.xyz[2] * normal.xyz[2]
//        );
//
//        if (length > 0)
//        {
//            normal.xyz[0] /= length;
//            normal.xyz[1] /= length;
//            normal.xyz[2] /= length;
//        }
//
//        return normal;
//    }
//}