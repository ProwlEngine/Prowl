using Prowl.Wicked;

namespace MobaGame;

// ── Maps ──

public class LobbyMap : Map
{
    [MapRpc(Target = RpcTarget.Observers)]
    public void RpcChatMessage(string sender, string message) { }
}

public class ArenaMap : Map { }

// ── Lobby Entity ──

public class LobbyEntity : NetworkEntity
{
    public SyncVar<string> PlayerName = new("");
}

// ── Champion Entity ──

public class ChampionEntity : NetworkEntity
{
    // Position (interpolated, rate-limited)
    public SyncVarInterpolated X = new(0f, interpSpeed: 15f) { SyncInterval = 0.05f };
    public SyncVarInterpolated Y = new(0f, interpSpeed: 15f) { SyncInterval = 0.05f };

    // Stats
    public SyncVar<float> HP = new(0f);
    public SyncVar<float> MaxHP = new(0f);
    public SyncVar<float> Mana = new(0f);
    public SyncVar<float> MaxMana = new(0f);

    // Identity
    public SyncVar<int> CharId = new(0);
    public SyncVar<byte> TeamId = new(0);
    public SyncVar<string> PlayerName = new("");
    public SyncVar<int> Level = new(1);
    public SyncVar<int> Gold = new(0, SyncTarget.Owner);
    public SyncVar<int> Kills = new(0);
    public SyncVar<int> Deaths = new(0);
    public SyncVar<int> Assists = new(0);
    public SyncVar<bool> IsDead = new(false);
    public SyncVar<float> RespawnTimer = new(0f, SyncTarget.Owner);
    public SyncVar<byte> FormId = new(0);

    // Item slots (item IDs, -1 = empty) — synced via RPC
    public int[] Items = { -1, -1, -1, -1, -1, -1 };

    // Server-side state (not synced)
    internal float TargetX, TargetY;
    internal bool IsMoving;
    internal float AttackCooldown;
    internal float[] AbilityCooldowns = new float[4];
    internal float BuffArmorBonus;
    internal float BuffTimer;
    internal float XP;
    internal float BonusAD, BonusArmor, BonusSpeed;
    internal uint AutoAttackTargetId;
    internal float KitBonusSpeed;
    internal float SlowAmount;
    internal float SlowTimer;
    internal float TransformLockout;
    internal bool TakedownEmpowered;
    internal object? KitData;

    // ── Commands (client → server) ──

    [EntityCommand]
    public void CmdMove(float targetX, float targetY)
    {
        TargetX = Math.Clamp(targetX, 0, GameConfig.MapWidth);
        TargetY = Math.Clamp(targetY, 0, GameConfig.MapHeight);
        IsMoving = true;
        AutoAttackTargetId = 0;
    }

    [EntityCommand]
    public void CmdAbility(int slot, float targetX, float targetY)
    {
        if (IsDead || slot < 0 || slot >= 4) return;
        AutoAttackTargetId = 0;
        MobaServer.HandleAbility(this, slot, targetX, targetY);
    }

    [EntityCommand]
    public void CmdBuyItem(int itemId)
    {
        if (IsDead) return;
        MobaServer.HandleBuyItem(this, itemId);
    }

    [EntityCommand]
    public void CmdSellItem(int slotIndex)
    {
        if (IsDead) return;
        MobaServer.HandleSellItem(this, slotIndex);
    }

    [EntityCommand]
    public void CmdAttackTarget(uint targetNetId)
    {
        if (IsDead) return;
        AutoAttackTargetId = targetNetId;
    }

    // ── RPCs (server → client) ──

    [EntityRpc(Target = RpcTarget.Owner)]
    public void RpcItemUpdate(int slot, int itemId)
    {
        if (slot >= 0 && slot < 6)
            Items[slot] = itemId;
    }

    [EntityRpc(Target = RpcTarget.Observers)]
    public void RpcAbilityEffect(float x, float y, float radius)
    {
        LastAbilityX = x;
        LastAbilityY = y;
        LastAbilityRadius = radius;
        AbilityEffectTimer = 0.5f;
    }

    [EntityRpc(Target = RpcTarget.Observers)]
    public void RpcDied(string killerName)
    {
        LastDeathMessage = $"{PlayerName} was slain by {killerName}";
    }

    [EntityRpc(Target = RpcTarget.Owner)]
    public void RpcCooldowns(float q, float w, float e, float r)
    {
        ClientCooldowns[0] = q;
        ClientCooldowns[1] = w;
        ClientCooldowns[2] = e;
        ClientCooldowns[3] = r;
    }

    // Client-side display state
    public float LastAbilityX, LastAbilityY, LastAbilityRadius;
    public float AbilityEffectTimer;
    public string? LastDeathMessage;
    public float[] ClientCooldowns = new float[4];

    // Kit access (works on both client and server)
    public CharacterKit? GetKit() => CharId >= 0 && CharId < GameConfig.Kits.Length
        ? GameConfig.Kits[CharId] : null;
}

// ── Minion Entity ──

public class MinionEntity : NetworkEntity
{
    public SyncVarInterpolated X = new(0f, interpSpeed: 15f) { SyncInterval = 0.05f };
    public SyncVarInterpolated Y = new(0f, interpSpeed: 15f) { SyncInterval = 0.05f };
    public SyncVar<float> HP = new(0f);
    public SyncVar<float> MaxHP = new(0f);
    public SyncVar<byte> TeamId = new(0);
    public SyncVar<byte> MinionTypeId = new(0);

    // Server-side
    internal float TargetX;
    internal bool ReachedEnd;
    internal float AttackCooldown;
    internal uint AttackTargetId;
}

// ── Turret Entity ──

public class TurretEntity : NetworkEntity
{
    public SyncVar<float> X = new(0f);
    public SyncVar<float> Y = new(0f);
    public SyncVar<float> HP = new(0f);
    public SyncVar<float> MaxHP = new(0f);
    public SyncVar<byte> TeamId = new(0);
    public int LaneIndex;
    public int TurretIndex;

    // Server-side
    internal float AttackCooldown;
    internal uint AttackTargetId;
    internal bool IsDestroyed;

    [EntityRpc(Target = RpcTarget.Observers)]
    public void RpcAttack(float targetX, float targetY)
    {
        LastAttackX = targetX;
        LastAttackY = targetY;
        AttackEffectTimer = 0.2f;
    }

    public float LastAttackX, LastAttackY;
    public float AttackEffectTimer;
}

// ── Nexus Entity ──

public class NexusEntity : NetworkEntity
{
    public SyncVar<float> X = new(0f);
    public SyncVar<float> Y = new(0f);
    public SyncVar<float> HP = new(0f);
    public SyncVar<float> MaxHP = new(0f);
    public SyncVar<byte> TeamId = new(0);
}

// ── Projectile Entity ──

public class ProjectileEntity : NetworkEntity
{
    public SyncVarInterpolated X = new(0f, interpSpeed: 20f) { SyncInterval = 0.05f };
    public SyncVarInterpolated Y = new(0f, interpSpeed: 20f) { SyncInterval = 0.05f };
    public SyncVar<byte> TeamId = new(0);
    public SyncVar<uint> TargetNetId = new(0);

    // Server-side
    internal float Damage;
    internal float Speed;
    internal NetworkEntity? Source;
}

// ── Javelin Entity (Nidaloo Q — straight-line skillshot) ──

public class JavelinEntity : NetworkEntity
{
    public SyncVarInterpolated X = new(0f, interpSpeed: 20f) { SyncInterval = 0.05f };
    public SyncVarInterpolated Y = new(0f, interpSpeed: 20f) { SyncInterval = 0.05f };
    public SyncVar<float> DirX = new(0f);
    public SyncVar<float> DirY = new(0f);
    public SyncVar<byte> TeamId = new(0);

    // Server-side
    internal float Damage;
    internal float Speed;
    internal float MaxRange;
    internal float DistanceTraveled;
    internal float SpawnX, SpawnY;
    internal ChampionEntity? Source;
}

// ── Trap Entity (Nidaloo W — ground trap) ──

public class TrapEntity : NetworkEntity
{
    public SyncVar<float> X = new(0f);
    public SyncVar<float> Y = new(0f);
    public SyncVar<byte> TeamId = new(0);

    // Server-side
    internal float Damage;
    internal float TriggerRadius;
    internal float SlowAmount;
    internal float SlowDuration;
    internal float Lifetime;
    internal ChampionEntity? Source;

    [EntityRpc(Target = RpcTarget.Observers)]
    public void RpcTriggered()
    {
        // Client can play an effect
    }
}
