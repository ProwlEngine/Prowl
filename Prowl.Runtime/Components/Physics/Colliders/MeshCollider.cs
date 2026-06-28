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

    // Cached convex hull shape and its tessellation for gizmo drawing — rebuilt when mesh or convex flag changes.
    [SerializeIgnore] private ConvexHullShape? _cachedConvexShape;
    [SerializeIgnore] private List<JTriangle>? _cachedHullTris;

    public override RigidBodyShape[] CreateShapes()
    {
        // Physics needs the mesh present now: a collider is built once, so a transient null
        // from async streaming would leave it permanently missing. Block-load it (prioritized).
        mesh.EnsureLoaded();
        var m = mesh.Res;
        if (m == null)
        {
            // Try to grab from a MeshRenderer on this GO
            var mr = GetComponent<MeshRenderer>();
            if (mr != null)
            {
                var rendererMesh = mr.Mesh;
                rendererMesh.EnsureLoaded();
                m = rendererMesh.Res;
            }
        }

        if (m == null)
        {
            Debug.LogError("MeshCollider: no mesh assigned.");
            return null;
        }

        // Shared, cached bake (built once per mesh, reused across all colliders and rebuilt on edit).
        var baked = PhysicsWorld.BakeMesh(m);
        if (baked.Triangles.Count == 0)
        {
            Debug.LogWarning("MeshCollider: mesh has no triangles.");
            return null;
        }

        if (convex)
        {
            return [new ConvexHullShape(baked.Triangles)];
        }
        else
        {
            var triMesh = baked.TriangleMesh;
            var shapes = new TriangleShape[baked.Triangles.Count];
            for (int i = 0; i < shapes.Length; i++)
                shapes[i] = new TriangleShape(triMesh, i);
            return shapes;
        }
    }

    public override void OnValidate()
    {
        _cachedConvexShape = null;
        _cachedHullTris = null;
        base.OnValidate();
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
            var baked = PhysicsWorld.BakeMesh(m);
            if (baked.Triangles.Count == 0) return;
            _cachedConvexShape = new ConvexHullShape(baked.Triangles);
        }

        _cachedHullTris ??= ShapeHelper.Tessellate(_cachedConvexShape, 2);
        JVector shift = _cachedConvexShape.Shift;

        foreach (JTriangle tri in _cachedHullTris)
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

}
