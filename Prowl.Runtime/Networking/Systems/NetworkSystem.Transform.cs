// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;
using Prowl.Warp;

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

    internal static TransformChannels SyncedChannels(GameObject go) =>
        (go.Network.SyncsTransform(NetworkFlags.NoPositionSync) ? TransformChannels.Position : 0) |
        (go.Network.SyncsTransform(NetworkFlags.NoRotationSync) ? TransformChannels.Rotation : 0) |
        (go.Network.SyncsTransform(NetworkFlags.NoScaleSync) ? TransformChannels.Scale : 0);

    /// <summary>Plays pending transform samples for objects this peer doesn't control, InterpolationDelay behind.</summary>
    internal void ApplyInterpolation()
    {
        double now = Now;
        foreach (NetworkObject entry in Entries.Values)
        {
            if (entry.TransformSync.HasPendingInterpolation && entry.GameObject.IsValid() && !HasControl(entry.GameObject))
            {
                entry.TransformSync.Interpolate(entry.GameObject, now);
            }
        }
    }

    internal static void ApplyNetworkTransform(GameObject go, Float3 position, Quaternion rotation, Float3 scale)
    {
        if (go.Network.SyncsTransform(NetworkFlags.NoPositionSync))
        {
            go.Transform.LocalPosition = position;
        }

        if (go.Network.SyncsTransform(NetworkFlags.NoRotationSync))
        {
            go.Transform.LocalRotation = rotation;
        }

        if (go.Network.SyncsTransform(NetworkFlags.NoScaleSync))
        {
            go.Transform.LocalScale = scale;
        }
    }

    internal static bool Finite(Float3 value) =>
        float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);
}
