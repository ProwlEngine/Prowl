using Prowl.Wicked;

namespace MobaGame;

public enum CastMode
{
    Instant,          // Self-cast, fires immediately (e.g., transform, takedown empower)
    AreaTarget,       // Click ground to cast (e.g., Javelin direction, Bushwhack placement)
    DashTarget,       // Click ground for dash destination
    AllyTarget,       // Click ally or self to target (e.g., Primal Surge heal)
    InstantDirection, // Fires immediately toward current mouse position (e.g., Pounce, Swipe)
}

public struct AbilityInfo
{
    public string Name;
    public float ManaCost;
    public float Cooldown;
    public float Range;
    public float Radius;
    public CastMode CastMode;
}

public abstract class CharacterKit
{
    public abstract string Name { get; }
    public abstract float BaseHP { get; }
    public abstract float BaseMana { get; }
    public abstract float BaseArmor { get; }

    public abstract float GetMoveSpeed(ChampionEntity champ);
    public abstract float GetAD(ChampionEntity champ);
    public abstract float GetAttackRange(ChampionEntity champ);
    public abstract bool GetIsRanged(ChampionEntity champ);

    public abstract int GetAbilityCount(ChampionEntity champ);
    public abstract AbilityInfo GetAbility(ChampionEntity champ, int slot);

    // Server-side callbacks
    public abstract void OnAbility(GameRoom room, ChampionEntity champ, int slot, float tx, float ty);

    public virtual void OnTick(GameRoom room, ChampionEntity champ, float dt) { }

    public virtual void OnAutoAttack(GameRoom room, ChampionEntity champ, NetworkEntity target, float baseDamage)
    {
        if (GetIsRanged(champ))
            CombatUtils.SpawnProjectile(room, champ, target, baseDamage);
        else
            CombatUtils.ApplyDamage(room, champ, target, baseDamage);
    }

    public virtual void OnSpawn(ChampionEntity champ) { }
}
