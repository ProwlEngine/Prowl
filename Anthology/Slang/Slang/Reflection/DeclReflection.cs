using System;
using System.Collections.Generic;
using System.Linq;

using static Prowl.Slang.Native.SlangNative_Deprecated;


namespace Prowl.Slang;


/// <summary>
/// Declaration kind for a <see cref="DeclReflection"/> object.
/// </summary>
public enum DeclKind : uint
{
    /// <summary>
    /// The declaration is not supported for reflection.
    /// </summary>
    UnsupportedForReflection,

    /// <summary>
    /// The declaration is a struct.
    /// </summary>
    Struct,

    /// <summary>
    /// The declaration is a method or function.
    /// </summary>
    Func,

    /// <summary>
    /// The declaration is a slang module.
    /// </summary>
    Module,

    /// <summary>
    /// The declaration is a generic type.
    /// </summary>
    Generic,

    /// <summary>
    /// The declaration is a variable.
    /// </summary>
    Variable,

    /// <summary>
    /// The declaration is a namespace.
    /// </summary>
    Namespace
}


/// <summary>
/// Reflection information for a shader declaration such as a module, function, struct, variable, or namespace.
/// </summary>
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


    /// <summary>
    /// The name of this declaration.
    /// </summary>
    public readonly string Name =>
        spReflectionDecl_getName(_ptr).String;

    /// <summary>
    /// The declaration type.
    /// </summary>
    public readonly DeclKind Kind =>
        spReflectionDecl_getKind(_ptr);

    /// <summary>
    /// The number of children in this <see cref="DeclReflection"/>.
    /// </summary>
    public readonly uint ChildrenCount =>
        spReflectionDecl_getChildrenCount(_ptr);

    /// <summary>
    /// Gets a child declaration at the given index.
    /// </summary>
    public readonly DeclReflection GetChild(uint index) =>
        new(spReflectionDecl_getChild(_ptr, index), _component);

    /// <summary>
    /// The child declarations of this declaration.
    /// </summary>
    public readonly IEnumerable<DeclReflection> Children =>
        Utility.For(ChildrenCount, GetChild);

    /// <summary>
    /// The type information for this declaration.
    /// </summary>
    public readonly TypeReflection Type =>
        new(spReflection_getTypeFromDecl(_ptr), _component);

    /// <summary>
    /// Casts this declaration into a <see cref="VariableReflection"/> reflection object. This is only valid if the declaration type is <see cref="DeclKind.Variable"/>.
    /// </summary>
    public readonly VariableReflection AsVariable() =>
        Kind == DeclKind.Variable ? new(spReflectionDecl_castToVariable(_ptr), _component) : throw new InvalidOperationException("Declaration is not a Variable type");

    /// <summary>
    /// Casts this declaration into a <see cref="VariableReflection"/> reflection object. This is only valid if the declaration type is <see cref="DeclKind.Func"/>.
    /// </summary>
    public readonly FunctionReflection AsFunction() =>
        Kind == DeclKind.Func ? new(spReflectionDecl_castToFunction(_ptr), _component) : throw new InvalidOperationException("Declaration is not a Function type");

    /// <summary>
    /// Casts this declaration into a <see cref="VariableReflection"/> reflection object. This is only valid if the declaration type is <see cref="DeclKind.Generic"/>.
    /// </summary>
    public readonly GenericReflection AsGeneric() =>
        Kind == DeclKind.Generic ? new(spReflectionDecl_castToGeneric(_ptr), _component) : throw new InvalidOperationException("Declaration is not a Generic type");

    /// <summary>
    /// The parent <see cref="DeclReflection"/> object, or null if this instance is the root.
    /// </summary>
    public readonly DeclReflection? Parent
    {
        get
        {
            Native.DeclReflection* parentPtr = spReflectionDecl_getParent(_ptr);

            if (parentPtr == null)
                return null;

            return new(parentPtr, _component);
        }
    }

    /// <summary>
    /// Gets the child <see cref="DeclReflection"/> objects that match a given <see cref="DeclKind"/>.
    /// </summary>
    public readonly IEnumerable<DeclReflection> GetChildrenOfKind(DeclKind kind) =>
        Children.Where(x => x.Kind == kind);
};
