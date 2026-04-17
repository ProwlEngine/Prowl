// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;

using Prowl.Echo;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime;

/// <summary>
/// Builds a physics collider from a Mesh asset.
/// </summary>
[AddComponentMenu("Physics/Colliders/Mesh Collider")]
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
