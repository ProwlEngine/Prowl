namespace Prowl.Editor;

public static class Program
{
    /// <summary>If set via --project arg, the editor opens this project directly (skips launcher).</summary>
    public static string? StartupProjectPath { get; private set; }

    /// <summary>If set via --restore-scene arg, this scene is loaded instead of the last saved scene.</summary>
    public static string? RestoreScenePath { get; private set; }

    public static void Main(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--project" && i + 1 < args.Length)
                StartupProjectPath = args[++i];
            else if (args[i] == "--restore-scene" && i + 1 < args.Length)
                RestoreScenePath = args[++i];
        }

        var editor = new EditorApplication();
        editor.Run("Prowl Editor", 1200, 800);
    }
}
