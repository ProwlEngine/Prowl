# Prowl.Wicked

A lightweight C# networking library for the Prowl Game Engine. Prowl.Wicked gives you authoritative client-server multiplayer with IL-weaved RPCs, automatic state replication, observation-scoped maps, and promise-based command replies - all without source generators or attribute boilerplate.

## How It Works

Prowl.Wicked rewrites your assembly after compilation. Methods marked with `[EntityCommand]`, `[EntityRpc]`, `[MapRpc]`, `[StaticCommand]`, or `[StaticRpc]` have their bodies moved to internal `__UserCode_*` helpers, and the original method becomes a marshalling stub that:

- On the calling side, serializes the arguments and sends them over the wire.
- On the receiving side, deserializes the arguments and invokes the original code.

The result is that you write a normal C# method and call it like any other method - the weaver inserts the network plumbing for you. No `partial` keyword, no generated code in your source tree, no manual `Begin/End` methods.

The weaver ships in the `Prowl.Wicked` NuGet package and runs automatically `AfterTargets="Build"` for any project that references it (directly or transitively).

## Features

- **Server / Client / Transport**
  - Static `Server` and `Client` entry points (one of each per process)
  - Built-in `TcpServerTransport` / `TcpClientTransport` with length-prefixed framing, non-blocking I/O, and Nagle disabled
  - Pluggable transport layer via `IServerTransport` / `IClientTransport`
  - Fixed-step tick loop driven by `Server.Tick()` / `Client.Tick()`
  - Connection timeout detection and forced disconnect

- **Entities and Maps**
  - `NetworkEntity` base class with full lifecycle callbacks: `OnSpawn`, `OnDespawn`, `OnStartServer`, `OnStopServer`, `OnStartClient`, `OnStopClient`, `OnStartOwner`, `OnStopOwner`, `OnOwnerChanged`, `OnMapChanged`, `ServerTick`, `ClientTick`
  - `Map` containers define observation scope - clients observe a map by being assigned a `PlayerEntity` inside it
  - Atomic `Map.TransferEntity` with correct observer hand-off for player entities
  - Custom `PackSpawnData` / `UnpackSpawnData` for any state that needs to arrive in the initial spawn packet
  - Auto-discovery of entity and map types across loaded assemblies (sorted by full name so server and client agree on type IDs)

- **SyncVars**
  - `SyncVar<T>` field-level replication, discovered via reflection at spawn time
  - Supported types: all primitives, `string`, `Guid`, `Vector2`, enums, and any `INetworkSerializable`
  - Per-SyncVar `SyncInterval` (rate limit) and `SyncTarget` (`Observers` or `Owner`)
  - `OnChanged((oldValue, newValue) => ...)` callbacks fire on both server and client
  - `SyncVarInterpolated` and `SyncVarInterpolatedVector2` smooth values on the client between updates
  - `ResetInterpolation()` to snap-on-respawn without lerping from the old position
  - Initial state shipped in the spawn packet; subsequent updates batched once per tick

- **RPCs**
  - `[EntityCommand]` for client-to-server calls on a `NetworkEntity` (with optional `RequireOwner` enforcement)
  - `[EntityRpc]` for server-to-client calls on a `NetworkEntity`, targeted at `Observers`, `Owner`, or a specific `Player`
  - `[MapRpc]` for server-to-client calls on a `Map`, targeted at `Observers` or a specific `Player`
  - `[StaticCommand]` / `[StaticRpc]` for non-entity messages (auth, matchmaking, lobby chat)
  - `ExcludeOwner` flag on `EntityRpc` for "tell everyone except me" patterns
  - Compile-time validation: wrong target on Map, void-only RPCs, mismatched parameter types, etc.
  - Argument types: all primitives, strings, `Guid`, `Vector2`, enums, `NetworkEntity` references (by network ID), `INetworkSerializable` objects, and arrays of any of the above

- **RpcPromise**
  - Return `RpcPromise` or `RpcPromise<T>` from an `[EntityCommand]` for promise-based replies
  - Fluent `.Then().Catch().Finally().Timeout()` chains
  - `RpcPromise.Completed` for synchronous acknowledgment
  - Implicit conversion: `return 42;` from a `RpcPromise<int>`-returning command
  - Server-side exceptions inside promise-returning commands are sent back as `RpcError`

- **Time and Connection Health**
  - Client/server time synchronization via Ping/Pong with EMA-smoothed RTT
  - `Client.ServerTime`, `Client.RoundTripTime`, `Client.StandardDeviation`
  - Configurable `Server.ConnectionTimeout` (default 6 seconds)

- **Authentication**
  - Client sets `Client.AuthToken` before `Connect`
  - Server receives via `Server.OnClientAuthenticate` and calls `Server.AcceptAuthentication(client, userId)` or `Server.RejectAuthentication(client, reason)`
  - `RemoteClient.IsAuthenticated`, `UserId`, and an `object? UserData` slot for arbitrary per-session state

- **Misc**
  - 470+ xUnit tests covering RPC dispatch, lifecycle ordering, observation, replication, SyncVars, serialization, time sync, and TCP transport
  - Symbol-preserving weaving: PDBs are re-written so breakpoints survive the IL rewrite
  - `Server.Reset()` / `Client.Reset()` for test isolation

## Usage

### Server and Client Setup

```csharp
using Prowl.Wicked;

// Server side
Server.OnClientConnected   += client => Console.WriteLine($"Client {client.ClientId} joined");
Server.OnClientDisconnected += client => Console.WriteLine($"Client {client.ClientId} left");
Server.Start(7777);

var lobby = Server.CreateMap<LobbyMap>();

while (running)
{
    Server.Tick();
    Thread.Sleep(16);
}

Server.Stop();
```

```csharp
// Client side
Client.OnConnected    += () => Console.WriteLine("Connected");
Client.OnDisconnected += () => Console.WriteLine("Disconnected");
Client.Connect("127.0.0.1", 7777);

while (running)
{
    Client.Tick();
    // ...render frame...
}

Client.Disconnect();
```

### Defining an Entity with SyncVars

```csharp
public class PlayerEntity : NetworkEntity
{
    public SyncVarInterpolated X = new(0f, interpSpeed: 15f) { SyncInterval = 0.05f };
    public SyncVarInterpolated Y = new(0f, interpSpeed: 15f) { SyncInterval = 0.05f };

    public SyncVar<float>  HP   = new(100f);
    public SyncVar<int>    Gold = new(0, SyncTarget.Owner);   // private to the owner
    public SyncVar<string> Name = new("");

    public override void OnStartClient()
    {
        // Per-SyncVar change callbacks fire on both sides
        HP.OnChanged((oldHp, newHp) => Console.WriteLine($"HP {oldHp} -> {newHp}"));
    }
}
```

`SyncVarInterpolated.Display` gives you the smoothed value for rendering; `Value` is always the latest known authoritative value.

### Defining a Map

```csharp
public class ArenaMap : Map
{
    public override void OnObserverEnter(RemoteClient client) { /* welcome packet */ }
    public override void OnEntityEnter(NetworkEntity e)       { /* spatial indexing */ }

    [MapRpc(Target = RpcTarget.Observers)]
    public void RpcWeatherChanged(byte weatherId) { /* ... */ }
}
```

### Spawning and Assigning a Player Entity

```csharp
// Server side - usually triggered by a [StaticCommand] from the client
var player = Server.Spawn<PlayerEntity>(lobby, sender, p =>
{
    p.Name.Value = "Hero";
    p.X.Value    = 100;
    p.Y.Value    = 100;
});

// Assigning a player entity makes the client observe the entity's map
sender.AssignPlayerEntity(player);
```

### Entity Commands (Client -> Server)

```csharp
public class PlayerEntity : NetworkEntity
{
    private float _targetX, _targetY;

    [EntityCommand]
    public void CmdMove(float x, float y)
    {
        // Runs on the server. By default, only the entity's owner can call this.
        _targetX = Math.Clamp(x, 0, MapWidth);
        _targetY = Math.Clamp(y, 0, MapHeight);
    }

    // RequireOwner = false makes this a "query" command any observer can call
    [EntityCommand(RequireOwner = false)]
    public RpcPromise<int> CmdGetGold()
    {
        return Gold.Value; // implicit conversion from int to RpcPromise<int>
    }
}

// Client side
localPlayer.CmdMove(mouseX, mouseY);

localPlayer.CmdGetGold()
    .Then(gold => Console.WriteLine($"Gold: {gold}"))
    .Catch(ex  => Console.WriteLine($"Failed: {ex.Message}"))
    .Timeout(5f);
```

### Entity RPCs (Server -> Client)

```csharp
public class PlayerEntity : NetworkEntity
{
    [EntityRpc(Target = RpcTarget.Observers)]
    public void RpcEmote(byte emoteId) { /* play animation on every observer */ }

    [EntityRpc(Target = RpcTarget.Owner)]
    public void RpcLevelUp(int newLevel) { /* show level-up effect to me only */ }

    // First parameter must be RemoteClient or RemoteClient[]
    // It is consumed for routing and not serialized (null on the client side).
    [EntityRpc(Target = RpcTarget.Player)]
    public void RpcWhisper(RemoteClient target, string message) { /* ... */ }

    // Observer broadcast but skip the owner (who already saw it locally)
    [EntityRpc(Target = RpcTarget.Observers, ExcludeOwner = true)]
    public void RpcMuzzleFlash() { /* ... */ }
}
```

### Static Commands and RPCs

For lobby, matchmaking, auth, or anything not tied to a specific entity, use static methods.

```csharp
public static class LobbyCommands
{
    [StaticCommand]
    public static void CmdJoinQueue(string region)
    {
        // Server-side. NetworkObject.Sender is the client who called it.
        var sender = NetworkObject.Sender;
        if (sender == null) return;
        Matchmaker.Enqueue(sender, region);
    }

    [StaticRpc]
    public static void RpcMatchFound(RemoteClient target, Guid matchId)
    {
        // Server-side. The first parameter targets a specific client and is not serialized.
        Console.WriteLine($"Notifying {target.ClientId} of match {matchId}");
    }
}

// Client side
LobbyCommands.CmdJoinQueue("eu-west");
```

### Custom Serializable Types

```csharp
public struct Inventory : INetworkSerializable
{
    public int Gold;
    public int[] ItemIds;

    public void Serialize(NetworkWriter writer)
    {
        writer.WriteInt(Gold);
        writer.WriteIntArray(ItemIds);
    }

    public void Deserialize(NetworkReader reader)
    {
        Gold    = reader.ReadInt();
        ItemIds = reader.ReadIntArray()!;
    }
}

// Now usable as an RPC parameter or SyncVar payload
[EntityRpc(Target = RpcTarget.Owner)]
public void RpcInventoryUpdate(Inventory inv) { /* ... */ }
```

### Authentication

```csharp
// Server side
Server.OnClientAuthenticate += (client, token) =>
{
    string? userId = ValidateMyToken(token);
    if (userId != null)
        Server.AcceptAuthentication(client, userId);
    else
        Server.RejectAuthentication(client, "invalid token");
};

// Client side
Client.AuthToken = "bearer-abc123";
Client.OnAuthenticated += () => Console.WriteLine("Auth OK");
Client.OnAuthRejected  += reason => Console.WriteLine($"Auth failed: {reason}");
Client.Connect("127.0.0.1", 7777);
```

### Time Synchronization

```csharp
// Both ServerTime and RoundTripTime are smoothed via an EMA over PingWindowSize samples
double serverNow = Client.ServerTime;
double rtt       = Client.RoundTripTime;
double jitter    = Client.StandardDeviation;
```

## Architecture Notes

- **Single static Server/Client.** Both expose a `Reset()` method for test isolation. Tests in `Prowl.Wicked.Tests` reset between cases and use the in-memory `InMemoryTransport` to avoid hitting real sockets.
- **Observation is map-scoped.** Replication is gated by `Map.Observers`. A client observes a map by having its `PlayerEntity` inside that map. To move a client between maps, call `Map.TransferEntity` on its player entity - the framework handles despawn-on-old-map / spawn-on-new-map automatically.
- **Wire protocol is binary little-endian, length-prefixed.** See `MessageType.cs` for the message codes and `NetworkWriter` / `NetworkReader` for the format. Transports are responsible only for framing; the rest of the protocol lives above the transport interface.
- **Static state is reset, not torn down.** `Server.Stop()` disconnects clients (firing `OnClientDisconnected` while `PlayerEntity` is still accessible), then despawns entities, then destroys maps, then unwires the transport.

## Limitations

- Only fields are replicated (properties are not), matching the rest of the Prowl ecosystem (Particularly Prowl.Echo).
- No Host Mode, Ideally you host a server seperatelly from the client, maybe as a sub-process.
- The default transport is TCP. UDP / reliable-UDP transports can be implemented by satisfying `IServerTransport` / `IClientTransport`.
- The weaver runs `AfterTargets="Build"`. If you add an RPC attribute and rebuild without weaving (for example, opting out via `<ProwlWickedWeave>false</ProwlWickedWeave>`), calls to those methods will run locally as plain C# methods.

## License

This component is part of the Prowl Game Engine and is licensed under the MIT License. See the LICENSE file in the project root for details.
