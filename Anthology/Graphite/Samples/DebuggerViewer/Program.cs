namespace DebuggerViewer;

internal sealed record ScriptStep(int Frame, float X, float Y, bool Click);

internal sealed class Options
{
    public string? ShotPath;
    public int ShotFrames = 30;
    public bool Paused;
    public readonly List<ScriptStep> Script = new();

    public static Options Parse(string[] args)
    {
        Options options = new();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--shot" when i + 1 < args.Length: options.ShotPath = args[++i]; break;
                case "--frames" when i + 1 < args.Length: options.ShotFrames = int.Parse(args[++i]); break;
                case "--paused": options.Paused = true; break;
                case "--click" or "--hover" when i + 1 < args.Length:
                {
                    bool click = args[i] == "--click";
                    string[] parts = args[++i].Split('@');
                    string[] xy = parts[0].Split(',');
                    options.Script.Add(new ScriptStep(int.Parse(parts[1]), float.Parse(xy[0]), float.Parse(xy[1]), click));
                    break;
                }
            }
        }
        return options;
    }
}

internal static class Program
{
    private static void Main(string[] args)
    {
        using ViewerWindow window = new(Options.Parse(args));
        window.Run();
    }
}
