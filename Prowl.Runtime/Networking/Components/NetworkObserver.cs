// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.Networking;

/// <summary>
/// A viewpoint for <see cref="InterestMode.Distance" />: the client that owns this object receives the networked objects
/// within <see cref="Radius" /> of it. A client may own several. Viewpoints set through <see cref="PeerInterest" /> replace them.
/// </summary>
[AddComponentMenu("Networking/Network Observer")]
public sealed class NetworkObserver : Component
{
    internal static readonly List<NetworkObserver> Observing = new();

    /// <summary>How far this viewpoint sees. Zero, or anything not above it, uses the project's interest radius.</summary>
    public float Radius;

    /// <summary>Where the viewpoint is, in world space, from the object's position. One that isn't finite counts as zero.</summary>
    public Float3 Offset;

    public override void OnEnable() => Observing.Add(this);

    public override void OnDisable() => Observing.Remove(this);

    internal Float3 Viewpoint =>
        float.IsFinite(Offset.X) && float.IsFinite(Offset.Y) && float.IsFinite(Offset.Z)
            ? GameObject.Transform.Position + Offset
            : GameObject.Transform.Position;
}
