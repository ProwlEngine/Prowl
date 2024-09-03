// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using BepuPhysics.Collidables;

using BepuUtilities.Memory;

using Prowl.Icons;

using NRigidPose = BepuPhysics.RigidPose;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.HillRockslide}  Physics/{FontAwesome6.ChevronUp}  Triangle Collider")]
public sealed class TriangleCollider : Collider
{
    [SerializeField, HideInInspector] private Vector3 _a = new Vector3(-0.5f, 0, 0);
    [SerializeField, HideInInspector] private Vector3 _b = new Vector3(0.5f, 0, 0);
    [SerializeField, HideInInspector] private Vector3 _c = new Vector3(0, 1, 0);

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
        var localA = A * this.Transform.lossyScale;
        var localB = B * this.Transform.lossyScale;
        var localC = C * this.Transform.lossyScale;
        builder.Add(new Triangle(localA, localB, localC), localPose, Mass);
    }
}
