using System;
using System.IO;
using System.Runtime.CompilerServices;

using Prowl.Slang;


public static class Program
{
    static string GetScriptPath([CallerFilePath] string filePath = "") => Directory.GetParent(filePath)!.FullName;


    public class FileProvider : IFileProvider
    {
        public Memory<byte>? LoadFile(string path)
        {
            if (!File.Exists(path))
                return null;

            return new Memory<byte>(File.ReadAllBytes(path));
        }
    }


    public static void Main()
    {
        Environment.CurrentDirectory = GetScriptPath();

        CompileCode();

        /*
        Process currentProcess = Process.GetCurrentProcess();

        Stopwatch stopwatch = Stopwatch.StartNew();

        long c = 0;
        while (true)
        {
            c++;
            CompileCode();

            if (stopwatch.ElapsedMilliseconds / 1000 > .5)
            {
                stopwatch.Restart();

                // WorkingSet64 includes both managed and native allocations

                currentProcess.Refresh();
                long memoryUsed = currentProcess.PrivateMemorySize64;

                Console.WriteLine($"Memory used: {memoryUsed / (1024.0 * 1024.0 * 1024):F2} GB. Iterations: {c}");
            }
        }
        */
    }


    private static void CompileCode()
    {
        TargetDescription targetDesc = new()
        {
            Format = SlangCompileTarget.Glsl,
            Profile = GlobalSession.FindProfile("glsl_450")
        };

        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc],
            SearchPaths = ["../Shaders/"],
            FileProvider = new FileProvider()
        };

        Session session = GlobalSession.CreateSession(sessionDesc);

        Module module = session.LoadModule("compute", out string? diagnostics);

        if (diagnostics != null)
            Console.WriteLine(diagnostics);

        EntryPoint entry = module.FindEntryPointByName("computeMain");
        ComponentType program = session.CreateCompositeComponentType([module, entry], out diagnostics);

        if (diagnostics != null)
            Console.WriteLine(diagnostics);

        Memory<byte> compiledCode = program.GetEntryPointCode(0, 0, out diagnostics);

        if (diagnostics != null)
            Console.WriteLine(diagnostics);

        ShaderReflection reflection = program.GetLayout(0, out diagnostics);

        if (diagnostics != null)
            Console.WriteLine(diagnostics);

        string json = reflection.ToJson();

        Console.WriteLine(json);
    }
}
