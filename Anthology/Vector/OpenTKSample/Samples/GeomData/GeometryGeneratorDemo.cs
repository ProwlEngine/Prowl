using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample.Samples
{
    /// <summary>
    /// Demonstrates all the geometry generators available in GeometryGenerator.
    /// </summary>
    public class GeometryGeneratorDemo : IDemo
    {
        public string Name => "Geometry Generator Showcase";

        private readonly GeometryData[] _geometries;
        private readonly string[] _names;
        private readonly Float4[] _colors;

        public GeometryGeneratorDemo()
        {
            // Create all geometry primitives
            _geometries = new GeometryData[]
            {
                // Basic shapes
                GeometryGenerator.Box(new Float3(1, 1, 1)),
                GeometryGenerator.Plane(2.0f, 4),
                GeometryGenerator.Sphere(0.6f, segments: 8, rings: 8),

                // Advanced shapes
                GeometryGenerator.Icosphere(0.6f, subdivisions: 1),
                GeometryGenerator.Cylinder(0.5f, 1.5f, segments: 8, rings: 1),
                GeometryGenerator.Cone(0.6f, 1.2f, segments: 8),
                GeometryGenerator.Torus(0.6f, 0.2f, majorSegments: 8, minorSegments: 8),

                // Platonic solids
                GeometryGenerator.Tetrahedron(1.0f),
                GeometryGenerator.Octahedron(0.8f),
                GeometryGenerator.Dodecahedron(0.7f)
            };

            _names = new string[]
            {
                "Box", "Plane", "UV Sphere",
                "Icosphere", "Cylinder", "Cone", "Torus",
                "Tetrahedron", "Octahedron", "Dodecahedron"
            };

            _colors = new Float4[]
            {
                new Float4(1.0f, 0.3f, 0.3f, 0.8f),  // Red
                new Float4(0.3f, 1.0f, 0.3f, 0.8f),  // Green
                new Float4(0.3f, 0.3f, 1.0f, 0.8f),  // Blue
                new Float4(1.0f, 1.0f, 0.3f, 0.8f),  // Yellow
                new Float4(1.0f, 0.3f, 1.0f, 0.8f),  // Magenta
                new Float4(0.3f, 1.0f, 1.0f, 0.8f),  // Cyan
                new Float4(1.0f, 0.6f, 0.2f, 0.8f),  // Orange
                new Float4(0.8f, 0.3f, 0.8f, 0.8f),  // Purple
                new Float4(0.3f, 0.8f, 0.6f, 0.8f),  // Teal
                new Float4(0.9f, 0.7f, 0.3f, 0.8f)   // Gold
            };
        }

        public void Draw(Float3 position, float timeInSeconds)
        {
            // Stack all shapes vertically within a 6x6 footprint
            float verticalSpacing = 2.2f;
            float startY = -(_geometries.Length - 1) * verticalSpacing * 0.5f;

            for (int i = 0; i < _geometries.Length; i++)
            {
                Float3 offset = position + new Float3(
                    0,
                    startY + i * verticalSpacing,
                    0
                );

                // Copy and transform geometry
                var copy = CopyGeometryData(_geometries[i]);

                // Rotate based on time for visual interest
                float rotationY = timeInSeconds * 0.5f + i * 0.3f;
                var rotation = Quaternion.AxisAngle(Float3.UnitY, rotationY);
                var transform = Float4x4.CreateFromQuaternion(rotation);
                GeometryOperators.Transform(copy, transform);

                // Translate to final position
                GeometryOperators.Translate(copy, (Float3)offset);

                // Draw with solid faces and edges
                Gizmo.DrawGeometryData(copy,
                    GeometryDataVisualization.Solid | GeometryDataVisualization.Edges);
            }
        }

        public Float3 GetBounds()
        {
            // 6x6 footprint with vertical stacking
            float totalHeight = _geometries.Length * 2.2f;
            return new Float3(6.0f, totalHeight, 6.0f);
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
