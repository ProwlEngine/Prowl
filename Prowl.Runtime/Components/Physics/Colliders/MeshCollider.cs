// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// Builds a physics collider from a Mesh asset.
/// </summary>
[AddComponentMenu("Physics/Colliders/Mesh Collider")]
[ComponentIcon("\uf1b3")] // Cubes
public sealed class MeshCollider : Collider
{
    [SerializeField] private AssetRef<Mesh> mesh;
    [SerializeField] private bool convex = false;

    public AssetRef<Mesh> Mesh
    {
        get => mesh;
        set
        {
            mesh = value;
            OnValidate();
        }
    }

    /// <summary>
    /// Toggling this will rebuild the collider shapes.
    /// </summary>
    public bool Convex
    {
        get => convex;
        set
        {
            if (convex == value) return;
            convex = value;
            OnValidate();
        }
    }

    // Cached convex hull shape for gizmo drawing — rebuilt when mesh or convex flag changes.
    [SerializeIgnore] private ConvexHullShape? _cachedConvexShape;

    public override RigidBodyShape[] CreateShapes()
    {
        var m = mesh.Res;
        if (m == null)
        {
            // Try to grab from a MeshRenderer on this GO
            var mr = GetComponent<MeshRenderer>();
            if (mr != null) m = mr.Mesh.Res;
        }

        if (m == null)
        {
            Debug.LogError("MeshCollider: no mesh assigned.");
            return null;
        }

        var triangles = ToTriangleList(m);
        if (triangles.Count == 0)
        {
            Debug.LogWarning("MeshCollider: mesh has no triangles.");
            return null;
        }

        if (convex)
        {
            return [new ConvexHullShape(triangles)];
        }
        else
        {
            var triMesh = new TriangleMesh(triangles, true);
            var shapes = new TriangleShape[triangles.Count];
            for (int i = 0; i < triangles.Count; i++)
                shapes[i] = new TriangleShape(triMesh, i);
            return shapes;
        }
    }

    public override void OnValidate()
    {
        _cachedConvexShape = null;
        base.OnValidate();
        Debug.Log("OnInvalidate called");
    }

    public override void OnEnable()
    {
        base.OnEnable();

        if (mesh.Res == null)
        {
            var mr = GetComponent<MeshRenderer>();
            if (mr.IsValid())
                mesh = mr.Mesh;
            else
                Debug.LogWarning("MeshCollider could not find a MeshRenderer to get the mesh from.");
        }
    }

    public override void DrawGizmos()
    {
        var m = mesh.Res;
        if (m == null)
        {
            var mr = GetComponent<MeshRenderer>();
            if (mr != null) m = mr.Mesh.Res;
        }
        if (m == null) return;

        Float4x4 matrix = Float4x4.CreateTRS(Transform.Position, Transform.Rotation * Quaternion.FromEuler(Rotation), Transform.LossyScale);
        Debug.PushMatrix(matrix);

        if (convex)
        {
            DrawConvexHullGizmo(m);
        }
        else
        {
            DrawMeshWireframeGizmo(m);
        }

        Debug.PopMatrix();
    }

    private void DrawMeshWireframeGizmo(Mesh m)
    {
        Float3[] vertices = m.Vertices;
        uint[] indices = m.Indices;
        if (vertices == null || indices == null) return;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            uint i0 = indices[i], i1 = indices[i + 1], i2 = indices[i + 2];
            if (i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
                continue;

            Float3 v0 = vertices[i0] + Center;
            Float3 v1 = vertices[i1] + Center;
            Float3 v2 = vertices[i2] + Center;

            Debug.DrawLine(v0, v1, Color.Green);
            Debug.DrawLine(v1, v2, Color.Green);
            Debug.DrawLine(v2, v0, Color.Green);
        }
    }

    private void DrawConvexHullGizmo(Mesh m)
    {
        if (_cachedConvexShape == null)
        {
            var triangles = ToTriangleList(m);
            if (triangles.Count == 0) return;
            _cachedConvexShape = new ConvexHullShape(triangles);
        }

        // ShapeHelper.Tessellate is the same API Jitter uses in RigidBody.DebugDraw.
        // It generates an approximation of the shape surface via spherical subdivision
        // projected onto the support function — no reflection required.
        List<JTriangle> hullTris = ShapeHelper.Tessellate(_cachedConvexShape, 2);
        JVector shift = _cachedConvexShape.Shift;

        foreach (var tri in hullTris)
        {
            // Hull vertices are CoM-centered; add Shift to convert back to mesh-local space.
            Float3 a = new Float3(tri.V0.X + shift.X, tri.V0.Y + shift.Y, tri.V0.Z + shift.Z) + Center;
            Float3 b = new Float3(tri.V1.X + shift.X, tri.V1.Y + shift.Y, tri.V1.Z + shift.Z) + Center;
            Float3 c = new Float3(tri.V2.X + shift.X, tri.V2.Y + shift.Y, tri.V2.Z + shift.Z) + Center;

            Debug.DrawLine(a, b, Color.Green);
            Debug.DrawLine(b, c, Color.Green);
            Debug.DrawLine(c, a, Color.Green);
        }
    }

    private static List<JTriangle> ToTriangleList(Mesh mesh)
    {
        var vertices = mesh.Vertices;
        var indices = mesh.Indices;
        var triangles = new List<JTriangle>(indices.Length / 3);

        // i + 2 < indices.Length protects against malformed meshes whose index count
        // isn't a multiple of 3, and also protects the i+1/i+2 reads.
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            uint i0 = indices[i];
            uint i1 = indices[i + 1];
            uint i2 = indices[i + 2];

            // Skip degenerate triangles whose indices are out of range
            if (i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
                continue;

            var v0 = vertices[i0];
            var v1 = vertices[i1];
            var v2 = vertices[i2];
            triangles.Add(new JTriangle(
                new JVector(v0.X, v0.Y, v0.Z),
                new JVector(v1.X, v1.Y, v1.Z),
                new JVector(v2.X, v2.Y, v2.Z)));
        }

        return triangles;
    }
}
