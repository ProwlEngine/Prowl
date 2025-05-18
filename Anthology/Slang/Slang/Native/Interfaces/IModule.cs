using SlangInt32 = int;

namespace Prowl.Slang.Native;


[UUID(0xc720e64, 0x8722, 0x4d31, 0x89, 0x90, 0x63, 0x8a, 0x98, 0xb1, 0xc2, 0x79)]
internal unsafe interface IModule : IComponentType
{
    SlangResult FindEntryPointByName(ConstU8Str name, out IEntryPoint* outEntryPoint);

    SlangInt32 GetDefinedEntryPointCount();

    SlangResult GetDefinedEntryPoint(SlangInt32 index, out IEntryPoint* outEntryPoint);

    SlangResult Serialize(out ISlangBlob* outSerializedBlob);

    SlangResult WriteToFile(ConstU8Str fileName);

    ConstU8Str GetName();

    ConstU8Str GetFilePath();

    ConstU8Str GetUniqueIdentity();

    SlangResult FindAndCheckEntryPoint(
        ConstU8Str name,
        SlangStage stage,
        out IEntryPoint* outEntryPoint,
        out ISlangBlob* outDiagnostics);

    SlangInt32 GetDependencyFileCount();

    ConstU8Str GetDependencyFilePath(SlangInt32 index);

    DeclReflection* GetModuleReflection();

    SlangResult Disassemble(out ISlangBlob* outDisassembledBlob);
}
