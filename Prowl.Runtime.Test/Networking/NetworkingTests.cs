// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Runtime.Diagnostics;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;
using Prowl.Courier;
using Prowl.Courier.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
/// Exactly what Prowl's script compiler generates for
/// <code>
/// [Sync] public int Health { get; set; } = 100;
/// [Sync(SyncFlags.None)] public int Score { get; set; }
/// [Rpc.Broadcast] public void Shout(string text) => Shouts.Add((text, Rpc.Caller));
/// [Rpc.Host] public void AskHost(int value) => Asked.Add(value);
/// [Rpc.Host] public void Explode() => throw new InvalidOperationException("boom");
/// </code>
/// written out by hand, since runtime tests don't go through the script compiler.
/// </summary>
public class NetTestComponent : NetworkBehaviour
{
    public const uint HealthSlot = 101, ScoreSlot = 102, ShoutId = 201, AskHostId = 202, ExplodeId = 203;

    public readonly List<(string Text, NetPeer? Caller)> Shouts = new();
    public readonly List<int> Asked = new();

    private int __sync_Health = 100;
    private static readonly SyncMember<NetTestComponent, int> __syncnet_Health = new(HealthSlot, "Health", static s => s.__sync_Health, static (s, v) => s.__sync_Health = v);
    public int Health { get => __sync_Health; set => __syncnet_Health.Set(this, ref __sync_Health, value); }

    private int __sync_Score;
    private static readonly SyncMember<NetTestComponent, int> __syncnet_Score = new(ScoreSlot, "Score", static s => s.__sync_Score, static (s, v) => s.__sync_Score = v);
    public int Score { get => __sync_Score; set => __syncnet_Score.Set(this, ref __sync_Score, value); }

    private static readonly RpcMember<NetTestComponent> __rpc_201 = new(ShoutId, "Shout(string)", RpcMode.Broadcast, RpcFlags.AnyCaller,
        static (NetTestComponent s, ref NetReader r, bool execute) => { string a0 = NetSerializer<string>.Read(ref r); if (!execute) return; RpcMember.ArgumentsRead(); s.Shout(a0); });

    public void Shout(string text)
    {
        if (__rpc_201.Begin(this, out NetWriter w)) { NetSerializer<string>.Write(w, text); if (!__rpc_201.Send(this, w)) return; }
        Shouts.Add((text, Rpc.Caller));
    }

    private static readonly RpcMember<NetTestComponent> __rpc_204 = new(204, "TellOwner(string)", RpcMode.Owner, RpcFlags.AnyCaller,
        static (NetTestComponent s, ref NetReader r, bool execute) => { string value = NetSerializer<string>.Read(ref r); if (!execute) return; RpcMember.ArgumentsRead(); s.TellOwner(value); });

    public void TellOwner(string value)
    {
        if (__rpc_204.Begin(this, out NetWriter w)) { w.WriteString(value); if (!__rpc_204.Send(this, w)) return; }
        Shouts.Add((value, Rpc.Caller));
    }

    private static readonly RpcMember<NetTestComponent> __rpc_202 = new(AskHostId, "AskHost(int)", RpcMode.Host, RpcFlags.AnyCaller,
        static (NetTestComponent s, ref NetReader r, bool execute) => { int a0 = NetSerializer<int>.Read(ref r); if (!execute) return; RpcMember.ArgumentsRead(); s.AskHost(a0); });

    public void AskHost(int value)
    {
        if (__rpc_202.Begin(this, out NetWriter w)) { NetSerializer<int>.Write(w, value); if (!__rpc_202.Send(this, w)) return; }
        Asked.Add(value);
    }

    private static readonly RpcMember<NetTestComponent> __rpc_203 = new(ExplodeId, "Explode()", RpcMode.Host, RpcFlags.AnyCaller,
        static (NetTestComponent s, ref NetReader r, bool execute) => { if (!execute) return; RpcMember.ArgumentsRead(); s.Explode(); });

    public void Explode()
    {
        if (__rpc_203.Begin(this, out NetWriter w)) { if (!__rpc_203.Send(this, w)) return; }
        throw new InvalidOperationException("boom");
    }
}

/// <summary>
/// The other end of a session, as a bare <see cref="NetSession"/> that records the engine's messages
/// and can send hand-made ones. <see cref="Networking"/> runs one session per process, so each test
/// drives one side of the protocol for real and plays the other with this.
/// </summary>
public sealed class FakePeer : IDisposable
{
    // Scene is the scene message: which scenes to load. Ready is the client's answer, and the host's spawns
    // for it sit between SpawnStarted and SpawnFinished.
    public const ushort Scene = Networking.Networking.EngineMessageIds, Spawn = Scene + 1, Despawn = Scene + 2,
        Owner = Scene + 3, Sync = Scene + 4, Transform = Scene + 5, RpcToHost = Scene + 6, RpcFromHost = Scene + 7,
        Ready = Scene + 8, SpawnStarted = Scene + 9, SpawnFinished = Scene + 10,
        Orphaned = Scene + 11, OwnershipState = Scene + 12,
        TransformAck = Scene + 13,
        SectionAdd = Scene + 14, SectionRemove = Scene + 15, SectionMove = Scene + 16, Time = Scene + 17,
        Player = Scene + 18;

    private FakePeer(NetSession session)
    {
        Session = session;
        foreach (ushort id in Enumerable.Range(Scene, SectionMove - Scene + 1).Select(i => (ushort)i).Append(Player))
        {
            ushort captured = id;
            session.OnRaw(id, (NetPeer sender, ref NetReader reader, Delivery delivery, byte channel) =>
            {
                NetReader wire = reader;
                WireReceived.Add((captured, wire.ReadBytes(wire.Remaining).ToArray()));
                Deliveries.Add((captured, delivery));
                Channels.Add((captured, channel));
                Received.Add((captured, ReadEnginePayload(captured, ref reader)));
                if (captured == Transform)
                {
                    NetworkingTests.Decoded.AddOrUpdate(WireReceived[^1].Data, NetworkingTests.DecodeTransforms(WireReceived[^1].Data, _positions));
                }
                if (AutoReady) HandleReadiness(sender, captured, WireReceived[^1].Data);
                if (AutoAck && captured == Transform) Acknowledge(sender, WireReceived[^1].Data);
            });
        }

        session.OnRaw(Time, (NetPeer sender, ref NetReader reader, Delivery delivery, byte channel) =>
            TimesReceived.Add(reader.ReadBytes(reader.Remaining).ToArray()));
    }

    /// <summary>The Time messages received, apart from the rest so they don't crowd them.</summary>
    public List<byte[]> TimesReceived { get; } = new();

    public bool AutoReady { get; set; } = true;

    // Positions received by object and snapshot tick, to resolve the deltas that name them.
    private readonly Dictionary<(uint NetId, uint Generation, byte Index), Dictionary<uint, (long X, long Y, long Z)>> _positions = new();

    /// <summary>Acknowledges every Transform that asks for it, as a real peer would.</summary>
    public bool AutoAck { get; set; } = true;

    private void Acknowledge(NetPeer sender, byte[] snapshot)
    {
        NetReader reader = new(snapshot);
        uint id = reader.ReadVarUInt32();
        if (id == 0) return;
        using NetWriter ack = new();
        ack.WriteVarUInt32(1); ack.WriteVarUInt32(id);
        ack.WriteVarUInt32(0);
        Session.SendRaw(sender, TransformAck, ack.WrittenSpan, Delivery.Unreliable);
    }

    /// <summary>
    ///     The [Sync] values in a state message, as (component, slot, value bytes). The engine side's own objects say
    ///     which component and member each bit is, so a record for an object it doesn't have is skipped.
    /// </summary>
    public static List<(Guid Component, uint Slot, byte[] Value)> SyncValues(byte[] state)
    {
        List<(Guid, uint, byte[])> values = new();
        NetReader r = new(state);
        uint count = r.ReadVarUInt32();
        for (uint i = 0; i < count; i++)
        {
            uint netId = r.ReadVarUInt32();
            r.ReadVarUInt32();
            ulong components = r.ReadVarUInt64();
            NetworkObject? entry = NetworkTestScene.Current.Entries.TryGetNet(netId, out NetworkObject? found) ? found : null;
            while (components != 0)
            {
                int c = System.Numerics.BitOperations.TrailingZeroCount(components);
                components &= components - 1;
                ReadOnlySpan<byte> bytes = r.ReadBytesWithLength();
                if (entry == null || c >= entry.Components.Length) continue;
                Component component = entry.Components[c];
                NetworkMembers members = NetworkMembers.Of(component);
                NetReader block = new(bytes);
                ulong bits = block.ReadVarUInt64();
                while (bits != 0)
                {
                    int m = System.Numerics.BitOperations.TrailingZeroCount(bits);
                    bits &= bits - 1;
                    SyncMember sync = members.SyncAt(m)!;
                    int start = block.Position;
                    sync.Validate(component, ref block);
                    values.Add((component.Identifier, sync.Slot, bytes[start..block.Position].ToArray()));
                }
            }
        }

        return values;
    }

    /// <summary>The latest int value of a [Sync] slot received, or null if none came.</summary>
    public int? SyncInt(Guid component, uint slot)
    {
        foreach ((Guid id, uint s, byte[] value) in Enumerable.Reverse(SyncValues()))
        {
            if (id == component && s == slot)
            {
                NetReader reader = new(value);
                return NetSerializer<int>.Read(ref reader);
            }
        }

        return null;
    }

    /// <summary>Every [Sync] value received in state messages so far, latest last.</summary>
    public List<(Guid Component, uint Slot, byte[] Value)> SyncValues() =>
        WireReceived.Where(m => m.Id == Sync).SelectMany(m => SyncValues(m.Data)).ToList();

    /// <summary>
    ///     One state record for each value: its object's NetId, the ownership generation given, and the component and
    ///     member the engine side knows the value by. A value for something it doesn't know gets a NetId nothing has.
    /// </summary>
    public static void WriteStateRecords(NetWriter w, params (Guid Component, uint Slot, uint Generation, byte[] Value)[] values)
    {
        w.WriteVarUInt32((uint)values.Length);
        foreach ((Guid componentId, uint slot, uint generation, byte[] value) in values)
        {
            NetworkSystem system = NetworkTestScene.Current;
            if (system.FindByIdentifier(componentId) is not Component component ||
                system.FindEntry(component.GameObject) is not { } entry || entry.IndexOf(component) < 0 ||
                NetworkMembers.Of(component).Sync(slot) is not { } sync || NetworkMembers.Of(component).IndexOf(sync) < 0)
            {
                w.WriteVarUInt32(uint.MaxValue >> 4); w.WriteVarUInt32(generation); w.WriteVarUInt64(0);
                continue;
            }

            w.WriteVarUInt32(entry.NetId);
            w.WriteVarUInt32(generation);
            w.WriteVarUInt64(1UL << entry.IndexOf(component));
            using NetWriter block = new();
            block.WriteVarUInt64(1UL << NetworkMembers.Of(component).IndexOf(sync));
            block.WriteBytes(value);
            w.WriteBytesWithLength(block.WrittenSpan);
        }
    }

    /// <summary>
    ///     A state message for values a test spelled out the old way: [i32 writer][varuint count], then per value
    ///     [guid component][varuint slot][varuint generation][bytes, length prefixed]. Anything after the values is
    ///     kept as it is, so a test can still send a message with a malformed tail.
    /// </summary>
    public static byte[] StateFromOld(ReadOnlySpan<byte> old)
    {
        NetReader reader = new(old);
        reader.ReadInt32();
        uint count = reader.ReadVarUInt32();
        List<(Guid, uint, uint, byte[])> values = new();
        try
        {
            for (uint i = 0; i < count; i++)
            {
                values.Add((reader.ReadGuid(), reader.ReadVarUInt32(), reader.ReadVarUInt32(), reader.ReadBytesWithLength().ToArray()));
            }
        }
        catch (EndOfStreamException)
        {
            // A record cut short: what was read of it goes as the tail.
        }

        using NetWriter state = new();
        WriteStateRecords(state, [.. values]);
        if (values.Count == count)
        {
            state.WriteBytes(reader.ReadBytes(reader.Remaining));
        }
        else
        {
            state.WriteByte(255);
        }

        return state.ToArray();
    }

    /// <summary>Sends [Sync] values the way a peer does: as state records, reliably. The id and tick are no longer sent.</summary>
    public void SendSyncValues(NetPeer? to, uint id, uint tick, params (Guid Component, uint Slot, uint Generation, byte[] Value)[] values)
    {
        using NetWriter w = new();
        WriteStateRecords(w, values);
        Session.SendRaw(to ?? Session.HostPeer!, Sync, w.WrittenSpan);
        Session.Flush();
    }

    public ulong SceneGeneration { get; private set; }
    public bool IsReady { get; private set; }

    private void HandleReadiness(NetPeer sender, ushort kind, byte[] payload)
    {
        NetReader reader = new(payload);
        if (kind == Scene && !Session.IsHost)
        {
            reader.ReadByte();
            SceneGeneration = reader.ReadUInt64();
            IsReady = false;
            Send(Ready, w => w.WriteUInt64(SceneGeneration));
        }
        else if (kind == SpawnFinished && !Session.IsHost)
            IsReady = reader.ReadUInt64() == SceneGeneration;
        else if (kind == Ready && Session.IsHost)
        {
            ulong generation = reader.ReadUInt64();
            Send(SpawnStarted, w => w.WriteUInt64(generation), sender);
            foreach (byte[] spawn in JoinSpawns) Send(Spawn, w => w.WriteBytes(spawn), sender);
            Send(SpawnFinished, w => w.WriteUInt64(generation), sender);
        }
    }


    public NetSession Session { get; }

    /// <summary>As a host: the spawns a client gets once it says it is ready, between the started and finished messages.</summary>
    public List<byte[]> JoinSpawns { get; } = new();

    /// <summary>As a host: a scene message for <paramref name="world" />, as the engine writes it.</summary>
    public static byte[] SceneMessage(Scene world)
    {
        using NetWriter writer = new();
        NetworkTestScene.Current.WriteScene(world, writer);
        return writer.ToArray();
    }

    public List<(ushort Id, byte[] Data)> Received { get; } = new();
    public List<(ushort Id, byte[] Data)> WireReceived { get; } = new();
    public List<(ushort Id, Delivery Delivery)> Deliveries { get; } = new();
    public List<(ushort Id, byte Channel)> Channels { get; } = new();

    public static FakePeer Join(LocalNetwork network, string name, NetSessionOptions? options = null) =>
        new(NetSession.Join(new LocalTransport(network, name), "host", name, options: options ?? SchemaOptions));

    public static FakePeer Host(LocalNetwork network) => new(NetSession.Host(new LocalTransport(network, "host"), "FakeHost", SchemaOptions));

    /// <summary>Options with the app version the engine sends, so a bare session can join or host it.</summary>
    public static NetSessionOptions SchemaOptions => new() { AppVersion = 0 };

    private static byte[] ReadEnginePayload(ushort id, ref NetReader reader)
    {
        if (id == Sync)
        {
            // Tests read state as a count, then per value its component, slot and bytes.
            List<(Guid Component, uint Slot, byte[] Value)> values = SyncValues(reader.ReadBytes(reader.Remaining).ToArray());
            using NetWriter logical = new();
            logical.WriteVarUInt32((uint)values.Count);
            foreach ((Guid component, uint slot, byte[] value) in values)
            {
                logical.WriteGuid(component); logical.WriteVarUInt32(slot); logical.WriteBytesWithLength(value);
            }
            return logical.ToArray();
        }
        if (id == RpcFromHost)
        {
            using NetWriter logical = new();
            logical.WriteVarUInt32((uint)NetPeer.HostId);
            (Guid component, uint method) = RpcNamed(reader.ReadVarUInt32(), reader.ReadByte(), reader.ReadVarUInt32());
            logical.WriteGuid(component);
            logical.WriteUInt32(method);
            logical.WriteBytes(reader.ReadBytesWithLength());
            return logical.ToArray();
        }
        return reader.ReadBytes(reader.Remaining).ToArray();
    }

    /// <summary>
    ///     How the wire names an RPC tests name by component and method id: its object's NetId, the component's index
    ///     in it and the method's index in its type. Anything the engine side doesn't know gets a name nothing has.
    /// </summary>
    public static (uint NetId, byte Component, uint Method) RpcTarget(Guid componentId, uint methodId)
    {
        NetworkSystem system = NetworkTestScene.Current;
        if (system.FindByIdentifier(componentId) is not Component component ||
            system.FindEntry(component.GameObject) is not { } entry || entry.IndexOf(component) < 0)
        {
            return (uint.MaxValue >> 4, 0, methodId);
        }

        NetworkMembers members = NetworkMembers.Of(component);
        int method = members.Rpc(methodId) is { } rpc ? members.IndexOf(rpc) : -1;
        return (entry.NetId, (byte)entry.IndexOf(component), method < 0 ? 1000u : (uint)method);
    }

    /// <summary>The component and method id an RPC on the wire names, or empty ones.</summary>
    public static (Guid Component, uint Method) RpcNamed(uint netId, byte index, uint method)
    {
        if (!NetworkTestScene.Current.Entries.TryGetNet(netId, out NetworkObject? entry) ||
            index >= entry.Components.Length)
        {
            return (Guid.Empty, method);
        }

        Component component = entry.Components[index];
        return (component.Identifier, NetworkMembers.Of(component).RpcAt(method)?.Id ?? method);
    }

    public bool Has(ushort id) => Received.Any(m => m.Id == id) &&
        (id != Scene || Session.IsHost || !AutoReady || IsReady);

    public byte[] Last(ushort id) => Received.Last(m => m.Id == id).Data;

    public void Send(ushort id, Action<NetWriter> write, NetPeer? to = null, byte channel = 0)
    {
        using NetWriter writer = new();
        write(writer);
        using NetWriter wire = new();
        if (id == RpcToHost || id == RpcFromHost)
        {
            NetReader arguments = new(writer.WrittenSpan);
            if (id == RpcFromHost) arguments.ReadVarUInt32();
            (uint netId, byte index, uint method) = RpcTarget(arguments.ReadGuid(), arguments.ReadUInt32());
            wire.WriteVarUInt32(netId);
            wire.WriteByte(index);
            wire.WriteVarUInt32(method);
            wire.WriteBytesWithLength(arguments.ReadBytes(arguments.Remaining));
        }
        else if (id == Sync)
        {
            NetReader block = new(writer.WrittenSpan);
            uint count = block.ReadVarUInt32();
            (Guid, uint, uint, byte[])[] values = new (Guid, uint, uint, byte[])[count];
            for (uint i = 0; i < count; i++)
            {
                Guid componentId = block.ReadGuid();
                uint slot = block.ReadVarUInt32();
                uint generation = NetworkTestScene.Current.FindByIdentifier(componentId) is Component part
                    ? NetworkTestScene.Current.GenerationOf(part.GameObject) : 0;
                values[i] = (componentId, slot, generation, block.ReadBytesWithLength().ToArray());
            }
            WriteStateRecords(wire, values);
        }
        else wire.WriteBytes(writer.WrittenSpan);
        Session.SendRaw(to ?? Session.HostPeer!, id, wire.WrittenSpan, channel: channel);
        Session.Flush();
    }

    public void SendSync(Guid component, uint slot, int value) => Send(Sync, w =>
    {
        w.WriteVarUInt32(1);
        w.WriteGuid(component);
        w.WriteVarUInt32(slot);
        using NetWriter v = new();
        NetSerializer<int>.Write(v, value);
        w.WriteBytesWithLength(v.WrittenSpan);
    });

    public void Dispose() => Session.Dispose();
}

public partial class NetworkingTests : RuntimeTestBase
{
    private static GameObject Find(Scene scene, Guid id) => scene.Objects.Single(o => o.Identifier == id);

    /// <summary>The object a Spawn message is for.</summary>
    private static Guid SpawnedId(byte[] spawn)
    {
        NetReader reader = new(spawn);
        return reader.ReadGuid();
    }

    /// <summary>
    ///     The [Sync] values a Spawn message carries, as (component, slot, value bytes), read with the engine side's own
    ///     object, which says what each block and bit is. Empty when it no longer has the object.
    /// </summary>
    private static List<(Guid Component, uint Slot, byte[] Value)> SpawnValues(byte[] spawn)
    {
        List<(Guid, uint, byte[])> values = new();
        NetReader reader = new(spawn);
        SpawnMessage message = SpawnMessage.Read(ref reader);
        Guid id = message.Identifier;
        if (!NetworkTestScene.Current.Entries.TryGetValue(id, out NetworkObject? entry)) return values;

        // The pose of each child a transform moves comes ahead of the state.
        int transforms = reader.ReadByte();
        for (int t = 0; t < transforms; t++)
        {
            if (reader.ReadBool()) reader.ReadBytes(40);
        }

        int count = reader.ReadByte();
        for (int c = 0; c < count && c < entry.Components.Length; c++)
        {
            ReadOnlySpan<byte> bytes = reader.ReadBytesWithLength();
            Component component = entry.Components[c];
            NetworkMembers members = NetworkMembers.Of(component);
            NetReader block = new(bytes);
            ulong bits = block.ReadVarUInt64();
            while (bits != 0)
            {
                int m = System.Numerics.BitOperations.TrailingZeroCount(bits);
                bits &= bits - 1;
                SyncMember sync = members.SyncAt(m)!;
                int start = block.Position;
                sync.Validate(component, ref block, true);
                values.Add((component.Identifier, sync.Slot, bytes[start..block.Position].ToArray()));
            }
        }

        return values;
    }

    /// <summary>The networked objects <paramref name="peer" /> was spawned so far, in order.</summary>
    private static List<Guid> SpawnedIds(FakePeer peer) =>
        [.. peer.WireReceived.Where(m => m.Id == FakePeer.Spawn).Select(m => SpawnedId(m.Data))];

    private static GameObject Find(Scene scene, string name) => scene.Objects.Single(o => o.Name == name);

    /// <summary>The Spawn messages <paramref name="peer" /> received so far, as sent.</summary>
    private static byte[][] RecordedSpawns(FakePeer peer) =>
        [.. peer.WireReceived.Where(m => m.Id == FakePeer.Spawn).Select(m => m.Data)];

    /// <summary>The scene message and spawns the real host running on <paramref name="network" /> sends a joining client.</summary>
    private static (byte[] Scene, byte[][] Spawns) RecordJoin(LocalNetwork network)
    {
        using FakePeer recorder = FakePeer.Join(network, "recorder");
        Pump(() => recorder.IsReady, recorder);
        return (recorder.WireReceived.First(m => m.Id == FakePeer.Scene).Data, RecordedSpawns(recorder));
    }

    /// <summary>The generation a scene message names.</summary>
    private static ulong SceneGenerationOf(byte[] scene)
    {
        NetReader reader = new(scene);
        reader.ReadByte();
        return reader.ReadUInt64();
    }

    /// <summary>Makes a fake host send every joining client this scene message, then these spawns once it is ready.</summary>
    private static void SendJoin(FakePeer host, byte[] scene, params byte[][] spawns)
    {
        host.JoinSpawns.AddRange(spawns);
        host.Session.PeerJoined += peer => host.Send(FakePeer.Scene, w => w.WriteBytes(scene), peer);
    }

    /// <summary>Runs frames, the way the game loop does, until <paramref name="condition"/> holds.</summary>
    private static void Pump(Func<bool> condition, params FakePeer[] others)
    {
        for (int i = 0; i < 1000 && !condition(); i++)
            Frame(others);
        Assert.True(condition(), "The expected network state never arrived.");
    }

    private static void PumpFor(int milliseconds, params FakePeer[] others)
    {
        DateTime end = DateTime.UtcNow.AddMilliseconds(milliseconds);
        while (DateTime.UtcNow < end)
            Frame(others);
    }

    private static void Frame(FakePeer[] others)
    {
        Networking.Networking.Receive();
        Networking.Networking.Flush();
        foreach (FakePeer other in others)
            other.Session.Update();
        Scene.ProcessPendingLoad();
        EngineObject.ProcessDestroyed();
        Thread.Sleep(1);
    }

    private void MakeCurrent(Scene scene)
    {
        Bind(scene);
    }

    // --- Scene sharing ---------------------------------------------------------------------------

    [Fact]
    public void NetworkModeIsOnlyStoredWhenSet()
    {
        GameObject go = CreateGameObject();
        EchoObject plain = Serializer.Serialize(go);
        Assert.False(plain.TryGet("Network", out _));

        go.Network.Networked = true;
        go.Network.Orphaned = NetworkOrphaned.ClearOwner;
        EchoObject stored = Serializer.Serialize(go);
        Assert.True(stored["Network"]["Networked"].BoolValue);
        Assert.False(stored["Network"].TryGet("Mode", out _));

        GameObject copy = Serializer.Deserialize<GameObject>(stored)!;
        Assert.True(copy.Network.Networked);
        Assert.Equal(NetworkOrphaned.ClearOwner, copy.Network.Orphaned);
        copy.Dispose();
    }

    // --- Host side -------------------------------------------------------------------------------

    [Fact]
    public void HostSpawnsAndFollowsClientOwnedObjects()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);

        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.Scene), alice);
            NetPeer aliceHere = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);

            GameObject ship = CreateGameObject("Ship");
            ship.AddComponent<NetworkTransform>();
            NetTestComponent component = ship.AddComponent<NetTestComponent>().OwnerWritten();
            NetCollectionTestComponent hostSide = ship.AddComponent<NetCollectionTestComponent>();
            scene.Add(ship);
            ship.SpawnBuilt(aliceHere);

            Assert.True(ship.Network.Networked);
            Assert.Same(aliceHere, ship.Network.Owner);
            Assert.True(ship.IsProxy && component.IsProxy);

            // Alice drives it, so the host's own changes are ignored, except for what only the host may set.
            component.Health = 1;
            hostSide.Health = 5;
            Assert.Equal(100, component.Health);
            Assert.Equal(5, hostSide.Health);

            Pump(() => alice.Has(FakePeer.Spawn) && alice.SyncInt(hostSide.Identifier, NetCollectionTestComponent.HealthSlot) == 5, alice);
            NetReader spawn = new(alice.Last(FakePeer.Spawn));
            Assert.Equal(ship.Identifier, spawn.ReadGuid());

            // Alice's changes land; she can't touch the host-only one.
            alice.SendSync(component.Identifier, NetTestComponent.HealthSlot, 42);
            alice.SendSync(hostSide.Identifier, NetCollectionTestComponent.HealthSlot, 99);
            Pump(() => component.Health == 42, alice);
            PumpFor(30, alice);
            Assert.Equal(5, hostSide.Health);

            // Her transform updates move it on the host, smoothed.
            alice.Send(FakePeer.Transform, w => WriteTransforms(w, 1, 1,
                (NetworkTestScene.Current.NetIdOf(ship), NetworkTestScene.Current.GenerationOf(ship), new Float3(3, 4, 5),
                    Quaternion.Identity, Float3.One)));
            Pump(() => ship.Transform.LocalPosition.Equals(new Float3(3, 4, 5)), alice);

            // Her host RPC runs here; the host's broadcast runs here and goes to her.
            alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(component.Identifier); w.WriteUInt32(NetTestComponent.AskHostId); w.WriteVarInt32(3); });
            Pump(() => component.Asked.Count == 1, alice);
            Assert.Equal(3, component.Asked[0]);

            component.Shout("from host");
            Assert.Equal("from host", component.Shouts[0].Text);
            Pump(() => alice.Has(FakePeer.RpcFromHost), alice);

            // When she leaves, what she owned goes with her.
            alice.Session.Shutdown();
            Pump(() => !ship.IsValid(), alice);
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Fact]
    public void HostOwnedObjectsReplicateToClients()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);

        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.Scene), alice);

            GameObject crate = CreateGameObject("Crate");
            crate.AddComponent<NetworkTransform>();
            NetTestComponent component = crate.AddComponent<NetTestComponent>();
            scene.Add(crate);
            crate.SpawnBuilt();
            Assert.False(crate.IsProxy);
            Assert.Null(crate.Network.Owner);

            component.Health = 7;
            crate.Transform.LocalPosition = new Float3(1, 2, 3);
            // Both travel in snapshots.
            Pump(() => alice.SyncInt(component.Identifier, NetTestComponent.HealthSlot) == 7 &&
                       TransformIds(alice).Contains(NetworkTestScene.Current.NetIdOf(crate)), alice);

            // A client can't change what it doesn't own, nor call host-only things through a broadcast.
            alice.SendSync(component.Identifier, NetTestComponent.HealthSlot, 1);
            alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(component.Identifier); w.WriteUInt32(NetTestComponent.AskHostId); w.WriteVarInt32(3); });
            Pump(() => component.Asked.Count == 1, alice);
            Assert.Equal(7, component.Health);
            Assert.Equal(3, component.Asked[0]);

            crate.Destroy();
            Pump(() => alice.Has(FakePeer.Despawn), alice);
            NetReader despawn = new(alice.Last(FakePeer.Despawn));
            Assert.Equal(crate.Identifier, despawn.ReadGuid());
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Fact]
    public void SpawningInsideANetworkedObjectThrowsAndChangesNothing()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);

        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        try
        {
            GameObject parent = CreateGameObject("Parent");
            GameObject child = CreateGameObject("Child");
            child.SetParent(parent);
            scene.Add(parent);
            parent.SpawnBuilt();

            Assert.Throws<InvalidOperationException>(() => child.SpawnBuilt(orphaned: NetworkOrphaned.ClearOwner));
            Assert.False(child.Network.Networked);
            Assert.Equal(NetworkOrphaned.Destroy, child.Network.Orphaned);
            Assert.Same(parent, child.Network.Root);
        }
        finally
        {
            Networking.Networking.Shutdown();
        }
    }

    [Fact]
    public void AnRpcThatThrowsNeitherCrashesTheHostNorDropsTheCaller()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);

        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.Has(FakePeer.Scene), alice);
            GameObject crate = CreateGameObject("Crate");
            NetTestComponent component = crate.AddComponent<NetTestComponent>();
            scene.Add(crate);
            crate.SpawnBuilt();

            alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(component.Identifier); w.WriteUInt32(NetTestComponent.ExplodeId); });
            alice.Send(FakePeer.RpcToHost, w => { w.WriteGuid(component.Identifier); w.WriteUInt32(NetTestComponent.AskHostId); w.WriteVarInt32(3); });
            Pump(() => component.Asked.Count == 1, alice);
            Assert.Equal(1, Networking.Networking.Session!.Peers.Count(p => !p.IsLocal));
        }
        finally
        {
            Networking.Networking.Shutdown();
            alice.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedRpcsAreRejectedBeforeRunning(bool trailingBytes)
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        using FakePeer bob = FakePeer.Join(network, "bob");
        try
        {
            Pump(() => alice.Has(FakePeer.Scene) && bob.Has(FakePeer.Scene), alice, bob);
            GameObject crate = CreateGameObject("Crate");
            NetTestComponent component = crate.AddComponent<NetTestComponent>();
            scene.Add(crate);
            crate.SpawnBuilt(Networking.Networking.Session!.Peers.Single(p => p.Name == "bob"));
            alice.Send(FakePeer.RpcToHost, w =>
            {
                w.WriteGuid(component.Identifier);
                w.WriteUInt32(NetTestComponent.AskHostId);
                if (trailingBytes) { w.WriteVarInt32(3); w.WriteByte(42); }
            });
            for (int i = 0; i < 10; i++) Frame([alice, bob]);
            Assert.False(bob.Has(FakePeer.RpcFromHost));
            Assert.Empty(component.Asked);
            Assert.Equal(SessionState.Ended, alice.Session.State);
            Assert.Equal(SessionState.Active, bob.Session.State);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    private GameObject JoinTransformProxy(out FakePeer host, out NetPeer client)
    {
        Scene scene = CreateScene();
        using NetWriter snapshot = new();
        NetworkTestScene.Current.WriteScene(scene, snapshot);
        GameObject original = CreateGameObject("Proxy");
        original.Network.Networked = true;
        original.AddComponent<NetworkTransform>();
        using NetWriter spawn = new();
        NetworkTestScene.Current.WriteSpawn(original, -1, NetworkOrphaned.ClearOwner, spawn);
        LocalNetwork network = new();
        host = FakePeer.Host(network);
        FakePeer remote = host;
        host.Session.PeerJoined += peer =>
        {
            remote.Session.SendRaw(peer, FakePeer.Scene, snapshot.WrittenSpan);
            remote.Session.SendRaw(peer, FakePeer.Spawn, spawn.WrittenSpan);
        };
        Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
        Pump(() => Networking.Networking.IsReady && Scene.Main.Objects.Any(o => o.Identifier == original.Identifier), remote);
        client = host.Session.Peers.Single(p => !p.IsLocal);
        return Find(Scene.Main, original.Identifier);
    }

    private static void SendProxyTransform(FakePeer host, NetPeer client, Guid id, uint tick, float position, uint generation = 0)
    {
        uint netId = NetworkTestScene.Current.NetIdOf(Find(Scene.Main, id));
        host.Send(FakePeer.Transform, w => WriteTransforms(w, tick, tick / 30.0,
            (netId, generation, new Float3(position, 0, 0), Quaternion.Identity, Float3.One)), client);
    }

    // Transform channel bits, as Networking.Transform.cs defines them.
    private const byte PositionChannel = 1, RotationChannel = 2, ScaleChannel = 4, ExactChannel = 8, ResumedChannel = 16, TeleportChannel = 32,
        IndexedChannel = 64;

    /// <summary>A NetworkTransform on the object, with the channels it syncs.</summary>
    private static NetworkTransform SyncTransform(GameObject go, bool position = true, bool rotation = true, bool scale = true,
        bool interpolate = true)
    {
        NetworkTransform transform = go.AddComponent<NetworkTransform>();
        transform.SyncPosition = position;
        transform.SyncRotation = rotation;
        transform.SyncScale = scale;
        transform.Interpolate = interpolate;
        return transform;
    }

    /// <summary>A Transform holding one transform group, at one tick and time, of a full, exact pose for each object.</summary>
    private static void WriteTransforms(NetWriter w, uint tick, double time,
        params (uint NetId, uint Generation, Float3 Position, Quaternion Rotation, Float3 Scale)[] records) =>
        WriteIndexedTransforms(w, tick, time, 0, records);

    /// <summary>The same, for the transform at an index of each object.</summary>
    private static void WriteIndexedTransforms(NetWriter w, uint tick, double time, byte index,
        params (uint NetId, uint Generation, Float3 Position, Quaternion Rotation, Float3 Scale)[] records)
    {
        w.WriteVarUInt32(0); w.WriteVarUInt32((uint)NetworkTestScene.Current.SceneGeneration); w.WriteVarUInt32(tick);
        w.WriteDouble(time); w.WriteVarUInt32((uint)records.Length);
        foreach (var record in records)
        {
            w.WriteVarUInt32(record.NetId); w.WriteVarUInt32(record.Generation);
            w.WriteByte((byte)(PositionChannel | RotationChannel | ScaleChannel | ExactChannel | (index != 0 ? IndexedChannel : 0)));
            if (index != 0) w.WriteByte(index);
            NetSerializers.WriteFloat3(w, record.Position);
            NetSerializers.WriteQuaternion(w, record.Rotation);
            NetSerializers.WriteFloat3(w, record.Scale);
        }
    }

    internal readonly record struct TransformRecord(uint NetId, uint Generation, byte Channels, Float3? Position,
        Quaternion? Rotation, Float3? Scale, uint BaselineAge = 0, byte Index = 0);

    internal static readonly System.Runtime.CompilerServices.ConditionalWeakTable<byte[], List<TransformRecord>> Decoded = new();

    /// <summary>
    ///     Decodes a Transform message's transform records, from every group; a channel left out is null. A delta
    ///     position resolves only for a message a FakePeer received, against the positions it had.
    /// </summary>
    private static List<TransformRecord> ReadTransforms(byte[] message) =>
        Decoded.TryGetValue(message, out List<TransformRecord>? decoded) ? decoded : DecodeTransforms(message, null);

    internal static List<TransformRecord> DecodeTransforms(byte[] message,
        Dictionary<(uint NetId, uint Generation, byte Index), Dictionary<uint, (long X, long Y, long Z)>>? positions)
    {
        const double step = 0.01;
        NetReader r = new(message);
        r.ReadVarUInt32(); r.ReadVarUInt32();
        uint tick = r.ReadVarUInt32();
        List<TransformRecord> records = new();
        r.ReadDouble();
        {
            uint count = r.ReadVarUInt32();
            for (uint i = 0; i < count; i++)
            {
                uint netId = r.ReadVarUInt32(), generation = r.ReadVarUInt32();
                byte channels = r.ReadByte();
                byte index = (channels & IndexedChannel) != 0 ? r.ReadByte() : (byte)0;
                if ((channels & 128) != 0) { uint stamp = r.ReadVarUInt32(); if ((stamp & 1) != 0) r.ReadVarUInt32(); }
                channels = (byte)(channels & ~(IndexedChannel | 128));
                if ((channels & ResumedChannel) != 0) r.ReadSingle();
                if ((channels & TeleportChannel) != 0) r.ReadByte();
                Float3? position = null;
                uint age = 0;
                if ((channels & PositionChannel) != 0 && (channels & ExactChannel) != 0)
                    position = NetSerializers.ReadFloat3(ref r);
                else if ((channels & PositionChannel) != 0)
                {
                    age = r.ReadVarUInt32();
                    (long X, long Y, long Z) grid = (r.ReadVarInt64(), r.ReadVarInt64(), r.ReadVarInt64());
                    Dictionary<uint, (long X, long Y, long Z)>? history = null;
                    if (positions != null && !positions.TryGetValue((netId, generation, index), out history))
                        positions[(netId, generation, index)] = history = new();
                    if (age != 0 && history != null && history.TryGetValue(tick - age, out var baseline))
                        grid = (baseline.X + grid.X, baseline.Y + grid.Y, baseline.Z + grid.Z);
                    if (age == 0 || history != null)
                    {
                        history?.TryAdd(tick, grid);
                        position = new Float3((float)(grid.X * step), (float)(grid.Y * step), (float)(grid.Z * step));
                    }
                }

                Quaternion? rotation = (channels & RotationChannel) == 0 ? null :
                    (channels & ExactChannel) != 0 ? NetSerializers.ReadQuaternion(ref r) : NetSerializers.ReadCompactQuaternion(ref r);
                Float3? scale = (channels & ScaleChannel) != 0 ? NetSerializers.ReadFloat3(ref r) : null;
                records.Add(new TransformRecord(netId, generation, channels, position, rotation, scale, age, index));
            }
        }

        return records;
    }

    [Fact]
    public void DelayedTransformsCannotCrossOwnershipChanges()
    {
        GameObject proxy = JoinTransformProxy(out FakePeer host, out NetPeer client);
        TimeSpan delay = Networking.Networking.InterpolationDelay;
        using (host)
        try
        {
            Networking.Networking.InterpolationDelay = TimeSpan.Zero;
            host.Send(FakePeer.Owner, w => { w.WriteGuid(proxy.Identifier); w.WriteVarUInt32(3); w.WriteUInt32(1); }, client);
            SendProxyTransform(host, client, proxy.Identifier, 100, 100, generation: 1);
            Frame([host]);
            host.Send(FakePeer.Owner, w => { w.WriteGuid(proxy.Identifier); w.WriteVarUInt32(4); w.WriteUInt32(2); }, client);
            // The previous owner's packet arrives after the reliable ownership change.
            SendProxyTransform(host, client, proxy.Identifier, 101, 999, generation: 1);
            SendProxyTransform(host, client, proxy.Identifier, 1, 5, generation: 2);
            Frame([host]);
            Assert.Equal(5f, proxy.Transform.LocalPosition.X);
        }
        finally
        {
            Networking.Networking.InterpolationDelay = delay;
            Networking.Networking.Shutdown();
        }
    }

    [Fact]
    public void HostRejectsAnOldGenerationWhenOwnershipReturnsToTheSamePeer()
    {
        Scene scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        TimeSpan delay = Networking.Networking.InterpolationDelay;
        try
        {
            Networking.Networking.InterpolationDelay = TimeSpan.Zero;
            Pump(() => alice.Has(FakePeer.Scene), alice);
            GameObject ship = CreateGameObject("Ship");
            ship.AddComponent<NetworkTransform>();
            scene.Add(ship);
            NetPeer owner = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            ship.SpawnBuilt(owner);
            Pump(() => alice.Has(FakePeer.Spawn), alice);
            NetReader spawn = new(alice.Last(FakePeer.Spawn));
            spawn.ReadGuid(); spawn.ReadGuid(); spawn.ReadVarUInt32(); spawn.ReadVarUInt32(); spawn.ReadByte();
            uint oldGeneration = spawn.ReadUInt32();
            ship.Network.AssignOwnership(null);
            ship.Network.AssignOwnership(owner);
            Pump(() => alice.Has(FakePeer.Owner), alice);
            NetReader change = new(alice.Last(FakePeer.Owner));
            change.ReadGuid(); change.ReadVarUInt32();
            uint generation = change.ReadUInt32();
            Assert.NotEqual(oldGeneration, generation);
            SendProxyTransform(alice, alice.Session.HostPeer!, ship.Identifier, 500, 999, oldGeneration);
            SendProxyTransform(alice, alice.Session.HostPeer!, ship.Identifier, 1, 5, generation);
            Frame([alice]);
            Assert.Equal(5f, ship.Transform.LocalPosition.X);
        }
        finally
        {
            Networking.Networking.InterpolationDelay = delay;
            Networking.Networking.Shutdown();
        }
    }

    [Fact]
    public void BunchedTransformsKeepTheirSenderSpacing()
    {
        GameObject proxy = JoinTransformProxy(out FakePeer host, out NetPeer client);
        using (host)
        try
        {
            SendProxyTransform(host, client, proxy.Identifier, 1, 1);
            SendProxyTransform(host, client, proxy.Identifier, 2, 2);
            SendProxyTransform(host, client, proxy.Identifier, 3, 3);
            // Receive all three at once, before interpolation can discard samples.
            Networking.Networking.Receive();
            double[] times = Networking.Networking.System!.FindEntry(proxy)!
                .RootTransform!.Sync.Samples.Select(sample => sample.Time).ToArray();
            Assert.Equal(3, times.Length);
            Assert.InRange(times[2] - times[0], 0.06, 0.07);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Theory]
    [InlineData(float.NaN, false)]
    [InlineData(float.PositiveInfinity, false)]
    [InlineData(0, true)]
    public void InvalidTransformsNeverReachTheScene(float position, bool zeroRotation)
    {
        GameObject proxy = JoinTransformProxy(out FakePeer host, out NetPeer client);
        using (host)
        try
        {
            host.Send(FakePeer.Transform, w => WriteTransforms(w, 1, 1,
                (NetworkTestScene.Current.NetIdOf(proxy), 0, new Float3(position, 0, 0),
                    zeroRotation ? new Quaternion(0, 0, 0, 0) : Quaternion.Identity, Float3.One)), client);
            Frame([host]);
            Assert.False(Networking.Networking.IsActive);
            Assert.True(float.IsFinite(proxy.Transform.LocalPosition.X));
            Assert.Equal(Quaternion.Identity, proxy.Transform.LocalRotation);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    // --- Client side -----------------------------------------------------------------------------

    [Fact]
    public void ClientLoadsTheHostSceneSpawnsAndControlsWhatItOwns()
    {
        // What a host would send, made with the host side's own writers.
        Scene hostScene = CreateScene();
        GameObject level = CreateGameObject("Level");
        level.AddComponent<NetTestComponent>().Health = 3;
        level.Network.Networked = true;
        hostScene.Add(level);
        using NetWriter snapshot = new();
        NetworkTestScene.Current.WriteScene(hostScene, snapshot);
        using NetWriter levelSpawn = new();
        NetworkTestScene.Current.WriteSpawn(level, owner: -1, NetworkOrphaned.Destroy, levelSpawn);

        GameObject ship = CreateGameObject("Ship");
        ship.AddComponent<NetworkTransform>();
        ship.AddComponent<NetTestComponent>().OwnerWritten().Health = 9;
        ship.Network.Networked = true;
        using NetWriter shipSpawn = new();
        NetworkTestScene.Current.WriteSpawn(ship, owner: 1, NetworkOrphaned.Destroy, shipSpawn); // the first client is peer 1

        GameObject crate = CreateGameObject("Crate");
        crate.AddComponent<NetworkTransform>();
        crate.Network.Networked = true;
        using NetWriter crateSpawn = new();
        NetworkTestScene.Current.WriteSpawn(crate, owner: -1, NetworkOrphaned.Destroy, crateSpawn);

        Guid levelId = level.Identifier, shipId = ship.Identifier, crateId = crate.Identifier;

        LocalNetwork network = new();
        FakePeer host = FakePeer.Host(network);
        SendJoin(host, snapshot.ToArray(), levelSpawn.ToArray(), shipSpawn.ToArray(), crateSpawn.ToArray());

        Networking.Networking.Join(new LocalTransport(network, "bob"), "host", "bob");
        try
        {
            Pump(() => Networking.Networking.IsReady && Scene.Main.Objects.Any(o => o.Identifier == crateId), host);

            // [Sync] values come with the spawns.
            NetTestComponent levelHere = Find(Scene.Main, levelId).GetComponent<NetTestComponent>()!;
            GameObject shipHere = Find(Scene.Main, shipId);
            NetTestComponent shipComponent = shipHere.GetComponent<NetTestComponent>()!;
            GameObject crateHere = Find(Scene.Main, crateId);
            Assert.Equal(3, levelHere.Health);
            Assert.Equal(9, shipComponent.Health);

            Assert.Same(Networking.Networking.Session!.LocalPeer, shipHere.Network.Owner);
            Assert.False(shipHere.IsProxy);
            Assert.True(crateHere.IsProxy);
            Assert.True(levelHere.IsProxy);

            // Ours to change: it goes to the host. The host's: ignored here.
            shipComponent.Health = 10;
            levelHere.Health = 50;
            Assert.Equal(3, levelHere.Health);
            Pump(() => host.SyncInt(shipComponent.Identifier, NetTestComponent.HealthSlot) == 10, host);

            // A host RPC goes to the host instead of running here.
            shipComponent.AskHost(7);
            Assert.Empty(shipComponent.Asked);
            Pump(() => host.Has(FakePeer.RpcToHost), host);

            // The host moves the crate; it follows, smoothed.
            NetPeer bob = host.Session.Peers.Single(p => !p.IsLocal);
            uint crateNetId = NetworkTestScene.Current.NetIdOf(Find(Scene.Main, crateId));
            host.Send(FakePeer.Transform, w => WriteTransforms(w, 1, 1,
                (crateNetId, 0, new Float3(7, 8, 9), Quaternion.Identity, Float3.One)), to: bob);
            Pump(() => crateHere.Transform.LocalPosition.Equals(new Float3(7, 8, 9)), host);

            // And removes it.
            host.Send(FakePeer.Despawn, w => w.WriteGuid(crateId), to: bob);
            Pump(() => !crateHere.IsValid(), host);
        }
        finally
        {
            Networking.Networking.Shutdown();
            host.Dispose();
        }
    }

    // --- Offline and lifecycle -------------------------------------------------------------------

    [Fact]
    public void WithoutASessionNetworkingStaysOutOfTheWay()
    {
        Assert.Null(Networking.Networking.Session);
        Networking.Networking.Receive();
        Networking.Networking.Flush();
        Networking.Networking.Shutdown();

        GameObject go = CreateGameObject();
        NetTestComponent component = go.AddComponent<NetTestComponent>();
        component.Health = 5;
        component.Shout("alone");
        go.SpawnBuilt();

        Assert.Equal(5, component.Health);
        Assert.Equal("alone", component.Shouts.Single().Text);
        Assert.Null(component.Shouts.Single().Caller);
        Assert.False(go.IsProxy);
        Assert.True(go.Network.Networked);
    }

    [Fact]
    public void OnlyOneSessionRunsAtATime()
    {
        Networking.Networking.Host(new LocalTransport(new LocalNetwork()));
        try
        {
            Assert.True(Networking.Networking.IsHost && Networking.Networking.IsActive);
            Assert.Throws<InvalidOperationException>(() => Networking.Networking.Host(new LocalTransport(new LocalNetwork())));
        }
        finally
        {
            Networking.Networking.Shutdown();
        }

        Assert.Null(Networking.Networking.Session);
        Assert.False(Networking.Networking.IsActive);
    }

    [Fact]
    public void ClientsCannotSpawn()
    {
        LocalNetwork network = new();
        FakePeer host = FakePeer.Host(network);
        Networking.Networking.Join(new LocalTransport(network, "bob"), "host", "bob");
        try
        {
            Pump(() => Networking.Networking.IsActive, host);
            GameObject go = CreateGameObject();
            Scene.Main.Add(go);
            Assert.Throws<InvalidOperationException>(() => go.SpawnBuilt());
        }
        finally
        {
            Networking.Networking.Shutdown();
            host.Dispose();
        }
    }

    [Theory]
    [InlineData(typeof(bool))]
    [InlineData(typeof(Float3))]
    [InlineData(typeof(Quaternion))]
    [InlineData(typeof(Color))]
    [InlineData(typeof(NetworkOrphaned))]
    public void SerializerSupportsCommonTypes(Type type)
    {
        bool supported = (bool)typeof(NetSerializer<>).MakeGenericType(type).GetProperty("IsSupported")!.GetValue(null)!;
        Assert.True(supported);
    }

    [Fact]
    public void SerializerRoundTripsValues()
    {
        using NetWriter writer = new();
        NetSerializer<Float3>.Write(writer, new Float3(1, 2, 3));
        NetSerializer<NetworkOrphaned>.Write(writer, NetworkOrphaned.ClearOwner);
        NetSerializer<string>.Write(writer, "hi");
        NetSerializer<long>.Write(writer, -5);

        NetReader reader = new(writer.WrittenSpan);
        Assert.Equal(new Float3(1, 2, 3), NetSerializer<Float3>.Read(ref reader));
        Assert.Equal(NetworkOrphaned.ClearOwner, NetSerializer<NetworkOrphaned>.Read(ref reader));
        Assert.Equal("hi", NetSerializer<string>.Read(ref reader));
        Assert.Equal(-5, NetSerializer<long>.Read(ref reader));
        Assert.False(NetSerializer<Dictionary<int, int>>.IsSupported);
        Assert.Throws<NotSupportedException>(() => NetSerializer<Dictionary<int, int>>.Write(writer, new Dictionary<int, int>()));
    }
}
