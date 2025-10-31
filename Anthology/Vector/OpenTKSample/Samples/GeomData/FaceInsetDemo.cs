// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace OpenTKSample.Samples;

/// <summary>
/// Demonstrates the InsetFaces operator on various shapes with two modes:
/// - Shared: edges move perpendicular to themselves, shared edges shrink properly
/// - PerFace: each face gets its own vertices (separated, no sharing)
/// </summary>
public class FaceInsetDemo : IDemo
{
    public string Name => "Face Inset";

    private GeometryData? _originalCube;
    private GeometryData? _originalSphere;

    public FaceInsetDemo()
    {
        _originalCube = GeometryGenerator.Box(Float3.One * 0.5f);

        _originalSphere = GeometryGenerator.Sphere(0.5f);
        GeometryOperators.WeldVertices(_originalSphere, 0.0001f);
    }

    public void Draw(Float3 position, float timeInSeconds)
    {
        if (_originalCube == null || _originalSphere == null) return;

        // Animate the inset amount
        float insetAmount = (float)(Math.Sin(timeInSeconds * 0.8) * 0.4 + 0.5); // 0.1 to 0.9

        // TOP ROW: Different modes on cube
        // LEFT: Multiple faces with Shared mode
        {
            var mesh = CopyGeometryData(_originalCube);

            // Find top and front faces
            var facesToInset = new List<GeometryData.Face>();
            foreach (var face in mesh.Faces)
            {
                var verts = face.NeighborVertices();
                if (verts.Count >= 3)
                {
                    var v0 = verts[0].Point;
                    var v1 = verts[1].Point;
                    var v2 = verts[2].Point;
                    var normal = Float3.Normalize(Float3.Cross(v1 - v0, v2 - v0));

                    // Top face or front face
                    if (normal.Y > 0.9 || normal.Z < -0.9)
                    {
                        facesToInset.Add(face);
                    }
                }
            }

            if (facesToInset.Count > 0)
            {
                GeometryOperators.InsetFaces(mesh, facesToInset, insetAmount, InsetMode.Shared);
            }

            Float3 leftPos = position + new Float3(-1.5f, 0, 0);
            DrawMesh(mesh, leftPos, new Float4(0.3f, 0.8f, 1.0f, 0.8f), MeshMode.Wireframe);
            DrawMesh(mesh, leftPos, new Float4(0.5f, 0.7f, 1.0f, 0.4f), MeshMode.Solid);
        }

        // CENTER: Multiple faces with Shared mode (shared vertices)
        {
            var mesh = CopyGeometryData(_originalCube);

            // Find top and front faces
            var facesToInset = new List<GeometryData.Face>();
            foreach (var face in mesh.Faces)
            {
                var verts = face.NeighborVertices();
                if (verts.Count >= 3)
                {
                    var v0 = verts[0].Point;
                    var v1 = verts[1].Point;
                    var v2 = verts[2].Point;
                    var normal = Float3.Normalize(Float3.Cross(v1 - v0, v2 - v0));

                    // Top face or front face
                    if (normal.Y > 0.9 || normal.Z < -0.9 || normal.X < -0.9)
                    {
                        facesToInset.Add(face);
                    }
                }
            }

            if (facesToInset.Count > 0)
            {
                GeometryOperators.InsetFaces(mesh, facesToInset, insetAmount, InsetMode.Shared);
            }

            DrawMesh(mesh, position, new Float4(1.0f, 0.5f, 0.2f, 0.8f), MeshMode.Wireframe);
            DrawMesh(mesh, position, new Float4(1.0f, 0.6f, 0.3f, 0.5f), MeshMode.Solid);
        }

        // RIGHT: Sphere with half faces - Shared mode (creates smooth inset)
        {
            var mesh = CopyGeometryData(_originalSphere);

            // Inset faces on the upper hemisphere
            var facesToInset = new List<GeometryData.Face>();
            foreach (var face in mesh.Faces)
            {
                var center = face.Center();
                if (center.Y > 0) // Upper half
                {
                    facesToInset.Add(face);
                }
            }

            if (facesToInset.Count > 0)
            {
                GeometryOperators.InsetFaces(mesh, facesToInset, insetAmount * 0.7f, InsetMode.Shared);
            }

            Float3 rightPos = position + new Float3(1.5f, 0, 0);
            DrawMesh(mesh, rightPos, new Float4(0.2f, 1.0f, 0.5f, 0.8f), MeshMode.Wireframe);
        }

        // BOTTOM ROW: Show PerFace mode and all faces inset
        float bottomY = -2.0f;

        // Bottom Left: Single face with PerFace mode
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
                    var normal = Float3.Normalize(Float3.Cross(v1 - v0, v2 - v0));

                    if (normal.Y > 0.9)
                    {
                        topFace = face;
                        break;
                    }
                }
            }

            if (topFace != null)
            {
                GeometryOperators.InsetFaces(mesh, new[] { topFace }, insetAmount, InsetMode.PerFace);
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

        // Bottom Right: All faces inset - PerFace mode (each face separated)
        {
            var mesh = CopyGeometryData(_originalCube);

            var allFaces = mesh.Faces.ToList();
            GeometryOperators.InsetFaces(mesh, allFaces, insetAmount * 0.6f, InsetMode.PerFace);

            Float3 bottomRightPos = position + new Float3(1.5f, bottomY, 0);
            DrawMesh(mesh, bottomRightPos, new Float4(1.0f, 1.0f, 0.3f, 0.8f), MeshMode.Wireframe);
            DrawMesh(mesh, bottomRightPos, new Float4(1.0f, 1.0f, 0.5f, 0.4f), MeshMode.Solid);
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

    public Float3 GetBounds() => new Float3(8.0f, 5.0f, 3.0f);
}
