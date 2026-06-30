using SlangInt32 = int;

namespace Prowl.Slang.NativeAPI;


/** A module is the granularity of shader code compilation and loading.

In most cases a module corresponds to a single compile "translation unit."
This will often be a single `.slang` or `.hlsl` file and everything it
`#include`s.

Notably, a module `M` does *not* include the things it `import`s, as these
as distinct modules that `M` depends on. There is a directed graph of
module dependencies, and all modules in the graph must belong to the
same session (`ISession`).

A module establishes a namespace for looking up types, functions, etc.
*/
[UUID(0xc720e64, 0x8722, 0x4d31, 0x89, 0x90, 0x63, 0x8a, 0x98, 0xb1, 0xc2, 0x79)]
public unsafe interface IModule : IComponentType
{
    /// Find and an entry point by name.
    /// Note that this does not work in case the function is not explicitly designated as an entry
    /// point, e.g. using a `[shader("...")]` attribute. In such cases, consider using
    /// `IModule::findAndCheckEntryPoint` instead.
    SlangResult FindEntryPointByName(ConstU8Str name, out IEntryPoint* outEntryPoint);

    /// Get number of entry points defined in the module. An entry point defined in a module
    /// is by default not included in the linkage, so calls to `IComponentType::getEntryPointCount`
    /// on an `IModule` instance will always return 0. However `IModule::getDefinedEntryPointCount`
    /// will return the number of defined entry points.
    SlangInt32 GetDefinedEntryPointCount();

    /// Get the name of an entry point defined in the module.
    SlangResult GetDefinedEntryPoint(SlangInt32 index, out IEntryPoint* outEntryPoint);

    /// Get a serialized representation of the checked module.
    SlangResult Serialize(out ISlangBlob* outSerializedBlob);

    /// Write the serialized representation of this module to a file.
    SlangResult WriteToFile(ConstU8Str fileName);

    /// Get the name of the module.
    ConstU8Str GetName();

    /// Get the path of the module.
    ConstU8Str GetFilePath();

    /// Get the unique identity of the module.
    ConstU8Str GetUniqueIdentity();

    /// Find and validate an entry point by name, even if the function is
    /// not marked with the `[shader("...")]` attribute.
    SlangResult FindAndCheckEntryPoint(
        ConstU8Str name,
        SlangStage stage,
        out IEntryPoint* outEntryPoint,
        out ISlangBlob* outDiagnostics);

    /// Get the number of dependency files that this module depends on.
    /// This includes both the explicit source files, as well as any
    /// additional files that were transitively referenced (e.g., via
    /// a `#include` directive).
    SlangInt32 GetDependencyFileCount();

    /// Get the path to a file this module depends on.
    ConstU8Str GetDependencyFilePath(SlangInt32 index);

    DeclReflection* GetModuleReflection();

    /** Disassemble a module.
     */
    SlangResult Disassemble(out ISlangBlob* outDisassembledBlob);
}
