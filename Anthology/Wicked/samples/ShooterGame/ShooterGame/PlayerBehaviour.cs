namespace ShooterGame;

using Prowl.Wicked.Attributes;
using Prowl.Wicked.Core;
using Prowl.Wicked.Network;

/// <summary>
/// Player behaviour with networked position, health, and shooting.
/// Uses [SyncVar] on fields - IL weaver transforms them into properties with sync.
/// Uses [Command] and [ClientRpc] for RPCs - IL weaver transforms these.
/// </summary>
public class PlayerBehaviour : EntityBehaviour
{
    // Movement settings
    public const float MoveSpeed = 200f;
    public const float MaxHealth = 100f;

    // Local state (not synced, used for interpolation)
    public float DisplayX { get; private set; }
    public float DisplayY { get; private set; }

    // Synced fields - IL weaver transforms these into properties
    [SyncVar]
    public float X;

    [SyncVar]
    public float Y;

    [SyncVar]
    public float Health = MaxHealth;

    [SyncVar]
    public uint PlayerColor;

    [SyncVar]
    public string PlayerName = "Player";

    public bool IsDead => Health <= 0;

    // Shooting cooldown
    private float _shootCooldown;
    private const float ShootCooldownTime = 0.25f;

    // Vehicle interaction cooldown
    private float _vehicleCooldown;
    private const float VehicleCooldownTime = 0.5f;

    public override void OnStartServer()
    {
        Console.WriteLine($"[PlayerBehaviour {NetId}] OnStartServer called");

        // Initialize player state on server
        Health = MaxHealth;

        // Random spawn position
        var random = new Random();
        X = random.Next(100, 700);
        Y = random.Next(100, 500);

        // Random color (RGB format stored as uint)
        PlayerColor = 0xFF000000u | ((uint)random.Next(256) << 16) | ((uint)random.Next(256) << 8) | (uint)random.Next(256);
    }

    public override void OnStartClient()
    {
        Console.WriteLine($"[PlayerBehaviour {NetId}] OnStartClient called - X={X}, Y={Y}");

        // Initialize display position to synced position
        DisplayX = X;
        DisplayY = Y;
    }

    public override void OnStartLocalPlayer()
    {
        Console.WriteLine($"[PlayerBehaviour {NetId}] OnStartLocalPlayer called - You are the local player!");
    }

    public override void OnStartAuthority()
    {
        Console.WriteLine($"[PlayerBehaviour {NetId}] OnStartAuthority called");
    }

    public override void OnStopAuthority()
    {
        Console.WriteLine($"[PlayerBehaviour {NetId}] OnStopAuthority called");
    }

    public override void OnUpdate()
    {
        // Update cooldowns
        if (_shootCooldown > 0)
            _shootCooldown -= GameLoop.DeltaTime;
        if (_vehicleCooldown > 0)
            _vehicleCooldown -= GameLoop.DeltaTime;

        // Check if we're in a vehicle by looking at all vehicles
        VehicleBehaviour? currentVehicle = FindVehicleWeAreIn();

        if (currentVehicle != null)
        {
            // We're in a vehicle - snap our position to the vehicle
            // This happens on both server and client
            X = currentVehicle.X;
            Y = currentVehicle.Y;
            DisplayX = currentVehicle.DisplayX;
            DisplayY = currentVehicle.DisplayY;

            // Don't process any player input - vehicle handles movement
            return;
        }

        // Interpolate display position towards actual position (only when not in vehicle)
        float lerpSpeed = 10f * GameLoop.DeltaTime;
        DisplayX = Lerp(DisplayX, X, lerpSpeed);
        DisplayY = Lerp(DisplayY, Y, lerpSpeed);

        // Only the local player handles input
        if (!IsLocalPlayer) return;
        if (IsDead) return;

        // Handle input and send commands to server
        HandleInput();
    }

    /// <summary>
    /// Finds the vehicle this player is currently driving (if any).
    /// </summary>
    private VehicleBehaviour? FindVehicleWeAreIn()
    {
        if (World == null) return null;

        foreach (var entity in World.Entities.Values)
        {
            var vehicle = entity.GetBehaviour<VehicleBehaviour>();
            if (vehicle != null && vehicle.DriverNetId == NetId)
            {
                return vehicle;
            }
        }
        return null;
    }

    private void HandleInput()
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
            CmdMove(dx, dy);
        }

        // Shooting with mouse
        if (Raylib_cs.Raylib.IsMouseButtonPressed(Raylib_cs.MouseButton.Left) && _shootCooldown <= 0)
        {
            var mousePos = Raylib_cs.Raylib.GetMousePosition();
            float dirX = mousePos.X - DisplayX;
            float dirY = mousePos.Y - DisplayY;

            // Normalize
            float len = MathF.Sqrt(dirX * dirX + dirY * dirY);
            if (len > 0)
            {
                dirX /= len;
                dirY /= len;
                CmdShoot(dirX, dirY);
                _shootCooldown = ShootCooldownTime;
            }
        }

        // Spawn vehicle with V
        if (Raylib_cs.Raylib.IsKeyPressed(Raylib_cs.KeyboardKey.V) && _vehicleCooldown <= 0)
        {
            CmdSpawnVehicle();
            _vehicleCooldown = VehicleCooldownTime;
        }

        // Enter nearby vehicle with F
        if (Raylib_cs.Raylib.IsKeyPressed(Raylib_cs.KeyboardKey.F) && _vehicleCooldown <= 0)
        {
            TryEnterNearbyVehicle();
            _vehicleCooldown = VehicleCooldownTime;
        }
    }

    private void TryEnterNearbyVehicle()
    {
        if (World == null) return;

        // Find the nearest vehicle within range
        VehicleBehaviour? nearestVehicle = null;
        float nearestDistance = VehicleBehaviour.InteractionDistance;

        foreach (var entity in World.Entities.Values)
        {
            var vehicle = entity.GetBehaviour<VehicleBehaviour>();
            if (vehicle == null) continue;
            if (vehicle.HasDriver) continue; // Skip vehicles that already have a driver

            float distX = vehicle.DisplayX - DisplayX;
            float distY = vehicle.DisplayY - DisplayY;
            float distance = MathF.Sqrt(distX * distX + distY * distY);

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestVehicle = vehicle;
            }
        }

        if (nearestVehicle != null)
        {
            // Send command to enter the vehicle
            nearestVehicle.CmdEnterVehicle(NetId);
        }
    }

    // ============ Commands (client -> server) ============

    [Command]
    public void CmdMove(float dx, float dy)
    {
        // This code runs on the SERVER
        if (IsDead) return;

        float newX = X + dx * MoveSpeed * GameLoop.DeltaTime;
        float newY = Y + dy * MoveSpeed * GameLoop.DeltaTime;

        // Clamp to screen bounds
        X = Math.Clamp(newX, 20, 780);
        Y = Math.Clamp(newY, 20, 580);
    }

    [Command]
    public void CmdShoot(float dirX, float dirY)
    {
        // This code runs on the SERVER
        if (IsDead) return;

        // Spawn bullet - use Spawn<T>() which adds behaviour before sending spawn message
        var bulletEntity = NetworkManager.Spawn<BulletBehaviour>();
        var bullet = bulletEntity.GetBehaviour<BulletBehaviour>()!;

        // Set bullet properties
        bullet.X = X;
        bullet.Y = Y;
        bullet.VelocityX = dirX * BulletBehaviour.Speed;
        bullet.VelocityY = dirY * BulletBehaviour.Speed;
        bullet.OwnerNetId = NetId;
        bullet.BulletColor = PlayerColor;

        // Notify all clients about the shot (for sound effects etc.)
        RpcOnShoot();
    }

    [Command]
    public void CmdSpawnVehicle()
    {
        // This code runs on the SERVER
        if (IsDead) return;

        // Spawn vehicle at player position (slightly offset so they don't overlap)
        var vehicleEntity = NetworkManager.CreateDeferred();
        var vehicle = vehicleEntity.AddBehaviour<VehicleBehaviour>()!;

        // Set vehicle position near the player
        vehicle.X = X + 80f;
        vehicle.Y = Y;

        Console.WriteLine($"[SERVER] Player {NetId} spawned vehicle {vehicleEntity.NetId} at ({vehicle.X}, {vehicle.Y})");

        NetworkManager.FinalizeSpawn(vehicleEntity);

        // Notify all clients about the vehicle spawn
        RpcOnVehicleSpawned(vehicleEntity.NetId);
    }

    // ============ Server methods ============

    public void TakeDamage(float damage, uint attackerNetId)
    {
        if (!IsServer || IsDead) return;

        Health -= damage;

        if (IsDead)
        {
            // Player died - notify all clients
            RpcOnDeath(attackerNetId);

            // Respawn after delay (simple approach: immediate respawn)
            var random = new Random();
            X = random.Next(100, 700);
            Y = random.Next(100, 500);
            Health = MaxHealth;
        }
    }

    // ============ Client RPCs (server -> all clients) ============

    [ClientRpc]
    public void RpcOnShoot()
    {
        // This code runs on ALL CLIENTS
        // Could play sound effect here
    }

    [ClientRpc]
    public void RpcOnDeath(uint killerNetId)
    {
        // This code runs on ALL CLIENTS
        Console.WriteLine($"Player {NetId} was killed by Player {killerNetId}!");
    }

    [ClientRpc]
    public void RpcOnVehicleSpawned(uint vehicleNetId)
    {
        // This code runs on ALL CLIENTS
        Console.WriteLine($"Player {NetId} spawned a vehicle (ID: {vehicleNetId})");
    }

    // ============ Utilities ============

    private static float Lerp(float a, float b, float t)
    {
        return a + (b - a) * Math.Clamp(t, 0, 1);
    }
}
