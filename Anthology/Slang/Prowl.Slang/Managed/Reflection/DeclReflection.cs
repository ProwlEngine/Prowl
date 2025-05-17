using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Slang.Native;

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


    public string Name =>
        spReflectionDecl_getName(_ptr).String;

    public SlangDeclKind Kind =>
        spReflectionDecl_getKind(_ptr);

    public uint ChildrenCount =>
        spReflectionDecl_getChildrenCount(_ptr);

    public DeclReflection GetChild(uint index) =>
        new(spReflectionDecl_getChild(_ptr, index), _session);

    public IEnumerable<DeclReflection> Children =>
        Utility.For(ChildrenCount, GetChild);

    public TypeReflection Type =>
        new(spReflection_getTypeFromDecl(_ptr), _session);

    public VariableReflection AsVariable() =>
        new(spReflectionDecl_castToVariable(_ptr), _session);

    public FunctionReflection AsFunction() =>
        new(spReflectionDecl_castToFunction(_ptr), _session);

    public GenericReflection AsGeneric() =>
        new(spReflectionDecl_castToGeneric(_ptr), _session);

    public DeclReflection Parent =>
        new(spReflectionDecl_getParent(_ptr), _session);

    public IEnumerable<DeclReflection> GetChildrenOfKind(SlangDeclKind kind) =>
        Children.Where(x => x.Kind == kind);
};
