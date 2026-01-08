namespace ShooterGame;

using Prowl.Wicked.Attributes;
using Prowl.Wicked.Core;
using Prowl.Wicked.Network;

/// <summary>
/// Bullet projectile that moves and damages players/vehicles on collision.
/// Uses [SyncVar] on fields - IL weaver transforms them into properties with sync.
/// </summary>
public class BulletBehaviour : EntityBehaviour
{
    // Settings
    public const float Speed = 500f;
    public const float PlayerDamage = 25f;
    public const float VehicleDamage = 35f;
    public const float Radius = 5f;
    public const float Lifetime = 3f;

    private float _lifetime;

    // Synced fields
    [SyncVar]
    public float X;

    [SyncVar]
    public float Y;

    [SyncVar]
    public float VelocityX;

    [SyncVar]
    public float VelocityY;

    [SyncVar]
    public uint OwnerNetId;

    [SyncVar]
    public uint BulletColor;

    public override void OnStartServer()
    {
        _lifetime = Lifetime;
    }

    public override void OnUpdate()
    {
        // Move bullet
        X += VelocityX * GameLoop.DeltaTime;
        Y += VelocityY * GameLoop.DeltaTime;

        // Server handles collision and lifetime
        if (IsServer)
        {
            _lifetime -= GameLoop.DeltaTime;

            // Check bounds
            if (X < 0 || X > 800 || Y < 0 || Y > 600 || _lifetime <= 0)
            {
                NetworkManager.Despawn(Entity);
                return;
            }

            // Check collision with vehicles first (they have priority)
            if (CheckVehicleCollision())
                return;

            // Check collision with players (only if not in vehicles)
            CheckPlayerCollision();
        }
    }

    /// <summary>
    /// Check collision with vehicles.
    /// Returns true if hit a vehicle.
    /// </summary>
    private bool CheckVehicleCollision()
    {
        if (World == null) return false;

        foreach (var entity in World.Entities.Values.ToArray())
        {
            // Skip self
            if (entity.NetId == NetId) continue;

            var vehicle = entity.GetBehaviour<VehicleBehaviour>();
            if (vehicle == null) continue;
            if (vehicle.IsDestroyed) continue;

            // Don't let players shoot their own vehicle
            if (vehicle.DriverNetId == OwnerNetId) continue;

            // Simple circle collision using vehicle's collision radius
            float dx = vehicle.X - X;
            float dy = vehicle.Y - Y;
            float distSq = dx * dx + dy * dy;
            float collisionDist = Radius + VehicleBehaviour.CollisionRadius;

            if (distSq < collisionDist * collisionDist)
            {
                // Hit vehicle!
                vehicle.TakeDamage(VehicleDamage, OwnerNetId);
                NetworkManager.Despawn(Entity);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Check collision with players not in vehicles.
    /// </summary>
    private void CheckPlayerCollision()
    {
        if (World == null) return;

        // First, build a set of all players currently in vehicles
        HashSet<uint> playersInVehicles = new();
        foreach (var entity in World.Entities.Values)
        {
            var vehicle = entity.GetBehaviour<VehicleBehaviour>();
            if (vehicle != null && vehicle.HasDriver)
            {
                playersInVehicles.Add(vehicle.DriverNetId);
            }
        }

        foreach (var entity in World.Entities.Values.ToArray())
        {
            // Skip self and owner
            if (entity.NetId == NetId) continue;
            if (entity.NetId == OwnerNetId) continue;

            var player = entity.GetBehaviour<PlayerBehaviour>();
            if (player == null) continue;
            if (player.IsDead) continue;

            // Skip players who are in vehicles (bullets hit the vehicle instead)
            if (playersInVehicles.Contains(entity.NetId)) continue;

            // Simple circle collision
            float dx = player.X - X;
            float dy = player.Y - Y;
            float distSq = dx * dx + dy * dy;
            float collisionDist = Radius + 15f; // 15 = player radius

            if (distSq < collisionDist * collisionDist)
            {
                // Hit!
                player.TakeDamage(PlayerDamage, OwnerNetId);
                NetworkManager.Despawn(Entity);
                return;
            }
        }
    }
}
