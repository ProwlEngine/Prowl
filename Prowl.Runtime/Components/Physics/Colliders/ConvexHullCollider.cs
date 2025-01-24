// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;

using Prowl.Icons;
using Prowl.Echo;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Box}  Convex Hull Collider")]
[ExecuteAlways]
public sealed class ConvexHullCollider : Collider
{
    [SerializeField] private AssetRef<Mesh> mesh;

    public AssetRef<Mesh> Mesh
    {
        get => mesh;
        set
        {
            mesh = value;
            OnValidate();
        }
    }

    public override RigidBodyShape[] CreateShapes()
    {
        if (mesh.IsAvailable == false)
        {
            Awake(); // Trigger awake to grab the mesh from a renderer
            if (mesh == null)
                Debug.LogError("Mesh is null");
            return null;
        }

        List<JTriangle> triangles = new();

        return [new ConvexHullShape(ToTriangleList(mesh.Res))];
    }

    public override void Awake()
    {
        if(mesh.IsAvailable == false)
        {
            MeshRenderer? renderer = GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                mesh = renderer.Mesh;
            }
        }
    }

    public List<JTriangle> ToTriangleList(Mesh mesh)
    {
        var vertices = mesh.Vertices;
        int[] indices;

        if (mesh.IndexFormat == Veldrid.IndexFormat.UInt16)
        {
            indices = mesh.Indices16.Select(i => (int)i).ToArray();
        }
        else
        {
            indices = mesh.Indices32.Select(i => (int)i).ToArray();
        }

        List<JTriangle> triangles = new();

        for (int i = 0; i < indices.Length; i += 3)
        {
            JVector v0 = new(vertices[indices[i]].X, vertices[indices[i]].Y, vertices[indices[i]].Z);
            JVector v1 = new(vertices[indices[i + 1]].X, vertices[indices[i + 1]].Y, vertices[indices[i + 1]].Z);
            JVector v2 = new(vertices[indices[i + 2]].X, vertices[indices[i + 2]].Y, vertices[indices[i + 2]].Z);
            triangles.Add(new JTriangle(v0, v1, v2));
        }

        return triangles;
    }
}
