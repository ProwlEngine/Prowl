using System.Security.Cryptography;

using Prowl.Runtime.Networking;
using Prowl.Courier;
using Prowl.Courier.Transports.Kcp;
using Prowl.Courier.Transports.Local;

using Xunit;

namespace Prowl.Runtime.Test;

public partial class NetworkingTests
{
    [Fact]
    public void AClientWithAnotherAppVersionIsTurnedAwayAtJoin()
    {
        LocalNetwork network = new();
        Networking.Networking.Host(new LocalTransport(network, "host"));
        using FakePeer stranger = FakePeer.Join(network, "stranger", new NetSessionOptions { AppVersion = 1 });
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
                options: new NetSessionOptions { AppVersion = 0 });
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
