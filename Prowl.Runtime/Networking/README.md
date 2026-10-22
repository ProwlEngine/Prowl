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

`NetworkConfiguration.BatchMessages` configures session batching for any backend.
Small messages are combined until the context flushes; both peers must enable it.

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

## Network lifecycle

Implement `INetworkEvents` on a component to observe its network root:

```csharp
public class NetworkActor : Component, INetworkEvents
{
    public void OnNetworkSpawn() { }
    public void OnNetworkDespawn() { }
    public void OnNetworkAuthorityGained() { }
    public void OnNetworkAuthorityLost() { }
    public void OnNetworkOwnerChanged(NetPeer? previousOwner, NetPeer? owner) { }
}
```

The interface provides empty defaults, so implement only the notifications you need. These events
include disabled components and are separate from `OnEnable`/`OnDisable`. Initial Sync state, ownership,
and proxy physics are installed before `OnNetworkSpawn`; an initial authority-gained notification
follows for the local simulator. Snapshot spawn notifications can run before the new scene becomes
active: these are network membership notifications, not gameplay readiness notifications.

Object authority belongs to the owner, or to the host when there is no owner. Individual `FromHost`
properties keep their own host-only write policy. Ownership transitions notify authority lost, owner
changed, then authority gained where applicable. Despawn notifies authority lost before network
despawn; listeners receive loss/despawn only if they previously received the corresponding gain/spawn.
Callbacks may end sessions or destroy whole roots without duplicate notifications. A null owner means
unowned; a previous owner that already disconnected is also reported as null. Scene replacement,
remote despawn, local destruction, and session shutdown end membership. `IsNetworked` is already false
inside `OnNetworkDespawn`. Starting another session from a teardown callback is rejected; schedule
that startup for the next frame. Preserved scene objects keep
membership when they survive a host scene change.

## State before RPCs

An RPC carries the caller's authorized Sync values, collection contents and pending operations, and
synchronized local transforms within its network root (or its own tree for a snapshot-only object).
Receivers validate the arguments and complete dependent-state envelope before applying it and invoking
the method. The host preserves client state when relaying and appends the host-authoritative values.
This works for reliable and unreliable RPCs without depending on ordering between delivery channels.

State revisions prevent an older Sync packet or captured value from overwriting newer state from
the same writer. Ownership generations reject traffic from an earlier owner assignment, including
when ownership returns to the same peer. Newer accepted state can supersede a call's captured state;
unreliable RPC execution itself can still be lost, duplicated, or reordered. Independent objects have
no implicit state ordering guarantee. An RPC does not grant authority to write another object's state.
Transform state used for a call is applied immediately and obsolete interpolation samples are cleared.

Collection capture retains pending operations for normal tick recipients and skips operations a peer
already includes. Known operations still raise `Changed`; a complete copy recovers contents when an
unreliable prerequisite was lost, without inventing callbacks for unknown historical operations.
Capturing state adds payload and serialization cost. An unreliable call, including its dependent state,
must fit the transport's message limit; reliable calls can use Warp fragmentation.

## Runtime structure

Build component trees before `NetworkSpawn`. Whole network roots can be spawned and destroyed by the
host. While a replicated tree is active, adding, removing, moving, destroying individual components,
reparenting its objects, changing its `NetworkMode`, or moving its tree between scenes is rejected
before mutation. Clients cannot destroy replicated roots locally. Ordinary edits outside active
replicated trees remain available. Use the GameObject/Scene mutation APIs; editing `Children` directly
is unsupported. Structural delta replication and gameplay readiness remain future work.

The engine Sync/RPC wire layout changed with these guarantees; scene snapshots use format 6. Peers
must run matching engine builds.
