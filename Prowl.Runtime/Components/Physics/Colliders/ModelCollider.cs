// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;

using Prowl.Echo;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime;

/// <summary>
/// Creates a triangle mesh collider from a Mesh asset.
/// </summary>
[AddComponentMenu("Physics/Colliders/Mesh Collider")]
public sealed class ModelCollider : Collider
{
    [SerializeField] private AssetRef<Mesh> mesh;

    public AssetRef<Mesh> Mesh
    {
        get => mesh;
        set { mesh = value; OnValidate(); }
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
            Debug.LogError("ModelCollider: no mesh assigned.");
            return null;
        }

        var triangles = new List<JTriangle>();
        var vertices = m.Vertices;
        var indices = m.Indices;

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            var v0 = vertices[indices[i]];
            var v1 = vertices[indices[i + 1]];
            var v2 = vertices[indices[i + 2]];
            triangles.Add(new JTriangle(
                new JVector(v0.X, v0.Y, v0.Z),
                new JVector(v1.X, v1.Y, v1.Z),
                new JVector(v2.X, v2.Y, v2.Z)));
        }

        var triMesh = new TriangleMesh(triangles, true);
        var shapes = new List<TriangleShape>();
        for (int i = 0; i < triMesh.Indices.Length; i++)
            shapes.Add(new TriangleShape(triMesh, i));

        return shapes.ToArray();
    }
}
