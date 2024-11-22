// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using BepuPhysics.Collidables;

using BepuUtilities.Memory;

using Prowl.Icons;

using NRigidPose = BepuPhysics.RigidPose;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.Circle}  Sphere Collider")]
public sealed class SphereCollider : Collider
{
    [SerializeField, HideInInspector] private float _radius = 0.5f;

    [ShowInInspector]
    public float Radius
    {
        get => _radius;
        set
        {
            _radius = value;
            Container?.ReAttach();
        }
    }

    public float WorldRadius
    {
        get
        {
            var worldScale = Transform.lossyScale;
            return _radius * (float)MathD.Max(worldScale.x, worldScale.y, worldScale.z);
        }
    }


    internal override void AddToCompoundBuilder(BufferPool pool, ref CompoundBuilder builder, NRigidPose localPose)
    {
        builder.Add(new Sphere(WorldRadius), localPose, Mass);
    }
}
