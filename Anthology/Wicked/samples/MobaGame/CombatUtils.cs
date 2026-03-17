using Prowl.Wicked;

namespace MobaGame;

/// <summary>
/// Server-side combat helpers: target finding, damage, abilities, shop, leveling.
/// </summary>
public static class CombatUtils
{
    // ── Target Finding ──

    public static NetworkEntity? FindClosestEnemy(GameRoom room, float x, float y, float range, byte myTeam)
    {
        NetworkEntity? closest = null;
        float closestDist = range;

        foreach (var m in room.Minions)
        {
            if (!m.IsSpawned || m.HP <= 0 || m.TeamId == myTeam) continue;
            float d = Dist(x, y, m.X, m.Y);
            if (d < closestDist) { closestDist = d; closest = m; }
        }

        if (closest == null)
        {
            foreach (var c in room.Champions)
            {
                if (!c.IsSpawned || c.IsDead || c.TeamId == myTeam) continue;
                float d = Dist(x, y, c.X, c.Y);
                if (d < closestDist) { closestDist = d; closest = c; }
            }
        }

        if (closest == null)
        {
            foreach (var t in room.Turrets)
            {
                if (!t.IsSpawned || t.IsDestroyed || t.TeamId == myTeam) continue;
                if (!CanTurretBeAttacked(room, t)) continue;
                float d = Dist(x, y, t.X, t.Y);
                if (d < closestDist) { closestDist = d; closest = t; }
            }
            var enemyNexus = myTeam == (byte)Team.Blue ? room.RedNexus : room.BlueNexus;
            if (enemyNexus != null && enemyNexus.IsSpawned && enemyNexus.HP > 0)
            {
                float d = Dist(x, y, enemyNexus.X, enemyNexus.Y);
                if (d < closestDist) { closest = enemyNexus; }
            }
        }

        return closest;
    }

    public static NetworkEntity? FindEnemyEntity(GameRoom room, uint netId, byte myTeam)
    {
        foreach (var c in room.Champions)
            if (c.NetworkId == netId && c.TeamId != myTeam && !c.IsDead) return c;
        foreach (var m in room.Minions)
            if (m.NetworkId == netId && m.TeamId != myTeam && m.HP > 0) return m;
        foreach (var t in room.Turrets)
            if (t.NetworkId == netId && t.TeamId != myTeam && !t.IsDestroyed) return t;
        if (room.BlueNexus?.NetworkId == netId && myTeam != (byte)Team.Blue) return room.BlueNexus;
        if (room.RedNexus?.NetworkId == netId && myTeam != (byte)Team.Red) return room.RedNexus;
        return null;
    }

    public static bool CanTurretBeAttacked(GameRoom room, TurretEntity turret)
    {
        if (turret.TurretIndex < 2)
        {
            foreach (var t in room.Turrets)
            {
                if (t.TeamId == turret.TeamId && t.TurretIndex == turret.TurretIndex + 1 && t.IsSpawned && !t.IsDestroyed)
                    return false;
            }
        }
        return true;
    }

    // ── Damage ──

    public static void ApplyDamage(GameRoom room, NetworkEntity attacker, NetworkEntity target, float damage)
    {
        switch (target)
        {
            case ChampionEntity champ:
                champ.HP.Value -= damage;
                if (champ.HP <= 0)
                {
                    champ.HP.Value = 0;
                    KillChampion(room, attacker, champ);
                }
                break;
            case MinionEntity minion:
                minion.HP.Value -= damage;
                if (minion.HP <= 0)
                {
                    minion.HP.Value = 0;
                    if (attacker is ChampionEntity killer)
                    {
                        killer.Gold.Value += GameConfig.MinionKillGold;
                        killer.XP += GameConfig.MinionKillXP;
                        CheckLevelUp(killer);
                    }
                    Server.Despawn(minion);
                }
                break;
            case TurretEntity turret:
                if (!CanTurretBeAttacked(room, turret)) break;
                turret.HP.Value -= damage;
                if (turret.HP <= 0)
                {
                    turret.HP.Value = 0;
                    turret.IsDestroyed = true;
                    Server.Despawn(turret);
                    string teamName = turret.TeamId == (byte)Team.Blue ? "Blue" : "Red";
                    BroadcastKillFeed(room, $"{teamName} turret destroyed!");
                }
                break;
            case NexusEntity nexus:
                nexus.HP.Value -= damage;
                if (nexus.HP <= 0) nexus.HP.Value = 0;
                break;
        }
    }

    private static void KillChampion(GameRoom room, NetworkEntity killer, ChampionEntity victim)
    {
        victim.IsDead.Value = true;
        victim.Deaths.Value++;
        victim.RespawnTimer.Value = GameConfig.BaseRespawnTime + victim.Level * GameConfig.RespawnTimePerLevel;
        victim.IsMoving = false;

        string killerName = "the environment";
        if (killer is ChampionEntity killerChamp)
        {
            killerChamp.Kills.Value++;
            killerChamp.Gold.Value += GameConfig.ChampionKillBaseGold + victim.Level * GameConfig.ChampionKillGoldPerLevel;
            killerChamp.XP += GameConfig.ChampionKillBaseXP + victim.Level * GameConfig.ChampionKillXPPerLevel;
            CheckLevelUp(killerChamp);
            killerName = killerChamp.PlayerName;
        }
        else if (killer is TurretEntity)
            killerName = "a turret";
        else if (killer is MinionEntity)
            killerName = "minions";

        if (victim.TeamId == (byte)Team.Blue) room.RedKills++;
        else room.BlueKills++;

        victim.RpcDied(killerName);
        BroadcastKillFeed(room, $"{victim.PlayerName} was slain by {killerName}!");
        BroadcastScore(room);
    }

    public static void CheckLevelUp(ChampionEntity champ)
    {
        while (champ.Level < GameConfig.MaxLevel && champ.XP >= champ.Level * GameConfig.XpPerLevel)
        {
            champ.XP -= champ.Level * GameConfig.XpPerLevel;
            champ.Level.Value++;
            RecalcMaxStats(champ);
            champ.HP.Value = champ.MaxHP;
            champ.Mana.Value = champ.MaxMana;
        }
    }

    // ── Abilities ──

    public static void HandleAbility(GameRoom room, ChampionEntity champ, int slot, float targetX, float targetY)
    {
        if (champ.IsDead) return;
        if (champ.TransformLockout > 0) return;
        var kit = champ.GetKit();
        if (kit == null) return;
        if (slot < 0 || slot >= kit.GetAbilityCount(champ)) return;
        var ability = kit.GetAbility(champ, slot);

        if (champ.AbilityCooldowns[slot] > 0) return;
        if (champ.Mana < ability.ManaCost) return;

        champ.Mana.Value -= ability.ManaCost;
        champ.AbilityCooldowns[slot] = ability.Cooldown;

        kit.OnAbility(room, champ, slot, targetX, targetY);
    }

    public static List<NetworkEntity> DamageArea(GameRoom room, ChampionEntity caster, float cx, float cy, float radius, float damage)
    {
        var hits = new List<NetworkEntity>();
        foreach (var m in room.Minions.ToArray())
        {
            if (!m.IsSpawned || m.HP <= 0 || m.TeamId == caster.TeamId) continue;
            if (Dist(cx, cy, m.X, m.Y) <= radius)
            {
                ApplyDamage(room, caster, m, GameConfig.CalculateDamage(damage, 0));
                hits.Add(m);
            }
        }
        foreach (var c in room.Champions)
        {
            if (!c.IsSpawned || c.IsDead || c.TeamId == caster.TeamId) continue;
            if (Dist(cx, cy, c.X, c.Y) <= radius)
            {
                float armor = GetEntityArmor(c);
                ApplyDamage(room, caster, c, GameConfig.CalculateDamage(damage, armor));
                hits.Add(c);
            }
        }
        foreach (var t in room.Turrets)
        {
            if (!t.IsSpawned || t.IsDestroyed || t.TeamId == caster.TeamId) continue;
            if (!CanTurretBeAttacked(room, t)) continue;
            if (Dist(cx, cy, t.X, t.Y) <= radius)
            {
                ApplyDamage(room, caster, t, GameConfig.CalculateDamage(damage, 0));
                hits.Add(t);
            }
        }
        var enemyNexus = caster.TeamId == (byte)Team.Blue ? room.RedNexus : room.BlueNexus;
        if (enemyNexus != null && enemyNexus.IsSpawned && enemyNexus.HP > 0 && Dist(cx, cy, enemyNexus.X, enemyNexus.Y) <= radius)
        {
            ApplyDamage(room, caster, enemyNexus, GameConfig.CalculateDamage(damage, 0));
            hits.Add(enemyNexus);
        }
        return hits;
    }

    // ── Shop ──

    public static void HandleBuyItem(ChampionEntity champ, int itemId)
    {
        if (itemId < 0 || itemId >= GameConfig.Items.Length) return;
        var item = GameConfig.Items[itemId];

        float baseX = champ.TeamId == (byte)Team.Blue ? GameConfig.BlueBaseX : GameConfig.RedBaseX;
        if (MathF.Abs(champ.X - baseX) > GameConfig.ShopRadius) return;
        if (champ.Gold < item.Cost) return;

        int slot = -1;
        for (int i = 0; i < GameConfig.MaxItemSlots; i++)
            if (champ.Items[i] == -1) { slot = i; break; }
        if (slot == -1) return;

        champ.Gold.Value -= item.Cost;
        champ.Items[slot] = itemId;
        champ.BonusAD += item.BonusAD;
        champ.BonusArmor += item.BonusArmor;
        champ.BonusSpeed += item.BonusSpeed;
        RecalcMaxStats(champ);

        champ.RpcItemUpdate(slot, itemId);
    }

    public static void HandleSellItem(ChampionEntity champ, int slotIndex)
    {
        if (slotIndex < 0 || slotIndex >= GameConfig.MaxItemSlots) return;
        int itemId = champ.Items[slotIndex];
        if (itemId < 0) return;

        var item = GameConfig.Items[itemId];
        champ.Gold.Value += item.Cost / 2;
        champ.Items[slotIndex] = -1;
        champ.BonusAD -= item.BonusAD;
        champ.BonusArmor -= item.BonusArmor;
        champ.BonusSpeed -= item.BonusSpeed;
        RecalcMaxStats(champ);

        champ.RpcItemUpdate(slotIndex, -1);
    }

    public static void RecalcMaxStats(ChampionEntity champ)
    {
        var kit = champ.GetKit();
        float baseHP = kit?.BaseHP ?? 500f;
        float baseMana = kit?.BaseMana ?? 300f;
        champ.MaxHP.Value = baseHP + (champ.Level - 1) * GameConfig.HpPerLevel;
        champ.MaxMana.Value = baseMana + (champ.Level - 1) * 15;
        champ.HP.Value = Math.Min(champ.HP, champ.MaxHP);
    }

    // ── Projectile Spawning ──

    public static void SpawnProjectile(GameRoom room, NetworkEntity source, NetworkEntity target, float damage)
    {
        if (room.Map == null) return;
        var (sx, sy) = GetEntityPos(source);
        byte teamId = source switch {
            ChampionEntity c => c.TeamId,
            MinionEntity m => m.TeamId,
            TurretEntity t => t.TeamId,
            _ => 0
        };

        var proj = Server.Spawn<ProjectileEntity>(room.Map, p => {
            p.X.Value = sx;
            p.Y.Value = sy;
            p.TeamId.Value = teamId;
            p.TargetNetId.Value = target.NetworkId;
            p.Damage = damage;
            p.Speed = GameConfig.ProjectileSpeed;
            p.Source = source;
        });
        room.Projectiles.Add(proj);
    }

    // ── Broadcast helpers ──

    public static void BroadcastKillFeed(GameRoom room, string message)
    {
        foreach (var p in room.AllPlayers)
            LobbyCommands.RpcKillFeed(p, message);
    }

    public static void BroadcastScore(GameRoom room)
    {
        foreach (var p in room.AllPlayers)
            LobbyCommands.RpcScoreUpdate(p, room.BlueKills, room.RedKills);
    }

    // ── Utility ──

    public static float Dist(float x1, float y1, float x2, float y2)
    {
        float dx = x2 - x1, dy = y2 - y1;
        return MathF.Sqrt(dx * dx + dy * dy);
    }

    public static float DistTo(ChampionEntity champ, NetworkEntity target)
    {
        var (tx, ty) = GetEntityPos(target);
        return Dist(champ.X, champ.Y, tx, ty);
    }

    public static (float x, float y) GetEntityPos(NetworkEntity entity) => entity switch {
        ChampionEntity c => (c.X, c.Y),
        MinionEntity m => (m.X, m.Y),
        TurretEntity t => (t.X, t.Y),
        NexusEntity n => (n.X, n.Y),
        _ => (0, 0)
    };

    public static float GetEntityArmor(NetworkEntity entity) => entity switch {
        ChampionEntity c => (c.GetKit()?.BaseArmor ?? 20f)
                            + (c.Level - 1) * GameConfig.ArmorPerLevel
                            + c.BonusArmor + c.BuffArmorBonus,
        _ => 0
    };

    public static NetworkEntity? FindEntityById(GameRoom room, uint netId)
    {
        foreach (var c in room.Champions)
            if (c.NetworkId == netId && c.IsSpawned) return c;
        foreach (var m in room.Minions)
            if (m.NetworkId == netId && m.IsSpawned) return m;
        foreach (var t in room.Turrets)
            if (t.NetworkId == netId && t.IsSpawned) return t;
        if (room.BlueNexus?.NetworkId == netId) return room.BlueNexus;
        if (room.RedNexus?.NetworkId == netId) return room.RedNexus;
        return null;
    }
}
