using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>
///     What the script compiler generates for <c>[Rpc.Target] public void Whisper(NetPeer target, string text)</c> and
///     <c>[Rpc.Broadcast(RpcFlags.ExcludeOwner)] public void Announce(string text)</c>.
/// </summary>
public class WhisperTestComponent : Component
{
    public const uint WhisperId = 961, AnnounceId = 962;
    public readonly List<(NetPeer? Target, string Text)> Heard = new();

    private static readonly RpcMember<WhisperTestComponent> __rpc_961 = new(WhisperId, "Whisper(NetPeer,string)", RpcMode.Target, RpcFlags.None,
        static (WhisperTestComponent s, ref NetReader r, bool execute) => { string a0 = NetSerializer<string>.Read(ref r); if (!execute) return; RpcMember.ArgumentsRead(); s.Whisper(Networking.Networking.Session!.LocalPeer!, a0); });

    public void Whisper(NetPeer target, string text)
    {
        if (__rpc_961.Begin(this, out NetWriter w)) { NetSerializer<string>.Write(w, text); if (!__rpc_961.Send(this, w, target)) return; }
        Heard.Add((target, text));
    }

    private static readonly RpcMember<WhisperTestComponent> __rpc_962 = new(AnnounceId, "Announce(string)", RpcMode.Broadcast, RpcFlags.ExcludeOwner,
        static (WhisperTestComponent s, ref NetReader r, bool execute) => { string a0 = NetSerializer<string>.Read(ref r); if (!execute) return; RpcMember.ArgumentsRead(); s.Announce(a0); });

    public void Announce(string text)
    {
        if (__rpc_962.Begin(this, out NetWriter w)) { NetSerializer<string>.Write(w, text); if (!__rpc_962.Send(this, w)) return; }
        Heard.Add((null, text));
    }
}

public partial class NetworkingTests
{
    private static int RpcsFromHost(FakePeer peer, uint method) =>
        peer.Received.Count(m => m.Id == FakePeer.RpcFromHost && MethodOf(m.Data) == method);

    private static uint MethodOf(byte[] data)
    {
        NetReader r = new(data);
        r.ReadVarUInt32();
        r.ReadGuid();
        return r.ReadUInt32();
    }

    [Fact]
    public void ATargetRpcGoesNowhereTheRecipientFiltersLeaveOut()
    {
        Scene scene = CreateScene();
        GameObject go = CreateGameObject("Speaker");
        WhisperTestComponent speaker = go.AddComponent<WhisperTestComponent>();
        scene.Add(go);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            NetPeer aliceHere = Networking.Networking.Session!.Peers.Single(p => p.Name == "alice");
            go.SpawnBuilt();
            Pump(() => alice.Has(FakePeer.Spawn), alice);

            using (Rpc.FilterInclude(_ => false))
            {
                speaker.Whisper(aliceHere, "filtered");
                speaker.Whisper(Networking.Networking.Session!.LocalPeer!, "filtered here");
            }

            PumpFor(30, alice);
            Assert.Equal(0, RpcsFromHost(alice, WhisperTestComponent.WhisperId));
            Assert.Empty(speaker.Heard);

            // Left in by the filter, it goes as usual.
            using (Rpc.FilterInclude(p => p == aliceHere))
            {
                speaker.Whisper(aliceHere, "allowed");
            }

            Pump(() => RpcsFromHost(alice, WhisperTestComponent.WhisperId) == 1, alice);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void ATargetRpcReachesOnlyItsPeerAndExcludeOwnerSkipsTheOwner()
    {
        Scene scene = CreateScene();
        GameObject go = CreateGameObject("Speaker");
        WhisperTestComponent speaker = go.AddComponent<WhisperTestComponent>();
        scene.Add(go);
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice"), bob = FakePeer.Join(network, "bob");
        try
        {
            Pump(() => alice.IsReady && bob.IsReady, alice, bob);
            NetPeer aliceHere = Networking.Networking.Session!.Peers.Single(p => p.Name == "alice");
            go.SpawnBuilt(aliceHere);
            Pump(() => alice.Has(FakePeer.Spawn) && bob.Has(FakePeer.Spawn), alice, bob);

            speaker.Whisper(aliceHere, "psst");
            speaker.Announce("hello");
            Pump(() => RpcsFromHost(alice, WhisperTestComponent.WhisperId) == 1 &&
                       RpcsFromHost(bob, WhisperTestComponent.AnnounceId) == 1, alice, bob);
            PumpFor(5, alice, bob);
            Assert.Equal(0, RpcsFromHost(bob, WhisperTestComponent.WhisperId));
            Assert.Equal(0, RpcsFromHost(alice, WhisperTestComponent.AnnounceId));
            Assert.Equal([(null, "hello")], speaker.Heard);

            speaker.Whisper(Networking.Networking.Session!.LocalPeer!, "to myself");
            Assert.Contains(speaker.Heard, h => h.Text == "to myself");
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
