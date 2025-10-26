// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample.Samples;

/// <summary>
/// Demonstrates the BisectPlane operator on a sphere.
/// Shows a sphere being cut by an animated plane that moves through it.
/// </summary>
public class BisectPlaneDemo : IDemo
{
    public string Name => "BisectPlane";

    private GeometryData? _originalMesh;
    private Plane _cuttingPlane;
    private readonly float _planeSpeed = 0.5f;
    private readonly double _sphereRadius = 0.8;

    public BisectPlaneDemo()
    {
        // Create a subdivided sphere for better cutting demonstration
        var sphere = new Sphere(Double3.Zero, _sphereRadius);
        _originalMesh = sphere.GetGeometryData(8);
    }

    public void Draw(Float3 position, float timeInSeconds)
    {
        if (_originalMesh == null) return;

        // Animate the cutting plane moving through the sphere
        float planeOffset = (float)Math.Sin(timeInSeconds * _planeSpeed);

        // Create cutting plane (horizontal plane moving up and down)
        Double3 planeNormal = Double3.UnitY;
        Double3 planePoint = new Double3(0, planeOffset, 0);
        _cuttingPlane = Plane.FromNormalAndPoint(planeNormal, planePoint);

        // LEFT: Show full bisected mesh
        {
            var mesh = CopyGeometryData(_originalMesh);
            GeometryOperators.BisectPlane(mesh, _cuttingPlane, epsilon: 0.001, snapToPlane: true);

            Float3 leftPos = position + new Float3(-1.5f, 0, 0);
            DrawMesh(mesh, leftPos, new Float4(0.3f, 0.8f, 1.0f, 0.8f), MeshMode.Wireframe);
            DrawMesh(mesh, leftPos, new Float4(0.5f, 0.7f, 1.0f, 0.4f), MeshMode.Solid);
        }

        // CENTER: Show only negative side (below plane)
        {
            var mesh = CopyGeometryData(_originalMesh);
            GeometryOperators.BisectPlane(mesh, _cuttingPlane, epsilon: 0.001, snapToPlane: true);
            GeometryOperators.RemoveVerticesOnPlanePositiveSide(mesh, _cuttingPlane, epsilon: 0.001);

            DrawMesh(mesh, position, new Float4(1.0f, 0.5f, 0.2f, 0.8f), MeshMode.Wireframe);
            DrawMesh(mesh, position, new Float4(1.0f, 0.6f, 0.3f, 0.5f), MeshMode.Solid);
        }

        // RIGHT: Show only positive side (above plane)
        {
            var mesh = CopyGeometryData(_originalMesh);
            GeometryOperators.BisectPlane(mesh, _cuttingPlane, epsilon: 0.001, snapToPlane: true);
            GeometryOperators.RemoveVerticesOnPlaneNegativeSide(mesh, _cuttingPlane, epsilon: 0.001);

            Float3 rightPos = position + new Float3(1.5f, 0, 0);
            DrawMesh(mesh, rightPos, new Float4(0.2f, 1.0f, 0.5f, 0.8f), MeshMode.Wireframe);
            DrawMesh(mesh, rightPos, new Float4(0.3f, 1.0f, 0.6f, 0.5f), MeshMode.Solid);
        }

        // Draw the cutting plane visualization across all three
        DrawCuttingPlane(position + new Float3(-1.5f, 0, 0), planeOffset);
        DrawCuttingPlane(position, planeOffset);
        DrawCuttingPlane(position + new Float3(1.5f, 0, 0), planeOffset);
    }

    private void DrawCuttingPlane(Float3 position, float offset)
    {
        // Draw a grid representing the cutting plane
        Float3 planeCenter = position + new Float3(0, offset, 0);
        float planeSize = 2.0f;
        int gridLines = 8;

        Float4 planeColor = new Float4(1.0f, 0.3f, 0.3f, 0.7f);

        // Draw grid lines
        for (int i = 0; i <= gridLines; i++)
        {
            float t = (i / (float)gridLines - 0.5f) * planeSize;

            // Lines along X
            Gizmo.DrawLine(
                planeCenter + new Float3(t, 0, -planeSize / 2),
                planeCenter + new Float3(t, 0, planeSize / 2),
                planeColor
            );

            // Lines along Z
            Gizmo.DrawLine(
                planeCenter + new Float3(-planeSize / 2, 0, t),
                planeCenter + new Float3(planeSize / 2, 0, t),
                planeColor
            );
        }

        // Draw plane normal
        Gizmo.DrawLine(
            planeCenter,
            planeCenter + new Float3(0, 0.5f, 0),
            new Float4(1.0f, 0.0f, 0.0f, 1.0f)
        );
    }

    private void DrawMesh(GeometryData mesh, Float3 position, Float4 color, MeshMode mode)
    {
        // Transform the mesh to the desired position
        var transformed = CopyGeometryData(mesh);
        GeometryOperators.Translate(transformed, (Double3)position);

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

    public Float3 GetBounds() => new Float3(5.0f, 3.0f, 3.0f);
}
