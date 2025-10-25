// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample.Samples;

/// <summary>
/// Demonstrates the BevelVertices operator on various shapes.
/// Shows how beveling vertices replaces sharp corners with small faces.
/// </summary>
public class BevelVerticesDemo : IDemo
{
    public string Name => "Bevel Vertices";

    private GeometryData? _originalCube;
    private GeometryData? _originalPyramid;

    public BevelVerticesDemo()
    {
        // Create a simple cube
        _originalCube = GeometryGenerator.Box(Double3.One);

        // Create a pyramid (4-sided)
        _originalPyramid = GeometryGenerator.Cone(0.5, 1);
        //_originalPyramid = new GeometryData();
        //var apex = _originalPyramid.AddVertex(new Double3(0, 1, 0));
        //var base1 = _originalPyramid.AddVertex(new Double3(-0.5, -0.5, -0.5));
        //var base2 = _originalPyramid.AddVertex(new Double3(0.5, -0.5, -0.5));
        //var base3 = _originalPyramid.AddVertex(new Double3(0.5, -0.5, 0.5));
        //var base4 = _originalPyramid.AddVertex(new Double3(-0.5, -0.5, 0.5));
        //
        //// Create pyramid faces
        //_originalPyramid.AddFace(base1, base2, base3, base4); // Base
        //_originalPyramid.AddFace(apex, base2, base1); // Side 1
        //_originalPyramid.AddFace(apex, base3, base2); // Side 2
        //_originalPyramid.AddFace(apex, base4, base3); // Side 3
        //_originalPyramid.AddFace(apex, base1, base4); // Side 4
    }

    public void Draw(Float3 position, float timeInSeconds)
    {
        if (_originalCube == null || _originalPyramid == null) return;

        // Animate the bevel offset
        float bevelOffset = (float)(Math.Sin(timeInSeconds * 0.6) * 0.15 + 0.25);

        // TOP ROW: Cube beveling
        // LEFT: Original cube
        {
            var mesh = CopyGeometryData(_originalCube);

            Float3 topLeftPos = position + new Float3(-2.5f, 1.5f, 0);
            DrawMesh(mesh, topLeftPos, new Float4(0.6f, 0.6f, 0.6f, 0.8f), MeshMode.Wireframe);
            DrawMesh(mesh, topLeftPos, new Float4(0.5f, 0.5f, 0.5f, 0.3f), MeshMode.Solid);
        }

        // CENTER: Cube with one corner beveled
        {
            var mesh = CopyGeometryData(_originalCube);

            // Find the top-front-right corner vertex (all positive coordinates)
            GeometryData.Vertex? cornerVertex = null;
            foreach (var v in mesh.Vertices)
            {
                if (v.Point.X > 0.4 && v.Point.Y > 0.4 && v.Point.Z > 0.4)
                {
                    cornerVertex = v;
                    break;
                }
            }

            if (cornerVertex != null)
            {
                GeometryOperators.BevelVertices(mesh, [.. mesh.Vertices], bevelOffset);
            }

            Float3 topCenterPos = position + new Float3(0, 1.5f, 0);
            DrawMesh(mesh, topCenterPos, new Float4(0.3f, 0.8f, 1.0f, 0.8f), MeshMode.Wireframe);
            DrawMesh(mesh, topCenterPos, new Float4(0.5f, 0.7f, 1.0f, 0.4f), MeshMode.Solid);
        }

        // RIGHT: Cube with all corners beveled
        {
            var mesh = CopyGeometryData(_originalCube);

            // Get all vertices (all corners)
            var allVertices = mesh.Vertices.ToList();
            GeometryOperators.BevelVertices(mesh, allVertices, bevelOffset * 0.8);

            Float3 topRightPos = position + new Float3(2.5f, 1.5f, 0);
            DrawMesh(mesh, topRightPos, new Float4(0.2f, 1.0f, 0.5f, 0.8f), MeshMode.Wireframe);
            DrawMesh(mesh, topRightPos, new Float4(0.3f, 1.0f, 0.6f, 0.4f), MeshMode.Solid);
        }

        // BOTTOM ROW: Pyramid beveling
        // LEFT: Original pyramid
        {
            var mesh = CopyGeometryData(_originalPyramid);

            Float3 bottomLeftPos = position + new Float3(-2.5f, -1.5f, 0);
            DrawMesh(mesh, bottomLeftPos, new Float4(0.6f, 0.6f, 0.6f, 0.8f), MeshMode.Wireframe);
            DrawMesh(mesh, bottomLeftPos, new Float4(0.5f, 0.5f, 0.5f, 0.3f), MeshMode.Solid);
        }

        // CENTER: Pyramid with apex beveled
        {
            var mesh = CopyGeometryData(_originalPyramid);

            // Find the apex (highest Y coordinate)
            GeometryData.Vertex? apex = null;
            double maxY = double.MinValue;
            foreach (var v in mesh.Vertices)
            {
                if (v.Point.Y > maxY)
                {
                    maxY = v.Point.Y;
                    apex = v;
                }
            }

            if (apex != null)
            {
                GeometryOperators.BevelVertices(mesh, new[] { apex }, bevelOffset);
            }

            Float3 bottomCenterPos = position + new Float3(0, -1.5f, 0);
            DrawMesh(mesh, bottomCenterPos, new Float4(1.0f, 0.5f, 0.2f, 0.8f), MeshMode.Wireframe);
            DrawMesh(mesh, bottomCenterPos, new Float4(1.0f, 0.6f, 0.3f, 0.5f), MeshMode.Solid);
        }

        // RIGHT: Pyramid with all vertices beveled
        {
            var mesh = CopyGeometryData(_originalPyramid);

            var allVertices = mesh.Vertices.ToList();
            GeometryOperators.BevelVertices(mesh, allVertices, bevelOffset * 0.7);

            Float3 bottomRightPos = position + new Float3(2.5f, -1.5f, 0);
            DrawMesh(mesh, bottomRightPos, new Float4(0.8f, 0.3f, 1.0f, 0.8f), MeshMode.Wireframe);
            DrawMesh(mesh, bottomRightPos, new Float4(0.7f, 0.5f, 1.0f, 0.4f), MeshMode.Solid);
        }

        // Draw animated offset indicator
        DrawOffsetIndicator(position + new Float3(0, -3.0f, 0), bevelOffset);
    }

    private void DrawOffsetIndicator(Float3 position, float offset)
    {
        // Draw a line showing the current bevel offset
        float lineLength = 2.0f;
        float markerPos = offset * lineLength;

        // Draw base line
        Gizmo.DrawLine(
            position + new Float3(-lineLength / 2, 0, 0),
            position + new Float3(lineLength / 2, 0, 0),
            new Float4(0.5f, 0.5f, 0.5f, 0.7f)
        );

        // Draw marker
        Gizmo.DrawLine(
            position + new Float3(-lineLength / 2 + markerPos, -0.1f, 0),
            position + new Float3(-lineLength / 2 + markerPos, 0.1f, 0),
            new Float4(1.0f, 1.0f, 0.0f, 1.0f)
        );

        // Draw labels
        float labelY = -0.3f;

        // "0.0" label
        Gizmo.DrawLine(
            position + new Float3(-lineLength / 2, labelY - 0.05f, 0),
            position + new Float3(-lineLength / 2, labelY + 0.05f, 0),
            new Float4(0.7f, 0.7f, 0.7f, 0.7f)
        );

        // "0.5" label (midpoint)
        Gizmo.DrawLine(
            position + new Float3(0, labelY - 0.05f, 0),
            position + new Float3(0, labelY + 0.05f, 0),
            new Float4(0.7f, 0.7f, 0.7f, 0.7f)
        );

        // "1.0" label
        Gizmo.DrawLine(
            position + new Float3(lineLength / 2, labelY - 0.05f, 0),
            position + new Float3(lineLength / 2, labelY + 0.05f, 0),
            new Float4(0.7f, 0.7f, 0.7f, 0.7f)
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

    public Float3 GetBounds() => new Float3(8.0f, 7.0f, 3.0f);
}
