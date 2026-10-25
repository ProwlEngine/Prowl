# Prowl.Warp

Dependency-free C# networking for .NET 8, 9 and 10. Provides transports, reliable and unreliable
messages, serialization and host/client sessions. Scene replication belongs to the engine.

```sh
dotnet add package Prowl.Warp
```

## Transports

| Transport           | Purpose                                                      |
|---------------------|--------------------------------------------------------------|
| `KcpTransport`      | Reliable ordered messages and unreliable datagrams over UDP  |
| `LocalTransport`    | Connections within one process                               |

Transports live under `Prowl.Warp.Transports.<Kcp/Local>`.

```csharp
using Prowl.Warp;
using Prowl.Warp.Transports.Kcp;

using KcpTransport transport = new(new() { Port = 7777 });
transport.Listen(); // On a client, use transport.Connect("127.0.0.1:7777") instead.

// Each frame:
transport.Receive();
while (transport.Poll(out TransportEvent ev))
{
    // Handle Connected, Data and Disconnected events.
}
// Send with connection.Send(data, Delivery.Reliable / Delivery.Unreliable).
transport.Flush();
```

- Drive each transport from one thread. KCP resolves hostnames asynchronously.
- Event data is borrowed until the next `Poll` or disposal. Copy it to retain it.
- `GetMaxMessageSize(delivery)` gives the message limit; `GetBatchThreshold(delivery)` gives the
  preferred batch size. `SendFlags.Immediate` submits queued messages without waiting for the next flush.
- `Transport.Capabilities` reports availability, supported deliveries and thread requirements.
- Options are copied at construction. Queue budgets and timeouts bound stalled peers;
  exhausted transport budgets disconnect with `ResourceLimit`.
- Transport `Shutdown` permits reuse; `Dispose` is terminal. Wrappers own their inner transport.

`NetWriter` and `NetReader` serialize primitives, strings, GUIDs and varints.

## Channels

Reliable messages take an optional channel, `0` to `Capabilities.Channels - 1` (KCP and Local default to 4). Each
channel is its own ordered stream; on KCP, its own conversation, so a lost packet on one never holds up another.
Unreliable messages have no order to keep, so they always use channel 0. Channel 0 costs nothing on the wire;
other channels add one byte per reliable packet.

```csharp
session.SendRaw(peer, ChatId, payload, Delivery.Reliable, channel: 1);  // doesn't wait behind channel 0
```

- Nothing is ordered across channels. Session messages (joining, the peer list, goodbyes) use channel 0, so a
  message on another channel may arrive before a `PeerJoined` or after a `PeerLeft` sent around it.
- Both ends must use the same channel count. A KCP host refuses a client with a different count, or from a build
  with a different wire format, with `DisconnectReason.ProtocolError`.

## Sessions

`NetSession` adds admission, peer lists, typed/raw messages and fragmentation. Clients connect to
one host; there is no host migration. Match `AppVersion` and transport settings between peers.

`NetSessionOptions.BatchMessages` defaults to true. When both peers enable it, sessions combine
small messages per connection and delivery mode until `Flush`. Full batches and
`SendFlags.Immediate` submit earlier. Raw transports preserve individual message boundaries.
Batching is negotiated during joining; changing the toggle does not require a different transport.

```csharp
using KcpTransport hostTransport = new(new() { Port = 7777 });
using NetSession host = NetSession.Host(hostTransport, "Host", new() { AppVersion = 1 });
host.JoinRequested += pending => pending.Accept(); // Optional: validate tokens, reject or defer.

using KcpTransport clientTransport = new();
using NetSession client = NetSession.Join(clientTransport, "127.0.0.1:7777", "Player",
    options: new() { AppVersion = 1 });

// Each frame:
host.Update();
client.Update();
```

Register typed `INetMessage` handlers with `On<T>` or raw handlers with `OnRaw`.
Use `SendToHost`, `SendRaw` or `Broadcast` to send messages. Validate application authentication
in `JoinRequested`; approval is automatic when no handler is registered.

`Host` / `Join` pump transport I/O inline when the session is updated. For an external driver,
use `CreateHost` / `CreateClient` and feed events through `ProcessEvent`. The driver owns transport
I/O and lifetime. Sessions require reliable delivery; callbacks run on the session's calling thread.

## Tests

From this directory:

```powershell
dotnet test Tests/Prowl.Warp.Tests.csproj -c Release
# Optional longer reconnect/transfer run:
$env:WARP_STRESS_ROUNDS = '100'
dotnet test Tests/Prowl.Warp.Tests.csproj -c Release --filter FullyQualifiedName~RobustnessTests
```
