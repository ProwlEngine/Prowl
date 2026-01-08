namespace ShooterGame;

using Prowl.Wicked.Attributes;
using Prowl.Wicked.Core;
using Prowl.Wicked.Network;

/// <summary>
/// Vehicle behaviour that demonstrates ownership transfer.
/// Players can spawn vehicles with V and enter them with F to take ownership.
/// When owned, the player can drive the vehicle around.
/// Vehicles can be destroyed by bullets.
/// </summary>
public class VehicleBehaviour : EntityBehaviour
{
    // Vehicle dimensions
    public const float Width = 50f;
    public const float Height = 30f;
    public const float MoveSpeed = 300f;
    public const float InteractionDistance = 60f;
    public const float MaxHealth = 150f;
    public const float CollisionRadius = 30f; // For bullet collision

    // Synced position
    [SyncVar]
    public float X;

    [SyncVar]
    public float Y;

    // Synced vehicle color
    [SyncVar]
    public uint VehicleColor = 0xFF4444FFu; // Default blue

    // Synced driver info
    // NetId of the player driving this vehicle (0 = no driver)
    [SyncVar]
    public uint DriverNetId;

    // Synced health
    [SyncVar]
    public float Health = MaxHealth;

    // Local display interpolation
    public float DisplayX { get; private set; }
    public float DisplayY { get; private set; }

    // Cooldown to prevent immediate exit after entering (fixes host mode bug)
    private float _exitCooldown;
    private const float ExitCooldownTime = 0.3f;

    /// <summary>
    /// True if someone is driving this vehicle.
    /// </summary>
    public bool HasDriver => DriverNetId != 0;

    /// <summary>
    /// True if the vehicle is destroyed.
    /// </summary>
    public bool IsDestroyed => Health <= 0;

    public override void OnStartServer()
    {
        Console.WriteLine($"[VehicleBehaviour {NetId}] OnStartServer called");

        // Initialize position (will be set by spawner)
        DisplayX = X;
        DisplayY = Y;
        Health = MaxHealth;

        // Random vehicle color
        var random = new Random();
        VehicleColor = 0xFF000000u | ((uint)random.Next(128, 256) << 16) | ((uint)random.Next(64, 128) << 8) | (uint)random.Next(128, 256);
    }

    public override void OnStartClient()
    {
        Console.WriteLine($"[VehicleBehaviour {NetId}] OnStartClient called - X={X}, Y={Y}");
        DisplayX = X;
        DisplayY = Y;
    }

    public override void OnStartAuthority()
    {
        Console.WriteLine($"[VehicleBehaviour {NetId}] OnStartAuthority called - You now own vehicle!");
        // Start exit cooldown when entering vehicle
        _exitCooldown = ExitCooldownTime;
    }

    public override void OnStopAuthority()
    {
        Console.WriteLine($"[VehicleBehaviour {NetId}] OnStopAuthority called - You no longer own vehicle");
    }

    public override void OnUpdate()
    {
        // Interpolate display position
        float lerpSpeed = 15f * GameLoop.DeltaTime;
        DisplayX = Lerp(DisplayX, X, lerpSpeed);
        DisplayY = Lerp(DisplayY, Y, lerpSpeed);

        // Update cooldowns
        if (_exitCooldown > 0)
            _exitCooldown -= GameLoop.DeltaTime;

        // Only the owner can drive
        if (!IsOwned) return;

        // Handle driving input
        HandleDrivingInput();
    }

    private void HandleDrivingInput()
    {
        float dx = 0, dy = 0;

        if (Raylib_cs.Raylib.IsKeyDown(Raylib_cs.KeyboardKey.W) || Raylib_cs.Raylib.IsKeyDown(Raylib_cs.KeyboardKey.Up))
            dy -= 1;
        if (Raylib_cs.Raylib.IsKeyDown(Raylib_cs.KeyboardKey.S) || Raylib_cs.Raylib.IsKeyDown(Raylib_cs.KeyboardKey.Down))
            dy += 1;
        if (Raylib_cs.Raylib.IsKeyDown(Raylib_cs.KeyboardKey.A) || Raylib_cs.Raylib.IsKeyDown(Raylib_cs.KeyboardKey.Left))
            dx -= 1;
        if (Raylib_cs.Raylib.IsKeyDown(Raylib_cs.KeyboardKey.D) || Raylib_cs.Raylib.IsKeyDown(Raylib_cs.KeyboardKey.Right))
            dx += 1;

        // Normalize diagonal movement
        if (dx != 0 && dy != 0)
        {
            dx *= 0.707f;
            dy *= 0.707f;
        }

        // Send movement command to server
        if (dx != 0 || dy != 0)
        {
            CmdDrive(dx, dy);
        }

        // Exit vehicle with F (only after cooldown expires)
        if (Raylib_cs.Raylib.IsKeyPressed(Raylib_cs.KeyboardKey.F) && _exitCooldown <= 0)
        {
            CmdExitVehicle();
        }
    }

    // ============ Commands (client -> server) ============

    [Command]
    public void CmdDrive(float dx, float dy)
    {
        // This code runs on the SERVER
        if (!HasDriver || IsDestroyed) return;

        float newX = X + dx * MoveSpeed * GameLoop.DeltaTime;
        float newY = Y + dy * MoveSpeed * GameLoop.DeltaTime;

        // Clamp to screen bounds
        X = Math.Clamp(newX, Width / 2, 800 - Width / 2);
        Y = Math.Clamp(newY, Height / 2, 600 - Height / 2);
    }

    /// <summary>
    /// Called by a player to enter this vehicle.
    /// </summary>
    [Command]
    public void CmdEnterVehicle(uint playerNetId)
    {
        // This code runs on the SERVER
        if (HasDriver || IsDestroyed)
        {
            Console.WriteLine($"[SERVER] Vehicle {NetId} already has a driver or is destroyed");
            return;
        }

        // Find the player entity to get their connection
        var playerEntity = World.Active?.FindEntity(playerNetId);
        if (playerEntity == null)
        {
            Console.WriteLine($"[SERVER] Player entity {playerNetId} not found");
            return;
        }

        // Check distance
        var playerBehaviour = playerEntity.GetBehaviour<PlayerBehaviour>();
        if (playerBehaviour == null) return;

        float distX = playerBehaviour.X - X;
        float distY = playerBehaviour.Y - Y;
        float distance = MathF.Sqrt(distX * distX + distY * distY);

        if (distance > InteractionDistance)
        {
            Console.WriteLine($"[SERVER] Player {playerNetId} too far from vehicle (dist={distance})");
            return;
        }

        // Set the driver
        DriverNetId = playerNetId;

        // Snap player position to vehicle
        playerBehaviour.X = X;
        playerBehaviour.Y = Y;

        // Transfer ownership to the player
        Console.WriteLine($"[SERVER] Transferring vehicle {NetId} ownership to player {playerNetId}");
        Entity.TransferOwnership(playerEntity.Owner);

        // IMPORTANT: Set exit cooldown directly for host mode
        // OnStartAuthority may not fire immediately in host mode
        _exitCooldown = ExitCooldownTime;

        // Notify all clients
        RpcOnPlayerEntered(playerNetId);
    }

    /// <summary>
    /// Called by the current driver to exit the vehicle.
    /// </summary>
    [Command]
    public void CmdExitVehicle()
    {
        // This code runs on the SERVER
        if (!HasDriver) return;

        uint oldDriver = DriverNetId;

        // Find the player and move them outside the vehicle
        var playerEntity = World.Active?.FindEntity(oldDriver);
        var playerBehaviour = playerEntity?.GetBehaviour<PlayerBehaviour>();
        if (playerBehaviour != null)
        {
            // Eject player to the side of the vehicle
            playerBehaviour.X = X + Width + 20f;
            playerBehaviour.Y = Y;
        }

        DriverNetId = 0;

        // Remove ownership (make server-owned)
        Console.WriteLine($"[SERVER] Player {oldDriver} exiting vehicle {NetId}");
        Entity.RemoveOwnership();

        // Notify all clients
        RpcOnPlayerExited(oldDriver);
    }

    // ============ Server methods ============

    /// <summary>
    /// Called on server when vehicle takes damage.
    /// </summary>
    public void TakeDamage(float damage, uint attackerNetId)
    {
        if (!IsServer || IsDestroyed) return;

        Health -= damage;

        Console.WriteLine($"[SERVER] Vehicle {NetId} took {damage} damage from {attackerNetId}, health={Health}");

        if (IsDestroyed)
        {
            // Vehicle destroyed!
            HandleDestruction(attackerNetId);
        }
    }

    /// <summary>
    /// Handles vehicle destruction on the server.
    /// </summary>
    private void HandleDestruction(uint attackerNetId)
    {
        Console.WriteLine($"[SERVER] Vehicle {NetId} destroyed by {attackerNetId}!");

        // If someone was driving, eject them
        if (HasDriver)
        {
            uint driverNetId = DriverNetId;

            // Find the driver and damage them
            var driverEntity = World.Active?.FindEntity(driverNetId);
            var driverBehaviour = driverEntity?.GetBehaviour<PlayerBehaviour>();
            if (driverBehaviour != null)
            {
                // Eject player
                driverBehaviour.X = X + Width + 20f;
                driverBehaviour.Y = Y;

                // Deal damage to driver from explosion
                driverBehaviour.TakeDamage(30f, attackerNetId);
            }

            DriverNetId = 0;
            Entity.RemoveOwnership();
        }

        // Notify all clients about destruction
        RpcOnVehicleDestroyed(attackerNetId);

        // Despawn the vehicle after a short delay (so clients see the destruction)
        // For now, despawn immediately
        NetworkManager.Despawn(Entity);
    }

    // ============ Client RPCs (server -> all clients) ============

    [ClientRpc]
    public void RpcOnPlayerEntered(uint playerNetId)
    {
        // This code runs on ALL CLIENTS
        Console.WriteLine($"Player {playerNetId} entered vehicle {NetId}");
    }

    [ClientRpc]
    public void RpcOnPlayerExited(uint playerNetId)
    {
        // This code runs on ALL CLIENTS
        Console.WriteLine($"Player {playerNetId} exited vehicle {NetId}");
    }

    [ClientRpc]
    public void RpcOnVehicleDestroyed(uint attackerNetId)
    {
        // This code runs on ALL CLIENTS
        Console.WriteLine($"Vehicle {NetId} was destroyed by player {attackerNetId}!");
    }

    // ============ Utilities ============

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * Math.Clamp(t, 0, 1);
    }
}
