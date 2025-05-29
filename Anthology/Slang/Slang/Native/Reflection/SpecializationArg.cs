// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace Prowl.Slang.Native;


[StructLayout(LayoutKind.Sequential)]
internal unsafe struct SpecializationArg
{
    public enum Kind : int
    {
        Unknown,
        Type,
    };


    public Kind kind;

    public TypeReflection* type;


    public static SpecializationArg FromType(TypeReflection* inType)
    {
        SpecializationArg rs;

        rs.kind = Kind.Type;
        rs.type = inType;

        return rs;
    }
};
