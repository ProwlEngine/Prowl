// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Slang;


///
public struct GenericArgReflection
{
    ///
    public TypeReflection TypeVal;

    ///
    public long IntVal;

    ///
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
