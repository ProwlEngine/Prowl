// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Runtime.Networking;
using Prowl.Warp;

namespace Prowl.Runtime;

public partial class GameObject
{
    private NetworkMode _networkMode = NetworkMode.Snapshot;
    private OwnerTransfer _ownerTransfer = OwnerTransfer.Fixed;
    private NetworkOrphaned _networkOrphaned = NetworkOrphaned.Destroy;
    private NetworkFlags _networkFlags;

    /// <summary>
    /// How this object takes part in a networked session. Has no effect when there is no session, so
    /// single player games can ignore it.
    /// </summary>
    public NetworkMode NetworkMode
    {
        get => _networkMode;
        set
        {
            if (value == _networkMode) return;
            Networking.Networking.AssertHierarchyEditable(this);
            _networkMode = value;
        }
    }

    /// <summary>Who may take ownership. Configurable before spawning; runtime changes are controlled by the host.</summary>
    public OwnerTransfer OwnerTransfer
    {
        get => _ownerTransfer;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == _ownerTransfer) return;
            Networking.Networking.SetOwnerTransfer(this, value);
            _ownerTransfer = value;
        }
    }

    internal void ApplyOwnerTransfer(OwnerTransfer value) => _ownerTransfer = value;

    /// <summary>Attempts to take ownership of this network root. Clients need Takeover policy.
    /// True means committed locally by the host or sent for host confirmation; wait for authority gained before simulating.</summary>
    public bool TakeOwnership() => Networking.Networking.TransferOwnership(this, take: true);

    /// <summary>Relinquishes ownership to the host's simulation. The current owner may drop even under Fixed policy.
    /// True means committed by the host or sent for confirmation.</summary>
    public bool DropOwnership() => Networking.Networking.TransferOwnership(this, take: false);

    /// <summary>For a <see cref="NetworkMode.Object"/>: what happens to it when the peer that owns it leaves.
    /// Before spawning this configures the object; after spawning only the host can change it, and clients receive the new policy.</summary>
    public NetworkOrphaned NetworkOrphaned
    {
        get => _networkOrphaned;
        set
        {
            if (!Enum.IsDefined(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == _networkOrphaned) return;
            Networking.Networking.SetOrphaned(this, value);
            _networkOrphaned = value;
        }
    }

    internal void ApplyNetworkOrphaned(NetworkOrphaned value) => _networkOrphaned = value;

    /// <summary>
    /// Options that disable transform channels or interpolation. Only the host may change them after spawning.
    /// </summary>
    public NetworkFlags NetworkFlags
    {
        get => _networkFlags;
        set
        {
            if (!ValidNetworkFlags(value)) throw new ArgumentOutOfRangeException(nameof(value));
            if (value == _networkFlags) return;
            Networking.Networking.SetNetworkFlags(this, value);
            _networkFlags = value;
        }
    }

    internal bool HasTransformSync => (NetworkFlags & NetworkFlags.NoTransformSync) != NetworkFlags.NoTransformSync;
    internal bool SyncsTransform(NetworkFlags exclusion) => (NetworkFlags & exclusion) == 0;
    internal static bool ValidNetworkFlags(NetworkFlags value) =>
        (value & ~(NetworkFlags.NoTransformSync | NetworkFlags.NoInterpolation)) == 0;
    internal void ApplyNetworkFlags(NetworkFlags value) => _networkFlags = value;

    /// <summary>True while a session runs and this object is part of a networked object everyone shares.</summary>
    public bool IsNetworked => Networking.Networking.IsNetworked(this);

    /// <summary>
    /// True when this runs on a peer that doesn't control the object: it's driven from elsewhere (its
    /// owner, or the host), so don't move it or change its [Sync] properties here. False without a session.
    /// </summary>
    public bool IsProxy => Networking.Networking.IsActive && !Networking.Networking.HasControl(this);

    /// <summary>
    /// The peer that owns this networked object, or null if it has no owner (the host controls it then),
    /// it isn't networked, or there is no session. Ownership belongs to the nearest
    /// <see cref="NetworkMode.Object"/> object at or above this one.
    /// </summary>
    public NetPeer? NetworkOwner => Networking.Networking.GetOwner(this);

    /// <summary>
    /// Host: makes this object a networked object (<see cref="NetworkMode.Object"/>) and creates it on
    /// every client, children and components included. From then on its transform and [Sync] properties
    /// follow whoever controls it, and destroying it destroys it everywhere. Without a session it only
    /// sets <see cref="NetworkMode"/>. Calling it on an object that is networked already, such as one placed
    /// in the scene as <see cref="NetworkMode.Object"/>, hands it to <paramref name="owner"/>.
    /// </summary>
    /// <param name="owner">The peer that controls it. Null leaves it to the host.</param>
    /// <param name="orphaned">What happens to it if its owner leaves. Null keeps <see cref="NetworkOrphaned"/>.</param>
    /// <exception cref="InvalidOperationException">
    /// This is a client (only the host spawns objects), or the object can't be networked here. Nothing is changed then.
    /// </exception>
    /// <exception cref="ArgumentException">In an active session, the owner is not a peer from that session.</exception>
    public void NetworkSpawn(NetPeer? owner = null, NetworkOrphaned? orphaned = null) =>
        Networking.Networking.Spawn(this, owner, orphaned ?? NetworkOrphaned);

    /// <summary>Host: hands control of this networked object to another peer, or back to the host with null.</summary>
    /// <exception cref="InvalidOperationException">This is a client, or the object was not spawned.</exception>
    /// <exception cref="ArgumentException">The owner is not a peer from the current session.</exception>
    public void SetNetworkOwner(NetPeer? owner) => Networking.Networking.SetOwner(this, owner);

    /// <summary>True if this object, or anything above it, is <see cref="NetworkMode.Never"/>.</summary>
    internal bool IsNetworkExcluded
    {
        get
        {
            for (GameObject? go = this; go.IsValid(); go = go.Parent)
                if (go.NetworkMode == NetworkMode.Never)
                    return true;
            return false;
        }
    }

    /// <summary>The nearest <see cref="NetworkMode.Object"/> object at or above this one, or null.</summary>
    internal GameObject? NetworkRoot
    {
        get
        {
            for (GameObject? go = this; go.IsValid(); go = go.Parent)
                if (go.NetworkMode == NetworkMode.Object)
                    return go;
            return null;
        }
    }

    /// <summary>
    /// Set while a scene is serialized to send to clients, so objects that never network are left out.
    /// Thread-local, like <see cref="PreservingIdentifiers"/>.
    /// </summary>
    [ThreadStatic]
    internal static bool WritingNetworkSnapshot;
}
