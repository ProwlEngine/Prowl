using System.Linq;
using System.Runtime.InteropServices;


namespace Prowl.Slang.Native;


[StructLayout(LayoutKind.Sequential)]
internal unsafe struct TargetDescription()
{
    private nuint _structureSize = (nuint)sizeof(TargetDescription);

    public SlangCompileTarget Format = SlangCompileTarget.Unknown;

    public SlangProfileID Profile = SlangProfileID.Unknown;

    public SlangTargetFlags Flags = SlangTargetFlags.Default;

    public SlangFloatingPointMode FloatingPointMode = SlangFloatingPointMode.Default;

    public SlangLineDirectiveMode LineDirectiveMode = SlangLineDirectiveMode.Default;

    public CBool ForceGLSLScalarBufferLayout = false;

    public NativeUIntArray<CompilerOptionEntry> CompilerOptionEntries;


    public TargetDescription Allocate(Slang.TargetDescription src)
    {
        Format = src.Format;
        Profile = src.Profile;
        Flags = src.Flags;
        FloatingPointMode = src.FloatingPointMode;
        LineDirectiveMode = src.LineDirectiveMode;
        ForceGLSLScalarBufferLayout = src.ForceGLSLScalarBufferLayout;

        if (src.CompilerOptionEntries != null)
            CompilerOptionEntries.Allocate([.. src.CompilerOptionEntries.Select(x => new CompilerOptionEntry().Allocate(x))]);

        return this;
    }


    public void Free()
    {
        CompilerOptionEntries.ForEach(x => x.Free());
        CompilerOptionEntries.Free();
    }


    public Slang.TargetDescription Read()
    {
        return new()
        {
            Format = Format,
            Profile = Profile,
            Flags = Flags,
            FloatingPointMode = FloatingPointMode,
            LineDirectiveMode = LineDirectiveMode,
            ForceGLSLScalarBufferLayout = ForceGLSLScalarBufferLayout,
            CompilerOptionEntries = [.. CompilerOptionEntries.Select(x => x.Read())]
        };
    }
}
