using SlangInt = nint;

namespace Prowl.Slang.NativeAPI;


/// <summary>
/// <para>A component type is a unit of shader code layout, reflection, and linking.</para>
///
/// <para>A component type is a unit of shader code that can be included into
/// a linked and compiled shader program. Each component type may have:</para>
///
/// <para>* Zero or more uniform shader parameters, representing textures,
///   buffers, etc. that the code in the component depends on.</para>
///
/// <para>* Zero or more <i>specialization</i> parameters, which are type or
///   value parameters that can be used to synthesize specialized
///   versions of the component type.</para>
///
/// <para>* Zero or more entry points, which are the individually invocable
///   kernels that can have final code generated.</para>
///
/// <para>* Zero or more <i>requirements</i>, which are other component
///   types on which the component type depends.</para>
///
/// <para>One example of a component type is a module of Slang code:</para>
///
/// <para>* The global-scope shader parameters declared in the module are
///   the parameters when considered as a component type.</para>
///
/// <para>* Any global-scope generic or interface type parameters introduce
///   specialization parameters for the module.</para>
///
/// <para>* A module does not by default include any entry points when
///   considered as a component type (although the code of the
///   module might <i>declare</i> some entry points).</para>
///
/// <para>* Any other modules that are <c>import</c>ed in the source code
///   become requirements of the module, when considered as a
///   component type.</para>
///
/// <para>An entry point is another example of a component type:</para>
///
/// <para>* The <c>uniform</c> parameters of the entry point function are
///   its shader parameters when considered as a component type.</para>
///
/// <para>* Any generic or interface-type parameters of the entry point
///   introduce specialization parameters.</para>
///
/// <para>* An entry point component type exposes a single entry point (itself).</para>
///
/// <para>* An entry point has one requirement for the module in which
///   it was defined.</para>
///
/// <para>Component types can be manipulated in a few ways:</para>
///
/// <para>* Multiple component types can be combined into a composite, which
///   combines all of their code, parameters, etc.</para>
///
/// <para>* A component type can be specialized, by "plugging in" types and
///   values for its specialization parameters.</para>
///
/// <para>* A component type can be laid out for a particular target, giving
///   offsets/bindings to the shader parameters it contains.</para>
///
/// <para>* Generated kernel code can be requested for entry points.</para>
/// </summary>
[UUID(0x5bc42be8, 0x5c50, 0x4929, 0x9e, 0x5e, 0xd1, 0x5e, 0x7c, 0x24, 0x1, 0x5f)]
public unsafe interface IComponentType : IUnknown
{

    /** Get the runtime session that this component type belongs to.
     */
    ISession* GetSession();

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
    ShaderReflection* GetLayout(SlangInt targetIndex, out ISlangBlob* outDiagnostics);

    /** Get the number of (unspecialized) specialization parameters for the component type.
     */
    SlangInt GetSpecializationParamCount();

    /** Get the compiled code for the entry point at `entryPointIndex` for the chosen `targetIndex`

    Entry point code can only be computed for a component type that
    has no specialization parameters (it must be fully specialized)
    and that has no requirements (it must be fully linked).

    If code has not already been generated for the given entry point and target,
    then a compilation error may be detected, in which case `outDiagnostics`
    (if non-null) will be filled in with a blob of messages diagnosing the error.
    */
    SlangResult GetEntryPointCode(
        SlangInt entryPointIndex,
        SlangInt targetIndex,
        out ISlangBlob* outCode,
        out ISlangBlob* outDiagnostics);

    /** Get the compilation result as a file system.

    Has the same requirements as getEntryPointCode.

    The result is not written to the actual OS file system, but is made available as an
    in memory representation.
    */
    SlangResult GetResultAsFileSystem(
        SlangInt entryPointIndex,
        SlangInt targetIndex,
        out ISlangMutableFileSystem* outFileSystem);

    /** Compute a hash for the entry point at `entryPointIndex` for the chosen `targetIndex`.

    This computes a hash based on all the dependencies for this component type as well as the
    target settings affecting the compiler backend. The computed hash is used as a key for caching
    the output of the compiler backend to implement shader caching.
    */
    void GetEntryPointHash(SlangInt entryPointIndex, SlangInt targetIndex, out ISlangBlob* outHash);

    /** Specialize the component by binding its specialization parameters to concrete arguments.

    The `specializationArgs` array must have `specializationArgCount` entries, and
    this must match the number of specialization parameters on this component type.

    If any diagnostics (error or warnings) are produced, they will be written to `outDiagnostics`.
    */
    SlangResult Specialize(SpecializationArg* specializationArgs, SlangInt specializationArgCount, out IComponentType* outSpecializedComponentType, out ISlangBlob* outDiagnostics);

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
    SlangResult Link(out IComponentType* outLinkedComponentType, out ISlangBlob* outDiagnostics);

    /** Get entry point 'callable' functions accessible through the ISlangSharedLibrary interface.

    The functions remain in scope as long as the ISlangSharedLibrary interface is in scope.

    NOTE! Requires a compilation target of SLANG_HOST_CALLABLE.

    @param entryPointIndex  The index of the entry point to get code for.
    @param targetIndex      The index of the target to get code for (default: zero).
    @param outSharedLibrary A pointer to a ISharedLibrary interface which functions can be queried
    on.
    @returns                A `SlangResult` to indicate success or failure.
    */
    SlangResult GetEntryPointHostCallable(int entryPointIndex, int targetIndex, out ISlangSharedLibrary* outSharedLibrary, out ISlangBlob* outDiagnostics);

    /** Get a new ComponentType object that represents a renamed entry point.

    The current object must be a single EntryPoint, or a CompositeComponentType or
    SpecializedComponentType that contains one EntryPoint component.
    */
    SlangResult RenameEntryPoint(ConstU8Str newName, out IComponentType* outEntryPoint);

    /** Link and specify additional compiler options when generating code
     *   from the linked program.
     */
    SlangResult LinkWithOptions(
        out IComponentType* outLinkedComponentType,
        uint compilerOptionEntryCount,
        CompilerOptionEntry* compilerOptionEntries,
        out ISlangBlob* outDiagnostics);

    SlangResult GetTargetCode(
        SlangInt targetIndex,
        out ISlangBlob* outCode,
        out ISlangBlob* outDiagnostics);

    SlangResult GetTargetMetadata(
        SlangInt targetIndex,
        out IMetadata* outMetadata,
        out ISlangBlob* outDiagnostics);

    SlangResult GetEntryPointMetadata(
        SlangInt entryPointIndex,
        SlangInt targetIndex,
        out IMetadata* outMetadata,
        out ISlangBlob* outDiagnostics);
}
