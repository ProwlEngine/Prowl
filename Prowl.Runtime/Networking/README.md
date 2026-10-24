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

Scene replication requires reliable delivery. Transforms and RPCs marked `Unreliable` prefer
datagrams, but automatically use reliable delivery when the backend has no unreliable channel or
the payload cannot fit. Transform batches are split at record boundaries for each connection's
message limit; reliable fallback can use Warp fragmentation even when one transform cannot fit
in a datagram. Reliable fallback can add latency under packet loss. Raw Warp messages keep their
explicit delivery requirements. The session's configured `MaxMessageSize` must allow at least
74 bytes for scene replication; this is a logical message limit, not a transport packet limit.

Incoming scenes and spawned objects have separate local decoding budgets, available in project
networking settings or `NetworkConfiguration`: `MaxSnapshotDecompressedBytes` (64 MiB),
`MaxSnapshotDepth` (128 levels, including the root), and `MaxSnapshotNodes` (250,000 Echo values,
including containers and scalars). These limits apply to the next incoming snapshot or spawn.
Decompression stops at the byte budget; lengths, container counts, total values, and nesting are
validated before Echo builds the tree or the engine creates objects. Exceeding a budget ends the
client session with `ProtocolError`. Increase the local budgets for larger trusted scenes; the
compressed session message limit still applies independently.

## Replicated state

Objects expose networking through `GameObject.Network`; `Component.Network` forwards to the same
accessor. It owns configuration and operations such as `Mode`, `Flags`, `Owner`, `OwnerTransfer`,
`Orphaned`, `Spawn`, `AssignOwnership`, `TakeOwnership`, and `DropOwnership`. `Network.Active`
reports live membership. Both base types also expose `IsProxy` as a convenience shortcut.
Accessors are created on first access, with no separate allocation per component. Ordinary object
and scene serialization does not create accessors, and non-default settings live in one `Network`
block. Compiler rewriting and serializer generation live in `Prowl.CodeGen`; generated scripts call
their networking descriptors rather than inherited methods on `Component`.

Network flags disable parts of the default replication behavior. Set them in code or the object's network inspector:

```csharp
go.Network.Flags |= NetworkFlags.NoScaleSync;
```

`NetworkFlags.None` synchronizes position, rotation and scale with interpolation. Use
`NoPositionSync`, `NoRotationSync`, `NoScaleSync`, or `NoTransformSync` to disable channels,
and `NoInterpolation` to apply incoming transform updates immediately. Only the host can change
flags after spawning; changes replicate reliably, including for children of a network root.
The host's configuration controls which channels receivers apply, including RPC state, late-join
catch-up, and ownership handoff. Disabled channels keep their local values. Initial scene/spawn
poses still include the complete transform. Old interpolation samples are discarded when flags change.

Transforms are sent every tick an object moves (changes below a small epsilon don't count). Once it
has been still for a few ticks, its final pose is sent reliably and nothing more is sent until it
moves again. Objects marked `IsStatic` are not transform-synced at all.

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
- Incoming Sync blocks validate their complete framing and accepted values before applying state,
  relaying values, or running callbacks. Malformed later records reject the block
  without applying earlier values. Unknown, unauthorized, and stale entries remain skippable;
  accepted values and standalone Sync messages must not contain trailing bytes. Custom serializers
  run during validation and application and must read deterministically without side effects.
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
remote despawn, local destruction, and session shutdown end membership. `Network.Active` is already false
inside `OnNetworkDespawn`. Starting another session from a teardown callback is rejected; schedule
that startup for the next frame. Preserved scene objects keep
membership when they survive a host scene change.

When assigning ownership through `Network.Spawn(owner)` or `Network.AssignOwnership(owner)`, use a peer from
`Networking.Session`. Peers from another session/context, or peers that already disconnected, are
rejected before ownership or spawn settings change. `null` clears ownership and leaves simulation to
the host.

`GameObject.Network.OwnerTransfer` defaults to `OwnerTransfer.Fixed`: only the host assigns an owner.
Set it to `OwnerTransfer.Takeover` in the GameObject's network menu or before spawning to let any
ready client take ownership. Only the host can change this policy once spawned; changes replicate
reliably and are included in late join snapshots.

```csharp
pickup.Network.OwnerTransfer = OwnerTransfer.Takeover; // Configure before spawning, or on the host.
pickup.Network.TakeOwnership();                       // Request local ownership.
pickup.Network.DropOwnership();                       // Return simulation to the host.
```

Take/drop calls on children act on their network root. The current owner can drop even with `Fixed`.
The host can always override ownership through `Network.AssignOwnership`. Take/drop return false when the
operation is unavailable or already satisfied. On clients, true means the request was sent: wait for
`OnNetworkAuthorityGained` or check `IsProxy` before simulating. The host commits transfers and rejects
requests from loading peers, unauthorized drops, and requests for an obsolete ownership generation.
Competing requests for the same generation cannot both succeed.

Each transfer sends the pending Sync values first, then the enabled transforms' final poses, ahead of
its ownership notification, and clears interpolation history. A voluntary drop includes the owner's final state; a forced takeover
uses the latest state accepted by the host. Root rigidbodies also hand off linear velocity. Gameplay
approval callbacks and a `Request` transfer policy are left to a future feature.

`GameObject.Network.Orphaned` configures disconnect handling before spawning. Once spawned, the host
can change it at runtime: the active policy and connected clients update reliably, and late joiners
receive the latest policy. Clients cannot override a spawned root's policy locally. `Destroy` removes
the object when its owner leaves; `ClearOwner` keeps it and returns simulation to the host.

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

## RPCs and state

An RPC carries only its arguments. A reliable call (the default) first sends the caller's pending Sync
values and collection operations, on the same ordered channel, so a receiver runs the method with the
state the caller had when it called. The host does the same with its own pending values before relaying
a call. An `Unreliable` call promises nothing: it can be lost, duplicated, or arrive before or after the
caller's state, which follows with the next tick. Transforms are not part of a call; a proxy is shown
`InterpolationDelay` in the past, so pass a position the method needs as an argument.

Each value has one writer at a time, its controller or the host for `FromHost` values, and its changes
arrive in the order they were made, so the latest to arrive is the newest. Ownership generations reject
traffic from an earlier owner assignment, including when ownership returns to the same peer. Independent objects have no implicit state ordering guarantee. An RPC does not grant
authority to write another object's state. An unreliable call uses reliable delivery with Warp
fragmentation when it exceeds the transport's datagram limit.

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

using (Rpc.FilterInclude(peer => peer.Name == "Justin"))
{
    GlobalEvents.Announce("Hello Justin");
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
call's recipients. The host still receives routed arguments to validate and relay
it. Filters do not limit ordinary Sync/transform replication or provide private-state visibility.

## Runtime structure

Build component trees before `Network.Spawn`. Whole network roots can be spawned and destroyed by the
host. While a replicated tree is active, adding, removing, moving, destroying individual components,
reparenting its objects, changing its `Network.Mode`, or moving its tree between scenes is rejected
before mutation. Clients cannot destroy replicated roots locally. Ordinary edits outside active
replicated trees remain available. Use the GameObject/Scene mutation APIs; editing `Children` directly
is unsupported. Structural delta replication and gameplay readiness remain future work.

The engine Sync/RPC wire layout changed with these guarantees; scene snapshots use format 10. Peers
must run matching engine builds.
