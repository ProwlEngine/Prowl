// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Jitter2.Collision;
using Jitter2.Collision.Shapes;

namespace Prowl.Runtime;

public class LayerFilter : IBroadPhaseFilter
{
    public bool Filter(IDynamicTreeProxy proxyA, IDynamicTreeProxy proxyB)
    {
        if (proxyA is RigidBodyShape rbsA && proxyB is RigidBodyShape rbsB)
        {
            if (rbsA.RigidBody.Tag is not Rigidbody3D.RigidBodyUserData udA ||
                rbsB.RigidBody.Tag is not Rigidbody3D.RigidBodyUserData udB)
                return true;

            return CollisionMatrix.GetLayerCollision(udA.Layer, udB.Layer);
        }

        return false;
    }
}
