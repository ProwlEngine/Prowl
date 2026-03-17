using Prowl.Wicked;
using Raylib_cs;

namespace MobaGame;

public static class HUD
{
    public static void Draw(ChampionEntity? myChamp, int castingSlot, bool showShop,
        ref bool showShopRef, ref int castingSlotRef)
    {
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();

        UI.Panel(0, sh - 120, sw, 120);

        if (myChamp != null)
        {
            var kit = myChamp.GetKit();
            string charName = kit?.Name ?? "???";

            // Champion info
            UI.Label(10, sh - 115, $"{charName} (Lv {myChamp.Level})", 16, Color.Gold);
            UI.ProgressBar(10, sh - 95, 200, 14, myChamp.MaxHP > 0 ? myChamp.HP / myChamp.MaxHP : 0,
                Color.Green, Color.DarkGray);
            UI.Label(15, sh - 94, $"HP: {myChamp.HP:F0}/{myChamp.MaxHP:F0}", 12, Color.White);

            UI.ProgressBar(10, sh - 78, 200, 10, myChamp.MaxMana > 0 ? myChamp.Mana / myChamp.MaxMana : 0,
                Color.Blue, Color.DarkGray);
            UI.Label(15, sh - 77, $"Mana: {myChamp.Mana:F0}/{myChamp.MaxMana:F0}", 10, Color.White);

            UI.Label(10, sh - 62, $"K/D/A: {myChamp.Kills}/{myChamp.Deaths}/{myChamp.Assists}", 14, Color.White);
            UI.Label(10, sh - 45, $"Gold: {myChamp.Gold}", 14, Color.Gold);

            // Abilities
            DrawAbilities(myChamp, kit, castingSlot, sh);

            // Items
            DrawItems(myChamp, sh);

            // Shop button
            if (UI.Button(600, sh - 48, 80, 25, "[B] Shop"))
            {
                showShopRef = !showShopRef;
                castingSlotRef = -1;
            }

            // Respawn timer
            if (myChamp.IsDead && myChamp.RespawnTimer > 0)
                UI.Label(sw / 2 - 80, sh / 2 - 20, $"DEAD - Respawning in {myChamp.RespawnTimer:F1}s", 20, Color.Red);
        }

        // Score
        UI.Label(sw / 2 - 60, 10, $"BLUE {MobaClient.BlueKills}", 20, Color.Blue);
        UI.Label(sw / 2 + 10, 10, $"{MobaClient.RedKills} RED", 20, Color.Red);

        // Kill feed
        DrawKillFeed(sw);

        // Minimap
        DrawMinimap(sw - 160, sh - 110, 150, 60);
    }

    private static void DrawAbilities(ChampionEntity myChamp, CharacterKit? kit, int castingSlot, int sh)
    {
        if (kit == null) return;

        string[] keys = { "Q", "W", "E", "R" };
        int count = kit.GetAbilityCount(myChamp);
        for (int i = 0; i < 4 && i < count; i++)
        {
            int ax = 250 + i * 80;
            int ay = sh - 110;
            var ab = kit.GetAbility(myChamp, i);
            float cd = myChamp.ClientCooldowns[i];
            bool onCd = cd > 0;
            bool isCasting = castingSlot == i;

            Color bgCol;
            if (isCasting)
                bgCol = new Color((byte)80, (byte)60, (byte)0, (byte)255);
            else if (onCd)
                bgCol = Color.DarkGray;
            else
                bgCol = new Color((byte)40, (byte)40, (byte)80, (byte)255);

            Raylib.DrawRectangle(ax, ay, 65, 55, bgCol);
            Raylib.DrawRectangleLines(ax, ay, 65, 55, isCasting ? Color.Gold : Color.White);

            UI.Label(ax + 3, ay + 3, $"[{keys[i]}] {ab.Name}", 10, onCd ? Color.Gray : Color.White);
            if (onCd)
                UI.Label(ax + 15, ay + 25, $"{cd:F1}s", 14, Color.Red);
            UI.Label(ax + 3, ay + 38, $"Mana:{ab.ManaCost}", 10, Color.SkyBlue);
        }
    }

    private static void DrawItems(ChampionEntity myChamp, int sh)
    {
        UI.Label(600, sh - 115, "Items:", 14, Color.SkyBlue);
        for (int i = 0; i < 6; i++)
        {
            int ix = 600 + i * 45;
            int iy = sh - 95;
            Raylib.DrawRectangle(ix, iy, 40, 40, new Color((byte)40, (byte)40, (byte)40, (byte)255));
            Raylib.DrawRectangleLines(ix, iy, 40, 40, Color.Gray);

            if (myChamp.Items[i] >= 0 && myChamp.Items[i] < GameConfig.Items.Length)
            {
                var item = GameConfig.Items[myChamp.Items[i]];
                UI.Label(ix + 2, iy + 10, item.Name, 9, Color.White);
            }
        }
    }

    private static void DrawKillFeed(int sw)
    {
        int kfy = 10;
        for (int i = Math.Max(0, MobaClient.KillFeed.Count - 5); i < MobaClient.KillFeed.Count; i++)
        {
            UI.Label(sw - 350, kfy, MobaClient.KillFeed[i], 13, Color.White);
            kfy += 16;
        }
    }

    private static void DrawMinimap(int mx, int my, int mw, int mh)
    {
        Raylib.DrawRectangle(mx, my, mw, mh, new Color((byte)10, (byte)20, (byte)10, (byte)200));
        Raylib.DrawRectangleLines(mx, my, mw, mh, Color.White);

        foreach (var entity in Client.Entities)
        {
            float ex, ey;
            Color col;

            if (entity is ChampionEntity c)
            { ex = c.X.Display; ey = c.Y.Display; col = c.TeamId == 0 ? Color.Blue : Color.Red; }
            else if (entity is TurretEntity t)
            { ex = t.X; ey = t.Y; col = t.TeamId == 0 ? Color.SkyBlue : Color.Orange; }
            else if (entity is NexusEntity n)
            { ex = n.X; ey = n.Y; col = n.TeamId == 0 ? Color.Blue : Color.Red; }
            else continue;

            int px = mx + (int)(ex / GameConfig.MapWidth * mw);
            int py = my + (int)(ey / GameConfig.MapHeight * mh);
            Raylib.DrawCircle(px, py, entity is NexusEntity ? 4 : 2, col);
        }
    }

    public static void DrawShop(ChampionEntity myChamp, ref bool showShop)
    {
        int sw = Raylib.GetScreenWidth();

        UI.Panel(sw / 2 - 200, 100, 400, 350);
        UI.Label(sw / 2 - 30, 110, "SHOP", 24, Color.Gold);
        UI.Label(sw / 2 - 50, 140, $"Gold: {myChamp.Gold}", 16, Color.Gold);

        for (int i = 0; i < GameConfig.Items.Length; i++)
        {
            var item = GameConfig.Items[i];
            int iy = 170 + i * 70;
            int ix = sw / 2 - 180;

            UI.Panel(ix, iy, 360, 60);
            UI.Label(ix + 10, iy + 5, item.Name, 16, Color.White);
            UI.Label(ix + 10, iy + 25, $"Cost: {item.Cost}g", 14, Color.Gold);

            string stats = "";
            if (item.BonusAD > 0) stats += $"+{item.BonusAD} AD  ";
            if (item.BonusArmor > 0) stats += $"+{item.BonusArmor} Armor  ";
            if (item.BonusSpeed > 0) stats += $"+{item.BonusSpeed} Speed  ";
            UI.Label(ix + 10, iy + 40, stats, 12, Color.LightGray);

            if (UI.Button(ix + 280, iy + 15, 60, 28, "Buy"))
                myChamp.CmdBuyItem(item.Id);
        }

        UI.Label(sw / 2 - 180, 390, "Sell (click item slot):", 14, Color.SkyBlue);
        for (int i = 0; i < 6; i++)
        {
            int sx = sw / 2 - 180 + i * 55;
            int sy = 410;
            if (myChamp.Items[i] >= 0 && myChamp.Items[i] < GameConfig.Items.Length)
            {
                var item = GameConfig.Items[myChamp.Items[i]];
                if (UI.Button(sx, sy, 50, 25, item.Name))
                    myChamp.CmdSellItem(i);
            }
            else
            {
                Raylib.DrawRectangle(sx, sy, 50, 25, Color.DarkGray);
                Raylib.DrawRectangleLines(sx, sy, 50, 25, Color.Gray);
            }
        }

        if (UI.Button(sw / 2 - 40, 445, 80, 25, "Close"))
            showShop = false;
    }

    public static void DrawGameOver()
    {
        int sw = Raylib.GetScreenWidth();
        int sh = Raylib.GetScreenHeight();

        Raylib.DrawRectangle(0, 0, sw, sh, new Color((byte)0, (byte)0, (byte)0, (byte)150));

        string msg = MobaClient.WinningTeam == MobaClient.MyTeam ? "VICTORY!" : "DEFEAT!";
        var col = MobaClient.WinningTeam == MobaClient.MyTeam ? Color.Gold : Color.Red;
        int tw = Raylib.MeasureText(msg, 48);
        Raylib.DrawText(msg, sw / 2 - tw / 2, sh / 2 - 40, 48, col);

        UI.Label(sw / 2 - 80, sh / 2 + 20, "Returning to lobby...", 16, Color.White);
    }
}
