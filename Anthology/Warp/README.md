# Prowl.Warp

Game networking transports for modern C#, with no dependencies.

Warp moves messages between processes. It gives you connections, reliable and unreliable delivery,
and a small polling API that fits a game loop. It deliberately stops there: replication, RPCs and
object ownership belong to the layer above (the Prowl engine), which only ever talks to the
`Transport` / `Connection` abstraction and so works over any transport.

## Transports

| Transport        | Namespace                         | Status | Use it for |
| ---------------- | --------------------------------- | ------ | ---------- |
| `KcpTransport`   | `Prowl.Warp.Transports.Kcp`       | Ready  | Dedicated servers, LAN, anything over plain UDP |
| `LocalTransport` | `Prowl.Warp.Transports.Local`     | Ready  | Host and clients in one process: editor play mode, tests |
| `SteamTransport` | `Prowl.Warp.Transports.Steam`     | Stub   | Steam P2P through Valve's relays (planned) |

`KcpTransport` runs on one UDP socket (dual-stack IPv6 where available) and carries:

- **Reliable** messages over an embedded copy of [kcp2k](https://github.com/MirrorNetworking/kcp2k)'s
  KCP implementation (see `Transports/Kcp/Protocol`): ordered, retransmitted, fragmented and
  reassembled, up to about 290 KB per message by default.
- **Unreliable** messages as single datagrams, up to `Mtu - 5` bytes (1195 by default).

Connections are set up by a stateless cookie handshake, so a host allocates nothing for a client
until the client proves it owns its address, and can't be used to amplify traffic. A random session id
then tags every packet. Keepalive pings measure round trip time and detect dead peers.

`KcpTransportOptions.Simulation` drops and delays outgoing packets for testing on a bad network.

## Usage

```csharp
using Prowl.Warp;
using Prowl.Warp.Transports.Kcp;

// Host
using KcpTransport host = new(new KcpTransportOptions { Port = 7777 });
host.Listen();

// Client
using KcpTransport client = new();
Connection server = client.Connect("127.0.0.1:7777");

// Game loop, for each transport
transport.Receive();                     // read packets
while (transport.Poll(out TransportEvent ev))
{
    switch (ev.Type)
    {
        case TransportEventType.Connected:    /* ev.Connection is ready */ break;
        case TransportEventType.Data:         Handle(ev.Connection, ev.Data.Span); break;
        case TransportEventType.Disconnected: /* ev.Reason says why */ break;
    }
}
// ... simulate, then send:
server.Send(message, Delivery.Reliable);
transport.Flush();                       // send queued data, run timers
```

Rules worth knowing:

- Nothing happens in the background. Transports do all their work inside `Receive`, `Flush`
  and `Poll`, on the calling thread. A transport is not thread safe.
- `TransportEvent.Data` is only valid until the next `Poll`. Copy it to keep it.
- Every connection gets exactly one `Disconnected` event, including when you close it yourself.

`NetWriter` and `NetReader` build and parse messages: little-endian primitives, var-ints, strings
and GUIDs, with every read bounds checked because network data is untrusted.

## Sessions

`NetSession` sits on any transport and turns connections into a game session: a host plus the
clients it let in. The host is peer 0, clients get ids from 1, and every member sees the same peer
list. Clients only talk to the host; the host relays. There is no host migration: when the host goes,
the session ends for everyone.

```csharp
public struct Chat : INetMessage
{
    public static ushort Id => 1;          // yours: 0 to 0xFEFF
    public string Text;
    public void Write(NetWriter w) => w.WriteString(Text);
    public void Read(ref NetReader r) => Text = r.ReadString();
}

NetSession host = NetSession.Host(new KcpTransport(), "Server", new NetSessionOptions { AppVersion = 3 });
host.JoinRequested += pending => pending.Accept();       // optional; can also Reject, or decide later
host.On<Chat>((sender, chat) => host.Broadcast(chat, except: sender));

NetSession client = NetSession.Join(new KcpTransport(), "127.0.0.1:7777", "alice", options: new() { AppVersion = 3 });
client.Joined += () => client.SendToHost(new Chat { Text = "hi" });
client.Ended += (reason, message) => Console.WriteLine($"{reason}: {message}");

// each frame, for each session
session.Update();
```

Joining checks the app version, capacity and your optional approval, with a timeout. The host can
`Kick` with a message; clients learn why they were turned away or removed. Malformed messages get
the sender removed with `ProtocolError`.

## Installation

```bash
dotnet add package Prowl.Warp
```
