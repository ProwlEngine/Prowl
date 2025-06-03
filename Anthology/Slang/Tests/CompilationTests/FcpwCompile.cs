using System.Runtime.CompilerServices;

namespace Prowl.Slang.Test;


public class FcpwCompile
{
    static string GetScriptPath([CallerFilePath] string filePath = "") => Directory.GetParent(filePath)!.FullName;


    [Fact]
    [DisplayTestMethodName]
    public void FcpwCompileTest()
    {
        TargetDescription targetDesc = new()
        {
            Format = CompileTarget.Spirv,
            Profile = GlobalSession.FindProfile("spirv_1_5"),
        };

        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc],

            PreprocessorMacros = [new PreprocessorMacroDescription
            {
                Name = "_BVH_TYPE",
                Value = "2",
            }],

            FileProvider = new FileProvider(),
            SearchPaths = [Path.Join(GetScriptPath(), "../Shaders")]
        };

        Session session = GlobalSession.CreateSession(sessionDesc);

        Module module = session.LoadModule("fcpw/bvh-traversal.cs.slang", out _);

        EntryPoint entryPoint = module.FindEntryPointByName("rayIntersection");

        ComponentType compositeProgram = session.CreateCompositeComponentType([module, entryPoint], out _);

        ComponentType linkedProgram = compositeProgram.Link(out _);

        Memory<byte> code = linkedProgram.GetEntryPointCode(0, 0, out _);

        Assert.NotEqual(0, code.Length);
    }
}
