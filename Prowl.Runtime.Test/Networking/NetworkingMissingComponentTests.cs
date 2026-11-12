using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Courier;
using Prowl.Courier.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void AComponentGivenUpOnIsSentAgainOnceItsObjectIsHandedOver()
    {
        SceneInstance scene = CreateScene();
        MakeCurrent(scene);
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => alice.IsReady, alice);
            GameObject crate = CreateGameObject("Crate");
            scene.Add(crate);
            crate.Network.Spawn();
            Pump(() => alice.Has(FakePeer.Spawn), alice);

            NetTestComponent late = crate.AddComponent<NetTestComponent>();
            alice.AutoAck = false;
            late.Health = 5;
            int handled = alice.WireReceived.Count;
            DateTime end = DateTime.UtcNow.AddMilliseconds(SnapshotSystem.MissingComponentSeconds * 1000 + 500);
            while (DateTime.UtcNow < end)
            {
                Frame([alice]);
                for (; handled < alice.WireReceived.Count; handled++)
                {
                    if (alice.WireReceived[handled].Id != FakePeer.Snapshot) continue;
                    NetReader reader = new(alice.WireReceived[handled].Data);
                    uint id = reader.ReadVarUInt32();
                    if (id == 0) continue;
                    using NetWriter ack = new();
                    ack.WriteVarUInt32(1); ack.WriteVarUInt32(id);
                    ack.WriteVarUInt32(1); ack.WriteGuid(late.Identifier);
                    ack.WriteVarUInt32(0);
                    alice.Session.SendRaw(alice.Session.HostPeer!, FakePeer.SnapshotAck, ack.WrittenSpan, Delivery.Unreliable);
                }
            }

            alice.AutoAck = true;
            NetPeer aliceHere = Networking.Networking.Session!.Peers.Single(p => !p.IsLocal);
            crate.Network.AssignOwnership(aliceHere);
            Pump(() => alice.Received.Count(m => m.Id == FakePeer.Owner) == 1, alice);
            crate.Network.AssignOwnership(null);
            Pump(() => alice.Received.Count(m => m.Id == FakePeer.Owner) == 2, alice);

            late.Health = 9;
            Pump(() => alice.SnapshotInt(late.Identifier, NetTestComponent.HealthSlot) == 9, alice);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
