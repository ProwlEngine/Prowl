using Jitter2.Dynamics;

using Prowl.Courier;
using Prowl.Courier.Transports.Local;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    private static PredictedRigidbody.BodyState PredictedState(Float3 position, Quaternion? rotation = null) =>
        new(position, rotation ?? Quaternion.Identity, Float3.Zero, Float3.Zero);

    /// <summary>Moves a body as the simulation would have: a simulated one ignores its Transform until the next step.</summary>
    private static void MoveBody(GameObject go, Float3 position)
    {
        Rigidbody3D body = go.GetComponent<Rigidbody3D>()!;
        if (body.IsSimulated) body.MovePosition(position);
        else go.Transform.Position = position;
    }

    private static (double Time, Float3 Position) ReadPredictedState(byte[] bytes)
    {
        NetReader reader = new(bytes);
        reader.ReadBool();
        return (reader.ReadDouble(), NetSerializers.ReadFloat3(ref reader));
    }

    [Fact]
    public void APredictingClientSnapsToTheHostOnlyPastTheThresholdAndKeepsItsMovementSince()
    {
        GameObject go = CreateGameObject("Predicted");
        go.AddComponent<Rigidbody3D>();
        PredictedRigidbody predicted = go.AddComponent<PredictedRigidbody>();
        predicted.Record(1.0);
        go.Transform.Position = new Float3(1, 0, 0);
        predicted.Record(1.1);
        go.Transform.Position = new Float3(2, 0, 0);
        predicted.Record(1.2);

        // Close to where it was then, and to where it was between two recorded steps: left alone.
        predicted.Correct(1.1, PredictedState(new Float3(1.05f, 0, 0)));
        predicted.Correct(1.05, PredictedState(new Float3(0.5f, 0, 0)));
        Assert.Equal(new Float3(2, 0, 0), go.Transform.Position);
        Assert.Equal(0, predicted.Corrections);

        // Two units off at 1.1: it moves by that, keeping the unit it travelled since.
        predicted.Correct(1.1, PredictedState(new Float3(3, 0, 0)));
        Assert.Equal(new Float3(4, 0, 0), go.Transform.Position);
        Assert.Equal(1, predicted.Corrections);

        // What it remembers moved along, so an older state from the same host path agrees now.
        predicted.Correct(1.0, PredictedState(new Float3(2, 0, 0)));
        Assert.Equal(new Float3(4, 0, 0), go.Transform.Position);
        Assert.Equal(1, predicted.Corrections);

        // A state older than anything remembered is compared with the oldest, a newer one with the newest.
        predicted.Correct(0.2, PredictedState(new Float3(2, 0, 0)));
        predicted.Correct(9, PredictedState(new Float3(4, 0, 0)));
        Assert.Equal(1, predicted.Corrections);

        Quaternion turned = Quaternion.AxisAngle(Float3.UnitY, MathF.PI * 0.5f);
        predicted.Correct(1.2, PredictedState(new Float3(4, 0, 0), Quaternion.AxisAngle(Float3.UnitY, 0.05f)));
        Assert.Equal(1, predicted.Corrections);
        predicted.Correct(1.2, PredictedState(new Float3(4, 0, 0), turned));
        Assert.Equal(2, predicted.Corrections);
        Assert.True(MathF.Abs(Quaternion.Dot(turned, go.Transform.Rotation)) > 0.999f);
    }

    [Fact]
    public void APredictingClientKeepsOnlyASecondOfItsOwnStates()
    {
        GameObject go = CreateGameObject("Predicted");
        go.AddComponent<Rigidbody3D>();
        PredictedRigidbody predicted = go.AddComponent<PredictedRigidbody>();
        for (int i = 0; i <= 30; i++)
        {
            go.Transform.Position = new Float3(i, 0, 0);
            predicted.Record(i * 0.1);
        }

        // Time 0.5 is forgotten, so the host state for it is compared with the oldest kept, at 2.0.
        predicted.Correct(0.5, PredictedState(new Float3(20, 0, 0)));
        Assert.Equal(0, predicted.Corrections);
        predicted.Correct(0.5, PredictedState(new Float3(5, 0, 0)));
        Assert.Equal(1, predicted.Corrections);
        Assert.Equal(new Float3(15, 0, 0), go.Transform.Position);
    }

    [Fact]
    public void TheHostSendsAPredictedBodyWhileItMovesAndOnceASecondAtRest()
    {
        SceneInstance scene = CreateScene();
        GameObject go = CreateGameObject("Predicted");
        go.Network.Networked = true;
        go.AddComponent<Rigidbody3D>().AffectedByGravity = false;
        PredictedRigidbody predicted = go.AddComponent<PredictedRigidbody>();
        predicted.Enabled = false; // Stepped by hand here, at chosen times.
        scene.Add(go); MakeCurrent(scene);
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.IsReady, client);
            int Sent() => client.SnapshotValues().Count(v => v.Component == predicted.Identifier);
            (double Time, Float3 Position) Last() =>
                ReadPredictedState(client.SnapshotValues().Last(v => v.Component == predicted.Identifier).Value);

            predicted.Step(10);
            Pump(() => Sent() == 1, client);
            Assert.Equal((10d, Float3.Zero), Last());

            // At rest nothing more goes until a second has passed.
            predicted.Step(10.5);
            PumpFor(10, client);
            Assert.Equal(1, Sent());

            MoveBody(go, new Float3(3, 0, 0));
            predicted.Step(10.6);
            Pump(() => Sent() == 2, client);
            Assert.Equal((10.6, new Float3(3, 0, 0)), Last());

            predicted.Step(11.5);
            PumpFor(10, client);
            Assert.Equal(2, Sent());
            predicted.Step(11.6);
            Pump(() => Sent() == 3, client);
            Assert.Equal(Delivery.Reliable, client.Deliveries.Last(m => m.Id == FakePeer.Sync).Delivery);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void APredictedBodyStaysDynamicOnPeersThatDoNotControlIt()
    {
        SceneInstance scene = CreateScene();
        GameObject root = CreateGameObject("Predicted");
        root.Network.Networked = true;
        root.AddComponent<NetworkTransform>();
        Rigidbody3D body = root.AddComponent<Rigidbody3D>();
        body.MotionType = MotionType.Dynamic;
        root.AddComponent<PredictedRigidbody>();
        scene.Add(root);
        Bind(scene);
        LocalNetwork network = new();
        try
        {
            Networking.Networking.Host(new LocalTransport(network, "host"));
            using FakePeer client = FakePeer.Join(network, "client");
            Pump(() => client.IsReady, client);
            root.Network.AssignOwnership(Networking.Networking.Session!.Peers.Single(p => !p.IsLocal));
            Assert.True(root.IsProxy);
            Assert.Equal(MotionType.Dynamic, body.MotionType);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void AClientTakesAPredictedBodysSpawnStateAndCorrectsFromLaterOnes()
    {
        SceneInstance scene = CreateScene();
        using NetWriter snapshot = new();
        NetworkTestScene.Current.WriteScene(scene, snapshot);
        GameObject original = CreateGameObject("Predicted");
        original.Network.Networked = true;
        original.AddComponent<Rigidbody3D>().AffectedByGravity = false;
        original.AddComponent<PredictedRigidbody>();
        original.Transform.Position = new Float3(1, 2, 3);
        using NetWriter spawn = new();
        NetworkTestScene.Current.WriteSpawn(original, -1, NetworkOrphaned.ClearOwner, spawn);
        LocalNetwork network = new();
        using FakePeer host = FakePeer.Host(network);
        host.Session.PeerJoined += peer =>
        {
            host.Session.SendRaw(peer, FakePeer.SceneSnapshot, snapshot.WrittenSpan);
            host.Session.SendRaw(peer, FakePeer.Spawn, spawn.WrittenSpan);
        };
        try
        {
            Networking.Networking.Join(new LocalTransport(network, "client"), "host", "client");
            Pump(() => Networking.Networking.IsReady && SceneInstance.Main.Objects.Any(o => o.Identifier == original.Identifier), host);
            NetPeer client = host.Session.Peers.Single(p => !p.IsLocal);
            GameObject proxy = Find(SceneInstance.Main, original.Identifier);
            PredictedRigidbody predicted = proxy.GetComponent<PredictedRigidbody>()!;
            predicted.Enabled = false; // Its states are recorded by hand here, at chosen times.
            Assert.Equal(new Float3(1, 2, 3), proxy.Transform.Position);

            predicted.Record(5.0);
            MoveBody(proxy, new Float3(2, 2, 3));
            predicted.Record(5.1);

            byte[] State(double time, Float3 position)
            {
                using NetWriter w = new();
                w.WriteBool(false);
                w.WriteDouble(time);
                NetSerializers.WriteFloat3(w, position);
                NetSerializers.WriteQuaternion(w, Quaternion.Identity);
                NetSerializers.WriteFloat3(w, Float3.Zero);
                NetSerializers.WriteFloat3(w, Float3.Zero);
                return w.ToArray();
            }

            // The host had it where the client did at 5.0: nothing moves.
            host.SendSnapshotValues(client, 0, 1, (predicted.Identifier, NetworkSerializeMember.CustomSlot, 0, State(5.0, new Float3(1, 2, 3))));
            PumpFor(15, host);
            Assert.Equal(0, predicted.Corrections);

            // Five units higher at 5.0: the client goes there, plus the unit it moved since.
            host.SendSnapshotValues(client, 0, 2, (predicted.Identifier, NetworkSerializeMember.CustomSlot, 0, State(5.0, new Float3(1, 7, 3))));
            Pump(() => predicted.Corrections == 1, host);
            Assert.Equal(new Float3(2, 7, 3), proxy.Transform.Position);
        }
        finally { Networking.Networking.Shutdown(); }
    }
}
