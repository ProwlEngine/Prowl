using System;
using System.Collections.Generic;
using System.Linq;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


public unsafe struct DeclReflection
{
    internal Session _session;
    internal Native.DeclReflection* _ptr;


    internal DeclReflection(Native.DeclReflection* ptr, Session session)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _session = session;
        _ptr = ptr;
    }


    public readonly string Name =>
        spReflectionDecl_getName(_ptr).String;

    public readonly SlangDeclKind Kind =>
        spReflectionDecl_getKind(_ptr);

    public readonly uint ChildrenCount =>
        spReflectionDecl_getChildrenCount(_ptr);

    public readonly DeclReflection GetChild(uint index) =>
        new(spReflectionDecl_getChild(_ptr, index), _session);

    public readonly IEnumerable<DeclReflection> Children =>
        Utility.For(ChildrenCount, GetChild);

    public readonly TypeReflection Type =>
        new(spReflection_getTypeFromDecl(_ptr), _session);

    public readonly VariableReflection AsVariable() =>
        new(spReflectionDecl_castToVariable(_ptr), _session);

    public readonly FunctionReflection AsFunction() =>
        new(spReflectionDecl_castToFunction(_ptr), _session);

    public readonly GenericReflection AsGeneric() =>
        new(spReflectionDecl_castToGeneric(_ptr), _session);

    public readonly DeclReflection Parent =>
        new(spReflectionDecl_getParent(_ptr), _session);

    public readonly IEnumerable<DeclReflection> GetChildrenOfKind(SlangDeclKind kind) =>
        Children.Where(x => x.Kind == kind);
};
