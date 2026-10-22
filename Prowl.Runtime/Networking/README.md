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

## Replicated state

```csharp
public struct Loadout { public int Ammo; public string Weapon; }

public class Player : Component
{
    [Sync, Change(nameof(OnHealthChanged))] public int Health { get; set; } = 100;
    [Sync] public Loadout Loadout { get; set; }
    [Sync] public NetList<string> Inventory { get; } = new();

    void OnHealthChanged(int oldValue, int newValue) { /* runs here and on every receiver */ }
}
```

- `[Change]` hooks and the collections' `Changed` events run for changes made locally and received, never
  for state that arrives with a snapshot or spawn: that is in place before `OnEnable`. Received `[Change]`
  hooks run once the whole tick's block of values is applied.
- `NetList`, `NetDictionary` and `NetSet` send operations, numbered so a late joiner's full copy never
  applies a pending one twice. Changes on a peer without control are dropped with a one-time warning.
- Your own structs and classes just work: the script compiler writes serializers for the ones `[Sync]`
  values, RPC arguments and collections use, sending public fields, `[SerializeField]` fields and public
  auto-properties (base class first, `[SerializeIgnore]` skipped). Implement `INetSerializable<T>` to
  write the bytes yourself, or call `NetSerializer<T>.Register` for types you don't own.
- Classes and interfaces are polymorphic: a `Weapon` property holding a `Bow` arrives as a `Bow`, for any
  non-generic class declared in the game's scripts. Within one value (a property, an RPC argument or a
  collection item), an object referenced twice arrives once, and cycles arrive as cycles.

```powershell
dotnet test Prowl.Runtime.Test/Prowl.Runtime.Test.csproj --filter 'FullyQualifiedName~Network|FullyQualifiedName~TransformTimeline'
```

## Typed scene events

Components can implement ordinary interfaces and receive local, synchronous events:

```csharp
public interface IDamageEvents : ISceneEvent<IDamageEvents>
{
    void OnDamaged(float amount);
}

// One object, its tree (the default), or an explicitly selected scene.
gameObject.RunEvent<IDamageEvents>(e => e.OnDamaged(10), EventScope.Self);
gameObject.RunEvent<IDamageEvents>(e => e.OnDamaged(10));
scene.RunEvent<IDamageEvents>(e => e.OnDamaged(10));
IDamageEvents.Post(e => e.OnDamaged(10)); // Scene.Current
```

Dispatch runs on the main thread. Disabled components and disabled objects are excluded unless
`includeInactive: true` is supplied. Listener order is `ExecutionOrder`, then stable scene insertion
order or tree/component order. Membership is captured at entry: new listeners wait until the next
dispatch; removed, moved out of scope, disposed, and destroy-queued listeners are skipped. Eligibility
is checked again before each call. Nested dispatch captures its own membership, and one listener's
exception is logged without stopping the others. Cached targets are invalidated on structural edits
and hot reload; warm dispatch with a reused delegate allocates no memory. Events do not send RPCs.
