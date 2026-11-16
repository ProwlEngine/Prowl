using System.Security.Cryptography;

using Prowl.Runtime.Networking;
using Prowl.Courier;
using Prowl.Courier.Transports.Kcp;
using Prowl.Courier.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>What the script compiler generates for <c>[Sync] public float Speed</c> and <c>[Rpc.Host] public void Boost(int)</c>.</summary>
public class SchemaProbe : Component
{
    private float __sync_Speed;
    private static readonly SyncMember<SchemaProbe, float> __syncnet_Speed = new(301, "Speed", SyncFlags.FromHost, static s => s.__sync_Speed, static (s, v) => s.__sync_Speed = v);

    private static readonly RpcMember<SchemaProbe> __rpc_302 = new(302, "Boost(int)", RpcMode.Host, RpcFlags.None,
        static (SchemaProbe s, ref NetReader r, bool execute) => NetSerializer<int>.Read(ref r));
}

public partial class NetworkingTests
{
    [Fact]
    public void TheSchemaListsEachNetworkedMemberWithItsTypes()
    {
        string[] entries = NetworkSchema.Describe(typeof(SchemaProbe)).ToArray();

        Assert.Contains(entries, e => e.Contains("Speed") && e.Contains("301") && e.Contains("System.Single") && e.Contains("FromHost"));
        Assert.Contains(entries, e => e.Contains("Boost(int)") && e.Contains("302") && e.Contains("Host"));
        Assert.Empty(NetworkSchema.Describe(typeof(Component)));
    }

    [Fact]
    public void TheAppVersionSentAtJoinCoversTheSchema()
    {
        Assert.Equal(NetworkSchema.AppVersion(7), NetworkSchema.AppVersion(7));
        Assert.NotEqual(7u, NetworkSchema.AppVersion(7));
        Assert.NotEqual(NetworkSchema.AppVersion(7), NetworkSchema.AppVersion(8));
    }

    [Fact]
    public void AClientWithAnotherSchemaIsTurnedAwayAtJoin()
    {
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer stranger = FakePeer.Join(network, "stranger", new NetSessionOptions { AppVersion = 0 });
        SessionEndReason? ended = null;
        stranger.Session.Ended += (reason, _) => ended = reason;
        try
        {
            Pump(() => ended != null, stranger);
            Assert.Equal(SessionEndReason.VersionMismatch, ended);
        }
        finally { Networking.Networking.Shutdown(); }
    }

    [Fact]
    public void JoinRequestedCanTurnAClientAway()
    {
        LocalNetwork network = new();
        List<string> asked = new();
        Action<PendingJoin> handler = pending =>
        {
            asked.Add(pending.Request.Name);
            pending.Reject("Invite only");
        };
        Networking.Networking.JoinRequested += handler;
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        string? message = null;
        alice.Session.Ended += (_, text) => message = text;
        try
        {
            Pump(() => message != null, alice);
            Assert.Equal(["alice"], asked);
            Assert.Equal("Invite only", message);
        }
        finally
        {
            Networking.Networking.JoinRequested -= handler;
            Networking.Networking.Shutdown();
        }
    }

    [Fact]
    public void JoinRequestedCanLetAClientInLater()
    {
        LocalNetwork network = new();
        PendingJoin? waiting = null;
        Action<PendingJoin> handler = pending => waiting = pending;
        Networking.Networking.JoinRequested += handler;
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer alice = FakePeer.Join(network, "alice");
        try
        {
            Pump(() => waiting != null, alice);
            PumpFor(50, alice);
            Assert.Equal(SessionState.Joining, alice.Session.State);

            waiting!.Accept();
            Pump(() => alice.Session.State == SessionState.Active, alice);
        }
        finally
        {
            Networking.Networking.JoinRequested -= handler;
            Networking.Networking.Shutdown();
        }
    }

    [Fact]
    public void AnEncryptedClientJoinsAHostItTrusts()
    {
        NetworkConfiguration previous = Networking.Networking.Configuration;
        using ECDsa signing = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        NetworkConfiguration config = new()
        {
            Port = 0,
            Execution = NetworkExecution.Inline,
            Encryption = true,
            TrustedHostKey = Convert.ToBase64String(signing.ExportSubjectPublicKeyInfo())
        };
        try
        {
            Networking.Networking.Configure(config);
            Networking.Networking.HostSigningKey = signing;
            Networking.Networking.Host();
            config.Port = Networking.Networking.LocalPort;
            using KcpTransport transport = new(config.CreateKcpOptions());
            using NetSession client = NetSession.Join(transport, "127.0.0.1", "client",
                options: new NetSessionOptions { AppVersion = NetworkSchema.AppVersion(0) });
            for (int i = 0; i < 2000 && client.State != SessionState.Active && client.State != SessionState.Ended; i++)
            {
                Networking.Networking.Receive();
                client.Update();
                Networking.Networking.Flush();
                Thread.Sleep(1);
            }

            Assert.Equal(SessionState.Active, client.State);
        }
        finally
        {
            Networking.Networking.Shutdown();
            Networking.Networking.HostSigningKey = null;
            Networking.Networking.Configure(previous);
        }
    }
}
