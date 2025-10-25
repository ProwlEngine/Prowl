// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample.Samples;

/// <summary>
/// Demonstrates the ExtrudeFaces operator on various shapes.
/// Shows both outward and inward extrusion with different configurations.
/// </summary>
public class FaceExtrusionDemo : IDemo
{
    public string Name => "Face Extrusion";

    private GeometryData? _originalCube;
    private GeometryData? _originalCylinder;

    public FaceExtrusionDemo()
    {
        _originalCube = GeometryGenerator.Box(Double3.One * 0.5);

        _originalCylinder = GeometryGenerator.Sphere(0.5);
        GeometryOperators.WeldVertices(_originalCylinder, 0.0001);
    }

    public void Draw(Float3 position, float timeInSeconds)
    {
        if (_originalCube == null || _originalCylinder == null) return;

        // Animate the extrusion distance
        float extrudeAmount = (float)(Math.Sin(timeInSeconds * 0.8) * 0.3 + 0.35);

        // LEFT: Original cube with top face extruded outward
        {
            var mesh = CopyGeometryData(_originalCube);

            // Find the top face (highest Y normal)
            GeometryData.Face? topFace = null;
            foreach (var face in mesh.Faces)
            {
                var verts = face.NeighborVertices();
                if (verts.Count >= 3)
                {
                    var v0 = verts[0].Point;
                    var v1 = verts[1].Point;
                    var v2 = verts[2].Point;
                    var normal = Double3.Normalize(Double3.Cross(v1 - v0, v2 - v0));

                    if (normal.Y > 0.9) // Top face
                    {
                        topFace = face;
                        break;
                    }
                }
            }

            if (topFace != null)
            {
                GeometryOperators.ExtrudeFaces(mesh, new[] { topFace }, extrudeAmount, useVertexNormals: false);
            }

            Float3 leftPos = position + new Float3(-1.5f, 0, 0);
            DrawMesh(mesh, leftPos, new Float4(0.3f, 0.8f, 1.0f, 0.8f), MeshMode.Wireframe);
            DrawMesh(mesh, leftPos, new Float4(0.5f, 0.7f, 1.0f, 0.4f), MeshMode.Solid);
        }

        // CENTER: Cube with multiple faces extruded
        {
            var mesh = CopyGeometryData(_originalCube);

            // Find top and front faces
            var facesToExtrude = new List<GeometryData.Face>();
            foreach (var face in mesh.Faces)
            {
                var verts = face.NeighborVertices();
                if (verts.Count >= 3)
                {
                    var v0 = verts[0].Point;
                    var v1 = verts[1].Point;
                    var v2 = verts[2].Point;
                    var normal = Double3.Normalize(Double3.Cross(v1 - v0, v2 - v0));

                    // Top face or front face
                    if (normal.Y > 0.9 || normal.Z < -0.9)
                    {
                        facesToExtrude.Add(face);
                    }
                }
            }

            if (facesToExtrude.Count > 0)
            {
                GeometryOperators.ExtrudeFaces(mesh, facesToExtrude, extrudeAmount * 0.7, useVertexNormals: false);
            }

            DrawMesh(mesh, position, new Float4(1.0f, 0.5f, 0.2f, 0.8f), MeshMode.Wireframe);
            DrawMesh(mesh, position, new Float4(1.0f, 0.6f, 0.3f, 0.5f), MeshMode.Solid);
        }

        // RIGHT: Sphere with half faces extruded (using vertex normals for smooth extrusion)
        {
            var mesh = CopyGeometryData(_originalCylinder);

            // Calculate normals for smooth extrusion
            GeometryOperators.RecalculateNormals(mesh);

            // Extrude faces on the upper hemisphere
            var facesToExtrude = new List<GeometryData.Face>();
            foreach (var face in mesh.Faces)
            {
                var center = face.Center();
                if (center.Y > 0) // Upper half
                {
                    facesToExtrude.Add(face);
                }
            }

            if (facesToExtrude.Count > 0)
            {
                GeometryOperators.ExtrudeFaces(mesh, facesToExtrude, extrudeAmount * 0.5, useVertexNormals: true);
            }

            Float3 rightPos = position + new Float3(1.5f, 0, 0);
            DrawMesh(mesh, rightPos, new Float4(0.2f, 1.0f, 0.5f, 0.8f), MeshMode.Wireframe);
        }

        // BOTTOM ROW: Show inward extrusion
        float bottomY = -2.0f;

        // Bottom Left: Cube with top face extruded inward
        {
            var mesh = CopyGeometryData(_originalCube);

            GeometryData.Face? topFace = null;
            foreach (var face in mesh.Faces)
            {
                var verts = face.NeighborVertices();
                if (verts.Count >= 3)
                {
                    var v0 = verts[0].Point;
                    var v1 = verts[1].Point;
                    var v2 = verts[2].Point;
                    var normal = Double3.Normalize(Double3.Cross(v1 - v0, v2 - v0));

                    if (normal.Y > 0.9)
                    {
                        topFace = face;
                        break;
                    }
                }
            }

            if (topFace != null)
            {
                // Negative distance for inward extrusion
                GeometryOperators.ExtrudeFaces(mesh, new[] { topFace }, -extrudeAmount * 0.8, useVertexNormals: false);
            }

            Float3 bottomLeftPos = position + new Float3(-1.5f, bottomY, 0);
            DrawMesh(mesh, bottomLeftPos, new Float4(0.8f, 0.3f, 1.0f, 0.8f), MeshMode.Wireframe);
            DrawMesh(mesh, bottomLeftPos, new Float4(0.7f, 0.5f, 1.0f, 0.4f), MeshMode.Solid);
        }

        // Bottom Center: Original mesh for reference
        {
            var mesh = CopyGeometryData(_originalCube);

            Float3 bottomCenterPos = position + new Float3(0, bottomY, 0);
            DrawMesh(mesh, bottomCenterPos, new Float4(0.6f, 0.6f, 0.6f, 0.8f), MeshMode.Wireframe);
            DrawMesh(mesh, bottomCenterPos, new Float4(0.5f, 0.5f, 0.5f, 0.3f), MeshMode.Solid);
        }

        // Bottom Right: All faces extruded outward (creates interesting shape)
        {
            var mesh = CopyGeometryData(_originalCube);

            var allFaces = mesh.Faces.ToList();
            GeometryOperators.ExtrudeFaces(mesh, allFaces, extrudeAmount * 0.4, useVertexNormals: false);

            Float3 bottomRightPos = position + new Float3(1.5f, bottomY, 0);
            DrawMesh(mesh, bottomRightPos, new Float4(1.0f, 1.0f, 0.3f, 0.8f), MeshMode.Wireframe);
            DrawMesh(mesh, bottomRightPos, new Float4(1.0f, 1.0f, 0.5f, 0.4f), MeshMode.Solid);
        }
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

    public Float3 GetBounds() => new Float3(8.0f, 5.0f, 3.0f);
}
