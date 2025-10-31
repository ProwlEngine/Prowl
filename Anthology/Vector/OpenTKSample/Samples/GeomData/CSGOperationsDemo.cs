using Prowl.Vector;
using Prowl.Vector.Geometry;
using System.Linq;
using System.Collections.Generic;
using System.Diagnostics;

namespace OpenTKSample.Samples
{
    /// <summary>
    /// Demonstrates CSG (Constructive Solid Geometry) operations: Union, Intersection, and Subtraction.
    /// Shows how to combine meshes using boolean operations with animated real-time CSG calculations.
    /// </summary>
    public class CSGOperationsDemo : IDemo
    {
        public string Name => "CSG Operations (Animated Union, Intersection, Subtraction)";

        private readonly GeometryData _baseSphereA;
        private readonly GeometryData _baseSphereB;

        private readonly Float4 _colorA = new Float4(1.0f, 0.3f, 0.3f, 0.5f);  // Red (semi-transparent)
        private readonly Float4 _colorB = new Float4(0.3f, 0.3f, 1.0f, 0.5f);  // Blue (semi-transparent)
        private readonly Float4 _colorResult = new Float4(0.3f, 1.0f, 0.3f, 0.8f);  // Green

        public CSGOperationsDemo()
        {
            // Create two base spheres (we'll animate their positions)
            _baseSphereA = GeometryGenerator.Icosphere(0.6f, subdivisions: 0);
            //_baseSphereB = GeometryGenerator.Icosphere(0.6, subdivisions: 1);
            _baseSphereB = GeometryGenerator.Box(new Float3(0.6f));
            GeometryOperators.Triangulate(_baseSphereB);
        }

        public void Draw(Float3 position, float timeInSeconds)
        {
            // Calculate animated offset for sphere B (smooth back and forth motion)
            float animationSpeed = 1.2f; // Speed of animation
            float animationAmplitude = 0.8f; // How far it moves
            float offsetX = Maths.Sin(timeInSeconds * animationSpeed) * animationAmplitude;
            float offsetY = Maths.Cos(timeInSeconds * animationSpeed) * animationAmplitude;

            // Create positioned copies of the base spheres
            var sphereA = CopyGeometryData(_baseSphereA);
            var sphereB = CopyGeometryData(_baseSphereB);
            GeometryOperators.Translate(sphereB, new Float3(offsetX, offsetY * 0.5f, 0));

            // Perform CSG operations with current positions
            GeometryData unionResult = null;
            GeometryData intersectionResult = null;
            GeometryData subtractionResult = null;

            try
            {
                var timeStamp = Stopwatch.GetTimestamp();

                unionResult = GeometryCSG.Union(sphereA, sphereB);
                intersectionResult = GeometryCSG.Intersect(sphereA, sphereB);
                subtractionResult = GeometryCSG.Subtraction(sphereA, sphereB);

                var time = Stopwatch.GetTimestamp();

                float elapsedMs = (time - timeStamp) * 1000.0f / Stopwatch.Frequency;
                System.Console.WriteLine($"CSG operations completed in {elapsedMs:F2}ms");
            }
            catch (System.Exception ex)
            {
                System.Console.WriteLine($"CSG operation failed: {ex.Message}");
            }

            // Layout: Show 4 rows
            // Row 1: Original shapes (A and B)
            // Row 2: Union (A ∪ B)
            // Row 3: Intersection (A ∩ B)
            // Row 4: Subtraction (A - B)

            float spacing = 1.5f;

            // Row 1: Original shapes
            DrawOriginalShapes(position + new Float3(0, spacing * 1.5f, 0), sphereA, sphereB);

            // Row 2: Union
            if (unionResult != null)
                DrawOperation(position + new Float3(0, spacing * 0.5f, 0), unionResult, "Union (A ∪ B)");

            // Row 3: Intersection
            if (intersectionResult != null)
                DrawOperation(position + new Float3(0, -spacing * 0.5f, 0), intersectionResult, "Intersection (A ∩ B)");

            // Row 4: Subtraction
            if (subtractionResult != null)
                DrawOperation(position + new Float3(0, -spacing * 1.5f, 0), subtractionResult, "Subtraction (A - B)");
        }

        private void DrawOriginalShapes(Float3 position, GeometryData sphereA, GeometryData sphereB)
        {
            // Draw both animated spheres at the specified position
            var sphereACopy = CopyGeometryData(sphereA);
            GeometryOperators.Translate(sphereACopy, (Float3)position);

            var sphereBCopy = CopyGeometryData(sphereB);
            GeometryOperators.Translate(sphereBCopy, (Float3)position);

            // Draw with transparency to show overlap
            Gizmo.DrawGeometryData(sphereACopy, GeometryDataVisualization.Solid | GeometryDataVisualization.Edges);
            Gizmo.DrawGeometryData(sphereBCopy, GeometryDataVisualization.Solid | GeometryDataVisualization.Edges);
        }

        private void DrawOperation(Float3 position, GeometryData result, string label)
        {
            if (result == null || result.Faces.Count == 0)
                return;

            var copy = CopyGeometryData(result);
            GeometryOperators.Translate(copy, (Float3)position);

            // Draw result with solid faces and edges
            Gizmo.DrawGeometryData(copy, GeometryDataVisualization.Solid | GeometryDataVisualization.Edges);
        }

        public Float3 GetBounds()
        {
            // Wide layout to accommodate 4 columns
            return new Float3(16.0f, 3.0f, 3.0f);
        }

        private static GeometryData CopyGeometryData(GeometryData source)
        {
            var copy = new GeometryData();
            var vertexMap = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();

            foreach (var vertex in source.Vertices)
            {
                var newVertex = copy.AddVertex(vertex.Point);
                vertexMap[vertex] = newVertex;
            }

            foreach (var face in source.Faces)
            {
                var faceVerts = face.NeighborVertices();
                var newVerts = faceVerts.Select(v => vertexMap[v]).ToArray();
                copy.AddFace(newVerts);
            }

            return copy;
        }
    }
}
