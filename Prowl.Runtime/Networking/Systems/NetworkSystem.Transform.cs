// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

// Networked objects' transforms. Each object's TransformSync decides what its controller sends and what everyone else
// shows; SnapshotSystem carries the records. Whoever controls an object sends the channels (position, rotation, scale)
// that changed while it moves, and once it is still, its full pose, owed until acknowledged. Static GameObjects are
// never synced.
internal sealed partial class NetworkSystem
{

    internal static void ValidateSceneMessageLimit(NetSessionOptions? options)
    {
        if ((options ?? Networking.Configuration.CreateSessionOptions()).MaxMessageSize <
            SnapshotMessage.MinMessageSize)
        {
            throw new NotSupportedException(
                $"Scene replication requires a message limit of at least {SnapshotMessage.MinMessageSize} bytes.");
        }
    }

    /// <summary>Plays pending transform samples for objects this peer doesn't control, InterpolationDelay behind.</summary>
    internal void ApplyInterpolation()
    {
        double now = Now;
        foreach (NetworkObject entry in Entries.Values)
        {
            NetworkTransform[] transforms = entry.Transforms;
            if (transforms.Length == 0 || !entry.GameObject.IsValid() || HasControl(entry.GameObject))
            {
                continue;
            }

            foreach (NetworkTransform transform in transforms)
            {
                if (transform.Sync.HasPendingInterpolation && transform.IsValid())
                {
                    transform.Sync.Interpolate(transform.Moved, now);
                }
            }
        }
    }

    internal static bool Finite(Float3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
