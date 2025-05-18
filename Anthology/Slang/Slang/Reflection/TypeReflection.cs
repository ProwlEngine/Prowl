using System;

using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Slang.Native;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


[StructLayout(LayoutKind.Sequential)]
public unsafe struct TypeReflection
{
    internal Session _session;
    internal Native.TypeReflection* _ptr;


    internal TypeReflection(Native.TypeReflection* ptr, Session session)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _session = session;
        _ptr = ptr;
    }

    public SlangTypeKind Kind =>
        spReflectionType_GetKind(_ptr);

    // only useful if `getKind() == Kind::Struct`
    public uint FieldCount =>
        spReflectionType_GetFieldCount(_ptr);

    public VariableReflection GetFieldByIndex(uint index) =>
        new(spReflectionType_GetFieldByIndex(_ptr, index), _session);

    public IEnumerable<VariableReflection> Fields =>
        Utility.For(FieldCount, GetFieldByIndex);

    public bool IsArray =>
        Kind == SlangTypeKind.ARRAY;

    public TypeReflection UnwrapArray()
    {
        TypeReflection type = this;

        while (type.IsArray)
            type = type.ElementType;

        return type;
    }

    // only useful if `getKind() == Kind::Array`
    public nuint ElementCount =>
        spReflectionType_GetElementCount(_ptr);

    public nuint GetTotalArrayElementCount()
    {
        if (!IsArray)
            return 0;

        nuint result = 1;
        TypeReflection type = this;
        for (; ; )
        {
            if (!type.IsArray)
                return result;

            result *= type.ElementCount;
            type = type.ElementType;
        }
    }

    public TypeReflection ElementType =>
        new(spReflectionType_GetElementType(_ptr), _session);

    public uint RowCount =>
        spReflectionType_GetRowCount(_ptr);

    public uint ColumnCount =>
        spReflectionType_GetColumnCount(_ptr);

    public SlangScalarType ScalarType =>
        spReflectionType_GetScalarType(_ptr);

    public TypeReflection ResourceResultType =>
        new(spReflectionType_GetResourceResultType(_ptr), _session);

    public SlangResourceShape ResourceShape =>
        spReflectionType_GetResourceShape(_ptr);

    public SlangResourceAccess ResourceAccess =>
        spReflectionType_GetResourceAccess(_ptr);

    public string Name =>
        spReflectionType_GetName(_ptr).String;

    public string FullName
    {
        get
        {
            spReflectionType_GetFullName(_ptr, out ISlangBlob* namePtr).Throw();
            return NativeComProxy.Create(namePtr).GetString();
        }
    }

    public uint UserAttributeCount =>
        spReflectionType_GetUserAttributeCount(_ptr);

    public Attribute GetUserAttributeByIndex(uint index) =>
        new(spReflectionType_GetUserAttribute(_ptr, index), _session);

    public IEnumerable<Attribute> UserAttributes =>
        Utility.For(UserAttributeCount, GetUserAttributeByIndex);

    public Attribute FindAttributeByName(ConstU8Str name) =>
        new(spReflectionType_FindUserAttributeByName(_ptr, name), _session);

    public Attribute FindUserAttributeByName(ConstU8Str name) =>
        FindAttributeByName(name);

    public TypeReflection ApplySpecializations(GenericReflection generic) =>
        new(spReflectionType_applySpecializations(_ptr, generic._ptr), _session);

    public GenericReflection GenericContainer =>
        new(spReflectionType_GetGenericContainer(_ptr), _session);
}
