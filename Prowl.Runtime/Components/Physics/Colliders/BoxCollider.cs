using BepuPhysics;
using BepuPhysics.Collidables;
using BepuUtilities.Memory;
using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Box}  Box Collider")]
public sealed class BoxCollider : Collider
{
    [SerializeField, HideInInspector] private Vector3 _size = new(1, 1, 1);

    [ShowInInspector]
    public Vector3 Size
    {
        get => _size;
        set
        {
            _size = value;
            Container?.ReAttach();
        }
    }

    public Vector3 WorldSize {
        get {
            return _size * this.Transform.lossyScale;
        }
    }

    internal override void AddToCompoundBuilder(BufferPool pool, ref CompoundBuilder builder, RigidPose localPose)
    {
        var size = WorldSize;
        builder.Add(new Box((float)size.x, (float)size.y, (float)size.z), localPose, Mass);
    }
}