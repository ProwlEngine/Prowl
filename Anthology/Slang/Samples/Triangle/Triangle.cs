using System;
using System.IO;
using System.Runtime.CompilerServices;

using Prowl.Slang;


public static class Program
{
    static string GetScriptPath([CallerFilePath] string filePath = "") => Directory.GetParent(filePath)!.FullName;

    public static void Main()
    {
        Environment.CurrentDirectory = GetScriptPath();

        CompileCode();
    }


    private static void CompileCode()
    {
        try
        {
            TargetDescription targetDesc = new()
            {
                Format = CompileTarget.Spirv,
                Profile = GlobalSession.FindProfile("spirv_1_5")
            };

            SessionDescription sessionDesc = new()
            {
                Targets = [targetDesc],
                SearchPaths = ["./", "../Shaders"],
                FileProvider = new FileProvider()
            };

            Session session = GlobalSession.CreateSession(sessionDesc);

            Module module = session.LoadModule("shaders", out DiagnosticInfo diagnostics);

            EntryPoint vertex = module.FindEntryPointByName("vertexMain");
            EntryPoint fragment = module.FindEntryPointByName("fragmentMain");

            ComponentType program = session.CreateCompositeComponentType([module, vertex, fragment], out diagnostics);

            Memory<byte> compiledCode = program.GetEntryPointCode(0, 0, out diagnostics);

            ShaderReflection reflection = program.GetLayout(0, out diagnostics);

            string json = reflection.ToJson();

            Console.WriteLine(json);
        }
        catch (CompilationException ex)
        {
            foreach (Diagnostic diagnostic in ex.Diagnostics.GetDiagnostics())
            {
                Console.WriteLine(diagnostic);
            }
        }
    }
}
