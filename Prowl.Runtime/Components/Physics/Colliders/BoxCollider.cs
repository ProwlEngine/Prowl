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
        var min = new Float3(float.MaxValue, float.MaxValue, float.MaxValue);
        var max = new Float3(float.MinValue, float.MinValue, float.MinValue);
        bool found = false;

        void Encapsulate(Transform owner, Mesh? mesh)
        {
            if (mesh.IsNotValid() || mesh.VertexCount == 0) return;
            Float3 lo = mesh.bounds.Min, hi = mesh.bounds.Max;
            for (int i = 0; i < 8; i++)
            {
                var corner = new Float3((i & 1) != 0 ? hi.X : lo.X, (i & 2) != 0 ? hi.Y : lo.Y, (i & 4) != 0 ? hi.Z : lo.Z);
                // The box's own mesh is already in its space; a child's comes through the world
                Float3 local = owner == Transform ? corner : Transform.InverseTransformPoint(owner.TransformPoint(corner));
                min = Maths.Min(min, local);
                max = Maths.Max(max, local);
            }
            found = true;
        }

        foreach (MeshRenderer renderer in GetComponentsInChildren<MeshRenderer>())
            Encapsulate(renderer.Transform, renderer.Mesh);
        foreach (SkinnedMeshRenderer renderer in GetComponentsInChildren<SkinnedMeshRenderer>())
            Encapsulate(renderer.Transform, renderer.SharedMesh);

        if (!found)
        {
            Debug.LogWarning($"BoxCollider on '{GameObject.Name}' has no mesh on it or its children to fit to.");
            return;
        }

        Rotation = Float3.Zero;
        Center = (min + max) * 0.5f;
        Size = max - min;
    }

    public override RigidBodyShape[] CreateShapes() => [new BoxShape(Maths.Max(size.X, 0.01f), Maths.Max(size.Y, 0.01f), Maths.Max(size.Z, 0.01f))];

    public override void DrawGizmosSelected()
    {
        Debug.PushMatrix(GizmoMatrix);
        Debug.DrawWireCube(Float3.Zero, size * 0.5f, Color.Green);
        Debug.PopMatrix();
    }
}
