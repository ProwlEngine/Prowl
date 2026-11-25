using System.Diagnostics;

using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void ASendWritesEachChangedValueOnceForAllPeers()
    {
        Scene scene = CreateScene();
        GameObject go = CreateGameObject("Counted");
        go.Network.Networked = true;
        CountedWritesTestComponent component = go.AddComponent<CountedWritesTestComponent>();
        scene.Add(go);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        List<FakePeer> peers = [];
        try
        {
            for (int i = 0; i < 16; i++) peers.Add(FakePeer.Join(network, $"peer{i}"));
            Pump(() => peers.All(p => p.IsReady), [.. peers]);
            PumpFor(5, [.. peers]);

            component.Value = new CountedWrites(7);
            CountedWrites.Writes = 0;
            Networking.Networking.System!.Sync.SendSyncChanges();
            Assert.Equal(1, CountedWrites.Writes);
        }
        finally
        {
            Networking.Networking.Shutdown();
            foreach (FakePeer peer in peers) peer.Dispose();
        }
    }
    [Fact]
    public void AnObjectEnteringSeveralPeersInterestTogetherIsWrittenOnce()
    {
        using InterestSession session = InterestHost(new DistanceInterest { Range = 10, RebuildInterval = 0 });
        Player(session.World, session.AliceHere, "Alice", Float3.Zero);
        Player(session.World, session.BobHere, "Bob", Float3.Zero);
        GameObject crate = CreateGameObject("Counted crate");
        crate.AddComponent<CountedWritesTestComponent>();
        crate.Transform.Position = new Float3(100, 0, 0);
        session.World.Add(crate);
        crate.SpawnBuilt();
        PumpFor(10, session.Alice, session.Bob);
        Assert.Equal(0, Spawns(session.Alice, crate));

        CountedWrites.Writes = 0;
        crate.Transform.Position = Float3.Zero;
        Pump(() => Spawns(session.Alice, crate) == 1 && Spawns(session.Bob, crate) == 1, session.Alice, session.Bob);
        Assert.Equal(1, CountedWrites.Writes);
    }
}

/// <summary>A value that counts how often it is written for the network.</summary>
public readonly record struct CountedWrites(int Number)
{
    public static int Writes;

    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Register() => NetSerializer<CountedWrites>.Register(
        (w, v) => { Writes++; w.WriteVarInt32(v.Number); }, (ref NetReader r) => new CountedWrites(r.ReadVarInt32()));
}

/// <summary>What the script compiler generates for <c>[Sync] public CountedWrites Value</c>.</summary>
public class CountedWritesTestComponent : NetworkBehaviour
{
    private CountedWrites __sync_Value;
    private static readonly SyncMember<CountedWritesTestComponent, CountedWrites> __syncnet_Value =
        new(951, "Value", static s => s.__sync_Value, static (s, v) => s.__sync_Value = v);
    public CountedWrites Value { get => __sync_Value; set => __syncnet_Value.Set(this, ref __sync_Value, value); }
}
