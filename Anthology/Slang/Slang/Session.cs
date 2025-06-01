// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Linq;

using Prowl.Slang.Native;


namespace Prowl.Slang;


/// <summary>
/// <para>
/// A session provides a scope for code that is loaded.
/// </para>
/// <para>
/// A session can be used to load modules of Slang source code,
/// and to request target-specific compiled binaries and layout
/// information.
/// </para>
/// <para>
/// In order to be able to load code, the session owns a set
/// of active "search paths" for resolving `#include` directives
/// and `import` declarations, as well as a set of global
/// preprocessor definitions that will be used for all code
/// that gets `import`ed in the session.
/// </para>
/// <para>
/// If multiple user shaders are loaded in the same session,
/// and import the same module (e.g., two source files do `import X`)
/// then there will only be one copy of `X` loaded within the session.
/// </para>
/// <para>
/// In order to be able to generate target code, the session
/// owns a list of available compilation targets, which specify
/// code generation options.
/// </para>
/// <para>
/// Code loaded and compiled within a session is owned by the session
/// and will remain resident in memory until the session is released.
/// Applications wishing to control the memory usage for compiled
/// and loaded code should use multiple sessions.
/// </para>
/// </summary>
public unsafe class Session
{
    internal ISession _session;


    internal Session(ISession session)
    {
        _session = session;
    }


    /// <summary>
    /// Load a module as it would be by code using `import`.
    /// </summary>
    public Module LoadModule(string moduleName, out DiagnosticInfo diagnostics)
    {
        using U8Str str = U8Str.Alloc(moduleName);

        IModule* modulePtr = _session.LoadModule(str, out ISlangBlob* diagnosticsPtr);

        diagnostics = default;

        if (diagnosticsPtr != null)
            diagnostics = new(diagnosticsPtr);

        if (modulePtr == null)
            throw diagnostics.GetException() ?? new InvalidOperationException($"Failed to load module '{moduleName}'");

        return new Module(NativeComProxy.Create(modulePtr, false), this);
    }


    /// <summary>
    /// Load a module from Slang source code.
    /// </summary>
    public Module LoadModuleFromSource(string moduleName, string path, Memory<byte> source, out DiagnosticInfo diagnostics)
    {
        using U8Str strA = U8Str.Alloc(moduleName);
        using U8Str strB = U8Str.Alloc(path);

        IModule* modulePtr = _session.LoadModuleFromSource(strA, strB, ManagedBlob.FromMemory(source), out ISlangBlob* diagnosticsPtr);

        diagnostics = default;

        if (diagnosticsPtr != null)
            diagnostics = new(diagnosticsPtr);

        if (modulePtr == null)
            throw diagnostics.GetException() ?? new InvalidOperationException($"Failed to load module '{moduleName}'"); ;

        return new Module(NativeComProxy.Create(modulePtr, false), this);
    }


    /// <summary>
    /// <para>
    /// Combine multiple component types to create a composite component type.
    /// </para>
    /// <para>
    /// The shader parameters and specialization parameters of the composite will
    /// be the union of those in `componentTypes`. The relative order of child
    /// component types is significant, and will affect the order in which
    /// parameters are reflected and laid out.
    /// </para>
    /// <para>
    /// The entry-point functions of the composite will be the union of those in
    /// `componentTypes`, and will follow the ordering of `componentTypes`.
    /// </para>
    /// <para>
    /// The requirements of the composite component type will be a subset of
    /// those in `componentTypes`. If an entry in `componentTypes` has a requirement
    /// that can be satisfied by another entry, then the composition will
    /// satisfy the requirement and it will not appear as a requirement of
    /// the composite. If multiple entries in `componentTypes` have a requirement
    /// for the same type, then only the first such requirement will be retained
    /// on the composite. The relative ordering of requirements on the composite
    /// will otherwise match that of `componentTypes`.
    /// </para>
    /// <para>
    /// It is an error to create a composite component type that recursively
    /// aggregates a single module more than once.
    /// </para>
    /// </summary>
    public ComponentType CreateCompositeComponentType(ComponentType[] componentTypes, out DiagnosticInfo diagnostics)
    {
        if (componentTypes.Any(x => x._session != this))
            throw new InvalidComponentException("Component not created by this session found!");

        IComponentType** componentsPtr = stackalloc IComponentType*[componentTypes.Length];

        for (int i = 0; i < componentTypes.Length; i++)
        {
            componentsPtr[i] = (IComponentType*)((NativeComProxy)componentTypes[i]._componentType).ComPtr;
        }

        _session.CreateCompositeComponentType(componentsPtr, componentTypes.Length, out IComponentType* componentPtr, out ISlangBlob* diagnosticsPtr)
            .Throw(diagnosticsPtr, out diagnostics);

        return new ComponentType(NativeComProxy.Create(componentPtr), this);
    }


    /// <summary>
    /// Specialize a type based on type arguments.
    /// </summary>
    public TypeReflection SpecializeType(
        TypeReflection type,
        TypeReflection[] specializationArgs,
        out DiagnosticInfo diagnostics)
    {
        SpecializationArg* argsPtr = stackalloc SpecializationArg[specializationArgs.Length];

        for (int i = 0; i < specializationArgs.Length; i++)
            argsPtr[i] = SpecializationArg.FromType(specializationArgs[i]._ptr);

        Native.TypeReflection* reflectionPtr = _session.SpecializeType(type._ptr, argsPtr, specializationArgs.Length, out ISlangBlob* diagnosticsPtr);

        Utility.ValidatePtr(reflectionPtr, diagnosticsPtr, out diagnostics);

        return new TypeReflection(reflectionPtr, type._component);
    }

    /// <summary>
    /// Get the layout `type` on the chosen `target`.
    /// </summary>
    public TypeLayoutReflection GetTypeLayout(
        TypeReflection type,
        int targetIndex,
        LayoutRules rules,
        out DiagnosticInfo diagnostics)
    {
        Native.TypeLayoutReflection* reflectionPtr = _session.GetTypeLayout(type._ptr, (nint)targetIndex, rules, out ISlangBlob* diagnosticsPtr);

        Utility.ValidatePtr(reflectionPtr, diagnosticsPtr, out diagnostics);

        return new TypeLayoutReflection(reflectionPtr, type._component);
    }


    /// <summary>
    /// Get a container type from `elementType`. For example, given type `T`, returns
    /// a type that represents `StructuredBuffer{T}`.
    /// </summary>
    /// <param name="elementType">The element type to wrap around.</param>
    /// <param name="containerType">The type of the container to wrap `elementType` in.</param>
    /// <param name="diagnostics">A string to receive diagnostic messages.</param>
    /// <returns></returns>
    public TypeReflection GetContainerType(
        TypeReflection elementType,
        ContainerType containerType,
        out DiagnosticInfo diagnostics)
    {
        Native.TypeReflection* reflectionPtr = _session.GetContainerType(elementType._ptr, containerType, out ISlangBlob* diagnosticsPtr);

        Utility.ValidatePtr(reflectionPtr, diagnosticsPtr, out diagnostics);

        return new TypeReflection(reflectionPtr, elementType._component);
    }


    /* Return a `TypeReflection` that represents the `__Dynamic` type.
        This type can be used as a specialization argument to indicate using
        dynamic dispatch.
    */
    // public TypeReflection GetDynamicType()
    // {
    //     return new TypeReflection(_session.GetDynamicType(), null);
    // }


    /// <summary>
    /// Get the mangled name for a type RTTI object.
    /// </summary>
    public string GetTypeRTTIMangledName(TypeReflection type)
    {
        _session.GetTypeRTTIMangledName(type._ptr, out ISlangBlob* nameBlob)
            .Throw("Failed to get runtime type information mangled name");

        return NativeComProxy.Create(nameBlob).GetString();
    }

    /// <summary>
    /// Get the mangled name for a type witness.
    /// </summary>
    public string GetTypeConformanceWitnessMangledName(TypeReflection type, TypeReflection interfaceType)
    {
        _session.GetTypeConformanceWitnessMangledName(type._ptr, interfaceType._ptr, out ISlangBlob* nameBlob)
            .Throw("Failed to get type confrmance witness mangled name");

        return NativeComProxy.Create(nameBlob).GetString();
    }


    /// <summary>
    /// Get the sequential ID used to identify a type witness in a dynamic object.
    /// </summary>
    public uint GetTypeConformanceWitnessSequentialID(TypeReflection type, TypeReflection interfaceType)
    {
        _session.GetTypeConformanceWitnessSequentialID(type._ptr, interfaceType._ptr, out nuint outId)
            .Throw("Failed to get type conformance witness sequential ID");

        return (uint)outId;
    }



    /// <summary>
    /// <para>
    /// Creates a `IComponentType` that represents a type's conformance to an interface.
    /// The retrieved `ITypeConformance` objects can be included in a composite `IComponentType`
    /// to explicitly specify which implementation types should be included in the final compiled
    /// code.
    /// </para>
    /// <para>
    /// For example, if an module defines `IMaterial` interface and `AMaterial`,
    /// `BMaterial`, `CMaterial` types that implements the interface, the user can exclude
    /// `CMaterial` implementation from the resulting shader code by explicitly adding
    /// `AMaterial:IMaterial` and `BMaterial:IMaterial` conformances to a composite
    /// `IComponentType` and get entry point code from it. The resulting code will not have
    /// anything related to `CMaterial` in the dynamic dispatch logic.
    /// </para>
    /// <para>
    /// If the user does not explicitly include any `TypeConformances` to an interface type, all implementations to
    /// that interface will be included by default. By linking a `ITypeConformance`, the user is
    /// also given the opportunity to specify the dispatch ID of the implementation type.
    /// </para>
    /// <para>
    /// If `conformanceIdOverride` is -1, there will be no override behavior and Slang will
    /// automatically assign IDs to implementation types.The automatically assigned IDs can be
    /// queried via `ISession::getTypeConformanceWitnessSequentialID`.
    /// </para>
    /// </summary>
    public ComponentType CreateTypeConformanceComponentType(
        TypeReflection type,
        TypeReflection interfaceType,
        int conformanceIdOverride,
        out DiagnosticInfo diagnostics)
    {
        _session.CreateTypeConformanceComponentType(type._ptr, interfaceType._ptr, out ITypeConformance* outConformance, conformanceIdOverride, out ISlangBlob* diagnosticsPtr)
            .Throw(diagnosticsPtr, out diagnostics);

        return new ComponentType(NativeComProxy.Create(outConformance), this);
    }


    /// <summary>
    /// Load a module from a Slang module blob.
    /// </summary>
    public Module LoadModuleFromIRBlob(string moduleName, string path, Memory<byte> source, out DiagnosticInfo diagnostics)
    {
        using U8Str strA = U8Str.Alloc(moduleName);
        using U8Str strB = U8Str.Alloc(path);

        IModule* modulePtr = _session.LoadModuleFromIRBlob(strA, strB, ManagedBlob.FromMemory(source), out ISlangBlob* diagnosticsPtr);

        diagnostics = default;

        if (diagnosticsPtr != null)
            diagnostics = new(diagnosticsPtr);

        if (modulePtr == null)
            throw diagnostics.GetException() ?? new InvalidOperationException($"Failed to load module '{moduleName}'"); ;

        return new Module(NativeComProxy.Create(modulePtr, false), this);
    }


    /// <summary>
    /// Gets the numer of loaded modules in this session.
    /// </summary>
    public int GetLoadedModuleCount()
    {
        return (int)_session.GetLoadedModuleCount();
    }


    /// <summary>
    /// Gets the loaded module at a given index.
    /// </summary>
    public Module GetLoadedModule(int index)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, GetLoadedModuleCount(), nameof(index));
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 0, nameof(index));

        IModule* modulePtr = _session.GetLoadedModule(index);

        return new Module(NativeComProxy.Create(modulePtr, false), this);
    }


    /// <summary>
    /// Checks if a precompiled binary module is up-to-date with the current compiler
    /// option settings and the source file contents.
    /// </summary>
    public bool IsBinaryModuleUpToDate(string modulePath, Memory<byte> binaryModuleBlob)
    {
        using U8Str str = U8Str.Alloc(modulePath);
        return _session.IsBinaryModuleUpToDate(str, ManagedBlob.FromMemory(binaryModuleBlob));
    }


    /// <summary>
    /// Load a module from a string.
    /// </summary>
    public Module LoadModuleFromSourceString(string moduleName, string path, string srcString, out DiagnosticInfo diagnostics)
    {
        using U8Str strA = U8Str.Alloc(moduleName);
        using U8Str strB = U8Str.Alloc(path);
        using U8Str strC = U8Str.Alloc(srcString);

        IModule* modulePtr = _session.LoadModuleFromSourceString(strA, strB, strC, out ISlangBlob* diagnosticsPtr);

        diagnostics = default;

        if (diagnosticsPtr != null)
            diagnostics = new(diagnosticsPtr);

        if (modulePtr == null)
            throw diagnostics.GetException() ?? new InvalidOperationException($"Failed to load module '{moduleName}'"); ;

        return new Module(NativeComProxy.Create(modulePtr, false), this);
    }
}
