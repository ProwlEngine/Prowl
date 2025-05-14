using SlangInt = nint;

namespace Prowl.Slang.NativeAPI;


/* Experimental interface for doing target precompilation of slang modules */
[UUID(0x8e12e8e3, 0x5fcd, 0x433e, 0xaf, 0xcb, 0x13, 0xa0, 0x88, 0xbc, 0x5e, 0xe5)]
public unsafe interface IModulePrecompileService_Experimental : IUnknown
{
    SlangResult PrecompileForTarget(SlangCompileTarget target, out ISlangBlob* outDiagnostics);

    SlangResult GetPrecompiledTargetCode(
        SlangCompileTarget target,
        out ISlangBlob* outCode,
        out ISlangBlob* outDiagnostics);

    SlangInt getModuleDependencyCount();

    SlangResult getModuleDependency(
        SlangInt dependencyIndex,
        out IModule* outModule,
        out ISlangBlob* outDiagnostics);
}
