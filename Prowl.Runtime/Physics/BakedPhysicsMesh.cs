// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

/// <summary>
/// The CPU-baked physics representation of a <see cref="Mesh"/>: the triangle soup (mesh-local space)
/// plus a Jitter <see cref="Jitter2.Collision.Shapes.TriangleMesh"/> (the concave acceleration
/// structure). Built once per mesh via <see cref="PhysicsWorld.BakeMesh"/> and shared by every collider
/// that uses the mesh, so the (potentially expensive) bake happens a single time instead of per
/// MeshCollider instance.
/// </summary>
public sealed class BakedPhysicsMesh
{
    /// <summary>The triangle soup in mesh-local space. Used to build convex hulls.</summary>
    public IReadOnlyList<JTriangle> Triangles { get; }

    /// <summary>The concave triangle mesh, shareable across many <see cref="TriangleShape"/>s.</summary>
    public TriangleMesh TriangleMesh { get; }

    /// <summary>The <see cref="Mesh.Version"/> this was baked from, used to detect staleness.</summary>
    public uint Version { get; }

    private BakedPhysicsMesh(List<JTriangle> triangles, TriangleMesh triangleMesh, uint version)
    {
        Triangles = triangles;
        TriangleMesh = triangleMesh;
        Version = version;
    }

    /// <summary>
    /// Bake a mesh into its physics representation. This is pure CPU work (it touches no GPU or
    /// main-thread-only state), so it is safe to call from any thread. Prefer
    /// <see cref="PhysicsWorld.BakeMesh"/>, which caches and shares the result per mesh.
    /// </summary>
    public static BakedPhysicsMesh Build(Mesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);

        uint version = mesh.Version;
        List<JTriangle> triangles = ExtractTriangles(mesh);
        var triangleMesh = new TriangleMesh(triangles, true);
        return new BakedPhysicsMesh(triangles, triangleMesh, version);
    }

    private static List<JTriangle> ExtractTriangles(Mesh mesh)
    {
        Float3[] vertices = mesh.Vertices;
        uint[] indices = mesh.Indices;
        var triangles = new List<JTriangle>(indices.Length / 3);

        // i + 2 < indices.Length guards malformed meshes whose index count isn't a multiple of 3,
        // and protects the i+1/i+2 reads.
        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            uint i0 = indices[i];
            uint i1 = indices[i + 1];
            uint i2 = indices[i + 2];

            // Skip triangles whose indices are out of range.
            if (i0 >= vertices.Length || i1 >= vertices.Length || i2 >= vertices.Length)
                continue;

            Float3 v0 = vertices[i0];
            Float3 v1 = vertices[i1];
            Float3 v2 = vertices[i2];
            triangles.Add(new JTriangle(
                new JVector(v0.X, v0.Y, v0.Z),
                new JVector(v1.X, v1.Y, v1.Z),
                new JVector(v2.X, v2.Y, v2.Z)));
        }

        return triangles;
    }
}
