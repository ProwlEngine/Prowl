using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;


[StructLayout(LayoutKind.Explicit)]
public unsafe struct GenericArgReflection
{
    [FieldOffset(0)]
    public TypeReflection* typeVal;

    [FieldOffset(0)]
    public long intVal;

    [FieldOffset(0)]
    public CBool boolVal;
}
