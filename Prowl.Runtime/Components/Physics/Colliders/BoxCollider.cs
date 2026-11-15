// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2.Collision.Shapes;

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime;

[AddComponentMenu("Physics/Colliders/Box Collider")]
public sealed class BoxCollider : Collider
{
    [SerializeField] private Float3 size = new(1, 1, 1);

    /// <summary>
    /// Gets or sets the dimensions of the box.
    /// </summary>
    public Float3 Size
    {
        get => size;
        set
        {
            size = value;
            Rebuild();
        }
    }

    /// <summary>
    /// Sets <see cref="Size"/> and <see cref="Collider.Center"/> to the bounds of the meshes drawn by this GameObject and
    /// its children, in this GameObject's local space, and clears <see cref="Collider.Rotation"/> so the box lines up with them.
    /// </summary>
    [Button("Fit To Bounds")]
    public void FitToBounds()
    {
        AABB? bounds = null;

        void Encapsulate(Transform owner, Mesh? mesh)
        {
            if (mesh.IsNotValid() || mesh.VertexCount == 0) return;
            // The box's own mesh is already in its space; a child's comes through in one go, so it is boxed only once
            AABB local = owner == Transform ? mesh.bounds : mesh.bounds.TransformBy(Transform.WorldToLocalMatrix * owner.LocalToWorldMatrix);
            if (bounds is AABB merged) { merged.Encapsulate(local); bounds = merged; }
            else bounds = local;
        }

        foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>())
            Encapsulate(renderer.Transform, renderer.Mesh);
        foreach (SkinnedMeshRenderer renderer in GetComponentsInChildren<SkinnedMeshRenderer>())
            Encapsulate(renderer.Transform, renderer.SharedMesh);

        if (bounds is not AABB box)
        {
            Debug.LogWarning($"BoxCollider on '{GameObject.Name}' has no mesh on it or its children to fit to.");
            return;
        }

        Rotation = Float3.Zero;
        Center = (box.Min + box.Max) * 0.5f;
        Size = box.Max - box.Min;
    }

    public override RigidBodyShape[] CreateShapes() => [new BoxShape(Maths.Max(size.X, 0.01f), Maths.Max(size.Y, 0.01f), Maths.Max(size.Z, 0.01f))];

    public override void DrawGizmosSelected()
    {
        Debug.PushMatrix(GizmoMatrix);
        Debug.DrawWireCube(Float3.Zero, size * 0.5f, Color.Green);
        Debug.PopMatrix();
    }
}
