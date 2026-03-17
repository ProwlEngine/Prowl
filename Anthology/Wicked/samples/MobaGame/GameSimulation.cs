using Prowl.Wicked;

namespace MobaGame;

/// <summary>
/// Server-side game tick: champion movement/combat, minions, turrets, projectiles, spawning, sync.
/// All coordinates in meters (1 unit = 1 meter).
/// </summary>
public static class GameSimulation
{
    public static void Tick(GameRoom room, float dt)
    {
        room.GameTimer += dt;

        foreach (var champ in room.Champions)
            TickChampion(room, champ, dt);

        for (int i = room.Minions.Count - 1; i >= 0; i--)
        {
            if (!room.Minions[i].IsSpawned) { room.Minions.RemoveAt(i); continue; }
            TickMinion(room, room.Minions[i], dt);
        }

        // Minion collision separation
        SeparateMinions(room);

        foreach (var turret in room.Turrets)
        {
            if (turret.IsSpawned && !turret.IsDestroyed)
                TickTurret(room, turret, dt);
        }

        TickProjectiles(room, dt);
        TickJavelins(room, dt);
        CleanupTraps(room);

        room.MinionSpawnTimer -= dt;
        if (room.MinionSpawnTimer <= 0)
        {
            room.MinionSpawnTimer = GameConfig.MinionSpawnInterval;
            SpawnMinionWave(room, Team.Blue);
            SpawnMinionWave(room, Team.Red);
        }

        // Cooldown sync (still via RPC since it's an array)
        room.CooldownSyncTimer -= dt;
        if (room.CooldownSyncTimer <= 0)
        {
            room.CooldownSyncTimer = GameConfig.SyncInterval;
            SyncCooldowns(room);
        }

        CheckWinCondition(room);
    }

    // ── Champion ──

    private static void TickChampion(GameRoom room, ChampionEntity champ, float dt)
    {
        if (!champ.IsSpawned) return;
        var kit = champ.GetKit();

        if (champ.IsDead)
        {
            champ.RespawnTimer.Value -= dt;
            if (champ.RespawnTimer <= 0)
                RespawnChampion(room, champ);
            return;
        }

        // Passive gold
        champ.Gold.Value += (int)(GameConfig.PassiveGoldPerSec * dt * 100) / 100;
        if ((int)(room.GameTimer) > (int)(room.GameTimer - dt))
            champ.Gold.Value += (int)GameConfig.PassiveGoldPerSec;

        // Buff timer
        if (champ.BuffTimer > 0)
        {
            champ.BuffTimer -= dt;
            if (champ.BuffTimer <= 0)
                champ.BuffArmorBonus = 0;
        }

        // Mana regen
        champ.Mana.Value = Math.Min(champ.MaxMana, champ.Mana + dt);

        // Ability cooldowns
        for (int i = 0; i < 4; i++)
            if (champ.AbilityCooldowns[i] > 0)
                champ.AbilityCooldowns[i] -= dt;

        // Kit tick (e.g. Nidaloo Hunted timers, bonus speed)
        kit?.OnTick(room, champ, dt);

        float speed = (kit?.GetMoveSpeed(champ) ?? 15f) + champ.BonusSpeed + champ.KitBonusSpeed;
        if (champ.SlowTimer > 0)
            speed = Math.Max(speed - champ.SlowAmount, speed * 0.3f); // slow can't reduce below 30% base
        float attackRange = kit?.GetAttackRange(champ) ?? 6f;
        bool isRanged = kit?.GetIsRanged(champ) ?? false;

        // Walk-to-target: if we have an attack target, walk toward it until in range
        if (champ.AutoAttackTargetId != 0 && !champ.IsMoving)
        {
            var attackTarget = CombatUtils.FindEnemyEntity(room, champ.AutoAttackTargetId, champ.TeamId);
            if (attackTarget != null)
            {
                float distToTarget = CombatUtils.DistTo(champ, attackTarget);
                if (distToTarget > attackRange)
                {
                    var (tx, ty) = CombatUtils.GetEntityPos(attackTarget);
                    float dx = tx - champ.X;
                    float dy = ty - champ.Y;
                    float dist = MathF.Sqrt(dx * dx + dy * dy);
                    if (dist > 0)
                    {
                        float move = speed * dt;
                        champ.X.Value += dx / dist * move;
                        champ.Y.Value += dy / dist * move;
                    }
                }
            }
            else
            {
                champ.AutoAttackTargetId = 0;
            }
        }

        // Movement (from right-click move command)
        if (champ.IsMoving)
        {
            float dx = champ.TargetX - champ.X;
            float dy = champ.TargetY - champ.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            if (dist > 0.25f)
            {
                float move = speed * dt;
                if (move >= dist) { champ.X.Value = champ.TargetX; champ.Y.Value = champ.TargetY; champ.IsMoving = false; }
                else { champ.X.Value += dx / dist * move; champ.Y.Value += dy / dist * move; }
            }
            else
            {
                champ.IsMoving = false;
            }
        }

        // Attack only explicit target
        champ.AttackCooldown -= dt;
        if (champ.AttackCooldown <= 0 && champ.AutoAttackTargetId != 0)
        {
            var target = CombatUtils.FindEnemyEntity(room, champ.AutoAttackTargetId, champ.TeamId);
            if (target != null && CombatUtils.DistTo(champ, target) <= attackRange)
            {
                float ad = (kit?.GetAD(champ) ?? 50f) + (champ.Level - 1) * GameConfig.AdPerLevel + champ.BonusAD;
                float armor = CombatUtils.GetEntityArmor(target);
                float dmg = GameConfig.CalculateDamage(ad, armor);
                champ.AttackCooldown = 1f / GameConfig.AutoAttackSpeed;

                if (kit != null)
                    kit.OnAutoAttack(room, champ, target, dmg);
                else if (isRanged)
                    CombatUtils.SpawnProjectile(room, champ, target, dmg);
                else
                    CombatUtils.ApplyDamage(room, champ, target, dmg);
            }
        }
    }

    private static void RespawnChampion(GameRoom room, ChampionEntity champ)
    {
        float spawnX = champ.TeamId == (byte)Team.Blue
            ? GameConfig.BlueBaseX + 5f
            : GameConfig.RedBaseX - 5f;

        champ.IsDead.Value = false;
        champ.X.ResetInterpolation();
        champ.Y.ResetInterpolation();
        champ.X.Value = spawnX;
        champ.Y.Value = GameConfig.LaneY;
        champ.TargetX = spawnX;
        champ.TargetY = GameConfig.LaneY;
        champ.HP.Value = champ.MaxHP;
        champ.Mana.Value = champ.MaxMana;
    }

    // ── Minion ──

    private static void TickMinion(GameRoom room, MinionEntity minion, float dt)
    {
        if (!minion.IsSpawned || minion.HP <= 0) return;

        bool isBlue = minion.TeamId == (byte)Team.Blue;
        bool isCaster = minion.MinionTypeId == 1;
        float goalX = isBlue ? GameConfig.RedBaseX : GameConfig.BlueBaseX;
        float range = isCaster ? GameConfig.CasterMinionRange : GameConfig.MeleeMinionRange;
        float ad = isCaster ? GameConfig.CasterMinionAD : GameConfig.MeleeMinionAD;
        float speed = isCaster ? GameConfig.CasterMinionSpeed : GameConfig.MeleeMinionSpeed;

        var target = CombatUtils.FindClosestEnemy(room, minion.X, minion.Y, range, minion.TeamId);

        if (target != null)
        {
            minion.AttackCooldown -= dt;
            if (minion.AttackCooldown <= 0)
            {
                float armor = CombatUtils.GetEntityArmor(target);
                float dmg = GameConfig.CalculateDamage(ad, armor);
                minion.AttackCooldown = 1f;

                if (isCaster)
                    CombatUtils.SpawnProjectile(room, minion, target, dmg);
                else
                    CombatUtils.ApplyDamage(room, minion, target, dmg);
            }
        }
        else
        {
            float dx = goalX - minion.X;
            float dist = MathF.Abs(dx);
            if (dist > 0.25f)
            {
                float move = speed * dt;
                minion.X.Value += MathF.Sign(dx) * MathF.Min(move, dist);
            }
            float dy = GameConfig.LaneY - minion.Y;
            if (MathF.Abs(dy) > 0.1f)
                minion.Y.Value += MathF.Sign(dy) * MathF.Min(speed * dt * 0.5f, MathF.Abs(dy));
        }
    }

    // ── Turret ──

    private static void TickTurret(GameRoom room, TurretEntity turret, float dt)
    {
        turret.AttackCooldown -= dt;
        if (turret.AttackCooldown > 0) return;

        // All turrets can shoot enemies in range — CanTurretBeAttacked only gates incoming damage
        var target = CombatUtils.FindClosestEnemy(room, turret.X, turret.Y, GameConfig.TurretRange, turret.TeamId);
        if (target != null)
        {
            float dmg = GameConfig.CalculateDamage(GameConfig.TurretAD, CombatUtils.GetEntityArmor(target));
            turret.AttackCooldown = 1f / GameConfig.TurretAttackSpeed;
            CombatUtils.SpawnProjectile(room, turret, target, dmg);
            var (tx, ty) = CombatUtils.GetEntityPos(target);
            turret.RpcAttack(tx, ty);
        }
    }

    // ── Projectiles ──

    private static void TickProjectiles(GameRoom room, float dt)
    {
        for (int i = room.Projectiles.Count - 1; i >= 0; i--)
        {
            var proj = room.Projectiles[i];
            if (!proj.IsSpawned) { room.Projectiles.RemoveAt(i); continue; }

            var target = FindProjectileTarget(room, proj.TargetNetId);
            if (target == null)
            {
                Server.Despawn(proj);
                room.Projectiles.RemoveAt(i);
                continue;
            }

            var (tx, ty) = CombatUtils.GetEntityPos(target);
            float dx = tx - proj.X;
            float dy = ty - proj.Y;
            float dist = MathF.Sqrt(dx * dx + dy * dy);
            float hitRadius = GameConfig.ProjectileRadius + 0.5f;

            if (dist <= hitRadius)
            {
                if (proj.Source != null)
                    CombatUtils.ApplyDamage(room, proj.Source, target, proj.Damage);
                Server.Despawn(proj);
                room.Projectiles.RemoveAt(i);
            }
            else
            {
                float move = proj.Speed * dt;
                if (move >= dist) { proj.X.Value = tx; proj.Y.Value = ty; }
                else { proj.X.Value += dx / dist * move; proj.Y.Value += dy / dist * move; }
            }
        }
    }

    private static NetworkEntity? FindProjectileTarget(GameRoom room, uint netId)
    {
        foreach (var c in room.Champions)
            if (c.NetworkId == netId && c.IsSpawned && !c.IsDead) return c;
        foreach (var m in room.Minions)
            if (m.NetworkId == netId && m.IsSpawned && m.HP > 0) return m;
        foreach (var t in room.Turrets)
            if (t.NetworkId == netId && t.IsSpawned && !t.IsDestroyed) return t;
        if (room.BlueNexus?.NetworkId == netId && room.BlueNexus.IsSpawned) return room.BlueNexus;
        if (room.RedNexus?.NetworkId == netId && room.RedNexus.IsSpawned) return room.RedNexus;
        return null;
    }

    // ── Javelins ──

    private static void TickJavelins(GameRoom room, float dt)
    {
        for (int i = room.Javelins.Count - 1; i >= 0; i--)
        {
            var jav = room.Javelins[i];
            if (!jav.IsSpawned) { room.Javelins.RemoveAt(i); continue; }

            float move = jav.Speed * dt;
            jav.X.Value += jav.DirX * move;
            jav.Y.Value += jav.DirY * move;
            jav.DistanceTraveled += move;

            // Check if out of range
            if (jav.DistanceTraveled > jav.MaxRange)
            {
                Server.Despawn(jav);
                room.Javelins.RemoveAt(i);
                continue;
            }

            // Check for enemy hit (first enemy within hit radius)
            bool hit = false;
            float hitRadius = NidalooKit.JavelinHitRadius;

            // Check champions
            foreach (var c in room.Champions)
            {
                if (!c.IsSpawned || c.IsDead || c.TeamId == jav.TeamId) continue;
                if (CombatUtils.Dist(jav.X, jav.Y, c.X, c.Y) <= hitRadius)
                {
                    ApplyJavelinDamage(room, jav, c);
                    hit = true;
                    break;
                }
            }

            if (!hit)
            {
                foreach (var m in room.Minions)
                {
                    if (!m.IsSpawned || m.HP <= 0 || m.TeamId == jav.TeamId) continue;
                    if (CombatUtils.Dist(jav.X, jav.Y, m.X, m.Y) <= hitRadius)
                    {
                        ApplyJavelinDamage(room, jav, m);
                        hit = true;
                        break;
                    }
                }
            }

            if (hit)
            {
                Server.Despawn(jav);
                room.Javelins.RemoveAt(i);
            }
        }
    }

    private static void ApplyJavelinDamage(GameRoom room, JavelinEntity jav, NetworkEntity target)
    {
        // Damage scales: 1x at point blank, 2x at max range
        float distRatio = Math.Clamp(jav.DistanceTraveled / jav.MaxRange, 0, 1);
        float scaledDmg = jav.Damage * (1f + distRatio);
        float armor = CombatUtils.GetEntityArmor(target);
        float dmg = GameConfig.CalculateDamage(scaledDmg, armor);

        if (jav.Source != null)
        {
            CombatUtils.ApplyDamage(room, jav.Source, target, dmg);
            // Mark as Hunted
            if (jav.Source.GetKit() is NidalooKit nidKit)
            {
                var state = jav.Source.KitData as NidalooState;
                if (state != null)
                    state.HuntedTargets[target.NetworkId] = NidalooKit.HuntedDuration;
            }
        }
    }

    // ── Trap Cleanup ──

    private static void CleanupTraps(GameRoom room)
    {
        for (int i = room.Traps.Count - 1; i >= 0; i--)
        {
            if (!room.Traps[i].IsSpawned)
                room.Traps.RemoveAt(i);
        }
    }

    // ── Minion Spawning ──

    private static void SpawnMinionWave(GameRoom room, Team team)
    {
        if (room.Map == null) return;
        byte teamId = (byte)team;
        float spawnX = team == Team.Blue ? GameConfig.BlueBaseX + 2.5f : GameConfig.RedBaseX - 2.5f;

        for (int i = 0; i < GameConfig.MeleeMinionsPerWave; i++)
        {
            float yOffset = (i - (GameConfig.MeleeMinionsPerWave - 1) / 2f) * 2f;
            var minion = Server.Spawn<MinionEntity>(room.Map, m => {
                m.TeamId.Value = teamId;
                m.MinionTypeId.Value = 0;
                m.X.Value = spawnX;
                m.Y.Value = GameConfig.LaneY + yOffset;
                m.HP.Value = m.MaxHP.Value = GameConfig.MeleeMinionHP;
            });
            room.Minions.Add(minion);
        }
        for (int i = 0; i < GameConfig.CasterMinionsPerWave; i++)
        {
            float yOffset = (i - (GameConfig.CasterMinionsPerWave - 1) / 2f) * 2.5f;
            var minion = Server.Spawn<MinionEntity>(room.Map, m => {
                m.TeamId.Value = teamId;
                m.MinionTypeId.Value = 1;
                m.X.Value = spawnX + (team == Team.Blue ? -2f : 2f);
                m.Y.Value = GameConfig.LaneY + yOffset;
                m.HP.Value = m.MaxHP.Value = GameConfig.CasterMinionHP;
            });
            room.Minions.Add(minion);
        }
    }

    // ── Minion Collision ──

    private const float MinionRadius = 0.5f;
    private const float MinionSeparationStrength = 2f;

    private static void SeparateMinions(GameRoom room)
    {
        float minDist = MinionRadius * 2f;
        for (int i = 0; i < room.Minions.Count; i++)
        {
            var a = room.Minions[i];
            if (!a.IsSpawned || a.HP <= 0) continue;
            for (int j = i + 1; j < room.Minions.Count; j++)
            {
                var b = room.Minions[j];
                if (!b.IsSpawned || b.HP <= 0) continue;

                float dx = b.X - a.X;
                float dy = b.Y - a.Y;
                float distSq = dx * dx + dy * dy;
                if (distSq < minDist * minDist && distSq > 0.0001f)
                {
                    float dist = MathF.Sqrt(distSq);
                    float overlap = minDist - dist;
                    float nx = dx / dist;
                    float ny = dy / dist;
                    float push = overlap * 0.5f;
                    a.X.Value -= nx * push;
                    a.Y.Value -= ny * push;
                    b.X.Value += nx * push;
                    b.Y.Value += ny * push;
                }
            }
        }
    }

    // ── Cooldown Sync (still via RPC since it's an array) ──

    private static void SyncCooldowns(GameRoom room)
    {
        foreach (var c in room.Champions)
        {
            if (!c.IsSpawned || c.Owner == null) continue;
            c.RpcCooldowns(c.AbilityCooldowns[0], c.AbilityCooldowns[1],
                           c.AbilityCooldowns[2], c.AbilityCooldowns[3]);
        }
    }

    // ── Win Condition ──

    private static void CheckWinCondition(GameRoom room)
    {
        if (room.Phase != GamePhase.Playing) return;

        if (room.BlueNexus != null && room.BlueNexus.HP <= 0)
            MobaServer.EndGame(room, 1);
        else if (room.RedNexus != null && room.RedNexus.HP <= 0)
            MobaServer.EndGame(room, 0);
    }
}
