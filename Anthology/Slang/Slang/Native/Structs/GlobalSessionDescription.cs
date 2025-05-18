using System.Runtime.InteropServices;


namespace Prowl.Slang.Native;


[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GlobalSessionDescription()
{
    private uint _structureSize = (uint)sizeof(GlobalSessionDescription);

    public uint ApiVersion = 0;

    public SlangLanguageVersion LanguageVersion = SlangLanguageVersion._2025;

    public CBool EnableGLSL = false;

    private unsafe fixed uint _reserved[16];
};
