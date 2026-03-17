using Prowl.Wicked;

namespace MobaGame;

/// <summary>
/// Static commands (client → server) and static RPCs (server → client)
/// for lobby, matchmaking, and game flow.
/// </summary>
public static class LobbyCommands
{
    // ── Auth ──

    [StaticCommand]
    public static void CmdLogin(string username, string password)
    {
        var sender = NetworkObject.Sender;
        if (sender == null) return;
        MobaServer.HandleLogin(sender, username, password);
    }

    [StaticCommand]
    public static void CmdRegister(string username, string password)
    {
        var sender = NetworkObject.Sender;
        if (sender == null) return;
        MobaServer.HandleRegister(sender, username, password);
    }

    [StaticRpc]
    public static void RpcLoginResult(RemoteClient target, bool success, string message)
    {
        MobaClient.OnLoginResult(success, message);
    }

    // ── Queue / Matchmaking ──

    [StaticCommand]
    public static void CmdJoinQueue()
    {
        var sender = NetworkObject.Sender;
        if (sender == null) return;
        MobaServer.HandleJoinQueue(sender);
    }

    [StaticCommand]
    public static void CmdLeaveQueue()
    {
        var sender = NetworkObject.Sender;
        if (sender == null) return;
        MobaServer.HandleLeaveQueue(sender);
    }

    [StaticRpc]
    public static void RpcQueueStatus(RemoteClient target, bool inQueue)
    {
        MobaClient.OnQueueStatus(inQueue);
    }

    // ── Character Select ──

    [StaticRpc]
    public static void RpcMatchFound(RemoteClient target)
    {
        MobaClient.OnMatchFound();
    }

    [StaticCommand]
    public static void CmdSelectCharacter(int charId)
    {
        var sender = NetworkObject.Sender;
        if (sender == null) return;
        MobaServer.HandleSelectCharacter(sender, charId);
    }

    [StaticCommand]
    public static void CmdLockIn()
    {
        var sender = NetworkObject.Sender;
        if (sender == null) return;
        MobaServer.HandleLockIn(sender);
    }

    [StaticRpc]
    public static void RpcCharSelectUpdate(RemoteClient target,
        string[] names, int[] teams, int[] chars, bool[] locked, float timeLeft)
    {
        MobaClient.OnCharSelectUpdate(names, teams, chars, locked, timeLeft);
    }

    // ── Game Flow ──

    [StaticRpc]
    public static void RpcGameStart(RemoteClient target)
    {
        MobaClient.OnGameStart();
    }

    [StaticRpc]
    public static void RpcGameOver(RemoteClient target, int winningTeam)
    {
        MobaClient.OnGameOver(winningTeam);
    }

    [StaticRpc]
    public static void RpcReturnToLobby(RemoteClient target)
    {
        MobaClient.OnReturnToLobby();
    }

    [StaticRpc]
    public static void RpcKillFeed(RemoteClient target, string message)
    {
        MobaClient.OnKillFeed(message);
    }

    [StaticRpc]
    public static void RpcScoreUpdate(RemoteClient target, int blueKills, int redKills)
    {
        MobaClient.OnScoreUpdate(blueKills, redKills);
    }

    // ── Lobby chat ──

    [StaticCommand]
    public static void CmdLobbyChat(string message)
    {
        var sender = NetworkObject.Sender;
        if (sender == null) return;
        MobaServer.HandleLobbyChat(sender, message);
    }

    [StaticRpc]
    public static void RpcSystemMessage(RemoteClient target, string message)
    {
        MobaClient.OnSystemMessage(message);
    }
}
