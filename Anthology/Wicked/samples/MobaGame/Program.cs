using Prowl.Wicked;
using Raylib_cs;

namespace MobaGame;

class Program
{
    static void Main(string[] args)
    {
        if (args.Contains("--server"))
        {
            RunServer(args);
        }
        else
        {
            RunClient(args);
        }
    }

    static void RunServer(string[] args)
    {
        int port = 7777;
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == "--port") int.TryParse(args[i + 1], out port);

        MobaServer.Start(port);

        Console.WriteLine("[Server] Running. Press Ctrl+C to stop.");
        var sw = System.Diagnostics.Stopwatch.StartNew();
        double lastTime = 0;

        while (true)
        {
            double now = sw.Elapsed.TotalSeconds;
            double elapsed = now - lastTime;

            // Tick at ~60 Hz
            if (elapsed < 1.0 / 60.0)
            {
                Thread.Sleep(1);
                continue;
            }
            lastTime = now;

            MobaServer.Tick();
        }
    }

    static void RunClient(string[] args)
    {
        string host = "127.0.0.1";
        int port = 7777;
        for (int i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == "--host") host = args[i + 1];
            if (args[i] == "--port") int.TryParse(args[i + 1], out port);
        }

        Raylib.InitWindow(1000, 600, "MOBA Game");
        Raylib.SetTargetFPS(60);

        MobaClient.Start(host, port);

        while (!Raylib.WindowShouldClose())
        {
            float dt = Raylib.GetFrameTime();

            MobaClient.Tick();
            Screens.Update(dt);

            Raylib.BeginDrawing();
            Raylib.ClearBackground(new Color((byte)15, (byte)15, (byte)25, (byte)255));

            Screens.Draw();

            // FPS
            Raylib.DrawFPS(Raylib.GetScreenWidth() - 80, 5);

            Raylib.EndDrawing();
        }

        Client.Disconnect();
        Raylib.CloseWindow();
    }
}
