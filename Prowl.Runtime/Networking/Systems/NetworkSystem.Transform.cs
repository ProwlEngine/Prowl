// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;
using Prowl.Courier;

namespace Prowl.Runtime.Networking;

// Networked objects' transforms. Each NetworkTransform has a TransformSync that decides what the object's controller
// sends and what everyone else shows; TransformSystem carries the records. Whoever controls an object sends the
// channels (position, rotation, scale) that changed while it moves, and once it is still, its full pose, owed until
// acknowledged. An object without a NetworkTransform, and a static GameObject, send nothing.
internal sealed partial class NetworkSystem
{

    internal static void ValidateMessageLimit(NetSessionOptions? options)
    {
        if ((options ?? Networking.Configuration.CreateSessionOptions()).MaxMessageSize <
            TransformBatchMessage.MinMessageSize)
        {
            throw new NotSupportedException(
                $"Networking requires a message limit of at least {TransformBatchMessage.MinMessageSize} bytes.");
        }
    }

    /// <summary>Plays pending transform samples for objects this peer doesn't control, InterpolationDelay behind.</summary>
    internal void ApplyInterpolation()
    {
        double now = Now;
        foreach (NetworkObject entry in Entries.Values)
        {
            NetworkTransform[] transforms = entry.Transforms;
            if (transforms.Length == 0 || !entry.GameObject.IsValid() || HasControl(entry))
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
}
