using System;
using System.Linq;

using Prowl.Slang.Native;


namespace Prowl.Slang;


public unsafe class Session
{
    internal ISession _session;


    internal Session(ISession session)
    {
        _session = session;
    }


    /** Load a module as it would be by code using `import`.
     */
    public Module? LoadModule(string moduleName, out string? diagnostics)
    {
        diagnostics = null;

        using U8Str str = U8Str.Alloc(moduleName);

        IModule* modulePtr = _session.LoadModule(str, out ISlangBlob* diagnosticsPtr);

        if (diagnosticsPtr != null)
            diagnostics = NativeComProxy.Create(diagnosticsPtr).GetString();

        if (modulePtr == null)
            return null;

        return new Module(NativeComProxy.Create(modulePtr, false), this);
    }


    /** Load a module from Slang source code.
     */
    public Module? LoadModuleFromSource(string moduleName, string path, Memory<byte> source, out string? diagnostics)
    {
        diagnostics = null;

        using U8Str strA = U8Str.Alloc(moduleName);
        using U8Str strB = U8Str.Alloc(path);

        IModule* modulePtr = _session.LoadModuleFromSource(strA, strB, ManagedBlob.FromMemory(source), out ISlangBlob* diagnosticsPtr);

        if (diagnosticsPtr != null)
            diagnostics = NativeComProxy.Create(diagnosticsPtr).GetString();

        if (modulePtr == null)
            return null;

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
    public ComponentType? CreateCompositeComponentType(ComponentType[] componentTypes, out string? diagnostics)
    {
        diagnostics = null;

        if (componentTypes.Any(x => x._session != this))
            throw new InvalidComponentException("Component not created by this session found!");

        IComponentType** componentsPtr = stackalloc IComponentType*[componentTypes.Length];

        for (int i = 0; i < componentTypes.Length; i++)
        {
            componentsPtr[i] = (IComponentType*)((NativeComProxy)componentTypes[i]._componentType).ComPtr;
        }

        SlangResult result = _session.CreateCompositeComponentType(componentsPtr, componentTypes.Length, out IComponentType* componentPtr, out ISlangBlob* diagnosticsPtr);

        if (diagnosticsPtr != null)
            diagnostics = NativeComProxy.Create(diagnosticsPtr).GetString();

        if (!result.IsOk())
            return null;

        return new ComponentType(NativeComProxy.Create(componentPtr), this);
    }


    /** Specialize a type based on type arguments.
     */
    TypeReflection* specializeType(
        TypeReflection* type,
        SpecializationArg* specializationArgs,
        SlangInt specializationArgCount,
        out ISlangBlob* outDiagnostics);


    /** Get the layout `type` on the chosen `target`.
     */
    TypeLayoutReflection* getTypeLayout(
        TypeReflection* type,
        SlangInt targetIndex,
        SlangLayoutRules rules,
        out ISlangBlob* outDiagnostics);


    /** Get a container type from `elementType`. For example, given type `T`, returns
        a type that represents `StructuredBuffer<T>`.

        @param `elementType`: the element type to wrap around.
        @param `containerType`: the type of the container to wrap `elementType` in.
        @param `outDiagnostics`: a blob to receive diagnostic messages.
    */
    TypeReflection* getContainerType(
        TypeReflection* elementType,
        ContainerType containerType,
        out ISlangBlob* outDiagnostics);


    /** Return a `TypeReflection` that represents the `__Dynamic` type.
        This type can be used as a specialization argument to indicate using
        dynamic dispatch.
    */
    TypeReflection* GetDynamicType();


    /** Get the mangled name for a type RTTI object.
     */
    SlangResult GetTypeRTTIMangledName(TypeReflection* type, out ISlangBlob* outNameBlob);


    /** Get the mangled name for a type witness.
     */
    SlangResult GetTypeConformanceWitnessMangledName(
        TypeReflection* type,
        TypeReflection* interfaceType,
        out ISlangBlob* outNameBlob);


    /** Get the sequential ID used to identify a type witness in a dynamic object.
     */
    SlangResult GetTypeConformanceWitnessSequentialID(
        TypeReflection* type,
        TypeReflection* interfaceType,
        out nuint outId);


    /** Creates a `IComponentType` that represents a type's conformance to an interface.
        The retrieved `ITypeConformance` objects can be included in a composite `IComponentType`
        to explicitly specify which implementation types should be included in the final compiled
        code. For example, if an module defines `IMaterial` interface and `AMaterial`,
        `BMaterial`, `CMaterial` types that implements the interface, the user can exclude
        `CMaterial` implementation from the resulting shader code by explicitly adding
        `AMaterial:IMaterial` and `BMaterial:IMaterial` conformances to a composite
        `IComponentType` and get entry point code from it. The resulting code will not have
        anything related to `CMaterial` in the dynamic dispatch logic. If the user does not
        explicitly include any `TypeConformances` to an interface type, all implementations to
        that interface will be included by default. By linking a `ITypeConformance`, the user is
        also given the opportunity to specify the dispatch ID of the implementation type. If
        `conformanceIdOverride` is -1, there will be no override behavior and Slang will
        automatically assign IDs to implementation types. The automatically assigned IDs can be
        queried via `ISession::getTypeConformanceWitnessSequentialID`.

        Returns SLANG_OK if succeeds, or SLANG_FAIL if `type` does not conform to `interfaceType`.
    */
    SlangResult CreateTypeConformanceComponentType(
        TypeReflection* type,
        TypeReflection* interfaceType,
        out ITypeConformance* outConformance,
        SlangInt conformanceIdOverride,
        out ISlangBlob* outDiagnostics);


    /** Load a module from a Slang module blob.
     */
    public Module? LoadModuleFromIRBlob(string moduleName, string path, Memory<byte> source, out string? diagnostics)
    {
        diagnostics = null;

        using U8Str strA = U8Str.Alloc(moduleName);
        using U8Str strB = U8Str.Alloc(path);

        IModule* modulePtr = _session.LoadModuleFromIRBlob(strA, strB, ManagedBlob.FromMemory(source), out ISlangBlob* diagnosticsPtr);

        if (diagnosticsPtr != null)
            diagnostics = NativeComProxy.Create(diagnosticsPtr).GetString();

        if (modulePtr == null)
            return null;

        return new Module(NativeComProxy.Create(modulePtr, false), this);
    }



    public int GetLoadedModuleCount()
    {
        return (int)_session.GetLoadedModuleCount();
    }


    public Module? GetLoadedModule(int index)
    {
        IModule* modulePtr = _session.GetLoadedModule(index);

        if (modulePtr == null)
            return null;

        return new Module(NativeComProxy.Create(modulePtr, false), this);
    }


    /** Checks if a precompiled binary module is up-to-date with the current compiler
     *   option settings and the source file contents.
     */
    public bool IsBinaryModuleUpToDate(string modulePath, Memory<byte> binaryModuleBlob)
    {
        using U8Str str = U8Str.Alloc(modulePath);
        bool result = _session.IsBinaryModuleUpToDate(str, ManagedBlob.FromMemory(binaryModuleBlob));

        return result;
    }


    /** Load a module from a string.
     */
    public Module? LoadModuleFromSourceString(string moduleName, string path, string srcString, out string? diagnostics)
    {
        diagnostics = null;

        using U8Str strA = U8Str.Alloc(moduleName);
        using U8Str strB = U8Str.Alloc(path);
        using U8Str strC = U8Str.Alloc(srcString);

        IModule* modulePtr = _session.LoadModuleFromSourceString(strA, strB, strC, out ISlangBlob* diagnosticsPtr);

        if (diagnosticsPtr != null)
            diagnostics = NativeComProxy.Create(diagnosticsPtr).GetString();

        if (modulePtr == null)
            return null;

        return new Module(NativeComProxy.Create(modulePtr, false), this);
    }
}