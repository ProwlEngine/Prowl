using System.Runtime.CompilerServices;

namespace Prowl.Slang.Test;

// Test metal argument buffer tier2 layout rules.

public class SessionTests
{
    static string GetScriptPath([CallerFilePath] string filePath = "") => Directory.GetParent(filePath)!.FullName;

    private static Session Create()
    {
        TargetDescription targetDesc = new()
        {

        };

        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc],
            FileProvider = new FileProvider(),
            SearchPaths = [Path.Join(GetScriptPath(), "Shaders")]
        };

        return GlobalSession.CreateSession(sessionDesc);
    }

    [Fact]
    public void CanLoadModule()
    {
        Create().LoadModule("basic-shader", out _);
    }

    [Fact]
    public void CanLoadModuleFromSourceString()
    {
        const string sourceString =
"""
float4 fragMain(float4 pos:SV_Position) : SV_Target
{
    return pos;
}
""";

        Create().LoadModuleFromSourceString("m", "m.slang", sourceString, out _);
    }
}
