// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.InteropServices;

using SlangInt = nint;

namespace Prowl.Slang.Native;


[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SourceLocation
{
    public ConstU8Str FilePath;
    public SlangInt Line;
    public SlangInt Column;
}
