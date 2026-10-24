// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Networking;
using Prowl.Vector;
using Prowl.Warp;

namespace NetworkingShowcase;

/// <summary>
///     Stage 2. A networked object goes round in a circle, driven by whoever controls it: the host, until it
///     hands the rover to a client. Only the controller moves it and sets its speed; everyone else follows its
///     transform, smoothed. Its color says who drives it, worked out on each peer from the owner.
/// </summary>
public class Rover : Component
{
    private readonly Tint _tint = new();
    [SerializeField] public Float3 Center;
    [SerializeField] public float Radius = 3f;

    [Sync] public float Speed { get; set; } = 1f;

    public override void Update()
    {
        NetPeer? owner = GameObject.Network.Owner;
        _tint.Apply(GameObject, owner == null ? new Color(0.9f, 0.9f, 0.95f, 1f) : PeerColors.Of(owner));

        // A proxy's transform comes from its controller.
        if (GameObject.IsProxy)
        {
            return;
        }

        Float3 offset = Transform.Position - Center;
        float angle = MathF.Atan2(offset.Z, offset.X) + Speed * Time.DeltaTime;
        Transform.Position = Center + new Float3(MathF.Cos(angle) * Radius, 0.6f, MathF.Sin(angle) * Radius);
    }
}

/// <summary>
///     Something anyone can pick up: the pucks of stage 2 and the bodies of the physics stage. Grab one with the
///     mouse and it asks the host for control, through a host RPC; once the host hands it over, this window moves
///     it and every other follows. Whoever grabs it next takes it over. <see cref="Home" /> is where it goes back to.
/// </summary>
public class Grabbable : Component
{
    private readonly Tint _tint = new();
    public Float3 Home;

    [Rpc.Host]
    public void TakeControl()
    {
        NetPeer? caller = Rpc.Caller;
        GameObject.Network.AssignOwnership(caller is { IsHost: false } ? caller : null);
    }

    public override void Update()
    {
        NetPeer? owner = GameObject.Network.Owner;
        _tint.Apply(GameObject, owner == null ? new Color(0.9f, 0.9f, 0.95f, 1f) : PeerColors.Of(owner));
    }
}

/// <summary>A color per peer, so whose something is shows at a glance in every window.</summary>
public static class PeerColors
{
    private static readonly Color[] s_colors =
    [
        new(1f, 0.55f, 0.1f, 1f), new(0.2f, 0.7f, 1f, 1f), new(0.4f, 1f, 0.35f, 1f), new(1f, 0.3f, 0.75f, 1f),
        new(0.85f, 0.85f, 0.2f, 1f)
    ];

    public static Color Of(NetPeer peer) => s_colors[peer.Id % s_colors.Length];
}
