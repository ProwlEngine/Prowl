// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;

using Jitter2.Collision.Shapes;
using Jitter2.LinearMath;

using Prowl.Echo;
using Prowl.Runtime.Resources;

namespace Prowl.Runtime;

[AddComponentMenu("Physics/Colliders/Model Collider")]
public sealed class ModelCollider : Collider
{
    [SerializeField] private Model model;

    public Model Model
    {
        get => model;
        set
        {
            model = value;
            OnValidate();
        }
    }

    public override RigidBodyShape[] CreateShapes()
    {
        if (model.IsNotValid())
        {
            OnEnable(); // Trigger OnEnable to grab the Model from a renderer
            if (model.IsNotValid())
                Debug.LogError("Model is null");
            return null;
        }

        List<JTriangle> triangles = ToTriangleList(model);
        TriangleMesh triangleMesh = new(triangles, true);

        List<TriangleShape> triangleShapes = new();
        int count = 0;
        foreach (var triangle in triangleMesh.Indices)
        {
            triangleShapes.Add(new TriangleShape(triangleMesh, count));
            count++;
        }

        return triangleShapes.ToArray();
    }

    public override void OnEnable()
    {
        base.OnEnable();

        if (model.IsNotValid())
        {
            ModelRenderer? renderer2 = GetComponent<ModelRenderer>();
            if (renderer2.IsValid())
            {
                model = renderer2.Model;
            }
            else
            {
                Debug.LogWarning("ConvexHullCollider could not find a MeshRenderer to get the mesh from.");
            }
        }
    }

    public List<JTriangle> ToTriangleList(Model model)
    {
        List<JTriangle> triangles = [];

        foreach (var mesh in model.Meshes)
        {
            Vector.Float3[] vertices = mesh.Mesh.Vertices;
            int[] indices = [.. mesh.Mesh.Indices.Select(i => (int)i)];


            for (int i = 0; i < indices.Length; i += 3)
            {
                JVector v0 = new(vertices[indices[i]].X, vertices[indices[i]].Y, vertices[indices[i]].Z);
                JVector v1 = new(vertices[indices[i + 1]].X, vertices[indices[i + 1]].Y, vertices[indices[i + 1]].Z);
                JVector v2 = new(vertices[indices[i + 2]].X, vertices[indices[i + 2]].Y, vertices[indices[i + 2]].Z);
                triangles.Add(new JTriangle(v0, v1, v2));
            }
        }

        return triangles;
    }
}
