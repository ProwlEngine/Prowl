// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample.Samples;

/// <summary>
/// Shows a simple side-by-side comparison of cube subdivision levels.
/// Left: Original cube, Right: Subdivided cube.
/// </summary>
public class CubeSubdivisionComparison : IDemo
{
    public string Name => "Cube Subdivision";

    private readonly GeometryData _originalCube;
    private readonly GeometryData _subdividedCube;

    public CubeSubdivisionComparison()
    {
        // Create original cube
        var aabb = new AABB(new Float3(-0.4f, -0.4f, -0.4f), new Float3(0.4f, 0.4f, 0.4f));
        _originalCube = aabb.GetGeometryData();

        // Create subdivided version
        var aabb2 = new AABB(new Float3(-0.4f, -0.4f, -0.4f), new Float3(0.4f, 0.4f, 0.4f));
        _subdividedCube = aabb2.GetGeometryData();
        GeometryOperators.Subdivide(_subdividedCube);
    }

    public void Draw(Float3 position, float timeInSeconds)
    {
        // Gentle rotation using quaternion
        float rotation = timeInSeconds * 0.3f;
        var quat = Quaternion.AxisAngle(Float3.UnitY, rotation);
        var rotationMatrix = Float4x4.CreateFromQuaternion(quat);

        // Left: Original cube (wireframe + solid)
        DrawCube(_originalCube, position + new Float3(-0.9f, 0, 0), rotationMatrix,
            new Float4(0.3f, 0.7f, 1.0f, 0.7f),
            new Float4(0.3f, 0.7f, 1.0f, 1.0f));

        // Right: Subdivided cube (wireframe + solid)
        DrawCube(_subdividedCube, position + new Float3(0.9f, 0, 0), rotationMatrix,
            new Float4(1.0f, 0.5f, 0.2f, 0.7f),
            new Float4(1.0f, 0.5f, 0.2f, 1.0f));
    }

    private void DrawCube(GeometryData mesh, Float3 position, Float4x4 rotation, Float4 solidColor, Float4 wireColor)
    {
        // Apply transformations
        var transformed = CopyAndTransform(mesh, position, rotation);

        // Draw solid first
        var triangleMesh = transformed.ToTriangleMesh();
        Gizmo.DrawTriangleMesh(triangleMesh, solidColor);

        // Draw wireframe on top
        var lineMesh = transformed.ToLineMesh();
        Gizmo.DrawLineMesh(lineMesh, wireColor);
    }

    private GeometryData CopyAndTransform(GeometryData source, Float3 position, Float4x4 rotation)
    {
        var copy = new GeometryData();

        // Copy vertices and transform
        var vertexMap = new Dictionary<GeometryData.Vertex, GeometryData.Vertex>();
        foreach (var v in source.Vertices)
        {
            var transformed = Float4x4.TransformPoint(v.Point, rotation);
            var newV = copy.AddVertex(transformed + position);
            vertexMap[v] = newV;
        }

        // Copy faces
        foreach (var face in source.Faces)
        {
            var verts = face.NeighborVertices();
            var newVerts = verts.Select(v => vertexMap[v]).ToArray();
            copy.AddFace(newVerts);
        }

        return copy;
    }

    public Float3 GetBounds() => new Float3(2.5f, 2.0f, 2.0f);
}
