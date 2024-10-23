using System.Numerics;

namespace Prowl.Unwrapper
{
    public class UvUnwrapper
    {
        private static readonly float[] RotateDegrees = [5, 15, 20, 25, 30, 35, 40, 45];

        public List<FaceTextureCoords> FaceUvs { get; private set; } = [];
        public List<UvRect> ChartRects { get; private set; } = [];
        public List<(List<int> faces, List<FaceTextureCoords> uvs)> Charts { get; private set; } = [];
        public List<int> ChartSourcePartitions { get; private set; } = [];

        private readonly Dictionary<int, List<int>> partitions = [];
        private readonly List<Vector2> chartSizes = [];
        private readonly List<Vector2> scaledChartSizes = [];

        // Configuration
        public bool SegmentByNormal = true;
        public float SegmentDotProductThreshold = 0.0f; // 90 degrees
        public float TexelSizePerUnit = 1.0f;
        public bool SegmentPreferMorePieces = true;
        public bool EnableRotation = true;

        private UVMesh mesh;
        private float resultTextureSize = 0;

        public void SetMesh(UVMesh mesh)
        {
            ArgumentNullException.ThrowIfNull(mesh);
            if (mesh.vertices == null || mesh.vertices.Count == 0)
                throw new ArgumentException("Mesh must have vertices", nameof(mesh));
            if (mesh.faces == null || mesh.faces.Count == 0)
                throw new ArgumentException("Mesh must have faces", nameof(mesh));
            this.mesh = mesh;
        }

        public float GetTextureSize() => resultTextureSize;

        private static void BuildEdgeToFaceMap(List<Face> faces, Dictionary<(int, int), int> edgeToFaceMap)
        {
            edgeToFaceMap.Clear();
            for (int index = 0; index < faces.Count; index++)
            {
                var face = faces[index];
                for (int i = 0; i < 3; i++)
                {
                    int j = (i + 1) % 3;
                    //var edge = (Math.Min(face.indices[i], face.indices[j]),
                    //          Math.Max(face.indices[i], face.indices[j]));
                    //edgeToFaceMap[edge] = index;
                    var edge = (face.indices[i], face.indices[j]);
                    edgeToFaceMap[edge] = index;
                }
            }
        }

        private void BuildEdgeToFaceMap(List<int> group, Dictionary<(int, int), int> edgeToFaceMap)
        {
            edgeToFaceMap.Clear();
            foreach (var index in group)
            {
                var face = mesh.faces[index];
                for (int i = 0; i < 3; i++)
                {
                    int j = (i + 1) % 3;
                    //var edge = (Math.Min(face.indices[i], face.indices[j]),
                    //          Math.Max(face.indices[i], face.indices[j]));
                    //edgeToFaceMap[edge] = index;
                    var edge = (face.indices[i], face.indices[j]);
                    edgeToFaceMap[edge] = index;
                }
            }
        }

        private void SplitPartitionToIslands(List<int> group, List<List<int>> islands)
        {
            var edgeToFaceMap = new Dictionary<(int, int), int>();
            BuildEdgeToFaceMap(group, edgeToFaceMap);
            //bool segmentByNormal = !mesh.faceNormals.IsNullOrEmpty() && this.segmentByNormal;
            bool segmentByNormal = (mesh.faceNormals != null && mesh.faceNormals.Count > 0) && this.SegmentByNormal;

            var processedFaces = new HashSet<int>();
            var waitFaces = new Queue<int>();

            foreach (var indexInGroup in group)
            {
                if (processedFaces.Contains(indexInGroup))
                    continue;

                waitFaces.Enqueue(indexInGroup);
                var island = new List<int>();

                while (waitFaces.Count > 0)
                {
                    int index = waitFaces.Dequeue();
                    if (processedFaces.Contains(index))
                        continue;

                    var face = mesh.faces[index];
                    for (int i = 0; i < 3; i++)
                    {
                        int j = (i + 1) % 3;
                        var oppositeEdge = (face.indices[j], face.indices[i]);
                        if (!edgeToFaceMap.TryGetValue(oppositeEdge, out int oppositeFaceIndex))
                            continue;

                        if (segmentByNormal)
                        {
                            var dot = Vector3.Dot(mesh.faceNormals[oppositeFaceIndex],
                                mesh.faceNormals[SegmentPreferMorePieces ? indexInGroup : index]);
                            if (dot < SegmentDotProductThreshold)
                                continue;
                        }

                        waitFaces.Enqueue(oppositeFaceIndex);
                    }

                    island.Add(index);
                    processedFaces.Add(index);
                }

                if (island.Count > 0)
                    islands.Add(island);
            }
        }

        private float CalculateFaceArea(Face face)
        {
            var v1 = mesh.vertices[face.indices[0]];
            var v2 = mesh.vertices[face.indices[1]];
            var v3 = mesh.vertices[face.indices[2]];

            return CalculateTriangleArea(
                new Vector3(v1.position[0], v1.position[1], v1.position[2]),
                new Vector3(v2.position[0], v2.position[1], v2.position[2]),
                new Vector3(v3.position[0], v3.position[1], v3.position[2])
            );
        }

        private static float CalculateTriangleArea(Vector3 a, Vector3 b, Vector3 c)
        {
            return Vector3.Cross(b - a, c - a).Length() * 0.5f;
        }

        private static float CalculateTriangleArea2D(Vector2 a, Vector2 b, Vector2 c)
        {
            return CalculateTriangleArea(
                new Vector3(a.X, a.Y, 0),
                new Vector3(b.X, b.Y, 0),
                new Vector3(c.X, c.Y, 0)
            );
        }

        private static void CalculateFaceTextureBoundingBox(List<FaceTextureCoords> faceTextureCoords,
            out float left, out float top, out float right, out float bottom)
        {
            left = top = right = bottom = 0;
            bool first = true;

            foreach (var item in faceTextureCoords)
            {
                for (int i = 0; i < 3; i++)
                {
                    var x = item.coords[i].uv[0];
                    var y = item.coords[i].uv[1];

                    if (first)
                    {
                        left = right = x;
                        top = bottom = y;
                        first = false;
                    }
                    else
                    {
                        left = Math.Min(left, x);
                        right = Math.Max(right, x);
                        top = Math.Min(top, y);
                        bottom = Math.Max(bottom, y);
                    }
                }
            }
        }

        private void CalculateSizeAndRemoveInvalidCharts()
        {
            var validCharts = new List<(List<int>, List<FaceTextureCoords>)>();
            var validPartitions = new List<int>();
            chartSizes.Clear();
            scaledChartSizes.Clear();

            for (int chartIndex = 0; chartIndex < Charts.Count; chartIndex++)
            {
                var chart = Charts[chartIndex];
                CalculateFaceTextureBoundingBox(chart.uvs,
                    out float left, out float top, out float right, out float bottom);

                var size = new Vector2(right - left, bottom - top);
                if (size.X <= 0 || float.IsNaN(size.X) || float.IsInfinity(size.X) ||
                    size.Y <= 0 || float.IsNaN(size.Y) || float.IsInfinity(size.Y))
                    continue;

                float surfaceArea = chart.faces.Sum(faceIndex => CalculateFaceArea(mesh.faces[faceIndex]));
                float uvArea = 0;

                // Normalize UVs
                foreach (var faceUv in chart.uvs)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        faceUv.coords[i].uv[0] -= left;
                        faceUv.coords[i].uv[1] -= top;
                    }

                    uvArea += CalculateTriangleArea2D(
                        new Vector2(faceUv.coords[0].uv[0], faceUv.coords[0].uv[1]),
                        new Vector2(faceUv.coords[1].uv[0], faceUv.coords[1].uv[1]),
                        new Vector2(faceUv.coords[2].uv[0], faceUv.coords[2].uv[1])
                    );
                }

                if (EnableRotation)
                {
                    var center = new Vector2(size.X * 0.5f, size.Y * 0.5f);
                    float minRectArea = size.X * size.Y;
                    float minRectLeft = 0;
                    float minRectTop = 0;
                    bool rotated = false;

                    foreach (float degrees in RotateDegrees)
                    {
                        const float Deg2Rad = MathF.PI / 180.0f;
                        float radians = degrees * Deg2Rad;
                        var rotatedUvs = new List<FaceTextureCoords>();

                        foreach (var faceUv in chart.uvs)
                        {
                            var rotatedCoords = new FaceTextureCoords { coords = new TextureCoord[3] };
                            for (int i = 0; i < 3; i++)
                            {
                                var point = new Vector2(faceUv.coords[i].uv[0], faceUv.coords[i].uv[1]) - center;
                                var rotatedP = new Vector2(
                                    point.X * (float)Math.Cos(radians) - point.Y * (float)Math.Sin(radians),
                                    point.X * (float)Math.Sin(radians) + point.Y * (float)Math.Cos(radians)
                                );
                                rotatedCoords.coords[i] = new TextureCoord(rotatedP.X, rotatedP.Y);
                            }
                            rotatedUvs.Add(rotatedCoords);
                        }

                        CalculateFaceTextureBoundingBox(rotatedUvs,
                            out float rotLeft, out float rotTop,
                            out float rotRight, out float rotBottom);

                        var newSize = new Vector2(rotRight - rotLeft, rotBottom - rotTop);
                        float newRectArea = newSize.X * newSize.Y;

                        if (newRectArea < minRectArea)
                        {
                            minRectArea = newRectArea;
                            size = newSize;
                            minRectLeft = rotLeft;
                            minRectTop = rotTop;
                            rotated = true;
                            chart.uvs = rotatedUvs;
                        }
                    }

                    if (rotated)
                    {
                        foreach (var faceUv in chart.uvs)
                        {
                            for (int i = 0; i < 3; i++)
                            {
                                faceUv.coords[i].uv[0] -= minRectLeft;
                                faceUv.coords[i].uv[1] -= minRectTop;
                            }
                        }
                    }
                }

                float ratioOfSurfaceAreaAndUvArea = uvArea > 0 ? surfaceArea / uvArea : 1.0f;
                float scale = ratioOfSurfaceAreaAndUvArea * TexelSizePerUnit;

                chartSizes.Add(size);
                scaledChartSizes.Add(new Vector2(size.X * scale, size.Y * scale));
                validCharts.Add(chart);
                validPartitions.Add(ChartSourcePartitions[chartIndex]);
            }

            Charts = validCharts;
            ChartSourcePartitions = validPartitions;
        }

        private void PackCharts()
        {
            var chartPacker = new ChartPacker();
            chartPacker.SetCharts(scaledChartSizes);
            resultTextureSize = chartPacker.Pack();

            ChartRects = new List<UvRect>(chartSizes.Count);
            var packedResult = chartPacker.GetResults();

            for (int i = 0; i < Charts.Count; i++)
            {
                var chartSize = chartSizes[i];
                var (_, uvs) = Charts[i];

                if (i >= packedResult.Count)
                {
                    foreach (var faceUv in uvs)
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            faceUv.coords[j].uv[0] = 0;
                            faceUv.coords[j].uv[1] = 0;
                        }
                    }
                    continue;
                }

                var (position, size, flipped) = packedResult[i];

                ChartRects.Add(new UvRect {
                    left = position.X,
                    top = position.Y,
                    width = flipped ? size.Y : size.X,
                    height = flipped ? size.X : size.Y
                });

                if (flipped)
                {
                    foreach (var faceUv in uvs)
                    {
                        for (int j = 0; j < 3; j++)
                        {
                            float temp = faceUv.coords[j].uv[0];
                            faceUv.coords[j].uv[0] = faceUv.coords[j].uv[1];
                            faceUv.coords[j].uv[1] = temp;
                        }
                    }
                }

                foreach (var faceUv in uvs)
                {
                    for (int j = 0; j < 3; j++)
                    {
                        faceUv.coords[j].uv[0] /= chartSize.X;
                        faceUv.coords[j].uv[1] /= chartSize.Y;
                        faceUv.coords[j].uv[0] *= size.X;
                        faceUv.coords[j].uv[1] *= size.Y;
                        faceUv.coords[j].uv[0] += position.X;
                        faceUv.coords[j].uv[1] += position.Y;
                    }
                }
            }
        }

        // Fixed FinalizeUv method
        private void FinalizeUv()
        {
            Console.WriteLine("Finalizing UVs...");
            Console.WriteLine($"Charts count: {Charts.Count}");

            // Make sure faceUvs is properly initialized
            if (FaceUvs.Count != mesh.faces.Count)
            {
                FaceUvs = [];
                for (int i = 0; i < mesh.faces.Count; i++)
                {
                    FaceUvs.Add(new FaceTextureCoords {
                        coords =
                        [
                            new() { uv = new float[2] },
                            new() { uv = new float[2] },
                            new() { uv = new float[2] }
                        ]
                    });
                }
            }

            foreach (var (faces, uvs) in Charts)
            {
                Console.WriteLine($"Processing chart with {faces.Count} faces");
                for (int i = 0; i < faces.Count; i++)
                {
                    int globalFaceIndex = faces[i];
                    if (globalFaceIndex < 0 || globalFaceIndex >= FaceUvs.Count)
                    {
                        Console.WriteLine($"Warning: Invalid face index {globalFaceIndex}");
                        continue;
                    }

                    var sourceUv = uvs[i];
                    var destUv = FaceUvs[globalFaceIndex];

                    for (int j = 0; j < 3; j++)
                    {
                        destUv.coords[j].uv[0] = sourceUv.coords[j].uv[0];
                        destUv.coords[j].uv[1] = sourceUv.coords[j].uv[1];
                    }
                }
            }

            Console.WriteLine($"Finalized {FaceUvs.Count} face UVs");
        }

        private void Partition()
        {
            partitions.Clear();
            if (mesh.facePartitions.Count == 0)
            {
                partitions[0] = Enumerable.Range(0, mesh.faces.Count).ToList();
            }
            else
            {
                for (int i = 0; i < mesh.faces.Count; i++)
                {
                    int partition = mesh.facePartitions[i];
                    if (!partitions.ContainsKey(partition))
                        partitions[partition] = [];
                    partitions[partition].Add(i);
                }
            }
        }

        private static float DistanceBetweenVertices(Vertex first, Vertex second)
        {
            float dx = first.position[0] - second.position[0];
            float dy = first.position[1] - second.position[1];
            float dz = first.position[2] - second.position[2];
            return (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private bool FixHolesExceptTheLongestRing(List<Vertex> vertices, List<Face> faces, out int remainingHoleNum)
        {
            Console.WriteLine($"\nFixHolesExceptTheLongestRing: Processing {faces.Count} faces");
            remainingHoleNum = 0;

            // Build edge to face map
            var edgeToFaceMap = new Dictionary<(int, int), int>();
            BuildEdgeToFaceMap(faces, edgeToFaceMap);
            Console.WriteLine($"Built edge map with {edgeToFaceMap.Count} edges");

            // Find boundary edges (edges with only one adjacent face)
            var boundaryEdges = new Dictionary<int, List<int>>();
            foreach (var face in faces)
            {
                for (int i = 0; i < 3; i++)
                {
                    int j = (i + 1) % 3;
                    var edge = (face.indices[j], face.indices[i]);  // Note: reversed for boundary check
                    if (!edgeToFaceMap.ContainsKey(edge))
                    {
                        // This is a boundary edge
                        if (!boundaryEdges.ContainsKey(face.indices[i]))
                            boundaryEdges[face.indices[i]] = [];
                        boundaryEdges[face.indices[i]].Add(face.indices[j]);
                    }
                }
            }

            Console.WriteLine($"Found {boundaryEdges.Count} vertices on boundaries");
            if (boundaryEdges.Count == 0)
            {
                Console.WriteLine("No boundary edges found - mesh is closed");
                remainingHoleNum = 0;
                return true;
            }

            // Find boundary loops
            var boundaryLoops = new List<List<int>>();
            var usedEdges = new HashSet<(int, int)>();

            while (boundaryEdges.Count > 0)
            {
                var startVertex = boundaryEdges.Keys.First();
                var currentLoop = new List<int> { startVertex };
                var currentVertex = startVertex;

                Console.WriteLine($"Starting new boundary loop from vertex {startVertex}");

                while (true)
                {
                    if (!boundaryEdges.TryGetValue(currentVertex, out var nextVertices))
                    {
                        Console.WriteLine($"Failed to find next vertex from {currentVertex}");
                        return false;
                    }

                    // Find an unused edge
                    int nextVertex = -1;
                    foreach (var candidate in nextVertices)
                    {
                        if (!usedEdges.Contains((currentVertex, candidate)))
                        {
                            nextVertex = candidate;
                            break;
                        }
                    }

                    if (nextVertex == -1)
                    {
                        Console.WriteLine($"No unused edges found from vertex {currentVertex}");
                        break;
                    }

                    usedEdges.Add((currentVertex, nextVertex));

                    if (nextVertex == startVertex)
                    {
                        Console.WriteLine("Loop closed successfully");
                        break;
                    }

                    currentLoop.Add(nextVertex);
                    currentVertex = nextVertex;

                    // Safety check for infinite loops
                    if (currentLoop.Count > vertices.Count)
                    {
                        Console.WriteLine("Safety limit reached - possible infinite loop");
                        return false;
                    }
                }

                if (currentLoop.Count >= 3)
                {
                    Console.WriteLine($"Found valid boundary loop with {currentLoop.Count} vertices");
                    boundaryLoops.Add(currentLoop);
                }
                else
                {
                    Console.WriteLine($"Discarding invalid loop with only {currentLoop.Count} vertices");
                }

                // Remove used vertices from boundary edges
                foreach (var vertex in currentLoop)
                {
                    boundaryEdges.Remove(vertex);
                }
            }

            Console.WriteLine($"Found {boundaryLoops.Count} boundary loops");

            if (boundaryLoops.Count == 0)
            {
                Console.WriteLine("No valid boundary loops found");
                return false;
            }

            // Sort loops by perimeter length
            boundaryLoops.Sort((a, b) =>
            {
                float lengthA = CalculateLoopLength(a, vertices);
                float lengthB = CalculateLoopLength(b, vertices);
                return lengthB.CompareTo(lengthA);  // Descending order
            });

            // Triangulate all but the longest loop
            for (int i = 1; i < boundaryLoops.Count; i++)
            {
                Console.WriteLine($"Triangulating hole {i} with {boundaryLoops[i].Count} vertices");
                Triangulator.Triangulate(vertices, faces, boundaryLoops[i]);
            }

            remainingHoleNum = 1; // Keep the longest loop
            return true;
        }

        private static float CalculateLoopLength(List<int> loop, List<Vertex> vertices)
        {
            float length = 0;
            for (int i = 0; i < loop.Count; i++)
            {
                int j = (i + 1) % loop.Count;
                length += DistanceBetweenVertices(vertices[loop[i]], vertices[loop[j]]);
            }
            return length;
        }

        // Helper method to visualize the mesh connectivity
        private void PrintMeshConnectivity(List<Face> faces)
        {
            var vertexFaces = new Dictionary<int, List<int>>();

            for (int faceIndex = 0; faceIndex < faces.Count; faceIndex++)
            {
                var face = faces[faceIndex];
                for (int i = 0; i < 3; i++)
                {
                    if (!vertexFaces.ContainsKey(face.indices[i]))
                        vertexFaces[face.indices[i]] = [];
                    vertexFaces[face.indices[i]].Add(faceIndex);
                }
            }

            Console.WriteLine("\nMesh Connectivity:");
            foreach (var kvp in vertexFaces.OrderBy(x => x.Key))
            {
                Console.WriteLine($"Vertex {kvp.Key} is connected to faces: {string.Join(", ", kvp.Value)}");
            }
        }

        private void MakeSeamAndCut(List<Vertex> vertices,
            List<Face> faces,
            Dictionary<int, int> localToGlobalFacesMap,
            out List<int> firstGroup,
            out List<int> secondGroup)
        {
            firstGroup = [];
            secondGroup = [];

            // Find top triangle (max Y)
            float maxY = float.MinValue;
            int chosenIndex = -1;

            for (int i = 0; i < faces.Count; i++)
            {
                var face = faces[i];
                for (int j = 0; j < 3; j++)
                {
                    float y = vertices[face.indices[j]].position[1];
                    if (y > maxY)
                    {
                        maxY = y;
                        chosenIndex = i;
                    }
                }
            }

            if (chosenIndex == -1)
                return;

            var edgeToFaceMap = new Dictionary<(int, int), int>();
            BuildEdgeToFaceMap(faces, edgeToFaceMap);

            var processedFaces = new HashSet<int>();
            var waitFaces = new Queue<int>();
            waitFaces.Enqueue(chosenIndex);

            while (waitFaces.Count > 0)
            {
                int index = waitFaces.Dequeue();
                if (processedFaces.Contains(index))
                    continue;

                var face = faces[index];
                for (int i = 0; i < 3; i++)
                {
                    int j = (i + 1) % 3;
                    var oppositeEdge = (face.indices[j], face.indices[i]);
                    if (edgeToFaceMap.TryGetValue(oppositeEdge, out int oppositeFaceIndex))
                    {
                        waitFaces.Enqueue(oppositeFaceIndex);
                    }
                }

                processedFaces.Add(index);
                firstGroup.Add(localToGlobalFacesMap[index]);

                if (firstGroup.Count * 2 >= faces.Count)
                    break;
            }

            for (int index = 0; index < faces.Count; index++)
            {
                if (!processedFaces.Contains(index))
                {
                    secondGroup.Add(localToGlobalFacesMap[index]);
                }
            }
        }

        private void UnwrapSingleIsland(List<int> group, int sourcePartition, bool skipCheckHoles = false)
        {
            if (group.Count == 0)
            {
                Console.WriteLine("Empty group, skipping");
                return;
            }

            Console.WriteLine($"UnwrapSingleIsland: Processing group of {group.Count} faces");

            // Create local mesh
            var localVertices = new List<Vertex>();
            var localFaces = new List<Face>();
            var globalToLocalVerticesMap = new Dictionary<int, int>();
            var localToGlobalFacesMap = new Dictionary<int, int>();

            // Build local mesh
            for (int i = 0; i < group.Count; i++)
            {
                var globalFace = mesh.faces[group[i]];
                var localFace = new Face { indices = new int[3] };

                for (int j = 0; j < 3; j++)
                {
                    int globalVertexIndex = globalFace.indices[j];
                    if (!globalToLocalVerticesMap.TryGetValue(globalVertexIndex, out int localIndex))
                    {
                        localVertices.Add(mesh.vertices[globalVertexIndex]);
                        localIndex = localVertices.Count - 1;
                        globalToLocalVerticesMap[globalVertexIndex] = localIndex;
                    }
                    localFace.indices[j] = localIndex;
                }

                localFaces.Add(localFace);
                localToGlobalFacesMap[localFaces.Count - 1] = group[i];
            }

            Console.WriteLine($"Created local mesh with {localVertices.Count} vertices and {localFaces.Count} faces");

            int faceNumBeforeFix = localFaces.Count;
            if (!skipCheckHoles)
            {
                if (!FixHolesExceptTheLongestRing(localVertices, localFaces, out int remainingHoleNum))
                {
                    Console.WriteLine("Failed to fix holes");
                    return;
                }

                Console.WriteLine($"Fixed holes. Remaining holes: {remainingHoleNum}");

                if (remainingHoleNum == 1)
                {
                    Console.WriteLine("One hole remains, parametrizing group");
                    ParametrizeSingleGroup(localVertices, localFaces, localToGlobalFacesMap,
                        faceNumBeforeFix, sourcePartition);
                    return;
                }

                if (remainingHoleNum == 0)
                {
                    Console.WriteLine("No holes remain, making seam and cut");
                    MakeSeamAndCut(localVertices, localFaces, localToGlobalFacesMap,
                        out var firstGroup, out var secondGroup);

                    if (firstGroup.Count == 0 || secondGroup.Count == 0)
                    {
                        Console.WriteLine("Invalid seam cut results");
                        return;
                    }

                    Console.WriteLine($"Cut into groups of {firstGroup.Count} and {secondGroup.Count} faces");
                    UnwrapSingleIsland(firstGroup, sourcePartition, true);
                    UnwrapSingleIsland(secondGroup, sourcePartition, true);
                    return;
                }
            }
            else
            {
                Console.WriteLine("Skipping hole check, parametrizing group directly");
                ParametrizeSingleGroup(localVertices, localFaces, localToGlobalFacesMap,
                    faceNumBeforeFix, sourcePartition);
            }
        }

        private void ParametrizeSingleGroup(
            List<Vertex> vertices,
            List<Face> faces,
            Dictionary<int, int> localToGlobalFacesMap,
            int faceNumToChart,
            int sourcePartition)
        {
            var localVertexUvs = new List<TextureCoord>();
            if (!Parametrizer.Parametrize(vertices, faces, localVertexUvs))
                return;

            var chartFaces = new List<int>();
            var chartUvs = new List<FaceTextureCoords>();

            for (int i = 0; i < faceNumToChart; i++)
            {
                var localFace = faces[i];
                var globalFaceIndex = localToGlobalFacesMap[i];
                var faceUv = new FaceTextureCoords();

                for (int j = 0; j < 3; j++)
                {
                    var localVertexIndex = localFace.indices[j];
                    var vertexUv = localVertexUvs[localVertexIndex];
                    faceUv.coords[j] = vertexUv;
                }

                chartFaces.Add(globalFaceIndex);
                chartUvs.Add(faceUv);
            }

            if (chartFaces.Count > 0)
            {
                Charts.Add((chartFaces, chartUvs));
                ChartSourcePartitions.Add(sourcePartition);
            }
        }

        public void Unwrap()
        {
            if (mesh == null)
                throw new InvalidOperationException("Mesh must be set before unwrapping");
            if (mesh.faces == null)
                throw new InvalidOperationException("Mesh faces collection is null");

            Partition();

            // Initialize faceUvs with properly constructed FaceTextureCoords
            FaceUvs = new List<FaceTextureCoords>(mesh.faces.Count);
            for (int i = 0; i < mesh.faces.Count; i++)
            {
                FaceUvs.Add(new FaceTextureCoords {
                    coords =
                    [
                        new TextureCoord { uv = new float[2] },
                        new TextureCoord { uv = new float[2] },
                        new TextureCoord { uv = new float[2] }
                    ]
                });
            }

            foreach (var group in partitions)
            {
                var islands = new List<List<int>>();
                SplitPartitionToIslands(group.Value, islands);
                foreach (var island in islands)
                {
                    UnwrapSingleIsland(island, group.Key);
                }
            }

            CalculateSizeAndRemoveInvalidCharts();
            PackCharts();
            FinalizeUv();
        }
    }
}