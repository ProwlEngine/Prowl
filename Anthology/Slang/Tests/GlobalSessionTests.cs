namespace Prowl.Slang.Test;


public class GlobalSessionTests
{
    [Fact]
    public void CanCreateSession()
    {
        TargetDescription targetDesc = new()
        {
            Format = CompileTarget.Hlsl,
        };

        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc]
        };

        GlobalSession.CreateSession(sessionDesc);
    }

    [Fact]
    public void CanFindProfile()
    {
        GlobalSession.FindProfile("spirv_1_5");
    }

    [Fact]
    public void CanSetDownstreamCompilerPath()
    {
        GlobalSession.SetDownstreamCompilerPath(PassThrough.Glslang, "system/glslang/path");
    }

    [Fact]
    public void CanGetBuildTagString()
    {
        string tag = GlobalSession.GetBuildTagString();

        Assert.NotNull(tag);
        Assert.NotEmpty(tag);
    }

    [Fact]
    public void CanSetAndGetDefaultDownstreamCompiler()
    {
        GlobalSession.SetDefaultDownstreamCompiler(SourceLanguage.HLSL, PassThrough.DXC);
        Assert.Equal(PassThrough.DXC, GlobalSession.GetDefaultDownstreamCompiler(SourceLanguage.HLSL));
    }

    [Fact]
    public void CanSetAndGetLanguagePrelude()
    {
        GlobalSession.SetLanguagePrelude(SourceLanguage.HLSL, "// My HLSL Prelude");
        Assert.Equal("// My HLSL Prelude", GlobalSession.GetLanguagePrelude(SourceLanguage.HLSL));
    }

    [Fact]
    public void CheckCompileTargetSupportReturnsCorrectFlags()
    {
        Assert.False(GlobalSession.CheckCompileTargetSupport(CompileTarget.Ptx, out bool notImplemented, out bool notFound));
        Assert.False(notImplemented);
        Assert.True(notFound);
    }

    [Fact]
    public void CheckPassThroughSupportReturnsCorrectFlags()
    {
        Assert.False(GlobalSession.CheckPassThroughSupport(PassThrough.NVRTC, out bool notImplemented, out bool notFound));
        Assert.False(notImplemented);
        Assert.True(notFound);
    }

    [Fact]
    public void CanFindCapability()
    {
        GlobalSession.FindCapability("spirv_1_0");
    }

    [Fact]
    public void CanSetAndGetDownstreamCompilerForTransition()
    {
        GlobalSession.SetDownstreamCompilerForTransition(CompileTarget.Hlsl, CompileTarget.Dxil, PassThrough.DXC);
        Assert.Equal(PassThrough.DXC, GlobalSession.GetDownstreamCompilerForTransition(CompileTarget.Hlsl, CompileTarget.Dxil));
    }

    [Fact]
    public void SessionDescriptionDigestMatchesExpectedChecksum()
    {
        TargetDescription targetDesc = new()
        {
            Profile = GlobalSession.FindProfile("glsl_450"),
            Format = CompileTarget.Hlsl
        };

        TargetDescription targetDesc2 = new()
        {
            Profile = GlobalSession.FindProfile("glsl_450"),
            Format = CompileTarget.Spirv
        };

        SessionDescription sessionDesc = new()
        {
            Targets = [targetDesc, targetDesc2],
            AllowGLSLSyntax = true,
            SearchPaths = ["My/Search/Path"]
        };

        Memory<byte> bytes = GlobalSession.GetSessionDescDigest(sessionDesc);

        byte[] expectedChecksum = [74, 178, 234, 126, 121, 17, 244, 9, 187, 58, 225, 145, 103, 148, 119, 235, 143, 182, 79, 252];
        Assert.True(expectedChecksum.SequenceEqual(bytes.ToArray()));
    }
}
