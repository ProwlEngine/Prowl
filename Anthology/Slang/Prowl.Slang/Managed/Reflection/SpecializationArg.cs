using System.Runtime.InteropServices;


namespace Prowl.Slang;


[StructLayout(LayoutKind.Sequential)]
public unsafe struct SpecializationArg
{
    public enum Kind : int
    {
        Unknown, /**< An invalid specialization argument. */
        Type,    /**< Specialize to a type. */
    };

    /** The kind of specialization argument. */
    public Kind kind;

    /** A type specialization argument, used for `Kind::Type`. */
    public TypeReflection* type;

    public static SpecializationArg fromType(TypeReflection* inType)
    {
        SpecializationArg rs;
        rs.kind = Kind.Type;
        rs.type = inType;
        return rs;
    }
};
