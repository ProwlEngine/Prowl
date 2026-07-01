// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Linq;
using System.Runtime.InteropServices;


namespace Prowl.Slang.Native;


[StructLayout(LayoutKind.Sequential)]
internal unsafe struct TargetDescription()
{
    private nuint _structureSize = (nuint)sizeof(TargetDescription);

    public CompileTarget Format = CompileTarget.Unknown;

    public ProfileID Profile = ProfileID.Unknown;

    public TargetFlags Flags = TargetFlags.Default;

    public FloatingPointMode FloatingPointMode = FloatingPointMode.Default;

    public LineDirectiveMode LineDirectiveMode = LineDirectiveMode.Default;

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
