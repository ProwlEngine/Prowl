using System;

using SlangInt = nint;

namespace Prowl.Slang.Native;


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
internal unsafe interface ISession : IUnknown
{
    IGlobalSession* GetGlobalSession();

    IModule* LoadModule(ConstU8Str moduleName, out ISlangBlob* outDiagnostics);

    IModule* LoadModuleFromSource(
        ConstU8Str moduleName,
        ConstU8Str path,
        ISlangBlob* source,
        out ISlangBlob* outDiagnostics);

    SlangResult CreateCompositeComponentType(
        IComponentType** componentTypes,
        SlangInt componentTypeCount,
        out IComponentType* outCompositeComponentType,
        out ISlangBlob* outDiagnostics);

    TypeReflection* SpecializeType(
        TypeReflection* type,
        SpecializationArg* specializationArgs,
        SlangInt specializationArgCount,
        out ISlangBlob* outDiagnostics);

    TypeLayoutReflection* GetTypeLayout(
        TypeReflection* type,
        SlangInt targetIndex,
        SlangLayoutRules rules,
        out ISlangBlob* outDiagnostics);

    TypeReflection* GetContainerType(
        TypeReflection* elementType,
        ContainerType containerType,
        out ISlangBlob* outDiagnostics);

    TypeReflection* GetDynamicType();

    SlangResult GetTypeRTTIMangledName(TypeReflection* type, out ISlangBlob* outNameBlob);

    SlangResult GetTypeConformanceWitnessMangledName(
        TypeReflection* type,
        TypeReflection* interfaceType,
        out ISlangBlob* outNameBlob);

    SlangResult GetTypeConformanceWitnessSequentialID(
        TypeReflection* type,
        TypeReflection* interfaceType,
        out nuint outId);

    [Obsolete("Method is deprecated")]
    SlangResult CreateCompileRequest(out /* ICompileRequest */ void* outCompileRequest);


    SlangResult CreateTypeConformanceComponentType(
        TypeReflection* type,
        TypeReflection* interfaceType,
        out ITypeConformance* outConformance,
        SlangInt conformanceIdOverride,
        out ISlangBlob* outDiagnostics);

    IModule* LoadModuleFromIRBlob(
        ConstU8Str moduleName,
        ConstU8Str path,
        ISlangBlob* source,
        out ISlangBlob* outDiagnostics);

    SlangInt GetLoadedModuleCount();
    IModule* GetLoadedModule(SlangInt index);

    CBool IsBinaryModuleUpToDate(ConstU8Str modulePath, ISlangBlob* binaryModuleBlob);

    IModule* LoadModuleFromSourceString(
        ConstU8Str moduleName,
        ConstU8Str path,
        ConstU8Str srcString,
        out ISlangBlob* outDiagnostics);
}
