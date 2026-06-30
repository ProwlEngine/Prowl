using System.Numerics;
using Prowl.Wicked.Transport;

namespace Prowl.Wicked.Tests;

public class TestMap : Map { }

public class TestEntity : NetworkEntity
{
    public Vector2 Position { get; set; }
    public bool FacingRight { get; set; } = true;

    public override void PackSpawnData(NetworkWriter writer)
    {
        writer.WriteVector2(Position);
        writer.WriteBool(FacingRight);
    }

    public override void UnpackSpawnData(NetworkReader reader)
    {
        Position = reader.ReadVector2();
        FacingRight = reader.ReadBool();
    }
}

public class ConnectionTests : IDisposable
{
    private readonly InMemoryServerTransport _serverTransport;
    private readonly InMemoryClientTransport _clientTransport;

    public ConnectionTests()
    {
        _serverTransport = new InMemoryServerTransport();
        _clientTransport = new InMemoryClientTransport(_serverTransport);

        Server.Transport = _serverTransport;
        Client.Transport = _clientTransport;
    }

    public void Dispose()
    {
        if (Server.Active) Server.Stop();
        Server.Reset();
        Client.Reset();
    }

    private void Tick(int count = 1)
    {
        for (int i = 0; i < count; i++)
        {
            Server.Tick();
            Client.Tick();
        }
    }

    [Fact]
    public void ServerStarts()
    {
        Server.Start(0);

        Assert.True(Server.Active);
    }

    [Fact]
    public void ClientConnectsToServer()
    {
        Server.Start(0);
        Client.Connect("localhost", 0);

        Tick();

        Assert.True(Client.Active);
        Assert.True(Client.IsConnected);
        Assert.NotNull(Client.LocalClient);
        Assert.Single(Server.Clients);
    }

    [Fact]
    public void ClientReceivesAssignedClientId()
    {
        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();

        var serverClient = Server.Clients.First();
        Assert.NotEqual(0u, Client.LocalClient!.ClientId);
        Assert.Equal(serverClient.ClientId, Client.LocalClient.ClientId);
    }

    [Fact]
    public void ClientReceivesOnConnectedCallback()
    {
        bool connected = false;
        Client.OnConnected += () => connected = true;

        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();

        Assert.True(connected);
    }

    [Fact]
    public void ServerReceivesOnClientConnectedCallback()
    {
        RemoteClient? connectedClient = null;
        Server.OnClientConnected += c => connectedClient = c;

        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();

        Assert.NotNull(connectedClient);
        Assert.IsType<RemoteClient>(connectedClient);
        Assert.True(connectedClient.IsConnected);
    }

    [Fact]
    public void ServerAssignsUniqueClientIds()
    {
        var clientTransport2 = new InMemoryClientTransport(_serverTransport);

        Server.Start(0);

        Client.Connect("localhost", 0);
        Tick();

        var firstClient = Server.Clients.First();

        // Reset client for a second connection
        Client.Reset();
        Client.Transport = clientTransport2;
        Client.Connect("localhost", 0);
        Tick();

        var secondClient = Server.Clients.Last();

        Assert.NotEqual(firstClient.ClientId, secondClient.ClientId);
    }

    [Fact]
    public void ClientDisconnectsCleanly()
    {
        bool disconnected = false;
        Client.OnDisconnected += () => disconnected = true;

        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();

        Assert.True(Client.IsConnected);

        Client.Disconnect();

        Assert.False(Client.Active);
        Assert.False(Client.IsConnected);
        Assert.Null(Client.LocalClient);
        Assert.True(disconnected);
    }

    [Fact]
    public void ServerDetectsClientDisconnect()
    {
        RemoteClient? disconnectedClient = null;
        Server.OnClientDisconnected += c => disconnectedClient = c;

        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();

        Assert.Single(Server.Clients);

        Client.Disconnect();
        Tick(); // Server processes the transport disconnect

        Assert.NotNull(disconnectedClient);
        Assert.Empty(Server.Clients);
    }

    [Fact]
    public void ServerStopDisconnectsClient()
    {
        bool clientDisconnected = false;
        Client.OnDisconnected += () => clientDisconnected = true;

        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();

        Server.Stop();
        Client.Tick(); // Client processes the transport disconnect

        Assert.True(clientDisconnected);
        Assert.False(Client.IsConnected);
    }

    [Fact]
    public void ServerStopFiresOnClientDisconnectedForEachClient()
    {
        var disconnected = new List<RemoteClient>();
        Server.OnClientDisconnected += c => disconnected.Add(c);

        var clientTransport2 = new InMemoryClientTransport(_serverTransport);

        Server.Start(0);

        Client.Connect("localhost", 0);
        Tick();

        Client.Reset();
        Client.Transport = clientTransport2;
        Client.Connect("localhost", 0);
        Tick();

        Assert.Equal(2, Server.Clients.Count);

        Server.Stop();

        Assert.Equal(2, disconnected.Count);
    }

    [Fact]
    public void ResetClearsAllState()
    {
        Server.Start(0);
        Client.Connect("localhost", 0);
        Tick();

        Server.Stop();
        Server.Reset();
        Client.Reset();

        Assert.False(Server.Active);
        Assert.False(Client.Active);
        Assert.False(Client.IsConnected);
        Assert.Null(Client.LocalClient);
        Assert.Empty(Server.Clients);
        Assert.Empty(Server.Maps);

        // Verify internal counters reset: ClientId should start from 1 again
        var transport2 = new InMemoryServerTransport();
        var clientTransport2 = new InMemoryClientTransport(transport2);
        Server.Transport = transport2;
        Client.Transport = clientTransport2;
        Server.Start(0);
        Client.Connect("localhost", 0);
        for (int i = 0; i < 3; i++) { Server.Tick(); Client.Tick(); }

        Assert.Equal(1u, Server.Clients.First().ClientId);
    }
}
