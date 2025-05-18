using System.Runtime.InteropServices;

using Prowl.Slang.Native;


namespace Prowl.Slang;


public struct GenericArgReflection
{
    public TypeReflection TypeVal;
    public long IntVal;
    public bool BoolVal;

    internal unsafe Native.GenericArgReflection ToNative()
    {
        return new()
        {
            typeVal = TypeVal._ptr,
            intVal = IntVal,
            boolVal = BoolVal
        };
    }
}
