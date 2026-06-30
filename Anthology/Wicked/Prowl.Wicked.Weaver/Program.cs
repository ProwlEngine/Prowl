namespace Prowl.Wicked.Weaver;

public class Program
{
    public static int Main(string[] args)
    {
        if (args.Length < 1)
        {
            Console.Error.WriteLine("Usage: Prowl.Wicked.Weaver <assembly-path>");
            return 1;
        }

        string assemblyPath = args[0];
        if (!File.Exists(assemblyPath))
        {
            Console.Error.WriteLine($"Assembly not found: {assemblyPath}");
            return 1;
        }

        try
        {
            var weaver = new Weaver(assemblyPath);
            return weaver.Run() ? 0 : 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Weaver failed: {ex.Message}");
            return 1;
        }
    }
}
