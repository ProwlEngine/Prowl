using System.Numerics;
using Prowl.Wicked;
using Raylib_cs;

namespace MultiplayerGame;

public static class Program
{
    private const int ScreenWidth = 800;
    private const int ScreenHeight = 600;
    private const int Port = 7777;
    private const float TickRate = 1f / 60f;

    private static string _playerName = "Player";

    public static void Main(string[] args)
    {
        bool isServer = args.Contains("--server");
        string host = "127.0.0.1";

        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--connect" && i + 1 < args.Length)
                host = args[i + 1];
            if (args[i] == "--name" && i + 1 < args.Length)
                _playerName = args[i + 1];
        }

        if (isServer)
            RunServer();
        else
            RunClient(host);
    }

    // ── Server ──

    private static GameMap? _serverMap;

    private static void RunServer()
    {
        Console.WriteLine("[Server] Starting dedicated server...");
        Server.OnClientConnected += OnServerClientConnected;
        Server.OnClientDisconnected += OnServerClientDisconnected;
        Server.Start(Port);
        _serverMap = Server.CreateMap<GameMap>();
        Console.WriteLine($"[Server] Listening on port {Port}. Press Ctrl+C to stop.");

        while (true)
        {
            Server.Tick();
            BroadcastPositions();
            Thread.Sleep((int)(TickRate * 1000));
        }
    }

    private static void OnServerClientConnected(RemoteClient client)
    {
        Console.WriteLine($"[Server] Client connected: {client.ClientId}");
    }

    private static void OnServerClientDisconnected(RemoteClient client)
    {
        Console.WriteLine($"[Server] Client disconnected: {client.ClientId}");
        if (client.PlayerEntity != null)
            Server.Despawn(client.PlayerEntity);
    }

    private static void BroadcastPositions()
    {
        if (_serverMap == null) return;
        foreach (var entity in _serverMap.GetEntities<PlayerEntity>())
            entity.RpcUpdatePosition(entity.X, entity.Y);
    }

    // ── Client ──

    private static void RunClient(string host)
    {
        Raylib.InitWindow(ScreenWidth, ScreenHeight, "Prowl.Wicked - Multiplayer Sample");
        Raylib.SetTargetFPS(60);

        Client.OnConnected += OnClientConnected;
        Client.OnDisconnected += OnClientDisconnected;
        Client.Connect(host, Port);

        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();

            if (Client.Active)
            {
                Client.Tick();
                HandleInput();
                UpdateChatTimers(dt);
            }

            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color(30, 30, 40, 255));

            if (Client.IsConnected)
            {
                DrawGame();
                DrawHUD();
            }
            else if (Client.Active)
            {
                Raylib.DrawText("Connecting...", 320, 280, 20, Color.White);
            }
            else
            {
                Raylib.DrawText("Disconnected", 320, 280, 20, Color.Red);
            }

            Raylib.EndDrawing();
        }

        if (Client.Active)
            Client.Disconnect();

        Raylib.CloseWindow();
    }

    private static void OnClientConnected()
    {
        Console.WriteLine("[Client] Connected to server!");
        GameCommands.CmdRequestSpawn(_playerName);
    }

    private static void OnClientDisconnected()
    {
        Console.WriteLine("[Client] Disconnected from server.");
    }

    private static void HandleInput()
    {
        if (!Client.IsConnected) return;

        var localPlayer = Client.LocalClient?.PlayerEntity as PlayerEntity;
        if (localPlayer == null) return;

        // Click to move
        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            var mousePos = Raylib.GetMousePosition();
            localPlayer.CmdMove(mousePos.X - 10, mousePos.Y - 10);
        }

        // WASD movement
        float moveX = 0, moveY = 0;
        if (Raylib.IsKeyDown(KeyboardKey.W) || Raylib.IsKeyDown(KeyboardKey.Up)) moveY -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.S) || Raylib.IsKeyDown(KeyboardKey.Down)) moveY += 1;
        if (Raylib.IsKeyDown(KeyboardKey.A) || Raylib.IsKeyDown(KeyboardKey.Left)) moveX -= 1;
        if (Raylib.IsKeyDown(KeyboardKey.D) || Raylib.IsKeyDown(KeyboardKey.Right)) moveX += 1;

        if (moveX != 0 || moveY != 0)
        {
            float targetX = localPlayer.X + moveX * 200 * Raylib.GetFrameTime();
            float targetY = localPlayer.Y + moveY * 200 * Raylib.GetFrameTime();
            localPlayer.CmdMove(
                Math.Clamp(targetX, 0, ScreenWidth - 20),
                Math.Clamp(targetY, 0, ScreenHeight - 20));
        }
    }

    private static void UpdateChatTimers(float dt)
    {
        foreach (var entity in Client.Entities)
        {
            if (entity is PlayerEntity player && player.ChatMessageTimer > 0)
                player.ChatMessageTimer -= dt;
        }
    }

    private static void DrawGame()
    {
        // Draw grid
        for (int x = 0; x < ScreenWidth; x += 40)
            Raylib.DrawLine(x, 0, x, ScreenHeight, new Color(50, 50, 60, 255));
        for (int y = 0; y < ScreenHeight; y += 40)
            Raylib.DrawLine(0, y, ScreenWidth, y, new Color(50, 50, 60, 255));

        // Draw all entities
        foreach (var entity in Client.Entities)
        {
            if (entity is PlayerEntity player)
            {
                var color = new Color(player.ColorR, player.ColorG, player.ColorB, (byte)255);

                // Draw player square
                Raylib.DrawRectangle((int)player.X, (int)player.Y, 20, 20, color);

                // Highlight local player
                if (player.IsOwner)
                    Raylib.DrawRectangleLines((int)player.X - 2, (int)player.Y - 2, 24, 24, Color.White);

                // Draw name
                int nameWidth = Raylib.MeasureText(player.Name, 12);
                Raylib.DrawText(player.Name,
                    (int)player.X + 10 - nameWidth / 2,
                    (int)player.Y - 16, 12, Color.White);

                // Draw chat bubble
                if (player.ChatMessageTimer > 0 && player.LastChatMessage != null)
                {
                    int msgWidth = Raylib.MeasureText(player.LastChatMessage, 14);
                    Raylib.DrawRectangle(
                        (int)player.X + 10 - msgWidth / 2 - 4,
                        (int)player.Y - 40,
                        msgWidth + 8, 20,
                        new Color((byte)0, (byte)0, (byte)0, (byte)180));
                    Raylib.DrawText(player.LastChatMessage,
                        (int)player.X + 10 - msgWidth / 2,
                        (int)player.Y - 38, 14, Color.Yellow);
                }
            }
        }
    }

    private static void DrawHUD()
    {
        Raylib.DrawText($"Players: {Client.Entities.Count}", 10, 10, 16, Color.LightGray);
        Raylib.DrawText($"RTT: {Client.RoundTripTime * 1000:F0}ms", 10, 30, 16, Color.LightGray);
        Raylib.DrawText("WASD/Arrows to move, Click to move", 10, ScreenHeight - 24, 14, Color.DarkGray);
    }
}
