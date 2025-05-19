using System;

using System.Collections.Generic;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


public unsafe struct TypeParameterReflection
{
    internal ComponentType _component;
    internal Native.TypeParameterReflection* _ptr;


    internal TypeParameterReflection(Native.TypeParameterReflection* ptr, ComponentType component)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _component = component;
        _ptr = ptr;
    }


    public readonly string Name =>
        spReflectionTypeParameter_GetName(_ptr).String;

    public readonly uint Index =>
        spReflectionTypeParameter_GetIndex(_ptr);

    public readonly uint ConstraintCount =>
        spReflectionTypeParameter_GetConstraintCount(_ptr);

    public readonly TypeReflection GetConstraintByIndex(uint index) =>
        new(spReflectionTypeParameter_GetConstraintByIndex(_ptr, index), _component);

    public readonly IEnumerable<TypeReflection> GetConstraints() =>
        Utility.For(ConstraintCount, GetConstraintByIndex);
}
