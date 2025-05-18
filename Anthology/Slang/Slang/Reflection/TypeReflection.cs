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

    public readonly SlangTypeKind Kind =>
        spReflectionType_GetKind(_ptr);

    // only useful if `getKind() == Kind::Struct`
    public readonly uint FieldCount =>
        spReflectionType_GetFieldCount(_ptr);

    public readonly VariableReflection GetFieldByIndex(uint index) =>
        new(spReflectionType_GetFieldByIndex(_ptr, index), _session);

    public readonly IEnumerable<VariableReflection> Fields =>
        Utility.For(FieldCount, GetFieldByIndex);

    public readonly bool IsArray =>
        Kind == SlangTypeKind.ARRAY;

    public readonly TypeReflection UnwrapArray()
    {
        TypeReflection type = this;

        while (type.IsArray)
            type = type.ElementType;

        return type;
    }

    // only useful if `getKind() == Kind::Array`
    public readonly nuint ElementCount =>
        spReflectionType_GetElementCount(_ptr);

    public readonly nuint GetTotalArrayElementCount()
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

    public readonly TypeReflection ElementType =>
        new(spReflectionType_GetElementType(_ptr), _session);

    public readonly uint RowCount =>
        spReflectionType_GetRowCount(_ptr);

    public readonly uint ColumnCount =>
        spReflectionType_GetColumnCount(_ptr);

    public readonly SlangScalarType ScalarType =>
        spReflectionType_GetScalarType(_ptr);

    public readonly TypeReflection ResourceResultType =>
        new(spReflectionType_GetResourceResultType(_ptr), _session);

    public readonly SlangResourceShape ResourceShape =>
        spReflectionType_GetResourceShape(_ptr);

    public readonly SlangResourceAccess ResourceAccess =>
        spReflectionType_GetResourceAccess(_ptr);

    public readonly string Name =>
        spReflectionType_GetName(_ptr).String;

    public readonly string FullName
    {
        get
        {
            spReflectionType_GetFullName(_ptr, out ISlangBlob* namePtr).Throw();
            return NativeComProxy.Create(namePtr).GetString();
        }
    }

    public readonly uint UserAttributeCount =>
        spReflectionType_GetUserAttributeCount(_ptr);

    public readonly Attribute GetUserAttributeByIndex(uint index) =>
        new(spReflectionType_GetUserAttribute(_ptr, index), _session);

    public readonly IEnumerable<Attribute> UserAttributes =>
        Utility.For(UserAttributeCount, GetUserAttributeByIndex);

    public readonly Attribute FindAttributeByName(string name)
    {
        using U8Str str = U8Str.Alloc(name);
        return new(spReflectionType_FindUserAttributeByName(_ptr, str), _session);
    }

    public readonly Attribute FindUserAttributeByName(string name) =>
        FindAttributeByName(name);

    public readonly TypeReflection ApplySpecializations(GenericReflection generic) =>
        new(spReflectionType_applySpecializations(_ptr, generic._ptr), _session);

    public readonly GenericReflection GenericContainer =>
        new(spReflectionType_GetGenericContainer(_ptr), _session);
}
