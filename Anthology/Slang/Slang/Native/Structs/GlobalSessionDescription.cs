// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.InteropServices;


namespace Prowl.Slang.Native;


[StructLayout(LayoutKind.Sequential)]
internal unsafe struct GlobalSessionDescription()
{
    private uint _structureSize = (uint)sizeof(GlobalSessionDescription);

    public uint ApiVersion = 0;

    public LanguageVersion LanguageVersion = LanguageVersion._2025;

    public CBool EnableGLSL = false;

    private unsafe fixed uint _reserved[16];
};
