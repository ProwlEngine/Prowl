// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
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
            Rebuild();
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
            Rebuild();
        }
    }

    // Cached convex hull shape and its tessellation for gizmo drawing rebuilt when mesh or convex flag changes.
    [SerializeIgnore] private ConvexHullShape? _cachedConvexShape;
    [SerializeIgnore] private List<JTriangle>? _cachedHullTris;

    public override RigidBodyShape[] CreateShapes() => BuildShapes(Float4x4.Identity);

    // Concave colliders bake the transform into their vertices instead of being wrapped: Jitter's
    // internal-edge filter tests `shape as TriangleShape`, so a wrapped triangle loses edge filtering
    // and back-face rejection. Convex hulls have no such requirement and take the wrapping path.
    protected override RigidBodyShape[] CreateBakedShapes(Float4x4 transform) => convex ? null : BuildShapes(transform);

    private RigidBodyShape[] BuildShapes(Float4x4 transform)
    {
        Mesh m = ResolveMesh();
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
            return [new ConvexHullShape(baked.Triangles)];

        // Triangles have no volume, so a dynamic body built from them cannot derive an inertia tensor
        // and falls back to a box approximation. Concave dynamic collision is not really supported.
        Rigidbody3D rb = RigidBody;
        if (rb.IsValid() && rb.MotionType == Jitter2.Dynamics.MotionType.Dynamic)
            Debug.LogWarning($"MeshCollider on '{GameObject.Name}' is concave but sits on a dynamic Rigidbody3D. Its inertia is approximated by a box; use Convex for dynamic bodies.");

        // Degenerate triangles are dropped from the baked mesh, so its triangle count is what
        // indexes into it - the source soup can hold more.
        TriangleMesh triMesh = transform == Float4x4.Identity ? baked.TriangleMesh : TransformMesh(baked.TriangleMesh, transform);
        int count = triMesh.Indices.Length;
        if (count == 0)
        {
            Debug.LogWarning("MeshCollider: mesh has no non-degenerate triangles.");
            return null;
        }

        // Every triangle becomes its own shape and its own dynamic tree leaf, so cost scales with the
        // triangle count. Past a few thousand a convex hull or a decimated collision mesh is the answer.
        const int TriangleBudget = 5000;
        if (count > TriangleBudget)
            Debug.LogWarning($"MeshCollider on '{GameObject.Name}' built {count} triangle shapes (over {TriangleBudget}). Consider a convex hull or a lower-poly collision mesh.");

        var shapes = new TriangleShape[count];
        for (int i = 0; i < count; i++)
            shapes[i] = new TriangleShape(triMesh, i);
        return shapes;
    }

    /// <summary>
    /// Copies a baked mesh with its vertices moved into another space. Topology and adjacency are
    /// preserved, so the internal-edge filter still sees a connected mesh.
    /// </summary>
    private static TriangleMesh TransformMesh(TriangleMesh source, Float4x4 transform)
    {
        ReadOnlySpan<JVector> sourceVertices = source.Vertices;
        var vertices = new JVector[sourceVertices.Length];
        for (int i = 0; i < sourceVertices.Length; i++)
        {
            Float3 v = Float4x4.TransformPoint(new Float3(sourceVertices[i].X, sourceVertices[i].Y, sourceVertices[i].Z), transform);
            vertices[i] = new JVector(v.X, v.Y, v.Z);
        }

        // A mirrored transform reverses winding, which would flip every triangle normal and make the
        // one-sided triangles solid from the wrong side. Swap two indices back to compensate.
        bool mirrored = Float4x4.Determinant(transform) < 0.0f;

        ReadOnlySpan<TriangleMesh.Triangle> sourceTriangles = source.Indices;
        var indices = new int[sourceTriangles.Length * 3];
        for (int i = 0; i < sourceTriangles.Length; i++)
        {
            indices[i * 3 + 0] = sourceTriangles[i].IndexA;
            indices[i * 3 + 1] = mirrored ? sourceTriangles[i].IndexC : sourceTriangles[i].IndexB;
            indices[i * 3 + 2] = mirrored ? sourceTriangles[i].IndexB : sourceTriangles[i].IndexC;
        }

        return new TriangleMesh(vertices, indices, true);
    }

    /// <summary>
    /// The mesh to build collision from: the assigned one, else a sibling MeshRenderer's. Physics needs
    /// it present now (a collider is built once, so a transient streaming null would leave it
    /// permanently missing), so the load is blocking and prioritized.
    /// </summary>
    private Mesh ResolveMesh()
    {
        mesh.EnsureLoaded();
        if (mesh.Res != null) return mesh.Res;

        var mr = GetComponent<MeshRenderer>();
        if (mr.IsNotValid()) return null;

        AssetRef<Mesh> rendererMesh = mr.Mesh;
        rendererMesh.EnsureLoaded();
        return rendererMesh.Res;
    }

    // The gizmo hull is derived from the mesh and the convex flag, so it has to die with the shapes.
    // Rebuild rather than OnValidate, because the Mesh and Convex setters go straight to Rebuild.
    public override void Rebuild()
    {
        _cachedConvexShape = null;
        _cachedHullTris = null;
        base.Rebuild();
    }

    public override void OnEnable()
    {
        if (mesh.Res == null)
        {
            var mr = GetComponent<MeshRenderer>();
            if (mr.IsValid())
                mesh = mr.Mesh;
            else
                Debug.LogWarning("MeshCollider could not find a MeshRenderer to get the mesh from.");
        }

        base.OnEnable();
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
