using Raylib_cs;

namespace MobaGame;

public static class LoginScreen
{
    private static TextInput _usernameInput = new(24);
    private static TextInput _passwordInput = new(24, isPassword: true);

    public static void Update(float dt)
    {
        _usernameInput.Update(dt);
        _passwordInput.Update(dt);
        _usernameInput.ClickCheck(360, 220, 280, 30);
        _passwordInput.ClickCheck(360, 270, 280, 30);

        if (Raylib.IsKeyPressed(KeyboardKey.Tab))
        {
            if (_usernameInput.Focused) { _usernameInput.Focused = false; _passwordInput.Focused = true; }
            else { _usernameInput.Focused = true; _passwordInput.Focused = false; }
        }
    }

    public static void Draw()
    {
        int cx = 500;

        UI.Panel(300, 150, 400, 280);
        UI.Label(cx - Raylib.MeasureText("MOBA Game", 32) / 2, 170, "MOBA Game", 32, Color.Gold);

        UI.Label(360, 205, "Username:");
        _usernameInput.Draw(360, 220, 280, 30, "Enter username");

        UI.Label(360, 255, "Password:");
        _passwordInput.Draw(360, 270, 280, 30, "Enter password");

        if (UI.Button(360, 320, 130, 35, "Login"))
        {
            if (MobaClient.IsConnected && _usernameInput.Text.Length > 0)
            {
                MobaClient.Username = _usernameInput.Text;
                LobbyCommands.CmdLogin(_usernameInput.Text, _passwordInput.Text);
            }
        }

        if (UI.Button(510, 320, 130, 35, "Register"))
        {
            if (MobaClient.IsConnected && _usernameInput.Text.Length > 0)
            {
                MobaClient.Username = _usernameInput.Text;
                LobbyCommands.CmdRegister(_usernameInput.Text, _passwordInput.Text);
            }
        }

        if (!string.IsNullOrEmpty(MobaClient.StatusMessage))
        {
            var msgColor = MobaClient.LoggedIn ? Color.Green : Color.Red;
            UI.Label(360, 370, MobaClient.StatusMessage, 14, msgColor);
        }

        if (!MobaClient.IsConnected)
            UI.Label(360, 395, "Connecting to server...", 14, Color.Yellow);
    }
}
