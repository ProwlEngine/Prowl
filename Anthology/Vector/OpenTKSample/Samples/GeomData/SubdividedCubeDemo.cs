// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample.Samples;

/// <summary>
/// Demonstrates the Subdivide operator on a cube with smooth animation.
/// Shows a regular cube transitioning through subdivision levels.
/// </summary>
public class SubdividedCubeDemo : IDemo
{
    public string Name => "Subdivided Cube";

    private GeometryData? _baseCube;
    private GeometryData?[] _subdivisionLevels = new GeometryData?[4];

    public SubdividedCubeDemo()
    {
        // Create base cube
        var aabb = new AABB(new Float3(-0.5f, -0.5f, -0.5f), new Float3(0.5f, 0.5f, 0.5f));
        _baseCube = aabb.GetGeometryData();

        // Pre-generate subdivision levels for smooth animation
        _subdivisionLevels[0] = CopyGeometryData(_baseCube);

        for (int i = 1; i < _subdivisionLevels.Length; i++)
        {
            var subdivided = CopyGeometryData(_subdivisionLevels[i - 1]!);
            GeometryOperators.Subdivide(subdivided);
            _subdivisionLevels[i] = subdivided;
        }
    }

    public void Draw(Float3 position, float timeInSeconds)
    {
        // Calculate smooth animation cycle (0 to 3 over 8 seconds)
        float animationSpeed = 0.3f;
        float cycle = (timeInSeconds * animationSpeed) % 4.0f;

        // Determine which subdivision levels to show and blend factor
        int level1 = (int)Maths.Floor(cycle);
        int level2 = (int)Maths.Ceiling(cycle) % 4;


        // Left side: Show current subdivision level (wireframe)
        if (_subdivisionLevels[level1] != null)
        {
            var leftPos = position + new Float3(-1.2f, 0, 0);
            DrawMesh(_subdivisionLevels[level1]!, leftPos,
                new Float4(0.3f, 0.8f, 1.0f, 1.0f), MeshMode.Wireframe);
        }

        // Right side: Show next subdivision level (wireframe) with fade
        if (level1 != level2 && _subdivisionLevels[level2] != null)
        {
            var rightPos = position + new Float3(1.2f, 0, 0);
            DrawMesh(_subdivisionLevels[level2]!, rightPos,
                new Float4(1.0f, 0.5f, 0.2f, 1f), MeshMode.Wireframe);
        }

        // Draw current level solid
        if (_subdivisionLevels[level1] != null)
        {
            DrawMesh(_subdivisionLevels[level1]!, position,
                new Float4(0.5f, 0.7f, 1.0f, 0.8f), MeshMode.Solid);
        }
    }

    private void DrawMesh(GeometryData mesh, Float3 position, Float4 color, MeshMode mode)
    {
        // Transform the mesh to the desired position
        var transformed = CopyGeometryData(mesh);
        GeometryOperators.Translate(transformed, (Float3)position);

        // Draw using the Gizmo system
        if (mode == MeshMode.Wireframe)
        {
            var lineMesh = transformed.ToLineMesh();
            Gizmo.DrawLineMesh(lineMesh, color);
        }
        else
        {
            var triangleMesh = transformed.ToTriangleMesh();
            Gizmo.DrawTriangleMesh(triangleMesh, color);
        }
    }

    private GeometryData CopyGeometryData(GeometryData source)
    {
        var copy = new GeometryData();

        // Copy all vertices with their IDs
        var vertexMap = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();
        foreach (var v in source.Vertices)
        {
            var newV = copy.AddVertex(v.Point);
            newV.Id = v.Id;
            vertexMap[v] = newV;

            // Copy attributes
            foreach (var attr in v.Attributes)
            {
                newV.Attributes[attr.Key] = GeometryData.AttributeValue.Copy(attr.Value);
            }
        }

        // Copy all faces
        foreach (var face in source.Faces)
        {
            var verts = face.NeighborVertices();
            var newVerts = verts.Select(v => vertexMap[v]).ToArray();
            copy.AddFace(newVerts);
        }

        return copy;
    }

    private float SmoothStep(float t)
    {
        // Smooth Hermite interpolation
        return t * t * (3.0f - 2.0f * t);
    }

    public Float3 GetBounds() => new Float3(3.5f, 3.0f, 2.0f);
}
