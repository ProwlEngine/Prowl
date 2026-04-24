// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Reflection;

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

    // Reflection fields for ConvexHullShape internals, initialized once.
    private static bool s_reflectionReady;
    private static FieldInfo? s_hullVerticesField; // ConvexHullShape.vertices  (CHullVector[])
    private static FieldInfo? s_hullIndicesField;  // ConvexHullShape.indices   (CHullTriangle[])
    private static FieldInfo? s_hullShiftField;    // ConvexHullShape.shifted   (JVector — hull centroid)
    private static FieldInfo? s_chullVertexField;  // CHullVector.Vertex        (JVector)
    private static FieldInfo? s_chullIndexAField;  // CHullTriangle.IndexA      (UInt16)
    private static FieldInfo? s_chullIndexBField;  // CHullTriangle.IndexB
    private static FieldInfo? s_chullIndexCField;  // CHullTriangle.IndexC

    private int convexGizmosDrawCount = 0;
    private int concaveGizmosDrawCount = 0;

    private static void EnsureReflection()
    {
        if (s_reflectionReady) return;
        s_reflectionReady = true;

        const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
        s_hullVerticesField = typeof(ConvexHullShape).GetField("vertices", F);
        s_hullIndicesField  = typeof(ConvexHullShape).GetField("indices",  F);
        s_hullShiftField    = typeof(ConvexHullShape).GetField("shifted",  F);

        if (s_hullVerticesField?.FieldType.GetElementType() is Type vt)
            s_chullVertexField = vt.GetField("Vertex", BindingFlags.Public | BindingFlags.Instance);

        if (s_hullIndicesField?.FieldType.GetElementType() is Type it)
        {
            s_chullIndexAField = it.GetField("IndexA", BindingFlags.Public | BindingFlags.Instance);
            s_chullIndexBField = it.GetField("IndexB", BindingFlags.Public | BindingFlags.Instance);
            s_chullIndexCField = it.GetField("IndexC", BindingFlags.Public | BindingFlags.Instance);
        }
    }

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
            convexGizmosDrawCount++;
            if (convexGizmosDrawCount <= 10)
            {
                Debug.LogWarning("DrawConvexHullGizmo");
            }
        }
        else
        {
            DrawMeshWireframeGizmo(m);
            concaveGizmosDrawCount++;
            if (concaveGizmosDrawCount <= 10)
            {
                Debug.LogWarning("DrawMeshWireframeGizmo");
            }
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

        EnsureReflection();

        if (s_hullVerticesField == null || s_hullIndicesField == null ||
            s_chullVertexField == null || s_chullIndexAField == null ||
            s_chullIndexBField == null || s_chullIndexCField == null)
            return;

        var hullVerts = (Array?)s_hullVerticesField.GetValue(_cachedConvexShape);
        var hullTris  = (Array?)s_hullIndicesField.GetValue(_cachedConvexShape);
        var shift     = s_hullShiftField?.GetValue(_cachedConvexShape) is JVector sv ? sv : JVector.Zero;

        if (hullVerts == null || hullTris == null) return;

        for (int i = 0; i < hullTris.Length; i++)
        {
            object? tri = hullTris.GetValue(i);
            if (tri == null) continue;

            int ia = (ushort)(s_chullIndexAField.GetValue(tri) ?? (ushort)0);
            int ib = (ushort)(s_chullIndexBField.GetValue(tri) ?? (ushort)0);
            int ic = (ushort)(s_chullIndexCField.GetValue(tri) ?? (ushort)0);

            object? va = hullVerts.GetValue(ia);
            object? vb = hullVerts.GetValue(ib);
            object? vc = hullVerts.GetValue(ic);
            if (va == null || vb == null || vc == null) continue;

            // Stored vertices are shifted to centroid-at-origin; add shift to recover mesh-local positions.
            var jva = (JVector)(s_chullVertexField.GetValue(va) ?? JVector.Zero) + shift;
            var jvb = (JVector)(s_chullVertexField.GetValue(vb) ?? JVector.Zero) + shift;
            var jvc = (JVector)(s_chullVertexField.GetValue(vc) ?? JVector.Zero) + shift;

            Float3 a = new Float3(jva.X, jva.Y, jva.Z) + Center;
            Float3 b = new Float3(jvb.X, jvb.Y, jvb.Z) + Center;
            Float3 c = new Float3(jvc.X, jvc.Y, jvc.Z) + Center;

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
