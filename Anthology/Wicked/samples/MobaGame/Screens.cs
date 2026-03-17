namespace MobaGame;

public static class Screens
{
    private static float _dt;

    public static void Update(float dt)
    {
        _dt = dt;
        switch (MobaClient.Screen)
        {
            case ClientScreen.Login: LoginScreen.Update(dt); break;
            case ClientScreen.Lobby: LobbyScreen.Update(dt); break;
            case ClientScreen.CharSelect: CharSelectScreen.Update(dt); break;
            case ClientScreen.InGame: InGameScreen.Update(dt); break;
        }
    }

    public static void Draw()
    {
        switch (MobaClient.Screen)
        {
            case ClientScreen.Login: LoginScreen.Draw(); break;
            case ClientScreen.Lobby: LobbyScreen.Draw(); break;
            case ClientScreen.CharSelect: CharSelectScreen.Draw(); break;
            case ClientScreen.InGame: InGameScreen.Draw(); break;
        }
    }
}
