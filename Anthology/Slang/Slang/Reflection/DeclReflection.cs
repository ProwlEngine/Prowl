using System;
using System.Collections.Generic;
using System.ComponentModel;
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
/// Represents reflection information for a declaration in a shader module.
/// Provides access to declaration metadata, relationships, and specialized type conversions.
/// </summary>
public unsafe struct DeclReflection
{
    internal ComponentType _component;
    internal Native.DeclReflection* _ptr;

    /// <summary>
    /// Initializes a new instance of the <see cref="DeclReflection"/> struct.
    /// </summary>
    /// <param name="ptr">Pointer to the native declaration reflection structure.</param>
    /// <param name="component">The component type associated with this declaration.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="ptr"/> is null.</exception>
    internal DeclReflection(Native.DeclReflection* ptr, ComponentType component)
    {
        ArgumentNullException.ThrowIfNull(ptr, nameof(ptr));

        _component = component;
        _ptr = ptr;
    }

    /// <summary>
    /// Gets the name of the declaration.
    /// </summary>
    public readonly string Name =>
        spReflectionDecl_getName(_ptr).String;

    /// <summary>
    /// Gets the kind of declaration represented.
    /// </summary>
    /// <value>A <see cref="DeclKind"/> enumeration value indicating the declaration type.</value>
    /// <remarks>
    /// The kind determines which specialized reflection type this declaration can be cast to
    /// using methods like <see cref="AsVariable"/>, <see cref="AsFunction"/>, or <see cref="AsGeneric"/>.
    /// </remarks>
    public readonly DeclKind Kind =>
        spReflectionDecl_getKind(_ptr);

    /// <summary>
    /// Gets the number of child declarations contained within this declaration.
    /// </summary>
    public readonly uint ChildrenCount =>
        spReflectionDecl_getChildrenCount(_ptr);

    /// <summary>
    /// Gets a child declaration at the specified index.
    /// </summary>
    /// <param name="index">The zero-based index of the child to retrieve.</param>
    /// <returns>A <see cref="DeclReflection"/> instance representing the child declaration.</returns>

    public readonly DeclReflection GetChild(uint index) =>
        new(spReflectionDecl_getChild(_ptr, index), _component);

    /// <summary>
    /// Gets an enumerable collection of all child declarations.
    /// </summary>
    public readonly IEnumerable<DeclReflection> Children =>
        Utility.For(ChildrenCount, GetChild);

    /// <summary>
    /// Gets the type reflection information for this declaration.
    /// </summary>
    public readonly TypeReflection Type =>
        new(spReflection_getTypeFromDecl(_ptr), _component);

    /// <summary>
    /// Converts this declaration to a specialized variable reflection.
    /// </summary>
    /// <returns>A <see cref="VariableReflection"/> instance representing this declaration as a variable.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the declaration is not of kind <see cref="DeclKind.Variable"/>.
    /// </exception>
    public readonly VariableReflection AsVariable() =>
        Kind == DeclKind.Variable ? new(spReflectionDecl_castToVariable(_ptr), _component) : throw new InvalidOperationException("Declaration is not a Variable type");

    /// <summary>
    /// Converts this declaration to a specialized function reflection.
    /// </summary>
    /// <returns>A <see cref="FunctionReflection"/> instance representing this declaration as a function.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the declaration is not of kind <see cref="DeclKind.Func"/>.
    /// </exception>
    public readonly FunctionReflection AsFunction() =>
        Kind == DeclKind.Func ? new(spReflectionDecl_castToFunction(_ptr), _component) : throw new InvalidOperationException("Declaration is not a Function type");

    /// <summary>
    /// Converts this declaration to a specialized generic reflection.
    /// </summary>
    /// <returns>A <see cref="GenericReflection"/> instance representing this declaration as a generic.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the declaration is not of kind <see cref="DeclKind.Generic"/>.
    /// </exception>
    public readonly GenericReflection AsGeneric() =>
        Kind == DeclKind.Generic ? new(spReflectionDecl_castToGeneric(_ptr), _component) : throw new InvalidOperationException("Declaration is not a Generic type");

    /// <summary>
    /// Gets the parent declaration that contains this declaration, if one exists.
    /// </summary>
    /// <value>
    /// A <see cref="DeclReflection"/> instance representing the parent declaration,
    /// or <c>null</c> if this declaration has no parent (e.g., it is a top-level declaration).
    /// </value>
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
    /// Gets all child declarations of a specified kind.
    /// </summary>
    /// <param name="kind">The kind of declarations to filter for.</param>
    /// <returns>
    /// An <see cref="IEnumerable{DeclReflection}"/> containing all child declarations
    /// that match the specified <paramref name="kind"/>.
    /// </returns>
    public readonly IEnumerable<DeclReflection> GetChildrenOfKind(DeclKind kind) =>
        Children.Where(x => x.Kind == kind);
}
