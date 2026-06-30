using Raylib_cs;

namespace MobaGame;

public static class LobbyScreen
{
    private static TextInput _lobbyChatInput = new(64);

    public static void Update(float dt)
    {
        _lobbyChatInput.Update(dt);
        _lobbyChatInput.ClickCheck(30, 545, 500, 25);

        if (_lobbyChatInput.Focused && Raylib.IsKeyPressed(KeyboardKey.Enter) && _lobbyChatInput.Text.Length > 0)
        {
            LobbyCommands.CmdLobbyChat(_lobbyChatInput.Text);
            _lobbyChatInput.Text = "";
        }
    }

    public static void Draw()
    {
        UI.Label(10, 10, $"Logged in as: {MobaClient.Username}", 18, Color.Gold);

        // Queue button
        if (!MobaClient.InQueue)
        {
            if (UI.Button(400, 50, 200, 50, "PLAY", Color.DarkGreen))
                LobbyCommands.CmdJoinQueue();
        }
        else
        {
            UI.Label(420, 55, "In Queue...", 24, Color.Yellow);
            if (UI.Button(600, 55, 120, 40, "Cancel", Color.Maroon))
                LobbyCommands.CmdLeaveQueue();
        }

        // System messages
        int sy = 120;
        for (int i = Math.Max(0, MobaClient.SystemMessages.Count - 5); i < MobaClient.SystemMessages.Count; i++)
        {
            UI.Label(30, sy, MobaClient.SystemMessages[i], 13, Color.Yellow);
            sy += 16;
        }

        // Lobby Chat
        UI.Panel(20, 210, 960, 360);
        UI.Label(30, 215, "Lobby Chat", 18, Color.SkyBlue);
        int cy = 240;
        for (int i = Math.Max(0, MobaClient.LobbyChatLog.Count - 18); i < MobaClient.LobbyChatLog.Count; i++)
        {
            UI.Label(30, cy, MobaClient.LobbyChatLog[i], 14, Color.LightGray);
            cy += 18;
        }
        _lobbyChatInput.Draw(30, 545, 500, 25, "Type to chat...");
    }
}
