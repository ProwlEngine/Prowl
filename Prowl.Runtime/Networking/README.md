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

## Loading and readiness

`Networking.IsActive` means the session admitted this peer. Use `Networking.JoinState` for connection,
admission, waiting for a snapshot, loading, synchronization, and ready progress. `Networking.IsReady`
means the network scene is active and the readiness handshake completed. On the host,
`Networking.IsPeerReady(peer)` reports whether an admitted remote peer is ready for this scene;
remote readiness is not replicated to other clients.

```csharp
public class Players : Component, INetworkListener
{
    public void OnPeerConnected(NetPeer peer) { } // Host: admitted, still loading.
    public void OnPeerReady(NetPeer peer) { }     // Host: spawn the player's object here.
    public void OnPeerDisconnected(NetPeer peer, SessionEndReason reason) { }
    public void OnNetworkLoading() { }           // Local: initial network state installed.
    public void OnNetworkReady() { }             // Local: active scene, catch-up complete.
}
```

These scene-wide callbacks include disabled components and isolate listener exceptions. Object
`OnNetworkSpawn` still describes network membership; use `OnNetworkReady` for local gameplay startup.
The host runs its own loading/ready callbacks when hosting and after scene replacement. It reports
remote connection, readiness, and disconnection through the peer callbacks. `OnPeerReady` runs once
per peer per scene generation, so scene-specific player objects can be created there.

Clients acknowledge scene activation, receive a reliable full Sync/collection and root transform
catch-up, acknowledge that state, and wait for host activation. Initial catch-up uses initial-state
semantics: it restores values and collection contents without firing change callbacks. Reliable
spawn/despawn, ownership, and Sync traffic stays ordered around the handshake. Transforms captured
in catch-up are applied directly, with obsolete interpolation samples cleared. A ready callback sees
the catch-up state or newer accepted updates, not a permanently frozen snapshot.

RPCs to or from loading peers are dropped; client Sync/transform submissions are ignored until the
host accepts readiness. Clients also defer their outgoing replication until local activation. Keep
gameplay logic behind readiness; ordinary component enable/update callbacks still follow the scene
lifecycle. Raw game messages sent directly through Warp remain the application's responsibility.

Host scene changes invalidate peer readiness. Generations reject stale/duplicate acknowledgements;
readiness must complete again for the new scene. `NetworkConfiguration.SceneLoadTimeoutSeconds`
(default 60) bounds waiting for the snapshot and the loading/catch-up handshake. Stalled peers are
kicked, or a waiting client shuts down, with the failed stage in the reason. Disconnects cancel a
network scene still queued for activation. Replacing the network scene locally on a client ends its
session. Authentication/admission remains Warp's version checks and join approval/token mechanism;
a separate content download pipeline and asynchronous snapshot capture are future work.

## State before RPCs

A component RPC carries the caller's authorized Sync values, collection contents and pending operations, and
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

## Static RPCs

Static methods on non-generic classes, including ordinary static classes, can use the same attributes:

```csharp
public static class GlobalEvents
{
    [Rpc.Broadcast]
    public static void Announce(string message) => Debug.Log(message);

    [Rpc.Host]
    public static void RequestAnnouncement(string message)
    {
        // Validate Rpc.Caller and the request before changing gameplay state.
        Announce(message);
    }
}
```

No component or spawned object is needed. `Broadcast` executes on the caller and sends to other ready
peers through the host; `Host` executes only on the host. `Rpc.Caller` preserves the original sender,
and `Rpc.IsRemote` works inside received static calls. `HostOnly` and `Unreliable` are supported.
`Owner` and `OwnerOnly` require an object, so the compiler rejects them on static methods, along with
generic methods and generic declaring/enclosing classes. Without an active session calls run locally.

Static RPCs carry their arguments, with the same serializers and argument validation as component
RPCs. They do not capture Sync values or transforms; pass the data the method needs as arguments.
Readiness guards apply to static calls too. Receivers resolve only generated descriptors, including
methods they have never called locally; descriptor caches do not retain replaced script types.

## RPC recipient filters

Use synchronous scopes to narrow the normal recipients of component and static RPCs:

```csharp
using (Rpc.FilterExclude(peer => peer.Name == "Harry"))
{
    GlobalEvents.Announce("Hello everyone else");
}

using (Rpc.FilterInclude(peer => peer.Name == "Garry"))
{
    GlobalEvents.Announce("Hello Garry");
}
```

Prowl peers expose `Name`, rather than `DisplayName`. Filters also govern local execution: excluding
the local peer skips its RPC body. Nested scopes combine by intersection, so an inner include cannot
restore a peer excluded by an outer scope. Disposing restores the previous scope, including when the
body throws. Dispose in reverse order. Use these scopes on the main thread, without crossing an
`await`; predicates run synchronously for each peer when a call is made and should have no side effects.

Filters only narrow an RPC's declared destination. `Host` still runs only on the host, `Owner` only
on the object's controller, and authorization/readiness checks still apply. A matching local-only
broadcast runs locally without sending anything; an empty selection does nothing. With no active
session, RPCs remain ordinary local calls and filters have no effect.

A client evaluates its predicates against its current peer list and sends selected peer IDs with the
call. The host validates the RPC and relays only to those peers, even when excluded from execution
itself. Predicates are not serialized, and filters active on the receiving host do not alter an incoming
call's recipients. The host still receives routed arguments and dependent state to validate and relay
it. Filters do not limit ordinary Sync/transform replication or provide private-state visibility.

## Runtime structure

Build component trees before `NetworkSpawn`. Whole network roots can be spawned and destroyed by the
host. While a replicated tree is active, adding, removing, moving, destroying individual components,
reparenting its objects, changing its `NetworkMode`, or moving its tree between scenes is rejected
before mutation. Clients cannot destroy replicated roots locally. Ordinary edits outside active
replicated trees remain available. Use the GameObject/Scene mutation APIs; editing `Children` directly
is unsupported. Structural delta replication and gameplay readiness remain future work.

The engine Sync/RPC wire layout changed with these guarantees; scene snapshots use format 7. Peers
must run matching engine builds.
