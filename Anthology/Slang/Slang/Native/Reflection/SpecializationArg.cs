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
