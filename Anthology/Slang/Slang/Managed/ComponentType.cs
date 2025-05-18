using System;

using Prowl.Slang.Native;


namespace Prowl.Slang;


public unsafe class ComponentType
{
    // Components depend on a Session instance, but sessions dont depend on components.
    // Keep a ref to the session to prevent its underlying native ptr from being disposed while this component still exists.
    internal Session _session;
    internal IComponentType _componentType;


    internal ComponentType(IComponentType componentType, Session session)
    {
        _session = session;
        _componentType = componentType;
    }


    /** Get the runtime session that this component type belongs to.
 */
    public Session GetSession()
    {
        return _session;
    }


    /** Get the layout for this program for the chosen `targetIndex`.

    The resulting layout will establish offsets/bindings for all
    of the global and entry-point shader parameters in the
    component type.

    If this component type has specialization parameters (that is,
    it is not fully specialized), then the resulting layout may
    be incomplete, and plugging in arguments for generic specialization
    parameters may result in a component type that doesn't have
    a compatible layout. If the component type only uses
    interface-type specialization parameters, then the layout
    for a specialization should be compatible with an unspecialized
    layout (all parameters in the unspecialized layout will have
    the same offset/binding in the specialized layout).

    If this component type is combined into a composite, then
    the absolute offsets/bindings of parameters may not stay the same.
    If the shader parameters in a component type don't make
    use of explicit binding annotations (e.g., `register(...)`),
    then the *relative* offset of shader parameters will stay
    the same when it is used in a composition.
    */
    public ShaderReflection GetLayout(nint targetIndex, out string? diagnostics)
    {
        Native.ShaderReflection* reflectionPtr = _componentType.GetLayout(targetIndex, out ISlangBlob* diagnosticsPtr);

        diagnostics = null;
        if (reflectionPtr == null)
            SlangResult.Uninitialized.ThrowOrDiagnose(diagnosticsPtr, out diagnostics);

        return new ShaderReflection(reflectionPtr, _session);
    }

    /** Get the number of (unspecialized) specialization parameters for the component type.
     */
    public nint GetSpecializationParamCount()
    {
        return _componentType.GetSpecializationParamCount();
    }


    /** Get the compiled code for the entry point at `entryPointIndex` for the chosen `targetIndex`

    Entry point code can only be computed for a component type that
    has no specialization parameters (it must be fully specialized)
    and that has no requirements (it must be fully linked).

    If code has not already been generated for the given entry point and target,
    then a compilation error may be detected, in which case `outDiagnostics`
    (if non-null) will be filled in with a blob of messages diagnosing the error.
    */
    public Memory<byte> GetEntryPointCode(nint entryPointIndex, nint targetIndex, out string? diagnostics)
    {
        _componentType.GetEntryPointCode(entryPointIndex, targetIndex, out ISlangBlob* codePtr, out ISlangBlob* diagnosticsPtr).ThrowOrDiagnose(diagnosticsPtr, out diagnostics);

        return NativeComProxy.Create(codePtr).ReadBytes();
    }


    /** Get the compilation result as a file system.

    Has the same requirements as getEntryPointCode.

    The result is not written to the actual OS file system, but is made available as an
    in memory representation.
    */
    /*

    SlangResult GetResultAsFileSystem(
        nint entryPointIndex,
        nint targetIndex,
        out ISlangMutableFileSystem* outFileSystem)
    {

    }

    */

    /** Compute a hash for the entry point at `entryPointIndex` for the chosen `targetIndex`.

    This computes a hash based on all the dependencies for this component type as well as the
    target settings affecting the compiler backend. The computed hash is used as a key for caching
    the output of the compiler backend to implement shader caching.
    */
    public Memory<byte> GetEntryPointHash(int entryPointIndex, int targetIndex)
    {
        _componentType.GetEntryPointHash(entryPointIndex, targetIndex, out ISlangBlob* outHash);

        return NativeComProxy.Create(outHash).ReadBytes();
    }


    /** Specialize the component by binding its specialization parameters to concrete arguments.

    The `specializationArgs` array must have `specializationArgCount` entries, and
    this must match the number of specialization parameters on this component type.

    If any diagnostics (error or warnings) are produced, they will be written to `outDiagnostics`.
    */
    public ComponentType Specialize(TypeReflection[] specializationArgs, out string? diagnostics)
    {
        SpecializationArg* specializationArgsPtr = stackalloc SpecializationArg[specializationArgs.Length];

        for (int i = 0; i < specializationArgs.Length; i++)
            specializationArgsPtr[i] = SpecializationArg.FromType(specializationArgs[i]._ptr);

        _componentType.Specialize(specializationArgsPtr, specializationArgs.Length, out IComponentType* componentPtr, out ISlangBlob* diagnosticsPtr).Throw();

        diagnostics = Utility.GetDiagnostic(diagnosticsPtr);

        return new ComponentType(NativeComProxy.Create(componentPtr), _session);
    }


    /** Link this component type against all of its unsatisfied dependencies.

    A component type may have unsatisfied dependencies. For example, a module
    depends on any other modules it `import`s, and an entry point depends
    on the module that defined it.

    A user can manually satisfy dependencies by creating a composite
    component type, and when doing so they retain full control over
    the relative ordering of shader parameters in the resulting layout.

    It is an error to try to generate/access compiled kernel code for
    a component type with unresolved dependencies, so if dependencies
    remain after whatever manual composition steps an application
    cares to perform, the `link()` function can be used to automatically
    compose in any remaining dependencies. The order of parameters
    (and hence the global layout) that results will be deterministic,
    but is not currently documented.
    */
    public ComponentType Link(out string? diagnostics)
    {
        _componentType.Link(out IComponentType* componentPtr, out ISlangBlob* diagnosticsPtr).ThrowOrDiagnose(diagnosticsPtr, out diagnostics);

        return new ComponentType(NativeComProxy.Create(componentPtr), _session);
    }


    /** Get entry point 'callable' functions accessible through the ISlangSharedLibrary interface.

    The functions remain in scope as long as the ISlangSharedLibrary interface is in scope.

    NOTE! Requires a compilation target of SLANG_HOST_CALLABLE.

    @param entryPointIndex  The index of the entry point to get code for.
    @param targetIndex      The index of the target to get code for (default: zero).
    @param outSharedLibrary A pointer to a ISharedLibrary interface which functions can be queried
    on.
    @returns                A `SlangResult` to indicate success or failure.
    */
    public SharedLibrary GetEntryPointHostCallable(int entryPointIndex, int targetIndex, out string? diagnostics)
    {
        _componentType.GetEntryPointHostCallable(entryPointIndex, targetIndex, out ISlangSharedLibrary* sharedLibPtr, out ISlangBlob* diagnosticsPtr).ThrowOrDiagnose(diagnosticsPtr, out diagnostics);

        return new SharedLibrary(NativeComProxy.Create(sharedLibPtr));
    }


    /** Get a new ComponentType object that represents a renamed entry point.

    The current object must be a single EntryPoint, or a CompositeComponentType or
    SpecializedComponentType that contains one EntryPoint component.
    */
    public ComponentType RenameEntryPoint(string newName)
    {
        using U8Str str = U8Str.Alloc(newName);

        _componentType.RenameEntryPoint(str, out IComponentType* entryPointPtr).Throw();

        return new ComponentType(NativeComProxy.Create(entryPointPtr), _session);
    }


    /** Link and specify additional compiler options when generating code
     *   from the linked program.
     */
    public ComponentType LinkWithOptions(CompilerOptionEntry[] compilerOptionEntries, out string? diagnostics)
    {
        Native.CompilerOptionEntry* compilerOptionEntriesPtr = stackalloc Native.CompilerOptionEntry[compilerOptionEntries.Length];

        for (int i = 0; i < compilerOptionEntries.Length; i++)
            compilerOptionEntriesPtr[i].Allocate(compilerOptionEntries[i]);

        _componentType.LinkWithOptions(out IComponentType* linkedComponentPtr, (uint)compilerOptionEntries.Length, compilerOptionEntriesPtr, out ISlangBlob* diagnosticsPtr).ThrowOrDiagnose(diagnosticsPtr, out diagnostics);

        for (int i = 0; i < compilerOptionEntries.Length; i++)
            compilerOptionEntriesPtr[i].Free();

        return new ComponentType(NativeComProxy.Create(linkedComponentPtr), _session);
    }


    public Memory<byte> GetTargetCode(int targetIndex, out string? diagnostics)
    {
        _componentType.GetTargetCode(targetIndex, out ISlangBlob* codePtr, out ISlangBlob* diagnosticsPtr).ThrowOrDiagnose(diagnosticsPtr, out diagnostics);

        return NativeComProxy.Create(codePtr).ReadBytes();
    }


    public Metadata GetTargetMetadata(int targetIndex, out string? diagnostics)
    {
        _componentType.GetTargetMetadata(targetIndex, out IMetadata* metadataPtr, out ISlangBlob* diagnosticsPtr).ThrowOrDiagnose(diagnosticsPtr, out diagnostics);
        return new Metadata(NativeComProxy.Create(metadataPtr));
    }


    public Metadata GetEntryPointMetadata(int entryPointIndex, int targetIndex, out string? diagnostics)
    {
        _componentType.GetEntryPointMetadata(entryPointIndex, targetIndex, out IMetadata* metadataPtr, out ISlangBlob* diagnosticsPtr).ThrowOrDiagnose(diagnosticsPtr, out diagnostics);
        return new Metadata(NativeComProxy.Create(metadataPtr));
    }
}
