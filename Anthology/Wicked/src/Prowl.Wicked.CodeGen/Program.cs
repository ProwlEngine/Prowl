namespace Prowl.Wicked.CodeGen;

/// <summary>
/// Console entry point for the Prowl.Wicked IL weaver.
/// Usage: Prowl.Wicked.CodeGen.exe <assembly-path>
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            Console.WriteLine("Usage: Prowl.Wicked.CodeGen <assembly-path>");
            Console.WriteLine("  Weaves [Command], [ClientRpc], and [TargetRpc] methods in the specified assembly.");
            return 1;
        }

        string assemblyPath = args[0];

        if (!File.Exists(assemblyPath))
        {
            Console.WriteLine($"Error: Assembly not found: {assemblyPath}");
            return 1;
        }

        try
        {
            bool modified = WickedWeaver.Weave(assemblyPath);
            return modified ? 0 : 0; // Return 0 even if no changes (not an error)
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during weaving: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            return 1;
        }
    }
}
