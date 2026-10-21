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
| `BatchingTransport` | `Prowl.Warp.Transports.Batching` | Optional | Combine small messages; both ends must use the wrapper |
| `EncryptedTransport` | `Prowl.Warp.Transports.Encryption` | Optional | Authenticated encryption over another transport using a provisioned shared key |
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

Wrap a transport in `BatchingTransport` to group small messages until `Flush`; message boundaries are
preserved. Use `SendFlags.Immediate` on a connection or session send to flush without waiting for the
next regular flush. Reliable ordering still applies. Both ends must agree on whether batching is enabled.

KCP limits each `Receive` call to `MaxDatagramsPerReceive` (1024 by default), including malformed
packets. `MaxPendingReliableBytes` bounds each peer's reliable send backlog (32 MB by default);
exceeding it disconnects the slow peer with `ResourceLimit` instead of growing indefinitely.
Transports also cap unpolled data by event count and pooled buffer bytes per connection, plus a
transport-wide byte limit (`MaxQueuedDataEventsPerConnection`, `MaxQueuedDataBytesPerConnection`,
`MaxQueuedDataBytes`). Receive overflow drops that peer's queued data and disconnects it. Poll
regularly and tune these budgets for your game's largest bursts.

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

## Foundation contracts

- Configure `KcpTransportOptions` and `NetSessionOptions` before construction. Settings are validated
  and copied, including nested reliability/simulation options. Their `Options` getters return detached
  copies; editing those copies does not change a running transport. Queue limits exposed directly on
  transports can be adjusted and must stay positive.
  `NetSessionOptions` is a value type: initialize defaults with `new()`. Reading `session.Options`
  allocates nothing; to edit a detached copy, assign it to a local variable first.
- Admission bounds UTF-8 peer names to 256 bytes and join tokens to 4096 bytes. Admission/version
  checks do not constitute application authentication: validate tokens in `JoinRequested`.
- `MaxReassemblyBytes` caps actual pooled storage for reliable messages (64 MiB by default).
  `ReassemblyTimeout` is an absolute deadline (10 seconds), not extended by keepalives or trickled
  fragments. Pending approvals cannot allocate fragment buffers; pre-admission requests are limited
  to 8 KiB. Completed, rejected, expired, disconnected, and shut-down transfers release their buffers.
- Loopback messages have independent count/byte budgets (`MaxLoopbackMessages`, `MaxLoopbackBytes`).
  Queue exhaustion throws locally; pump `Receive` before sending more. Local transport inboxes are
  bounded before `Receive` runs, and shutdown drains queued payloads and refuses late data.
- `ResourceLimit` distinguishes exhausted budgets from normal departures and timeouts at the session
  layer. The remote side of a transport disconnect may only know the link closed; application-level
  goodbye reasons are best effort during shutdown or packet loss.
- Typed message decoding errors reject malformed data. Exceptions in the typed application handler
  report through `HandlerFailed` without disconnecting the sender. Raw handlers should throw
  `InvalidDataException` or `EndOfStreamException` for malformed input; other exceptions are reported
  as application failures. Failure-notification listeners cannot break the receive loop.
- `Shutdown` allows reuse; `Dispose` is terminal. Dispose sessions/transports to return pooled storage.

## Optional encryption

Plain KCP remains available for LAN/trusted deployments. Encryption is an optional wrapper:

```csharp
using Prowl.Warp.Transports.Encryption;

// Load a cryptographically random 32-byte secret provisioned securely to both peers.
byte[] sharedKey = LoadProvisionedKey();
Transport transport = new EncryptedTransport(new KcpTransport(), sharedKey);
NetSession host = NetSession.Host(transport);
```

`EncryptedTransport` uses .NET cryptography: HMAC-SHA256 authenticates a four-message nonce handshake,
HKDF-SHA256 derives independent directional session keys, and AES-256-GCM encrypts application messages.
Each delivery mode has its own sequence numbers; unreliable traffic has a 64-message replay window.
The underlying transport is not exposed as connected until key confirmation. Invalid reliable ciphertext
ends the connection; invalid unreliable ciphertext and replays are discarded. Keys are copied at
construction and erased from wrapper-owned storage on disposal.

This is pre-shared-key authentication, not account authentication or public-key infrastructure. Provision
keys out of band. A common key embedded in a downloadable game can be extracted by any player and must
not be treated as a trusted player/server identity. The mode has no forward secrecy, does not hide
transport metadata, and does not prevent denial of service against the underlying transport. The wrapper
is covered by tests, not an independent cryptographic audit. Steam transport remains a separate stub.

Mirror's separate encryption middleware and KCP queue protections informed the layering and failure
policies. Warp does not depend on Mirror or its cryptographic implementation.

## Verification

```powershell
dotnet test Tests/Prowl.Warp.Tests.csproj -c Release
dotnet build Warp/Prowl.Warp.csproj

# Longer reconnect/large-transfer churn; each plain/encrypted test runs this many cycles.
$env:WARP_STRESS_ROUNDS = '100'
dotnet test Tests/Prowl.Warp.Tests.csproj -c Release --filter FullyQualifiedName~RobustnessTests
```

The suite includes deterministic reader fuzzing against a big-integer oracle (100,000 inputs), malformed
KCP input fuzzing (50,000 inputs), admission/fragment/parser regressions, queue reclamation, encryption
and replay/tamper tests, lossy reliable transfers, and 32-client slow-peer isolation. These checks are
repeatable regression coverage, not a substitute for deployment-specific WAN and long-duration soak tests.

## Installation

```bash
dotnet add package Prowl.Warp
```
