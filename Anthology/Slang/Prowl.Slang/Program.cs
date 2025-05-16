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

        if (!session.LoadModule("MyShaders", out Module? module, out string? diagnostics))
        {
            Console.WriteLine("Error loading module: " + diagnostics);
        }
    }
}
