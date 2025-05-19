using System;
using System.Collections.Generic;
using System.Linq;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


public unsafe struct DeclReflection
{
    internal ComponentType _component;
    internal Native.DeclReflection* _ptr;


    internal DeclReflection(Native.DeclReflection* ptr, ComponentType component)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _component = component;
        _ptr = ptr;
    }


    public readonly string Name =>
        spReflectionDecl_getName(_ptr).String;

    public readonly SlangDeclKind Kind =>
        spReflectionDecl_getKind(_ptr);

    public readonly uint ChildrenCount =>
        spReflectionDecl_getChildrenCount(_ptr);

    public readonly DeclReflection GetChild(uint index) =>
        new(spReflectionDecl_getChild(_ptr, index), _component);

    public readonly IEnumerable<DeclReflection> Children =>
        Utility.For(ChildrenCount, GetChild);

    public readonly TypeReflection Type =>
        new(spReflection_getTypeFromDecl(_ptr), _component);

    public readonly VariableReflection AsVariable() =>
        new(spReflectionDecl_castToVariable(_ptr), _component);

    public readonly FunctionReflection AsFunction() =>
        new(spReflectionDecl_castToFunction(_ptr), _component);

    public readonly GenericReflection AsGeneric() =>
        new(spReflectionDecl_castToGeneric(_ptr), _component);

    public readonly DeclReflection Parent =>
        new(spReflectionDecl_getParent(_ptr), _component);

    public readonly IEnumerable<DeclReflection> GetChildrenOfKind(SlangDeclKind kind) =>
        Children.Where(x => x.Kind == kind);
};
