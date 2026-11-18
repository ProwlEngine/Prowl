using System.Diagnostics;

using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void ChangingAValueManyTimesBetweenTicksCostsTheSameWithManyPeers()
    {
        SceneInstance scene = CreateScene();
        GameObject go = CreateGameObject("Counter");
        NetTestComponent component = go.AddComponent<NetTestComponent>();
        scene.Add(go);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        List<FakePeer> peers = [];
        try
        {
            for (int i = 0; i < 16; i++) peers.Add(FakePeer.Join(network, $"peer{i}"));
            Pump(() => peers.All(p => p.IsReady), [.. peers]);

            Stopwatch watch = Stopwatch.StartNew();
            for (int i = 0; i < 200_000; i++) component.Health = i;
            long elapsed = watch.ElapsedMilliseconds;
            Assert.True(elapsed < 60, $"200k changes took {elapsed} ms");

            Pump(() => peers.All(p => p.SnapshotInt(component.Identifier, NetTestComponent.HealthSlot) == 199_999), [.. peers]);
        }
        finally
        {
            Networking.Networking.Shutdown();
            foreach (FakePeer peer in peers) peer.Dispose();
        }
    }

    [Fact]
    public void ASendWritesEachChangedValueOnceForAllPeers()
    {
        SceneInstance scene = CreateScene();
        GameObject go = CreateGameObject("Counted");
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
            Networking.Networking.System!.Snapshots.Send();
            Assert.Equal(1, CountedWrites.Writes);
        }
        finally
        {
            Networking.Networking.Shutdown();
            foreach (FakePeer peer in peers) peer.Dispose();
        }
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
public class CountedWritesTestComponent : Component
{
    private CountedWrites __sync_Value;
    private static readonly SyncMember<CountedWritesTestComponent, CountedWrites> __syncnet_Value =
        new(951, "Value", SyncFlags.None, static s => s.__sync_Value, static (s, v) => s.__sync_Value = v);
    public CountedWrites Value { get => __sync_Value; set => __syncnet_Value.Set(this, ref __sync_Value, value); }
}
