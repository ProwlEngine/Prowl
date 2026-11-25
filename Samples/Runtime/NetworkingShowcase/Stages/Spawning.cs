// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Courier;

using static Prowl.Samples.Sample;

namespace NetworkingShowcase;

/// <summary>
///     Stage 7. Only the host creates networked objects. Every peer registers the same "crate" factory, the host
///     spawns from it, and each peer builds its own copy with that factory: only the pose and the [Sync] values
///     travel. A client asks for one with a host RPC, and the host spawns it owned by that client, who then drives
///     it. When its owner leaves, it goes too.
/// </summary>
public class Hangar : NetworkBehaviour
{
    private int _spawned;

    public int Crates => GameObject.Scene.IsValid() ? GameObject.Scene!.GetAll<Crate>(includeDisabled: true).Length : 0;

    [Rpc.Host(RpcFlags.AnyCaller)]
    public void RequestCrate()
    {
        NetPeer? caller = Rpc.Caller;
        Spawn(caller is { IsHost: false } ? caller : null);
    }

    public void ClearCrates()
    {
        foreach (Crate? crate in GameObject.Scene!.GetAll<Crate>(includeDisabled: true))
        {
            crate?.GameObject.Destroy();
        }
    }

    private void Spawn(NetPeer? owner)
    {
        _spawned++;
        Float3 at = Transform.Position +
                    new Float3(Random.Shared.NextSingle() * 6f - 3f, 0.5f, Random.Shared.NextSingle() * 4f - 2f);
        Networking.SpawnFactory(Factory, at, Quaternion.Identity, owner: owner);
    }

    public const string Factory = "crate";

    /// <summary>Builds a crate, the same on every peer. Its NetworkTransform is part of it before it spawns.</summary>
    public static GameObject BuildCrate(in NetworkSpawn spawn)
    {
        GameObject crate = Model("Crate", Mesh.CreateCube(new Float3(0.7f, 0.7f, 0.7f)),
            Lit(new Color(0.5f, 0.5f, 0.5f, 1f)), spawn.Position);
        crate.AddComponent<NetworkTransform>();
        crate.AddComponent<Crate>().Home = spawn.Position;
        return crate;
    }
}

/// <summary>Bobs while its owner runs it, colored after its owner on every peer.</summary>
public class Crate : Component
{
    private readonly Tint _tint = new();
    public Float3 Home;

    public override void Update()
    {
        NetPeer? owner = GameObject.Network.Owner;
        _tint.Apply(GameObject, owner == null ? new Color(0.6f, 0.6f, 0.62f, 1f) : PeerColors.Of(owner),
            owner == null ? 0f : 1.5f);

        if (!GameObject.IsProxy)
        {
            float phase = Home.X * 1.7f + Home.Z;
            Transform.Position =
                Home + new Float3(0f, MathF.Abs(MathF.Sin(Time.TimeSinceStartup * 2f + phase)) * 0.8f, 0f);
        }
    }
}

/// <summary>
///     Stage 10. The host writes everyone who joins into a network list. Whoever joins late gets the whole list,
///     like everything else here: it builds the level itself, and receives every networked object with its [Sync]
///     values and collections.
/// </summary>
public class Guestbook : NetworkBehaviour
{
    [Sync] public NetList<string> Entries { get; } = new();

    public void Sign(string entry)
    {
        Entries.Add(entry);
        while (Entries.Count > 8)
        {
            Entries.RemoveAt(0);
        }
    }
}
