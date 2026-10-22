// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Jitter2.Dynamics;

using Prowl.Vector;

namespace Prowl.Runtime.Networking;

// Rigidbodies on networked objects. Each peer simulates only what it controls: on a proxy, a dynamic body is
// made kinematic, so it follows the transform its controller sends, pushing what it meets without being
// pushed back. When control moves here, it's made dynamic again and carries on at the speed it was seen
// moving, so something thrown keeps flying. Nothing in Rigidbody3D knows; it's switched from here.
public static partial class Networking
{
    // The bodies made kinematic here, with the motion type each was set up with. Weak, so a destroyed one goes.
    private static readonly ConditionalWeakTable<Rigidbody3D, StrongBox<MotionType>> s_proxyBodies = new();

    /// <summary>
    ///     Whether this peer controls <paramref name="root" />'s object may have changed: its dynamic bodies
    ///     simulate where it's controlled and follow it where it's a proxy. <paramref name="velocity" /> is how
    ///     fast it was last seen moving, for a body that starts simulating here.
    /// </summary>
    private static void ControlChanged(GameObject root, Float3 velocity = default)
    {
        if (root.IsNotValid())
        {
            return;
        }

        bool proxy = root.Network.IsProxy;
        foreach (Rigidbody3D body in root.GetComponentsInChildren<Rigidbody3D>(includeInactive: true))
        {
            if (proxy && body.MotionType == MotionType.Dynamic && !s_proxyBodies.TryGetValue(body, out _))
            {
                s_proxyBodies.Add(body, new StrongBox<MotionType>(body.MotionType));
                body.LinearVelocity = Float3.Zero;
                body.AngularVelocity = Float3.Zero;
                body.MotionType = MotionType.Kinematic;
            }
            else if (!proxy && s_proxyBodies.TryGetValue(body, out StrongBox<MotionType>? authored))
            {
                s_proxyBodies.Remove(body);
                body.MotionType = authored.Value;
                body.LinearVelocity = velocity;
            }
        }
    }

    /// <summary>How fast an object was moving, from the last two transform samples its controller sent.</summary>
    private static Float3 SampledVelocity(NetworkEntry entry)
    {
        List<TransformSample> samples = entry.Samples;
        if (samples.Count < 2)
        {
            return Float3.Zero;
        }

        TransformSample last = samples[^1], before = samples[^2];
        double elapsed = last.Time - before.Time;
        return elapsed > 0 ? (last.Position - before.Position) / (float)elapsed : Float3.Zero;
    }

    /// <summary>
    ///     Puts the bodies made kinematic here back as they were set up while <paramref name="write" /> runs, so a
    ///     snapshot or spawn sends them as the game made them, not as this peer happens to be running them.
    /// </summary>
    private static void AsSetUp(Action write)
    {
        List<Rigidbody3D> switched = new();
        foreach (KeyValuePair<Rigidbody3D, StrongBox<MotionType>> body in s_proxyBodies)
        {
            if (body.Key.IsValid())
            {
                body.Key.MotionType = body.Value.Value;
                switched.Add(body.Key);
            }
        }

        try
        {
            write();
        }
        finally
        {
            foreach (Rigidbody3D body in switched)
            {
                body.MotionType = MotionType.Kinematic;
            }
        }
    }
}
