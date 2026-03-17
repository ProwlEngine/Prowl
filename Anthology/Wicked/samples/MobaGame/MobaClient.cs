using Prowl.Wicked;

namespace MobaGame;

public enum ClientScreen { Login, Lobby, CharSelect, InGame }

public static class MobaClient
{
    // Connection
    public static ClientScreen Screen = ClientScreen.Login;
    public static string StatusMessage = "";
    public static bool IsConnected;

    // Auth
    public static string Username = "";
    public static bool LoggedIn;

    // Queue
    public static bool InQueue;

    // Char Select
    public static string[] CharSelectNames = Array.Empty<string>();
    public static int[] CharSelectTeams = Array.Empty<int>();
    public static int[] CharSelectChars = Array.Empty<int>();
    public static bool[] CharSelectLocked = Array.Empty<bool>();
    public static float CharSelectTimeLeft;

    // In-Game
    public static int MyTeam;
    public static int BlueKills, RedKills;
    public static List<string> KillFeed = new();
    public static int WinningTeam = -1;
    public static bool GameOver;
    public static List<string> SystemMessages = new();

    // Lobby chat
    public static List<string> LobbyChatLog = new();

    public static void Start(string host, int port)
    {
        Client.OnConnected += () => {
            IsConnected = true;
            StatusMessage = "Connected. Please log in.";
        };
        Client.OnDisconnected += () => {
            IsConnected = false;
            LoggedIn = false;
            Screen = ClientScreen.Login;
            StatusMessage = "Disconnected from server.";
        };
        Client.Connect(host, port);
    }

    public static void Tick()
    {
        Client.Tick();
    }

    // ── RPC Handlers ──

    public static void OnLoginResult(bool success, string message)
    {
        if (success)
        {
            LoggedIn = true;
            Screen = ClientScreen.Lobby;
            StatusMessage = "";
        }
        else
        {
            StatusMessage = message;
        }
    }

    public static void OnQueueStatus(bool inQueue)
    {
        InQueue = inQueue;
    }

    public static void OnMatchFound()
    {
        Screen = ClientScreen.CharSelect;
        InQueue = false;
        StatusMessage = "Match found!";
    }

    public static void OnCharSelectUpdate(string[] names, int[] teams, int[] chars, bool[] locked, float timeLeft)
    {
        CharSelectNames = names;
        CharSelectTeams = teams;
        CharSelectChars = chars;
        CharSelectLocked = locked;
        CharSelectTimeLeft = timeLeft;

        for (int i = 0; i < names.Length; i++)
        {
            if (names[i].Equals(Username, StringComparison.OrdinalIgnoreCase))
            {
                MyTeam = teams[i];
                break;
            }
        }
    }

    public static void OnGameStart()
    {
        Screen = ClientScreen.InGame;
        GameOver = false;
        WinningTeam = -1;
        KillFeed.Clear();
        StatusMessage = "";
    }

    public static void OnGameOver(int winningTeam)
    {
        WinningTeam = winningTeam;
        GameOver = true;
    }

    public static void OnReturnToLobby()
    {
        Screen = ClientScreen.Lobby;
        GameOver = false;
        WinningTeam = -1;
        KillFeed.Clear();
        BlueKills = 0;
        RedKills = 0;
        StatusMessage = "Returned to lobby.";
    }

    public static void OnKillFeed(string message)
    {
        KillFeed.Add(message);
        if (KillFeed.Count > 10) KillFeed.RemoveAt(0);
    }

    public static void OnScoreUpdate(int blueKills, int redKills)
    {
        BlueKills = blueKills;
        RedKills = redKills;
    }

    public static void OnSystemMessage(string message)
    {
        SystemMessages.Add(message);
        if (SystemMessages.Count > 20) SystemMessages.RemoveAt(0);
    }

    // ── Helpers ──

    public static ChampionEntity? GetMyChampion()
    {
        return Client.LocalClient?.PlayerEntity as ChampionEntity;
    }

    public static void Reset()
    {
        Screen = ClientScreen.Login;
        StatusMessage = "";
        IsConnected = false;
        Username = "";
        LoggedIn = false;
        InQueue = false;
        KillFeed.Clear();
        SystemMessages.Clear();
        LobbyChatLog.Clear();
        GameOver = false;
        WinningTeam = -1;
        BlueKills = 0;
        RedKills = 0;
    }
}
