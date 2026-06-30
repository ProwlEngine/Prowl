using Prowl.Wicked;

namespace MultiplayerGame;

/// <summary>
/// Static commands for lobby-style operations (spawn requests, etc.).
/// </summary>
public static class GameCommands
{
    /// <summary>
    /// Client requests the server to spawn a player entity for them.
    /// </summary>
    [StaticCommand]
    public static void CmdRequestSpawn(string playerName)
    {
        var sender = NetworkObject.Sender;
        if (sender == null) return;

        // Don't spawn if they already have an entity
        if (sender.HasPlayerEntity) return;

        // Find the game map
        Map? gameMap = null;
        foreach (var map in Server.Maps)
        {
            if (map is GameMap)
            {
                gameMap = map;
                break;
            }
        }
        if (gameMap == null) return;

        // Spawn a player entity for this client
        var player = Server.Spawn<PlayerEntity>(gameMap, sender, p =>
        {
            p.Name.Value = string.IsNullOrWhiteSpace(playerName) ? $"Player_{sender.ClientId}" : playerName;
            p.X.Value = 100 + (sender.ClientId * 80) % 600;
            p.Y.Value = 100 + (sender.ClientId * 60) % 400;
            p.AssignRandomColor();
        });

        // Assign as their player entity (triggers map observation)
        sender.AssignPlayerEntity(player);

        Console.WriteLine($"[Server] Spawned player '{player.Name}' for client {sender.ClientId}");
    }
}
