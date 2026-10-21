# Runtime networking

`Networking.Host` and `Networking.Join` manage the scene session. Use separate
`NetworkingContext` instances for additional lobby or game connections:

```csharp
using Prowl.Runtime.Networking;
using Prowl.Warp.Transports.Kcp;

using var lobby = new NetworkingContext(Networking.Configuration);
var session = lobby.Join(new KcpTransport(), "lobby.example:7777", "Player");

// Each frame, on the creating thread:
lobby.Receive();
// Simulate and send messages.
lobby.Flush();
```

- Contexts own their transports and release them when the session ends. Shutdown permits another
  session; disposal is terminal. Scene replication remains on the static `Networking` session.
- `NetworkConfiguration.Execution` selects `Auto`, `Inline` or `DedicatedThread`. Automatic mode
  follows backend capabilities: local prefers inline and KCP prefers a worker. Thread-affine backends
  cannot use dedicated execution. Legacy `UseNetworkThread = false` keeps automatic mode inline.
- Workers drive transport I/O; session callbacks stay on the context thread. Backends must return
  promptly. If shutdown times out after five seconds, ownership is retained so disposal can be retried.

```powershell
dotnet test Prowl.Runtime.Test/Prowl.Runtime.Test.csproj --filter 'FullyQualifiedName~Network|FullyQualifiedName~TransformTimeline'
```
