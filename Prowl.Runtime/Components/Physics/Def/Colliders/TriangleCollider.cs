using BepuPhysics.Collidables;
using BepuUtilities.Memory;
using Prowl.Icons;
using NRigidPose = BepuPhysics.RigidPose;

namespace Prowl.Runtime.Components.NewPhysics.Colliders;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.ChevronUp}  Triangle Collider")]
public sealed class TriangleCollider : Collider
{
    [SerializeField, HideInInspector] private Vector3 _a = new(1, 1, 1);
    [SerializeField, HideInInspector] private Vector3 _b = new(1, 1, 1);
    [SerializeField, HideInInspector] private Vector3 _c = new(1, 1, 1);

    [ShowInInspector]
    public Vector3 A
    {
        get => _a;
        set
        {
            _a = value;
            Container?.ReAttach();
        }
    }

    [ShowInInspector]
    public Vector3 B
    {
        get => _b;
        set
        {
            _b = value;
            Container?.ReAttach();
        }
    }

    [ShowInInspector]
    public Vector3 C
    {
        get => _c;
        set
        {
            _c = value;
            Container?.ReAttach();
        }
    }

    internal override void AddToCompoundBuilder(BufferPool pool, ref CompoundBuilder builder, NRigidPose localPose)
    {
        builder.Add(new Triangle(A, B, C), localPose, Mass);
    }
}