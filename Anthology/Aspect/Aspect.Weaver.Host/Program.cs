using Aspect.Weaver;

namespace Aspect.Weaver.Host;

class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("==============================================");
        Console.WriteLine("               Aspect IL Weaver               ");
        Console.WriteLine("==============================================");
        Console.WriteLine();

        if (args.Length == 0)
        {
            PrintUsage();
            return;
        }

        var assemblyPath = args[0];

        if (!File.Exists(assemblyPath))
        {
            Console.WriteLine($"Error: Assembly not found: {assemblyPath}");
            return;
        }

        try
        {
            var weaver = new ModuleWeaver();
            weaver.Weave(assemblyPath);

            Console.WriteLine();
            Console.WriteLine("Success! Your assembly has been woven with aspects.");
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine($"Error during weaving: {ex.Message}");
            Console.WriteLine(ex.StackTrace);
            Environment.Exit(1);
        }
    }

    static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  Aspect.Weaver.Host.exe <assembly-path>");
        Console.WriteLine();
        Console.WriteLine("Example:");
        Console.WriteLine("  Aspect.Weaver.Host.exe MyApp.dll");
        Console.WriteLine();
        Console.WriteLine("This will weave all aspect attributes found in the assembly.");
        Console.WriteLine("The assembly will be modified in-place.");
    }
}
