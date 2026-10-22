// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Warp;

using static Prowl.Samples.Sample;

namespace NetworkingShowcase;

/// <summary>
/// Stage 8. A stress test: the host spawns as many drones as asked for, each a networked object moving every
/// frame, so every transform goes out every tick. Churn changes a [Sync] value on every drone every frame too,
/// and spreading hands the drones out across every peer, so each window drives its share and sends it on.
/// </summary>
public class Swarm : Component
{
    [Sync(SyncFlags.FromHost)] public bool Churn { get; set; }

    public int Drones => GameObject.Scene?.FindObjectsOfType<Drone>().Length ?? 0;

    public void Spawn(int count)
    {
        Mesh mesh = Mesh.CreateCube(new Float3(0.25f, 0.25f, 0.25f));
        for (int i = 0; i < count; i++)
        {
            GameObject drone = Model("Drone", mesh, Lit(new Color(0.7f, 0.7f, 0.75f, 1f)), Transform.Position);
            drone.AddComponent<Drone>().Seed = Random.Shared.NextSingle() * 1000f;
            drone.Network.Mode = NetworkMode.Object;
            GameObject.Scene!.Add(drone);
            drone.Network.Spawn();
        }
    }

    public void Clear()
    {
        foreach (Drone? drone in GameObject.Scene!.FindObjectsOfType<Drone>())
            drone?.GameObject.Destroy();
    }

    /// <summary>Hands the drones out round the peers, the host included.</summary>
    public void Spread()
    {
        List<NetPeer?> peers = [null, .. Networking.Session!.Peers.Where(p => !p.IsLocal)];
        int next = 0;
        foreach (Drone? drone in GameObject.Scene!.FindObjectsOfType<Drone>())
            drone?.GameObject.Network.AssignOwnership(peers[next++ % peers.Count]);
    }

    public void Gather()
    {
        foreach (Drone? drone in GameObject.Scene!.FindObjectsOfType<Drone>())
            drone?.GameObject.Network.AssignOwnership(null);
    }
}

/// <summary>Flies a figure of its own while its owner runs it; in churn, rerolls a synced hue every frame.</summary>
public class Drone : Component
{
    public float Seed;

    [Sync] public float Hue { get; set; }

    private readonly Tint _tint = new();
    private Swarm? _swarm;

    public override void Update()
    {
        NetPeer? owner = GameObject.Network.Owner;
        _tint.Apply(GameObject, owner == null ? Hsv(Hue, 0.6f, 1f) : PeerColors.Of(owner), 1.5f);

        if (GameObject.IsProxy)
            return;

        _swarm ??= GameObject.Scene?.FindObjectsOfType<Swarm>().FirstOrDefault();
        if (_swarm?.Churn == true)
            Hue = Random.Shared.NextSingle();

        Float3 center = _swarm?.Transform.Position ?? Float3.Zero;
        float t = Time.TimeSinceStartup * 0.6f + Seed;
        Transform.Position = center + new Float3(MathF.Sin(t * 1.3f + Seed) * 6f, 1.5f + MathF.Sin(t * 2.1f) * 1.2f, MathF.Cos(t * 0.9f + Seed * 0.5f) * 4f);
    }
}
