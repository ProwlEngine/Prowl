// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using BepuPhysics.Collidables;

using BepuUtilities.Memory;

using NRigidPose = BepuPhysics.RigidPose;

namespace Prowl.Runtime;

public abstract class Collider : MonoBehaviour
{
    [SerializeField, HideInInspector] private float _mass = 1f;
    private uint _transformVersion = 1;

    public PhysicsBody? Container { get; internal set; }

    [ShowInInspector]
    public float Mass
    {
        get => _mass;
        set
        {
            _mass = value;
            Container?.ReAttach();
        }
    }

    public override void OnEnable()
    {
        base.OnEnable();
        _transformVersion = this.Transform.version;
    }

    public override void LateUpdate()
    {
        if (this.Transform.version != _transformVersion)
        {
            if (Container.Transform != this.Transform)
                Container?.ReAttach();
            _transformVersion = this.Transform.version;
        }
    }


    internal abstract void AddToCompoundBuilder(BufferPool pool, ref CompoundBuilder builder, NRigidPose localPose);
}
