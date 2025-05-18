using System;
using System.Runtime.InteropServices;
using System.IO;
using System.Diagnostics;
using Prowl.Slang.Native;

namespace Prowl.Slang;


public static class Program
{
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
        SessionDescription sessionDesc = new();
        TargetDescription targetDesc = new();

        targetDesc.Format = SlangCompileTarget.Glsl;
        targetDesc.Profile = GlobalSession.FindProfile("glsl_450");

        sessionDesc.Targets = [targetDesc];
        sessionDesc.SearchPaths = ["./Shaders/"];

        sessionDesc.FileProvider = new FileProvider();

        Session session = GlobalSession.CreateSession(sessionDesc);

        Module module = session.LoadModule("MyShaders", out string? diagnostics);

        if (diagnostics != null)
            Console.WriteLine(diagnostics);

        EntryPoint entry = module.FindEntryPointByName("computerMain");
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

        // Console.WriteLine(System.Text.Encoding.UTF8.GetString(compiledCode.ToArray()));
        Console.WriteLine(json);
    }
}
