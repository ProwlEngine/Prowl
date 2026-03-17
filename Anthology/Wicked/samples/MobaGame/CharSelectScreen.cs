using Raylib_cs;

namespace MobaGame;

public static class CharSelectScreen
{
    private static int _selectedChar = -1;

    public static void Update(float dt) { }

    public static void Draw()
    {
        UI.Label(350, 20, "CHARACTER SELECT", 28, Color.Gold);
        UI.Label(350, 55, $"Time: {MobaClient.CharSelectTimeLeft:F0}s", 20, Color.White);

        // Character cards
        for (int i = 0; i < GameConfig.Kits.Length; i++)
        {
            var kit = GameConfig.Kits[i];
            int cx = 50 + i * 240;
            int cy = 100;
            bool selected = _selectedChar == i;

            UI.Panel(cx, cy, 220, 300);
            if (selected)
                Raylib.DrawRectangleLines(cx, cy, 220, 300, Color.Gold);

            UI.Label(cx + 10, cy + 10, kit.Name, 22, Color.White);
            UI.Label(cx + 10, cy + 40, $"HP: {kit.BaseHP}  Mana: {kit.BaseMana}", 14, Color.LightGray);
            UI.Label(cx + 10, cy + 58, $"Armor: {kit.BaseArmor}", 14, Color.LightGray);

            // Use a dummy champion to query abilities (form 0)
            var dummy = new ChampionEntity();
            dummy.CharId.Value = i;
            dummy.FormId.Value = 0;
            int abCount = kit.GetAbilityCount(dummy);
            UI.Label(cx + 10, cy + 80, "Abilities:", 14, Color.SkyBlue);
            for (int a = 0; a < abCount && a < 4; a++)
            {
                var ab = kit.GetAbility(dummy, a);
                string key = a switch { 0 => "Q", 1 => "W", 2 => "E", _ => "R" };
                UI.Label(cx + 15, cy + 98 + a * 35, $"[{key}] {ab.Name}", 14, Color.White);
                UI.Label(cx + 15, cy + 113 + a * 35, $"  CD:{ab.Cooldown}s Mana:{ab.ManaCost}", 12, Color.Gray);
            }

            if (UI.Button(cx + 45, cy + 265, 130, 28, selected ? "Selected" : "Select"))
            {
                _selectedChar = i;
                LobbyCommands.CmdSelectCharacter(i);
            }
        }

        // Lock In
        bool alreadyLocked = false;
        for (int i = 0; i < MobaClient.CharSelectNames.Length; i++)
        {
            if (MobaClient.CharSelectNames[i].Equals(MobaClient.Username, StringComparison.OrdinalIgnoreCase))
            {
                alreadyLocked = MobaClient.CharSelectLocked[i];
                break;
            }
        }

        if (!alreadyLocked && _selectedChar >= 0)
        {
            if (UI.Button(400, 430, 200, 40, "LOCK IN", Color.DarkGreen))
                LobbyCommands.CmdLockIn();
        }
        else if (alreadyLocked)
        {
            UI.Label(420, 440, "Locked In!", 20, Color.Green);
        }

        // Player list
        UI.Panel(50, 450, 900, 130);
        UI.Label(60, 455, "Players:", 16, Color.SkyBlue);
        int py = 475;
        for (int i = 0; i < MobaClient.CharSelectNames.Length; i++)
        {
            var teamStr = MobaClient.CharSelectTeams[i] == 0 ? "BLUE" : "RED";
            var teamCol = MobaClient.CharSelectTeams[i] == 0 ? Color.Blue : Color.Red;
            var charName = MobaClient.CharSelectChars[i] >= 0 && MobaClient.CharSelectChars[i] < GameConfig.Kits.Length
                ? GameConfig.Kits[MobaClient.CharSelectChars[i]].Name : "???";
            var lockStr = MobaClient.CharSelectLocked[i] ? " [LOCKED]" : "";

            UI.Label(60, py, $"[{teamStr}] {MobaClient.CharSelectNames[i]} - {charName}{lockStr}", 14, teamCol);
            py += 18;
        }
    }
}
