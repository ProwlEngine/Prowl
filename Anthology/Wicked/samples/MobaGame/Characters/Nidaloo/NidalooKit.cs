using Prowl.Wicked;

namespace MobaGame;

public class NidalooState
{
    public Dictionary<uint, float> HuntedTargets = new();
    public float[] OtherFormCooldowns = new float[4];
    public List<TrapEntity> ActiveTraps = new();
    public bool SmackdownEmpowered;
}

/// <summary>
/// Nidaloo — The Beastly Huntress.
/// Two forms: Human (ranged, id=0) and Big Cat (melee, id=1).
///
/// Passive (Sneaky Feet): Hitting enemies with Pointy Stick Toss or Shrub Smack marks them as
/// Hunted for 4 seconds. Nidaloo gains bonus move speed toward Hunted targets.
/// Big Cat abilities are enhanced against Hunted targets.
///
/// Human Form:
///   Q - Pointy Stick Toss: Straight-line skillshot projectile. Damage scales with distance (up to 2x).
///                           Marks hit enemy as Hunted.
///   W - Shrub Smack:       Places a ground trap (max 3). When sprung: damage, slow, marks Hunted.
///   E - Primal Purr:       Heals self or target ally.
///   R - Big Cat Form:      Transform to Big Cat form (1s transition lockout).
///
/// Big Cat Form:
///   Q - Smackdown:    Empowers next basic attack to deal bonus damage (more vs low HP + Hunted).
///   W - Bouncy Leap:  Instant leap in mouse direction, AoE damage on landing.
///   E - Claw Swat:    Instant AoE claw in mouse direction.
///   R - Smol Human Form: Transform back to Human form (1s transition lockout).
/// </summary>
public class NidalooKit : CharacterKit
{
    public const float HuntedDuration = 4f;
    public const float HuntedSpeedBonus = 3f;
    public const float TransformLockoutTime = 1f;
    public const int MaxTraps = 3;
    public const float TrapLifetime = 120f;
    public const float TrapTriggerRadius = 1.5f;
    public const float TrapSlowAmount = 5f;
    public const float TrapSlowDuration = 2f;
    public const float JavelinSpeed = 20f;
    public const float JavelinMaxRange = 75f;
    public const float JavelinHitRadius = 1f;

    public override string Name => "Nidaloo";
    public override float BaseHP => 570f;
    public override float BaseMana => 350f;
    public override float BaseArmor => 28f;

    public override float GetMoveSpeed(ChampionEntity champ)
        => champ.FormId == 0 ? 16f : 18f;

    public override float GetAD(ChampionEntity champ)
        => champ.FormId == 0 ? 45f : 55f;

    public override float GetAttackRange(ChampionEntity champ)
        => champ.FormId == 0 ? 25f : 6f;

    public override bool GetIsRanged(ChampionEntity champ)
        => champ.FormId == 0;

    public override int GetAbilityCount(ChampionEntity champ) => 4;

    public override void OnSpawn(ChampionEntity champ)
    {
        champ.FormId.Value = 0;
        champ.KitData = new NidalooState();
    }

    private NidalooState GetState(ChampionEntity champ)
    {
        if (champ.KitData is not NidalooState state)
        {
            state = new NidalooState();
            champ.KitData = state;
        }
        return state;
    }

    // ── Ability Info ──

    public override AbilityInfo GetAbility(ChampionEntity champ, int slot)
    {
        if (champ.FormId == 0)
        {
            return slot switch {
                0 => new AbilityInfo { Name = "Pointy Stick Toss", ManaCost = 70, Cooldown = 6, Range = 30, Radius = 0, CastMode = CastMode.AreaTarget },
                1 => new AbilityInfo { Name = "Shrub Smack", ManaCost = 40, Cooldown = 13, Range = 20, Radius = 1.5f, CastMode = CastMode.AreaTarget },
                2 => new AbilityInfo { Name = "Primal Purr", ManaCost = 60, Cooldown = 12, Range = 30, Radius = 0, CastMode = CastMode.AllyTarget },
                3 => new AbilityInfo { Name = "Big Cat Form", ManaCost = 0, Cooldown = 3, Range = 0, Radius = 0, CastMode = CastMode.Instant },
                _ => default
            };
        }
        else
        {
            return slot switch {
                0 => new AbilityInfo { Name = "Smackdown", ManaCost = 0, Cooldown = 5, Range = 0, Radius = 0, CastMode = CastMode.Instant },
                1 => new AbilityInfo { Name = "Bouncy Leap", ManaCost = 0, Cooldown = 5, Range = 10, Radius = 3, CastMode = CastMode.InstantDirection },
                2 => new AbilityInfo { Name = "Claw Swat", ManaCost = 0, Cooldown = 5, Range = 6, Radius = 4, CastMode = CastMode.InstantDirection },
                3 => new AbilityInfo { Name = "Smol Human Form", ManaCost = 0, Cooldown = 3, Range = 0, Radius = 0, CastMode = CastMode.Instant },
                _ => default
            };
        }
    }

    // ── Tick ──

    public override void OnTick(GameRoom room, ChampionEntity champ, float dt)
    {
        var state = GetState(champ);

        // Tick transform lockout
        if (champ.TransformLockout > 0)
            champ.TransformLockout -= dt;

        // Tick slow
        if (champ.SlowTimer > 0)
        {
            champ.SlowTimer -= dt;
            if (champ.SlowTimer <= 0)
                champ.SlowAmount = 0;
        }

        // Tick down Hunted durations
        var expired = new List<uint>();
        foreach (var (id, remaining) in state.HuntedTargets)
        {
            state.HuntedTargets[id] = remaining - dt;
            if (remaining - dt <= 0) expired.Add(id);
        }
        foreach (var id in expired)
            state.HuntedTargets.Remove(id);

        // Tick down other form's cooldowns
        for (int i = 0; i < 4; i++)
            if (state.OtherFormCooldowns[i] > 0)
                state.OtherFormCooldowns[i] -= dt;

        // Clean up despawned traps
        state.ActiveTraps.RemoveAll(t => !t.IsSpawned);

        // Tick traps (check for enemies walking over them)
        foreach (var trap in state.ActiveTraps.ToArray())
        {
            if (!trap.IsSpawned) continue;
            trap.Lifetime -= dt;
            if (trap.Lifetime <= 0)
            {
                Server.Despawn(trap);
                continue;
            }

            // Check for enemy triggers
            bool triggered = false;
            foreach (var enemy in room.Champions)
            {
                if (!enemy.IsSpawned || enemy.IsDead || enemy.TeamId == trap.TeamId) continue;
                if (CombatUtils.Dist(trap.X, trap.Y, enemy.X, enemy.Y) <= trap.TriggerRadius)
                {
                    float armor = CombatUtils.GetEntityArmor(enemy);
                    CombatUtils.ApplyDamage(room, trap.Source ?? champ, enemy, GameConfig.CalculateDamage(trap.Damage, armor));
                    enemy.SlowAmount = trap.SlowAmount;
                    enemy.SlowTimer = trap.SlowDuration;
                    state.HuntedTargets[enemy.NetworkId] = HuntedDuration;
                    triggered = true;
                    break;
                }
            }
            if (!triggered)
            {
                foreach (var minion in room.Minions.ToArray())
                {
                    if (!minion.IsSpawned || minion.HP <= 0 || minion.TeamId == trap.TeamId) continue;
                    if (CombatUtils.Dist(trap.X, trap.Y, minion.X, minion.Y) <= trap.TriggerRadius)
                    {
                        CombatUtils.ApplyDamage(room, trap.Source ?? champ, minion, GameConfig.CalculateDamage(trap.Damage, 0));
                        state.HuntedTargets[minion.NetworkId] = HuntedDuration;
                        triggered = true;
                        break;
                    }
                }
            }
            if (triggered)
            {
                trap.RpcTriggered();
                Server.Despawn(trap);
            }
        }

        // Tick javelins
        for (int i = room.Javelins.Count - 1; i >= 0; i--)
        {
            var jav = room.Javelins[i];
            if (!jav.IsSpawned || jav.Source != champ) continue;
            // Javelin movement is handled in GameSimulation.TickJavelins
        }

        // Bonus move speed toward Hunted targets
        champ.KitBonusSpeed = 0;
        if (state.HuntedTargets.Count > 0)
        {
            foreach (var (id, _) in state.HuntedTargets)
            {
                var target = CombatUtils.FindEntityById(room, id);
                if (target != null)
                {
                    float dist = CombatUtils.DistTo(champ, target);
                    if (dist < 40f)
                    {
                        champ.KitBonusSpeed = HuntedSpeedBonus;
                        break;
                    }
                }
            }
        }
    }

    // ── Ability Execution ──

    public override void OnAbility(GameRoom room, ChampionEntity champ, int slot, float tx, float ty)
    {
        // Block abilities during transform lockout
        if (champ.TransformLockout > 0) return;

        if (champ.FormId == 0)
            HumanAbility(room, champ, slot, tx, ty);
        else
            BigCatAbility(room, champ, slot, tx, ty);
    }

    private void HumanAbility(GameRoom room, ChampionEntity champ, int slot, float tx, float ty)
    {
        switch (slot)
        {
            case 0: PointyStickToss(room, champ, tx, ty); break;
            case 1: ShrubSmack(room, champ, tx, ty); break;
            case 2: PrimalPurr(room, champ, tx, ty); break;
            case 3: Transform(room, champ, 1); break;
        }
    }

    private void BigCatAbility(GameRoom room, ChampionEntity champ, int slot, float tx, float ty)
    {
        switch (slot)
        {
            case 0: Smackdown(room, champ); break;
            case 1: BouncyLeap(room, champ, tx, ty); break;
            case 2: ClawSwat(room, champ, tx, ty); break;
            case 3: Transform(room, champ, 0); break;
        }
    }

    // ── Human Q: Pointy Stick Toss ──
    // Straight-line skillshot projectile. Damage scales 1x-2x based on distance traveled.
    // Marks first enemy hit as Hunted.

    private void PointyStickToss(GameRoom room, ChampionEntity champ, float tx, float ty)
    {
        if (room.Map == null) return;

        float dx = tx - champ.X;
        float dy = ty - champ.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        if (dist < 0.1f) { dx = 1; dy = 0; dist = 1; }

        float dirX = dx / dist;
        float dirY = dy / dist;

        float baseDmg = 70f + champ.Level * 15f;

        var jav = Server.Spawn<JavelinEntity>(room.Map, j => {
            j.X.Value = champ.X;
            j.Y.Value = champ.Y;
            j.DirX.Value = dirX;
            j.DirY.Value = dirY;
            j.TeamId.Value = champ.TeamId;
            j.Damage = baseDmg;
            j.Speed = JavelinSpeed;
            j.MaxRange = JavelinMaxRange;
            j.DistanceTraveled = 0;
            j.SpawnX = champ.X;
            j.SpawnY = champ.Y;
            j.Source = champ;
        });
        room.Javelins.Add(jav);
    }

    // ── Human W: Shrub Smack ──
    // Places a ground trap. Max 3 traps. When triggered: damage, slow, mark Hunted.

    private void ShrubSmack(GameRoom room, ChampionEntity champ, float tx, float ty)
    {
        if (room.Map == null) return;
        var state = GetState(champ);

        float dx = tx - champ.X;
        float dy = ty - champ.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        float range = 20f;
        if (dist > range && dist > 0)
        {
            tx = champ.X + dx / dist * range;
            ty = champ.Y + dy / dist * range;
        }

        // Remove oldest trap if at max
        while (state.ActiveTraps.Count >= MaxTraps)
        {
            var oldest = state.ActiveTraps[0];
            if (oldest.IsSpawned) Server.Despawn(oldest);
            state.ActiveTraps.RemoveAt(0);
        }

        float dmg = 40f + champ.Level * 10f;
        var trap = Server.Spawn<TrapEntity>(room.Map, t => {
            t.X.Value = tx;
            t.Y.Value = ty;
            t.TeamId.Value = champ.TeamId;
            t.Damage = dmg;
            t.TriggerRadius = TrapTriggerRadius;
            t.SlowAmount = TrapSlowAmount;
            t.SlowDuration = TrapSlowDuration;
            t.Lifetime = TrapLifetime;
            t.Source = champ;
        });
        state.ActiveTraps.Add(trap);
        room.Traps.Add(trap);
        champ.RpcAbilityEffect(tx, ty, TrapTriggerRadius);
    }

    // ── Human E: Primal Purr ──
    // Heals target champion (self or ally). tx, ty used to find nearest ally.

    private void PrimalPurr(GameRoom room, ChampionEntity champ, float tx, float ty)
    {
        float heal = 50f + champ.Level * 10f;

        // Find nearest allied champion to target point (including self)
        ChampionEntity? target = null;
        float bestDist = 5f; // must click within 5m of ally
        foreach (var ally in room.Champions)
        {
            if (!ally.IsSpawned || ally.IsDead || ally.TeamId != champ.TeamId) continue;
            float d = CombatUtils.Dist(tx, ty, ally.X, ally.Y);
            if (d < bestDist) { bestDist = d; target = ally; }
        }

        // Default to self if no ally found near click
        target ??= champ;

        target.HP.Value = Math.Min(target.MaxHP, target.HP + heal);
        champ.RpcAbilityEffect(target.X, target.Y, 0);
    }

    // ── Big Cat Q: Smackdown ──
    // Empowers next basic attack to deal bonus damage.
    // More damage vs low HP targets and Hunted.

    private void Smackdown(GameRoom room, ChampionEntity champ)
    {
        var state = GetState(champ);
        state.SmackdownEmpowered = true;
        champ.TakedownEmpowered = true;
        champ.RpcAbilityEffect(champ.X, champ.Y, 0);
    }

    // ── Big Cat W: Bouncy Leap ──
    // Instant leap in mouse direction. AoE damage on landing.
    // Extended range (15m) toward Hunted targets.

    private void BouncyLeap(GameRoom room, ChampionEntity champ, float tx, float ty)
    {
        var state = GetState(champ);
        float range = 10f;

        // Extended range if target area is near a Hunted enemy
        foreach (var (id, _) in state.HuntedTargets)
        {
            var huntedTarget = CombatUtils.FindEntityById(room, id);
            if (huntedTarget != null)
            {
                var (hx, hy) = CombatUtils.GetEntityPos(huntedTarget);
                float distToHunted = CombatUtils.Dist(tx, ty, hx, hy);
                if (distToHunted < 5f) { range = 15f; break; }
            }
        }

        float ddx = tx - champ.X;
        float ddy = ty - champ.Y;
        float ddist = MathF.Sqrt(ddx * ddx + ddy * ddy);
        if (ddist > range && ddist > 0)
        {
            tx = champ.X + ddx / ddist * range;
            ty = champ.Y + ddy / ddist * range;
        }

        float dmg = 60f + champ.Level * 10f;
        CombatUtils.DamageArea(room, champ, tx, ty, 3f, dmg);

        champ.X.Value = Math.Clamp(tx, 0, GameConfig.MapWidth);
        champ.Y.Value = Math.Clamp(ty, 0, GameConfig.MapHeight);
        champ.TargetX = champ.X;
        champ.TargetY = champ.Y;
        champ.IsMoving = false;
        champ.RpcAbilityEffect(champ.X, champ.Y, 3f);
    }

    // ── Big Cat E: Claw Swat ──
    // Instant AoE claw in mouse direction. Damage centered 3m in front of Nidaloo.

    private void ClawSwat(GameRoom room, ChampionEntity champ, float tx, float ty)
    {
        float dx = tx - champ.X;
        float dy = ty - champ.Y;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        float cx = champ.X, cy = champ.Y;
        if (dist > 0)
        {
            cx = champ.X + dx / dist * 3f;
            cy = champ.Y + dy / dist * 3f;
        }

        float dmg = 75f + champ.Level * 10f;
        CombatUtils.DamageArea(room, champ, cx, cy, 4f, dmg);
        champ.RpcAbilityEffect(cx, cy, 4f);
    }

    // ── R: Transform ──

    private void Transform(GameRoom room, ChampionEntity champ, byte newForm)
    {
        var state = GetState(champ);

        // Swap cooldowns between forms (Q, W, E only — R is shared)
        float[] temp = new float[4];
        Array.Copy(champ.AbilityCooldowns, temp, 4);
        Array.Copy(state.OtherFormCooldowns, champ.AbilityCooldowns, 3);
        state.OtherFormCooldowns[0] = temp[0];
        state.OtherFormCooldowns[1] = temp[1];
        state.OtherFormCooldowns[2] = temp[2];
        state.OtherFormCooldowns[3] = temp[3];

        champ.FormId.Value = newForm;
        champ.TransformLockout = TransformLockoutTime;
    }

    // ── Auto Attack Override ──
    // Smackdown empowers next melee auto attack

    public override void OnAutoAttack(GameRoom room, ChampionEntity champ, NetworkEntity target, float baseDamage)
    {
        var state = GetState(champ);

        if (state.SmackdownEmpowered && champ.FormId == 1)
        {
            // Bonus damage: base + level scaling, multiplied by missing HP and Hunted
            float bonusDmg = 80f + champ.Level * 20f;
            float multiplier = 1f;

            if (state.HuntedTargets.ContainsKey(target.NetworkId))
                multiplier += 0.33f;

            float targetHpRatio = GetHpRatio(target);
            // More damage the lower HP they are (up to +100% at 0 HP)
            multiplier += (1f - targetHpRatio);

            float armor = CombatUtils.GetEntityArmor(target);
            float totalDmg = baseDamage + GameConfig.CalculateDamage(bonusDmg * multiplier, armor);
            CombatUtils.ApplyDamage(room, champ, target, totalDmg);

            state.SmackdownEmpowered = false;
            champ.TakedownEmpowered = false;

            var (ex, ey) = CombatUtils.GetEntityPos(target);
            champ.RpcAbilityEffect(ex, ey, 0);
        }
        else
        {
            // Normal attack
            if (GetIsRanged(champ))
                CombatUtils.SpawnProjectile(room, champ, target, baseDamage);
            else
                CombatUtils.ApplyDamage(room, champ, target, baseDamage);
        }
    }

    // ── Helpers ──

    private void MarkHunted(ChampionEntity champ, uint targetId)
    {
        var state = GetState(champ);
        state.HuntedTargets[targetId] = HuntedDuration;
    }

    private static float GetHpRatio(NetworkEntity entity) => entity switch {
        ChampionEntity c => c.MaxHP > 0 ? c.HP / c.MaxHP : 1,
        MinionEntity m => m.MaxHP > 0 ? m.HP / m.MaxHP : 1,
        TurretEntity t => t.MaxHP > 0 ? t.HP / t.MaxHP : 1,
        NexusEntity n => n.MaxHP > 0 ? n.HP / n.MaxHP : 1,
        _ => 1
    };

    // Called by GameSimulation when a javelin hits
    public void OnJavelinHit(ChampionEntity champ, NetworkEntity target, float distanceTraveled, float baseDamage)
    {
        // Damage scales: 1x at point blank, 2x at max range
        float distRatio = Math.Clamp(distanceTraveled / JavelinMaxRange, 0, 1);
        float scaledDmg = baseDamage * (1f + distRatio);
        float armor = CombatUtils.GetEntityArmor(target);
        float dmg = GameConfig.CalculateDamage(scaledDmg, armor);
        // We need room to apply damage, but we get called from GameSimulation which has it
        // Just mark hunted here — damage is applied by caller
        MarkHunted(champ, target.NetworkId);
    }
}
