using SlangInt = nint;

namespace Prowl.Slang.NativeAPI;


/** A session provides a scope for code that is loaded.

A session can be used to load modules of Slang source code,
and to request target-specific compiled binaries and layout
information.

In order to be able to load code, the session owns a set
of active "search paths" for resolving `#include` directives
and `import` declarations, as well as a set of global
preprocessor definitions that will be used for all code
that gets `import`ed in the session.

If multiple user shaders are loaded in the same session,
and import the same module (e.g., two source files do `import X`)
then there will only be one copy of `X` loaded within the session.

In order to be able to generate target code, the session
owns a list of available compilation targets, which specify
code generation options.

Code loaded and compiled within a session is owned by the session
and will remain resident in memory until the session is released.
Applications wishing to control the memory usage for compiled
and loaded code should use multiple sessions.
*/
[UUID(0x67618701, 0xd116, 0x468f, 0xab, 0x3b, 0x47, 0x4b, 0xed, 0xce, 0xe, 0x3d)]
public unsafe interface ISession : IUnknown
{
    /** Get the global session thas was used to create this session.
     */
    IGlobalSession* GetGlobalSession();

    /** Load a module as it would be by code using `import`.
     */
    IModule* LoadModule(ConstU8Str moduleName, out ISlangBlob* outDiagnostics);

    /** Load a module from Slang source code.
     */
    IModule* loadModuleFromSource(
        ConstU8Str moduleName,
        ConstU8Str path,
        ISlangBlob* source,
        out ISlangBlob* outDiagnostics);

    /** Combine multiple component types to create a composite component type.

    The `componentTypes` array must contain `componentTypeCount` pointers
    to component types that were loaded or created using the same session.

    The shader parameters and specialization parameters of the composite will
    be the union of those in `componentTypes`. The relative order of child
    component types is significant, and will affect the order in which
    parameters are reflected and laid out.

    The entry-point functions of the composite will be the union of those in
    `componentTypes`, and will follow the ordering of `componentTypes`.

    The requirements of the composite component type will be a subset of
    those in `componentTypes`. If an entry in `componentTypes` has a requirement
    that can be satisfied by another entry, then the composition will
    satisfy the requirement and it will not appear as a requirement of
    the composite. If multiple entries in `componentTypes` have a requirement
    for the same type, then only the first such requirement will be retained
    on the composite. The relative ordering of requirements on the composite
    will otherwise match that of `componentTypes`.

    If any diagnostics are generated during creation of the composite, they
    will be written to `outDiagnostics`. If an error is encountered, the
    function will return null.

    It is an error to create a composite component type that recursively
    aggregates a single module more than once.
    */
    SlangResult CreateCompositeComponentType(
        IComponentType** componentTypes,
        SlangInt componentTypeCount,
        out IComponentType* outCompositeComponentType,
        out ISlangBlob* outDiagnostics);

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

    /** Create a request to load/compile front-end code.
     */
    SlangResult CreateCompileRequest(out ICompileRequest* outCompileRequest);


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
    IModule* LoadModuleFromIRBlob(
        ConstU8Str moduleName,
        ConstU8Str path,
        ISlangBlob* source,
        out ISlangBlob* outDiagnostics);

    SlangInt GetLoadedModuleCount();
    IModule* GetLoadedModule(SlangInt index);

    /** Checks if a precompiled binary module is up-to-date with the current compiler
     *   option settings and the source file contents.
     */
    CBool IsBinaryModuleUpToDate(ConstU8Str modulePath, ISlangBlob* binaryModuleBlob);

    /** Load a module from a string.
     */
    IModule* loadModuleFromSourceString(
        ConstU8Str moduleName,
        ConstU8Str path,
        ConstU8Str srcString,
        out ISlangBlob* outDiagnostics);
}
