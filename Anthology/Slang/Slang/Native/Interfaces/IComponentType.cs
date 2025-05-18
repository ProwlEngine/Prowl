using SlangInt = nint;

namespace Prowl.Slang.Native;


[UUID(0x5bc42be8, 0x5c50, 0x4929, 0x9e, 0x5e, 0xd1, 0x5e, 0x7c, 0x24, 0x1, 0x5f)]
internal unsafe interface IComponentType : IUnknown
{
    ISession* GetSession();

    ShaderReflection* GetLayout(SlangInt targetIndex, out ISlangBlob* outDiagnostics);

    SlangInt GetSpecializationParamCount();

    SlangResult GetEntryPointCode(
        SlangInt entryPointIndex,
        SlangInt targetIndex,
        out ISlangBlob* outCode,
        out ISlangBlob* outDiagnostics);

    SlangResult GetResultAsFileSystem(
        SlangInt entryPointIndex,
        SlangInt targetIndex,
        out ISlangMutableFileSystem* outFileSystem);

    void GetEntryPointHash(SlangInt entryPointIndex, SlangInt targetIndex, out ISlangBlob* outHash);

    SlangResult Specialize(SpecializationArg* specializationArgs, SlangInt specializationArgCount, out IComponentType* outSpecializedComponentType, out ISlangBlob* outDiagnostics);

    SlangResult Link(out IComponentType* outLinkedComponentType, out ISlangBlob* outDiagnostics);

    SlangResult GetEntryPointHostCallable(int entryPointIndex, int targetIndex, out ISlangSharedLibrary* outSharedLibrary, out ISlangBlob* outDiagnostics);

    SlangResult RenameEntryPoint(ConstU8Str newName, out IComponentType* outEntryPoint);

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
