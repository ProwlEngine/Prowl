using System.Numerics;

namespace Prowl.Unwrapper
{
    public class Parametrizer
    {
        public static bool Parametrize(List<Vertex> vertices, List<Face> faces, List<TextureCoord> vertexUvs)
        {
            if (vertices == null || vertices.Count == 0 || faces == null || faces.Count == 0)
                return false;

            vertexUvs.Clear();

            try
            {
                // Find boundary vertices
                var boundaryLoop = FindBoundaryLoop(faces);
                if (boundaryLoop.Count == 0)
                    return false;

                // Map boundary to circle
                var boundaryUvs = MapVerticesToCircle(boundaryLoop);

                // Create initial UV mapping for all vertices
                var allUvs = CreateHarmonicMapping(vertices, boundaryLoop, boundaryUvs);

                // Optional: Improve the mapping with a simple relaxation step
                ImproveMapping(allUvs, faces, 5);

                // Convert results
                foreach (var uv in allUvs)
                {
                    vertexUvs.Add(new TextureCoord(uv.X, uv.Y));
                }

                return true;
            }
            catch (Exception)
            {
                vertexUvs.Clear();
                return false;
            }
        }

        private static List<int> FindBoundaryLoop(List<Face> faces)
        {
            // Create edge to face lookup
            var edgeFaces = new Dictionary<(int, int), List<int>>();

            for (int i = 0; i < faces.Count; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    int v1 = faces[i].indices[j];
                    int v2 = faces[i].indices[(j + 1) % 3];

                    var edge = (Math.Min(v1, v2), Math.Max(v1, v2));

                    if (!edgeFaces.ContainsKey(edge))
                        edgeFaces[edge] = [];

                    edgeFaces[edge].Add(i);
                }
            }

            // Find boundary edges (edges with only one adjacent face)
            var boundaryEdges = edgeFaces.Where(kvp => kvp.Value.Count == 1)
                                       .Select(kvp => kvp.Key)
                                       .ToList();

            if (boundaryEdges.Count == 0)
                return [];

            // Create the boundary loop
            var boundaryLoop = new List<int>();
            var currentEdge = boundaryEdges[0];
            boundaryLoop.Add(currentEdge.Item1);

            while (boundaryLoop.Count < boundaryEdges.Count)
            {
                var nextEdge = boundaryEdges.FirstOrDefault(e =>
                    e.Item1 == currentEdge.Item2 || e.Item2 == currentEdge.Item2);

                if (nextEdge == default)
                    break;

                boundaryLoop.Add(currentEdge.Item2);
                currentEdge = nextEdge;
            }

            return boundaryLoop;
        }

        private static List<Vector2> MapVerticesToCircle(List<int> boundaryLoop)
        {
            var boundaryUvs = new List<Vector2>();
            float angleStep = 2f * (float)Math.PI / boundaryLoop.Count;

            for (int i = 0; i < boundaryLoop.Count; i++)
            {
                float angle = i * angleStep;
                boundaryUvs.Add(new Vector2(
                    (float)Math.Cos(angle) * 0.5f + 0.5f,
                    (float)Math.Sin(angle) * 0.5f + 0.5f
                ));
            }

            return boundaryUvs;
        }

        private static List<Vector2> CreateHarmonicMapping(
            List<Vertex> vertices,
            List<int> boundaryLoop,
            List<Vector2> boundaryUvs)
        {
            var uvs = new List<Vector2>();
            for (int i = 0; i < vertices.Count; i++)
            {
                int boundaryIndex = boundaryLoop.IndexOf(i);
                if (boundaryIndex >= 0)
                {
                    uvs.Add(boundaryUvs[boundaryIndex]);
                }
                else
                {
                    // Initialize interior points with average of boundary positions
                    uvs.Add(new Vector2(0.5f, 0.5f));
                }
            }

            return uvs;
        }

        private static void ImproveMapping(List<Vector2> uvs, List<Face> faces, int iterations)
        {
            for (int iter = 0; iter < iterations; iter++)
            {
                var newUvs = new List<Vector2>(uvs);

                for (int i = 0; i < uvs.Count; i++)
                {
                    var neighbors = GetVertexNeighbors(i, faces);
                    if (neighbors.Count > 0)
                    {
                        var average = Vector2.Zero;
                        foreach (var neighbor in neighbors)
                        {
                            average += uvs[neighbor];
                        }
                        average /= neighbors.Count;
                        newUvs[i] = Vector2.Lerp(uvs[i], average, 0.5f);
                    }
                }

                uvs.Clear();
                uvs.AddRange(newUvs);
            }
        }

        private static HashSet<int> GetVertexNeighbors(int vertexIndex, List<Face> faces)
        {
            var neighbors = new HashSet<int>();
            foreach (var face in faces)
            {
                for (int i = 0; i < 3; i++)
                {
                    if (face.indices[i] == vertexIndex)
                    {
                        neighbors.Add(face.indices[(i + 1) % 3]);
                        neighbors.Add(face.indices[(i + 2) % 3]);
                    }
                }
            }
            return neighbors;
        }
    }
}