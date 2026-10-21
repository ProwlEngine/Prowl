# Runtime networking

`Networking.Host` and `Networking.Join` drive the scene session. Use independent
`NetworkingContext` instances for additional lobby, matchmaking or game connections:

```csharp
using Prowl.Runtime.Networking;
using Prowl.Warp.Transports.Kcp;

using var lobby = new NetworkingContext(Networking.Configuration);
var session = lobby.Join(new KcpTransport(), "lobby.example:7777", "Player");

// On the creating thread, each frame:
lobby.Receive();
// Read messages and simulate; registered session handlers run on this thread.
lobby.Flush();
```

A context owns its transport after startup begins and releases it when the session ends.
It can start another session after shutdown. Disposing the context is terminal.
Additional contexts handle Warp messages; scene replication remains attached to the static
`Networking` session.

`NetworkConfiguration.Execution` selects `Auto`, `Inline` or `DedicatedThread`.
Automatic execution follows transport capabilities, including through wrappers: local prefers
inline execution and KCP prefers a worker. A thread-affine backend rejects dedicated execution.
Existing projects with `UseNetworkThread = false` retain inline behavior in automatic mode.

Workers own transport I/O and bounded queues; sessions and application callbacks stay on the
context thread. Backends must return promptly from pumping and shutdown. Startup and shutdown
wait at most five seconds. A shutdown timeout retains ownership; retry shutdown/disposal after
the backend recovers. Built-in KCP DNS resolution is asynchronous and cancellable.

Run the networking tests with:

```powershell
dotnet test Prowl.Runtime.Test/Prowl.Runtime.Test.csproj --filter 'FullyQualifiedName~Network|FullyQualifiedName~TransformTimeline'
$env:PROWL_NETWORK_STRESS_ROUNDS = '20'
dotnet test Prowl.Runtime.Test/Prowl.Runtime.Test.csproj --filter FullyQualifiedName~ReconnectsAndLargeTransfersSurviveSimulatedWan
```

The simulated WAN test covers reconnects, encrypted batches, ordered messages and 350 KB transfers
with packet loss, latency and jitter, in both execution modes. Real WAN and long-running deployment
soaks remain release validation work. Steam is unavailable until its backend is implemented.
