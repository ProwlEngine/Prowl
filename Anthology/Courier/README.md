# Prowl.Courier Networking

A dependency free networking library built for the Prowl Game Engine. Courier gives you reliable and unreliable messages over UDP.

## Features

- **Transports**
  - `KcpTransport`: UDP with an embedded KCP for reliable delivery
  - `LocalTransport`: connections inside one process, for the editor, tests and listen servers
  - `ThreadedTransport`: runs any transport on a worker thread
  - Pluggable: write your own by deriving from `Transport`
  - 590+ tests, run against every transport

- **Delivery**
  - Reliable and ordered, per channel
  - Unreliable, fire and forget
  - Unreliable sequenced, where anything older than the newest message is dropped
  - Up to 32 independent channels, so a lost packet on one never holds up another
  - Large reliable messages are fragmented and reassembled for you
  - Large unreliable messages are split and delivered whole or not at all

- **Sessions**
  - Host and client roles with join requests, auth tokens, kicks and goodbyes with reasons
  - Peer list kept in sync on every client, or kept on the host for large sessions
  - Typed messages through `INetMessage`, or raw messages with your own ids
  - Message batching, so many small messages share a packet
  - Loopback: the host sends to itself through the same handlers

- **Security**
  - Optional encryption: an ephemeral P-256 key exchange per connection, AES-256-GCM per packet
  - Optional host signing key that clients pin, to stop a man in the middle
  - Stateless cookie handshake, so a host holds nothing for a client until it proves its address
  - Handshake requests are padded so a host can never amplify traffic
  - Per address and global limits on handshakes and connections
  - Every queue and buffer has a budget, so a misbehaving peer is disconnected before it costs memory

- **Resilience**
  - Sessions survive an address change, such as a phone moving from Wi-Fi to mobile data
  - A restarted host resets clients of its previous run instead of leaving them to time out
  - Host names resolve to every address and the first that answers wins, so IPv6 only networks work
  - Retransmit timeouts are capped, so a link that comes back after an outage recovers quickly

- **Performance**
  - No allocations per message in steady state
  - Optional send rate cap per connection, serving lower channels first
  - Optional socket I/O threads, and the worker thread wrapper
  - Connection stats: packets, bytes, retransmits, drops, round trip time and send queue depth

## Usage

### Installation

```
dotnet add package Prowl.Courier
```

### Hosting and Joining

```csharp
using Prowl.Courier;
using Prowl.Courier.Transports.Kcp;

// On the server
NetSession host = NetSession.Host(new KcpTransport(new() { Port = 7777 }), "Server", new() { AppVersion = 1 });

// On a client
NetSession client = NetSession.Join(new KcpTransport(), "127.0.0.1:7777", "Player", options: new() { AppVersion = 1 });

// Every frame, on both
host.Update();
client.Update();
```

`Update` is `Receive` followed by `Flush`. Call `Receive` before your simulation step and `Flush` after it if you want this frame's sends to go out straight away. Events and message handlers run inside `Receive`, on your thread. Clients with a different `AppVersion` are turned away.

### Messages

```csharp
public struct ChatMessage : INetMessage
{
    public static ushort Id => 1;

    public string Text;

    public void Write(NetWriter writer) => writer.WriteString(Text);
    public void Read(ref NetReader reader) => Text = reader.ReadString();
}

session.On<ChatMessage>((sender, message) => Console.WriteLine($"{sender.Name}: {message.Text}"));

// Client to host
client.SendToHost(new ChatMessage { Text = "Hello" });

// Host to everyone
host.Broadcast(new ChatMessage { Text = "Welcome" });

// Positions: only the newest matters, on a channel of their own
host.Broadcast(position, Delivery.UnreliableSequenced, channel: 1);
```

Message ids below `NetSession.ReservedMessageIds` are yours. Raw messages skip the struct: `SendRaw(peer, id, payload)` and `OnRaw(id, (sender, ref reader, delivery, channel) => ...)`. Small messages are batched into one packet per connection until `Flush`, or sooner with `SendFlags.Immediate`.

### Membership

```csharp
host.JoinRequested += pending =>
{
    if (IsValidTicket(pending.Request.Token.Span))
        pending.Accept();
    else
        pending.Reject("Invalid ticket.");
};

host.PeerJoined += peer => SpawnPlayer(peer);
host.PeerLeft += (peer, reason) => DespawnPlayer(peer);
client.Ended += (reason, message) => ShowDisconnected(reason, message);

host.Kick(peer, "Cheating.");
```

With no `JoinRequested` handler every client that passes the version and capacity checks is accepted. Clients pass their token to `NetSession.Join`. A request can also be resolved later, such as after checking the ticket with a web service.

### Channels

```csharp
host.SendRaw(peer, MapDataId, mapBytes, Delivery.Reliable, channel: 2);
```

Each channel is its own ordered stream, so a big transfer on channel 2 never delays chat on channel 0. Nothing is ordered across channels. Plain unreliable messages always use channel 0. Session messages such as joins and goodbyes travel on channel 0, so keep bulk transfers on other channels.

### Encryption

```csharp
// Server: sign each handshake with a key you keep
KcpTransportOptions serverOptions = new()
{
    Port = 7777,
    Encryption = new KcpEncryption { SigningKey = serverKey, ResetKey = resetSecret }
};

// Client: trust only that server
KcpTransportOptions clientOptions = new()
{
    Encryption = new KcpEncryption { TrustedHostKey = serverKey.ExportSubjectPublicKeyInfo() }
};
```

Encryption is off by default and both ends must agree. Without `TrustedHostKey` traffic is private from anyone who only listens, but someone in the middle could still pose as the server. The `ResetKey` lets a restarted server tell clients of its previous run that their session is gone.

### Worker Thread

```csharp
Transport transport = new ThreadedTransport(new KcpTransport(new() { Port = 7777, UseIoThreads = true }));
NetSession host = NetSession.Host(transport, "Server");
```

Socket reads and writes, retransmits, acknowledgements and encryption move to a worker thread. Your thread uses the session exactly as before, and connections keep acknowledging data even while your thread is in a long frame. With `UseIoThreads` the worker wakes the moment a packet arrives.

`Shutdown(TimeSpan)` gives up with a `TimeoutException` if the worker is stuck in the inner transport. The worker and the inner transport are kept, so you can call it again later.

Sends waiting for the worker have a budget too. `MaxQueuedSendsPerConnection` (4096) and `MaxQueuedSendBytes` (64 MB in all) close a connection with `ResourceLimit` when they are passed. Over the byte budget it's the connection with the most waiting that goes, not whichever sent last.

### Raw Transports

```csharp
using KcpTransport transport = new(new() { Port = 7777 });
transport.Listen();

transport.Receive();
while (transport.Poll(out TransportEvent ev))
{
    switch (ev.Type)
    {
        case TransportEventType.Connected: break;
        case TransportEventType.Data: Handle(ev.Connection, ev.Data.Span); break;
        case TransportEventType.Disconnected: break;
    }
}

connection.Send(data, Delivery.Reliable);
transport.Flush();
```

Event data is only valid until the next `Poll`, so copy it to keep it. To drive a session from your own transport loop, create it with `NetSession.CreateHost` or `CreateClient` and hand it each event with `ProcessEvent`.

### Serialization

```csharp
using NetWriter writer = new();
writer.WriteVarInt32(health);
writer.WriteSingle(position.X);
writer.WriteString(name);

NetReader reader = new(writer.WrittenSpan);
int health = reader.ReadVarInt32();
```

`NetWriter` and `NetReader` cover primitives, varints, strings, GUIDs and byte spans. For richer types, [Prowl.Echo](../Echo) with `[FixedEchoStructure]` makes compact network messages.

## Tuning Tips

For large servers:
1. Raise `KcpTransportOptions.MaxConnections` and `NetSessionOptions.MaxPeers` together, since a host refuses a `MaxPeers` above the transport's limit
2. Turn off `NetSessionOptions.SharePeerList` when clients don't need to know about each other
3. Set `MaxSendRate` so one busy connection can't flood a player's link, and keep bulk data on higher channels
4. Use `ThreadedTransport` with `UseIoThreads` when the game thread can't pump the network every frame
5. Check `Connection.PendingSendBytes` against `MaxPendingSendBytes` and send less to peers that fall behind

Players behind carrier grade NAT can share one address. Sessions mark players who joined as trusted, so `MaxConnectionsPerAddress` only limits connections that haven't joined yet.

On a server with several addresses, set `BindAddress` to the one clients connect to.

## Limitations

- One host per session, with no host migration
- Scene replication and interest management, deciding who receives which updates, belong to the engine
- Encryption needs a pinned `TrustedHostKey` to rule out a man in the middle
- Both ends must use the same channel count, MTU and receive window

## License

This component is part of the Prowl Game Engine and is licensed under the MIT License. See the LICENSE file in the project root for details.
