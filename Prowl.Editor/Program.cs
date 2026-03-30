namespace Prowl.Editor;

public static class Program
{
    public static void Main(string[] args)
    {
        var editor = new EditorApplication();
        editor.Run("Prowl Editor", 1600, 900);
    }
}
