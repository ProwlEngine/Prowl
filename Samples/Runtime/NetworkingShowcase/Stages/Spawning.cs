// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;
using Prowl.Runtime.Networking.Attributes;
using Prowl.Runtime.Networking.Collections;
using Prowl.Runtime.Networking.Enums;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Warp;

using static Prowl.Samples.Sample;

namespace NetworkingShowcase;

/// <summary>
///     Stage 8. Only the host creates networked objects: it makes one, adds it to the scene and spawns it, and every
///     peer gets it. A client asks for one with a host RPC, and the host spawns it owned by that client, who then
///     drives it. When its owner leaves, it goes too.
/// </summary>
public class Hangar : Component
{
    private int _spawned;

    public int Crates => GameObject.Scene?.FindObjectsOfType<Crate>().Length ?? 0;

    [Rpc.Host]
    public void RequestCrate()
    {
        NetPeer? caller = Rpc.Caller;
        Spawn(caller is { IsHost: false } ? caller : null);
    }

    public void ClearCrates()
    {
        foreach (Crate? crate in GameObject.Scene!.FindObjectsOfType<Crate>())
        {
            crate?.GameObject.Destroy();
        }
    }

    private void Spawn(NetPeer? owner)
    {
        _spawned++;
        Float3 at = Transform.Position +
                    new Float3(Random.Shared.NextSingle() * 6f - 3f, 0.5f, Random.Shared.NextSingle() * 4f - 2f);
        GameObject crate = Model($"Crate {_spawned}", Mesh.CreateCube(new Float3(0.7f, 0.7f, 0.7f)),
            Lit(new Color(0.5f, 0.5f, 0.5f, 1f)), at);
        crate.AddComponent<Crate>().Home = at;
        crate.Network.Mode = NetworkMode.Object;
        GameObject.Scene!.Add(crate);
        crate.Network.Spawn(owner);
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
///     Stage 9. The host writes everyone who joins into a network list. Whoever joins late gets the whole list,
///     like everything else here: the scene, every [Sync] value, every collection and every spawned object, all at once.
/// </summary>
public class Guestbook : Component
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
