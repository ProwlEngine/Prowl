// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Jitter2.Dynamics;

using Prowl.Vector;

namespace Prowl.Runtime.Networking;

internal sealed partial class NetworkObject
{
    internal readonly Dictionary<Rigidbody3D, MotionType> ProxyBodies = new();

    internal void UpdatePhysics(Float3 velocity = default)
    {
        if (GameObject.IsNotValid())
        {
            return;
        }

        // A body here is only driven from elsewhere when a NetworkTransform brings its object's poses. Any other
        // body in the tree, a loose part say, keeps simulating here.
        bool proxy = System.IsProxy(GameObject) && Transforms.Length > 0;
        foreach (Rigidbody3D body in GameObject.GetComponentsInChildren<Rigidbody3D>(includeInactive: true))
        {
            // A predicted body keeps simulating here, corrected from the host's states.
            if (proxy && body.MotionType == MotionType.Dynamic && !ProxyBodies.TryGetValue(body, out _) &&
                Moves(body.GameObject) && body.GetComponent<PredictedRigidbody>().IsNotValid())
            {
                ProxyBodies.Add(body, body.MotionType);
                body.LinearVelocity = Float3.Zero;
                body.AngularVelocity = Float3.Zero;
                body.MotionType = MotionType.Kinematic;
            }
            else if (!proxy && ProxyBodies.TryGetValue(body, out MotionType authored))
            {
                ProxyBodies.Remove(body);
                body.MotionType = authored;
                body.LinearVelocity = ReferenceEquals(body.GameObject, GameObject) ? velocity : Float3.Zero;
            }
        }
    }

    // True if one of this object's transforms sends where go is.
    private bool Moves(GameObject go)
    {
        foreach (NetworkTransform transform in Transforms)
        {
            if (transform.IsValid() && ReferenceEquals(transform.Moved, go))
            {
                return true;
            }
        }

        return false;
    }

    internal Float3 SampledVelocity()
    {
        List<TransformSample>? samples = RootTransform is { } root ? root.Sync.Samples : null;
        if (samples == null || samples.Count < 2)
        {
            return Float3.Zero;
        }

        TransformSample last = samples[^1], before = samples[^2];
        double elapsed = last.Time - before.Time;
        if (elapsed <= 0)
        {
            return Float3.Zero;
        }

        // Samples are local positions; rigidbody velocity is world space.
        Float3 velocity = (last.Position - before.Position) / (float)elapsed;
        GameObject? parent = GameObject.Parent;
        return parent.IsValid() ? parent.Transform.TransformVector(velocity) : velocity;
    }

    private void RestorePhysics()
    {
        foreach ((Rigidbody3D body, MotionType authored) in ProxyBodies)
        {
            if (!body.IsValid())
            {
                continue;
            }

            body.MotionType = authored;
            body.LinearVelocity = Float3.Zero;
        }

        ProxyBodies.Clear();
    }
}
