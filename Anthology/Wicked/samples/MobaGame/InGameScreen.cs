using Prowl.Wicked;
using Raylib_cs;

namespace MobaGame;

public static class InGameScreen
{
    private static Camera2D _camera = new() { Zoom = MobaGame.Draw.PixelsPerMeter };
    private static bool _showShop;
    private static int _castingSlot = -1;

    public static void Update(float dt)
    {
        var myChamp = MobaClient.GetMyChampion();
        if (myChamp == null) return;

        // Camera follows champion (use interpolated position)
        _camera.Target = new System.Numerics.Vector2(myChamp.X.Display, myChamp.Y.Display);
        _camera.Offset = new System.Numerics.Vector2(Raylib.GetScreenWidth() / 2f, Raylib.GetScreenHeight() / 2f);

        // Scroll zoom (in pixels-per-meter)
        float wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0)
            _camera.Zoom = Math.Clamp(_camera.Zoom + wheel * 2f, 6f, 60f);

        // Update hover target
        var mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), _camera);
        WorldRenderer.HoveredEntityId = FindHoverTarget(mouseWorld.X, mouseWorld.Y, myChamp);

        var kit = myChamp.GetKit();

        // Ability keys toggle casting mode
        if (Raylib.IsKeyPressed(KeyboardKey.Q)) ToggleCasting(myChamp, kit, 0, mouseWorld);
        if (Raylib.IsKeyPressed(KeyboardKey.W)) ToggleCasting(myChamp, kit, 1, mouseWorld);
        if (Raylib.IsKeyPressed(KeyboardKey.E)) ToggleCasting(myChamp, kit, 2, mouseWorld);
        if (Raylib.IsKeyPressed(KeyboardKey.R)) ToggleCasting(myChamp, kit, 3, mouseWorld);

        // Escape cancels casting / shop
        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            _castingSlot = -1;
            _showShop = false;
        }

        if (_castingSlot >= 0 && !_showShop)
        {
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && !IsMouseOverHUD())
            {
                myChamp.CmdAbility(_castingSlot, mouseWorld.X, mouseWorld.Y);
                _castingSlot = -1;
            }
            if (Raylib.IsMouseButtonPressed(MouseButton.Right))
            {
                _castingSlot = -1;
                myChamp.CmdMove(mouseWorld.X, mouseWorld.Y);
            }
        }
        else if (!_showShop)
        {
            if (Raylib.IsMouseButtonPressed(MouseButton.Right))
            {
                uint targetId = FindClickTarget(mouseWorld.X, mouseWorld.Y, myChamp);
                if (targetId != 0)
                    myChamp.CmdAttackTarget(targetId);
                else
                    myChamp.CmdMove(mouseWorld.X, mouseWorld.Y);
            }
        }

        if (Raylib.IsKeyPressed(KeyboardKey.B))
        {
            _showShop = !_showShop;
            _castingSlot = -1;
        }
    }

    public static void Draw()
    {
        float dt = Raylib.GetFrameTime();
        var myChamp = MobaClient.GetMyChampion();

        MobaGame.Draw.SetZoom(_camera.Zoom);

        Raylib.BeginMode2D(_camera);
        WorldRenderer.Render(myChamp, dt);
        if (_castingSlot >= 0 && myChamp != null && !_showShop)
            DrawAbilityPreview(myChamp);
        Raylib.EndMode2D();

        HUD.Draw(myChamp, _castingSlot, _showShop, ref _showShop, ref _castingSlot);

        if (_showShop && myChamp != null)
            HUD.DrawShop(myChamp, ref _showShop);

        if (MobaClient.GameOver)
            HUD.DrawGameOver();

        if (_castingSlot >= 0 && myChamp != null)
            DrawCastingHint(myChamp);
    }

    // ── Casting ──

    private static void ToggleCasting(ChampionEntity champ, CharacterKit? kit, int slot,
        System.Numerics.Vector2 mouseWorld)
    {
        if (kit == null || slot >= kit.GetAbilityCount(champ)) return;
        if (_castingSlot == slot) { _castingSlot = -1; return; }
        if (champ.ClientCooldowns[slot] > 0) return;

        var ability = kit.GetAbility(champ, slot);

        switch (ability.CastMode)
        {
            case CastMode.Instant:
                // Fire immediately with champ position
                champ.CmdAbility(slot, champ.X, champ.Y);
                _castingSlot = -1;
                return;

            case CastMode.InstantDirection:
                // Fire immediately toward current mouse position
                champ.CmdAbility(slot, mouseWorld.X, mouseWorld.Y);
                _castingSlot = -1;
                return;

            default:
                // AreaTarget, DashTarget, AllyTarget — enter casting mode
                _castingSlot = slot;
                _showShop = false;
                break;
        }
    }

    private static void DrawAbilityPreview(ChampionEntity myChamp)
    {
        var kit = myChamp.GetKit();
        if (kit == null || _castingSlot < 0 || _castingSlot >= kit.GetAbilityCount(myChamp)) return;
        var ability = kit.GetAbility(myChamp, _castingSlot);

        var mouseWorld = Raylib.GetScreenToWorld2D(Raylib.GetMousePosition(), _camera);

        float dx = mouseWorld.X - myChamp.X.Display;
        float dy = mouseWorld.Y - myChamp.Y.Display;
        float dist = MathF.Sqrt(dx * dx + dy * dy);
        float targetX = mouseWorld.X, targetY = mouseWorld.Y;
        if (ability.Range > 0 && dist > ability.Range && dist > 0)
        {
            targetX = myChamp.X.Display + dx / dist * ability.Range;
            targetY = myChamp.Y.Display + dy / dist * ability.Range;
        }

        // Range circle
        if (ability.Range > 0)
            MobaGame.Draw.CircleOutline(myChamp.X.Display, myChamp.Y.Display, ability.Range,
                new Color((byte)255, (byte)255, (byte)255, (byte)60));

        switch (ability.CastMode)
        {
            case CastMode.AreaTarget:
                if (ability.Radius > 0)
                {
                    MobaGame.Draw.Circle(targetX, targetY, ability.Radius,
                        new Color((byte)255, (byte)100, (byte)0, (byte)40));
                    MobaGame.Draw.CircleOutline(targetX, targetY, ability.Radius,
                        new Color((byte)255, (byte)150, (byte)0, (byte)150));
                }
                else
                {
                    // Skillshot line (e.g., Javelin Toss — no radius, shows direction)
                    MobaGame.Draw.Line(myChamp.X.Display, myChamp.Y.Display, targetX, targetY,
                        new Color((byte)100, (byte)255, (byte)100, (byte)150));
                    MobaGame.Draw.Circle(targetX, targetY, 0.3f,
                        new Color((byte)100, (byte)255, (byte)100, (byte)200));
                }
                break;

            case CastMode.DashTarget:
                MobaGame.Draw.Line(myChamp.X.Display, myChamp.Y.Display, targetX, targetY,
                    new Color((byte)0, (byte)200, (byte)255, (byte)150));
                float dashRadius = ability.Radius > 0 ? ability.Radius : 3f;
                MobaGame.Draw.Circle(targetX, targetY, dashRadius,
                    new Color((byte)0, (byte)200, (byte)255, (byte)30));
                MobaGame.Draw.CircleOutline(targetX, targetY, dashRadius,
                    new Color((byte)0, (byte)200, (byte)255, (byte)120));
                MobaGame.Draw.Circle(targetX, targetY, 0.3f,
                    new Color((byte)0, (byte)255, (byte)255, (byte)200));
                break;

            case CastMode.AllyTarget:
                // Show range and highlight nearest ally
                MobaGame.Draw.CircleOutline(targetX, targetY, 1f,
                    new Color((byte)0, (byte)255, (byte)100, (byte)150));
                break;
        }
    }

    private static void DrawCastingHint(ChampionEntity myChamp)
    {
        var kit = myChamp.GetKit();
        if (kit == null || _castingSlot >= kit.GetAbilityCount(myChamp)) return;

        string[] keys = { "Q", "W", "E", "R" };
        var ab = kit.GetAbility(myChamp, _castingSlot);
        string hint = ab.CastMode == CastMode.AllyTarget
            ? $"Casting [{keys[_castingSlot]}] {ab.Name} - Left-click ally or self, Right-click to cancel"
            : $"Casting [{keys[_castingSlot]}] {ab.Name} - Left-click to cast, Right-click to cancel";
        int tw = Raylib.MeasureText(hint, 16);
        UI.Label(Raylib.GetScreenWidth() / 2 - tw / 2, 40, hint, 16, Color.Orange);
    }

    // ── Helpers ──

    private static uint FindHoverTarget(float wx, float wy, ChampionEntity myChamp)
    {
        float bestDist = 2.5f; // ~2.5 meter hover radius
        uint bestId = 0;

        foreach (var entity in Client.Entities)
        {
            float ex, ey;
            byte team;

            if (entity is ChampionEntity c && c != myChamp && !c.IsDead)
            { ex = c.X.Display; ey = c.Y.Display; team = c.TeamId; }
            else if (entity is MinionEntity m)
            { ex = m.X.Display; ey = m.Y.Display; team = m.TeamId; }
            else if (entity is TurretEntity t)
            { ex = t.X; ey = t.Y; team = t.TeamId; }
            else if (entity is NexusEntity n)
            { ex = n.X; ey = n.Y; team = n.TeamId; }
            else continue;

            if (team == myChamp.TeamId) continue;

            float ddx = ex - wx, ddy = ey - wy;
            float d = MathF.Sqrt(ddx * ddx + ddy * ddy);
            if (d < bestDist)
            {
                bestDist = d;
                bestId = entity.NetworkId;
            }
        }
        return bestId;
    }

    private static uint FindClickTarget(float wx, float wy, ChampionEntity myChamp)
    {
        return FindHoverTarget(wx, wy, myChamp);
    }

    private static bool IsMouseOverHUD()
    {
        int my = Raylib.GetMouseY();
        int sh = Raylib.GetScreenHeight();
        return my > sh - 120;
    }
}
