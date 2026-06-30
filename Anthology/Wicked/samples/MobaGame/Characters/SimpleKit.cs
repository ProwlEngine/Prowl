namespace MobaGame;

/// <summary>
/// Wraps the old CharacterDef data into the CharacterKit interface.
/// Used for Vanguard, Sorcerer, Ranger.
/// </summary>
public class SimpleKit : CharacterKit
{
    private readonly CharacterDef _def;

    public SimpleKit(CharacterDef def) { _def = def; }

    public override string Name => _def.Name;
    public override float BaseHP => _def.BaseHP;
    public override float BaseMana => _def.BaseMana;
    public override float BaseArmor => _def.BaseArmor;

    public override float GetMoveSpeed(ChampionEntity champ) => _def.BaseMoveSpeed;
    public override float GetAD(ChampionEntity champ) => _def.BaseAD;
    public override float GetAttackRange(ChampionEntity champ) => _def.AttackRange;
    public override bool GetIsRanged(ChampionEntity champ) => _def.AttackRange > 7.5f;

    public override int GetAbilityCount(ChampionEntity champ) => _def.Abilities.Length;

    public override AbilityInfo GetAbility(ChampionEntity champ, int slot)
    {
        var ab = _def.Abilities[slot];
        return new AbilityInfo {
            Name = ab.Name,
            ManaCost = ab.ManaCost,
            Cooldown = ab.Cooldown,
            Range = ab.Range,
            Radius = ab.Radius,
            CastMode = ab.Type switch {
                AbilityType.Buff => CastMode.Instant,
                AbilityType.Dash => CastMode.DashTarget,
                _ => CastMode.AreaTarget,
            }
        };
    }

    public override void OnAbility(GameRoom room, ChampionEntity champ, int slot, float tx, float ty)
    {
        var ability = _def.Abilities[slot];

        switch (ability.Type)
        {
            case AbilityType.AreaDamage:
            {
                float dx = tx - champ.X;
                float dy = ty - champ.Y;
                float dist = MathF.Sqrt(dx * dx + dy * dy);
                if (dist > ability.Range && dist > 0)
                {
                    tx = champ.X + dx / dist * ability.Range;
                    ty = champ.Y + dy / dist * ability.Range;
                }
                CombatUtils.DamageArea(room, champ, tx, ty, ability.Radius, ability.Damage);
                champ.RpcAbilityEffect(tx, ty, ability.Radius);
                break;
            }
            case AbilityType.Buff:
            {
                champ.BuffArmorBonus = ability.BuffArmor;
                champ.BuffTimer = ability.BuffDuration;
                champ.RpcAbilityEffect(champ.X, champ.Y, 0);
                break;
            }
            case AbilityType.Dash:
            {
                float ddx = tx - champ.X;
                float ddy = ty - champ.Y;
                float ddist = MathF.Sqrt(ddx * ddx + ddy * ddy);
                if (ddist > ability.Range && ddist > 0)
                {
                    tx = champ.X + ddx / ddist * ability.Range;
                    ty = champ.Y + ddy / ddist * ability.Range;
                }
                float dashRadius = ability.Radius > 0 ? ability.Radius : 3f;
                if (ability.Damage > 0)
                    CombatUtils.DamageArea(room, champ, tx, ty, dashRadius, ability.Damage);
                champ.X.Value = Math.Clamp(tx, 0, GameConfig.MapWidth);
                champ.Y.Value = Math.Clamp(ty, 0, GameConfig.MapHeight);
                champ.TargetX = champ.X;
                champ.TargetY = champ.Y;
                champ.IsMoving = false;
                champ.RpcAbilityEffect(champ.X, champ.Y, dashRadius);
                break;
            }
        }
    }
}
