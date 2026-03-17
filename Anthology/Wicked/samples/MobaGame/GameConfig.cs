namespace MobaGame;

// ── Enums ──

public enum Team : byte { Blue = 0, Red = 1 }
public enum GamePhase : byte { CharSelect, Playing, Finished }
public enum AbilityType : byte { AreaDamage, Buff, Dash, Projectile }
public enum EntityKind : byte { Champion, Minion, Turret, Nexus, Projectile }
public enum MinionType : byte { Melee, Caster }

// ── Character Definitions ──

public static class GameConfig
{
    public const int TeamSize = 1;

    // Map dimensions (meters)
    public const float MapWidth = 200f;
    public const float MapHeight = 75f;
    public const float LaneY = 37.5f;

    // Bases
    public const float BlueBaseX = 10f;
    public const float RedBaseX = 190f;
    public const float BaseRadius = 15f;

    // Nexus
    public const float BlueNexusX = 15f;
    public const float RedNexusX = 185f;
    public const float NexusHP = 3000f;

    // Turrets (x positions along the lane)
    public static readonly float[] BlueTurretX = { 30f, 55f, 85f };
    public static readonly float[] RedTurretX = { 170f, 145f, 115f };
    public const float TurretHP = 2000f;
    public const float TurretAD = 60f;
    public const float TurretRange = 12.5f;
    public const float TurretAttackSpeed = 1f;

    // Minion spawning
    public const float MinionSpawnInterval = 30f;
    public const float FirstMinionSpawnTime = 5f;
    public const int MeleeMinionsPerWave = 2;
    public const int CasterMinionsPerWave = 3;

    // Minion stats
    public const float MeleeMinionHP = 400f;
    public const float MeleeMinionAD = 20f;
    public const float MeleeMinionRange = 2f;
    public const float MeleeMinionSpeed = 7.5f;

    public const float CasterMinionHP = 280f;
    public const float CasterMinionAD = 35f;
    public const float CasterMinionRange = 8f;
    public const float CasterMinionSpeed = 7.5f;

    // Projectiles
    public const float ProjectileSpeed = 30f;
    public const float ProjectileRadius = 0.25f;

    // Champion leveling
    public const int MaxLevel = 18;
    public const float XpPerLevel = 100f;
    public const float HpPerLevel = 40f;
    public const float AdPerLevel = 5f;
    public const float ArmorPerLevel = 2f;

    // Gold
    public const int StartingGold = 500;
    public const float PassiveGoldPerSec = 2f;
    public const int MinionKillGold = 22;
    public const int ChampionKillBaseGold = 300;
    public const int ChampionKillGoldPerLevel = 20;
    public const float MinionKillXP = 30f;
    public const float ChampionKillBaseXP = 200f;
    public const float ChampionKillXPPerLevel = 20f;

    // Respawn
    public const float BaseRespawnTime = 8f;
    public const float RespawnTimePerLevel = 2f;

    // Auto-attack
    public const float AutoAttackSpeed = 1f;

    // Char select
    public const float CharSelectTime = 60f;

    // Sync rate
    public const float SyncInterval = 0.05f;

    // Shop radius (must be near base to shop)
    public const float ShopRadius = 20f;

    // Characters
    public static readonly CharacterDef[] Characters =
    {
        new CharacterDef
        {
            Id = 0,
            Name = "Vanguard",
            BaseHP = 700, BaseMana = 250, BaseMoveSpeed = 15f, BaseAD = 60, BaseArmor = 40,
            AttackRange = 6f,
            Abilities = new[]
            {
                new AbilityDef { Name = "Cleave",       Type = AbilityType.AreaDamage, Damage = 80,  Radius = 7.5f,  Range = 7.5f,  Cooldown = 5,  ManaCost = 40 },
                new AbilityDef { Name = "Iron Guard",   Type = AbilityType.Buff,       Damage = 0,   Radius = 0,     Range = 0,     Cooldown = 12, ManaCost = 50, BuffArmor = 30, BuffDuration = 4 },
                new AbilityDef { Name = "Shield Charge", Type = AbilityType.Dash,      Damage = 60,  Radius = 3f,    Range = 15f,   Cooldown = 10, ManaCost = 60 },
                new AbilityDef { Name = "Earthquake",   Type = AbilityType.AreaDamage, Damage = 200, Radius = 12.5f, Range = 12.5f, Cooldown = 60, ManaCost = 100 },
            }
        },
        new CharacterDef
        {
            Id = 1,
            Name = "Sorcerer",
            BaseHP = 500, BaseMana = 400, BaseMoveSpeed = 16f, BaseAD = 40, BaseArmor = 20,
            AttackRange = 25f,
            Abilities = new[]
            {
                new AbilityDef { Name = "Fireball",    Type = AbilityType.AreaDamage, Damage = 100, Radius = 4f,  Range = 30f, Cooldown = 4,  ManaCost = 50 },
                new AbilityDef { Name = "Frost Ring",  Type = AbilityType.AreaDamage, Damage = 70,  Radius = 10f, Range = 25f, Cooldown = 8,  ManaCost = 70 },
                new AbilityDef { Name = "Blink",       Type = AbilityType.Dash,       Damage = 0,   Radius = 0,   Range = 20f, Cooldown = 14, ManaCost = 80 },
                new AbilityDef { Name = "Inferno",     Type = AbilityType.AreaDamage, Damage = 300, Radius = 15f, Range = 30f, Cooldown = 80, ManaCost = 120 },
            }
        },
        new CharacterDef
        {
            Id = 2,
            Name = "Ranger",
            BaseHP = 550, BaseMana = 300, BaseMoveSpeed = 17f, BaseAD = 55, BaseArmor = 25,
            AttackRange = 22.5f,
            Abilities = new[]
            {
                new AbilityDef { Name = "Power Shot", Type = AbilityType.AreaDamage, Damage = 90,  Radius = 3f,    Range = 35f, Cooldown = 6,  ManaCost = 45 },
                new AbilityDef { Name = "Trap",       Type = AbilityType.AreaDamage, Damage = 80,  Radius = 4f,    Range = 20f, Cooldown = 16, ManaCost = 60 },
                new AbilityDef { Name = "Tumble",     Type = AbilityType.Dash,       Damage = 0,   Radius = 0,     Range = 10f, Cooldown = 8,  ManaCost = 40 },
                new AbilityDef { Name = "Arrow Rain", Type = AbilityType.AreaDamage, Damage = 150, Radius = 12.5f, Range = 30f, Cooldown = 70, ManaCost = 100 },
            }
        },
    };

    // Items (BonusSpeed is now in meters/sec)
    public static readonly ItemDef[] Items =
    {
        new ItemDef { Id = 0, Name = "Long Sword",  Cost = 500,  BonusAD = 20 },
        new ItemDef { Id = 1, Name = "Chain Vest",  Cost = 600,  BonusArmor = 25 },
        new ItemDef { Id = 2, Name = "Swift Boots", Cost = 400,  BonusSpeed = 2.5f },
    };

    public const int MaxItemSlots = 6;

    // Character kits (indexed by CharId)
    public static readonly CharacterKit[] Kits =
    {
        new SimpleKit(Characters[0]), // 0 = Vanguard
        new SimpleKit(Characters[1]), // 1 = Sorcerer
        new SimpleKit(Characters[2]), // 2 = Ranger
        new NidalooKit(),             // 3 = Nidaloo
    };

    public static float CalculateDamage(float rawDamage, float armor)
    {
        return rawDamage * 100f / (100f + Math.Max(0, armor));
    }
}

public class CharacterDef
{
    public int Id;
    public string Name = "";
    public float BaseHP, BaseMana, BaseMoveSpeed, BaseAD, BaseArmor;
    public float AttackRange;
    public AbilityDef[] Abilities = Array.Empty<AbilityDef>();
}

public class AbilityDef
{
    public string Name = "";
    public AbilityType Type;
    public float Damage;
    public float Radius;
    public float Range;
    public float Cooldown;
    public float ManaCost;
    public float BuffArmor;
    public float BuffDuration;
}

public class ItemDef
{
    public int Id;
    public string Name = "";
    public int Cost;
    public float BonusAD;
    public float BonusArmor;
    public float BonusSpeed;
}
