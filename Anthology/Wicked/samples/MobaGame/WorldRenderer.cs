using Prowl.Wicked;
using Raylib_cs;

namespace MobaGame;

/// <summary>
/// Renders all world-space entities using the Draw class. Coordinates in meters.
/// </summary>
public static class WorldRenderer
{
    public static uint HoveredEntityId;

    public static void Render(ChampionEntity? myChamp, float dt)
    {
        // Ground
        Draw.RectCorner(0, 0, GameConfig.MapWidth, GameConfig.MapHeight,
            new Color((byte)30, (byte)60, (byte)30, (byte)255));

        // Lane path
        Draw.RectCorner(0, GameConfig.LaneY - 2f, GameConfig.MapWidth, 4f,
            new Color((byte)80, (byte)70, (byte)50, (byte)255));

        // Bases
        Draw.Circle(GameConfig.BlueBaseX, GameConfig.LaneY, GameConfig.BaseRadius,
            new Color((byte)20, (byte)20, (byte)80, (byte)100));
        Draw.Circle(GameConfig.RedBaseX, GameConfig.LaneY, GameConfig.BaseRadius,
            new Color((byte)80, (byte)20, (byte)20, (byte)100));

        foreach (var entity in Client.Entities)
        {
            bool hovered = entity.NetworkId == HoveredEntityId;
            if (entity is NexusEntity nexus) DrawNexus(nexus, hovered);
            else if (entity is TurretEntity turret) DrawTurret(turret, hovered);
            else if (entity is MinionEntity minion) DrawMinion(minion, hovered);
            else if (entity is ChampionEntity champ) DrawChampion(champ, champ == myChamp, dt, hovered);
            else if (entity is ProjectileEntity proj) DrawProjectile(proj);
            else if (entity is JavelinEntity jav) DrawJavelin(jav);
            else if (entity is TrapEntity trap) DrawTrap(trap, myChamp);
        }
    }

    private static void DrawNexus(NexusEntity n, bool hovered)
    {
        var col = n.TeamId == 0 ? Color.Blue : Color.Red;
        if (hovered) Draw.CircleOutline(n.X, n.Y, 2.3f, Color.Yellow);
        Draw.Circle(n.X, n.Y, 2f, col);
        Draw.CircleOutline(n.X, n.Y, 2f, Color.White);
        Draw.TextCentered($"{n.HP:F0}/{n.MaxHP:F0}", n.X, n.Y - 2.5f, 12, Color.White);
    }

    private static void DrawTurret(TurretEntity t, bool hovered)
    {
        var col = t.TeamId == 0
            ? new Color((byte)60, (byte)60, (byte)200, (byte)255)
            : new Color((byte)200, (byte)60, (byte)60, (byte)255);
        if (hovered) Draw.RectOutline(t.X, t.Y, 1.2f, 1.2f, Color.Yellow);
        Draw.Rect(t.X, t.Y, 1f, 1f, col);
        Draw.RectOutline(t.X, t.Y, 1f, 1f, Color.White);
        Draw.HealthBar(t.X, t.Y - 1.5f, 1f, 0.12f, t.HP, t.MaxHP, col);
    }

    private static void DrawMinion(MinionEntity m, bool hovered)
    {
        float dx = m.X.Display, dy = m.Y.Display;
        var col = m.TeamId == 0
            ? new Color((byte)100, (byte)100, (byte)255, (byte)255)
            : new Color((byte)255, (byte)100, (byte)100, (byte)255);
        float size = m.MinionTypeId == 0 ? 0.4f : 0.3f;
        if (hovered) Draw.CircleOutline(dx, dy, size + 0.2f, Color.Yellow);
        Draw.Circle(dx, dy, size, col);
        Draw.HealthBar(dx, dy - size - 0.3f, 0.5f, 0.08f, m.HP, m.MaxHP, col);
    }

    private static void DrawChampion(ChampionEntity c, bool isMe, float dt, bool hovered)
    {
        float dx = c.X.Display, dy = c.Y.Display;
        if (c.IsDead)
        {
            Draw.TextCentered("X", dx, dy - 0.2f, 16, Color.Gray);
            return;
        }

        var teamCol = c.TeamId == 0
            ? new Color((byte)50, (byte)50, (byte)255, (byte)255)
            : new Color((byte)255, (byte)50, (byte)50, (byte)255);

        if (hovered) Draw.CircleOutline(dx, dy, 1.1f, Color.Yellow);

        // Nidaloo big cat form: draw as oval; all others: circle
        var kit = c.GetKit();
        bool isCougar = kit is NidalooKit && c.FormId == 1;

        if (isCougar)
        {
            // Oval shape for cougar (wider than tall)
            Draw.Rect(dx, dy, 1.0f, 0.55f, teamCol);
            Draw.RectOutline(dx, dy, 1.0f, 0.55f, Color.White);
            if (isMe) Draw.RectOutline(dx, dy, 1.1f, 0.65f, Color.Gold);
        }
        else
        {
            Draw.Circle(dx, dy, 0.8f, teamCol);
            if (isMe) Draw.CircleOutline(dx, dy, 0.9f, Color.Gold);
        }

        // Name above head
        Draw.TextCentered(c.PlayerName, dx, dy - 1.8f, 12, Color.White);

        // Health and mana bars (shown for all champions)
        Draw.HealthBar(dx, dy - 1.3f, 1f, 0.12f, c.HP, c.MaxHP, Color.Green);
        Draw.HealthBar(dx, dy - 1.0f, 1f, 0.08f, c.Mana, c.MaxMana, Color.Blue);

        Draw.TextCentered($"Lv{c.Level}", dx, dy + 1f, 10, Color.White);

        // Nidaloo form indicator
        if (kit is NidalooKit)
        {
            string formName = c.FormId == 0 ? "Human" : "Big Cat";
            Draw.TextCentered(formName, dx, dy + 1.5f, 9,
                new Color((byte)200, (byte)200, (byte)100, (byte)200));
        }

        // Ability effect visualization
        if (c.AbilityEffectTimer > 0)
        {
            if (c.LastAbilityRadius > 0)
                Draw.CircleOutline(c.LastAbilityX, c.LastAbilityY, c.LastAbilityRadius, Color.Orange);
            c.AbilityEffectTimer -= dt;
        }
    }

    private static void DrawProjectile(ProjectileEntity p)
    {
        float dx = p.X.Display, dy = p.Y.Display;
        var col = p.TeamId == 0
            ? new Color((byte)150, (byte)150, (byte)255, (byte)255)
            : new Color((byte)255, (byte)150, (byte)150, (byte)255);
        Draw.Circle(dx, dy, GameConfig.ProjectileRadius, col);
        Draw.CircleOutline(dx, dy, GameConfig.ProjectileRadius + 0.05f, Color.White);
    }

    private static void DrawJavelin(JavelinEntity j)
    {
        float dx = j.X.Display, dy = j.Y.Display;
        // Draw as an elongated shape in the direction of travel
        var col = j.TeamId == 0
            ? new Color((byte)100, (byte)200, (byte)100, (byte)255)
            : new Color((byte)200, (byte)100, (byte)100, (byte)255);

        // Javelin trail line
        float trailLen = 1.5f;
        float tx = dx - j.DirX * trailLen;
        float ty = dy - j.DirY * trailLen;
        Draw.Line(tx, ty, dx, dy, col);

        // Javelin tip
        Draw.Circle(dx, dy, 0.3f, col);
        Draw.CircleOutline(dx, dy, 0.35f, Color.White);
    }

    private static void DrawTrap(TrapEntity trap, ChampionEntity? myChamp)
    {
        // Only draw friendly traps (or faintly for enemies, they shouldn't fully see them)
        bool friendly = myChamp != null && trap.TeamId == myChamp.TeamId;

        if (friendly)
        {
            // Draw as a small triangle/diamond marker
            var col = new Color((byte)100, (byte)200, (byte)50, (byte)180);
            Draw.Circle(trap.X, trap.Y, 0.4f, col);
            Draw.CircleOutline(trap.X, trap.Y, NidalooKit.TrapTriggerRadius,
                new Color((byte)100, (byte)200, (byte)50, (byte)60));
        }
        else
        {
            // Enemy traps: very faint hint (in a real game they'd be invisible)
            var col = new Color((byte)200, (byte)100, (byte)50, (byte)30);
            Draw.Circle(trap.X, trap.Y, 0.2f, col);
        }
    }
}
