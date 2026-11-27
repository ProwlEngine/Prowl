// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

namespace Prowl.Slang.Native;


[UUID(0xc140b5fd, 0xc78, 0x452e, 0xba, 0x7c, 0x1a, 0x1e, 0x70, 0xc7, 0xf7, 0x1c)]
internal unsafe interface IGlobalSession : IUnknown
{
    SlangResult CreateSession(SessionDescription* desc, out ISession* outSession);

    ProfileID FindProfile(ConstU8Str name);

    void SetDownstreamCompilerPath(PassThrough passThrough, ConstU8Str path);

    [Obsolete("Method is deprecated")]
    void SetDownstreamCompilerPrelude(PassThrough passThrough, ConstU8Str preludeText);

    [Obsolete("Method is deprecated")]
    void GetDownstreamCompilerPrelude(PassThrough passThrough, out ISlangBlob* outPrelude);

    ConstU8Str GetBuildTagString();

    SlangResult SetDefaultDownstreamCompiler(SourceLanguage sourceLanguage, PassThrough defaultCompiler);

    PassThrough GetDefaultDownstreamCompiler(SourceLanguage sourceLanguage);

    void SetLanguagePrelude(SourceLanguage sourceLanguage, ConstU8Str preludeText);

    void GetLanguagePrelude(SourceLanguage sourceLanguage, out ISlangBlob* outPrelude);

    [Obsolete("Method is deprecated")]
    SlangResult CreateCompileRequest(out /* ICompileRequest */ void* outCompileRequest);

    void AddBuiltins(ConstU8Str sourcePath, ConstU8Str sourceString);

    void SetSharedLibraryLoader(ISlangSharedLibraryLoader* loader);

    ISlangSharedLibraryLoader* GetSharedLibraryLoader();

    SlangResult CheckCompileTargetSupport(CompileTarget target);

    SlangResult CheckPassThroughSupport(PassThrough passThrough);

    SlangResult CompileCoreModule(CompileCoreModuleFlags flags);

    SlangResult LoadCoreModule(void* coreModule, nuint coreModuleSizeInBytes);

    SlangResult SaveCoreModule(SlangArchiveType archiveType, out ISlangBlob* outBlob);

    CapabilityID FindCapability(ConstU8Str name);

    void SetDownstreamCompilerForTransition(CompileTarget source, CompileTarget target, PassThrough compiler);

    PassThrough GetDownstreamCompilerForTransition(CompileTarget source, CompileTarget target);

    void GetCompilerElapsedTime(out double outTotalTime, out double outDownstreamTime);

    SlangResult SetSPIRVCoreGrammar(ConstU8Str jsonPath);

    SlangResult ParseCommandLineArguments(int argc, ConstU8Str* argv, SessionDescription* outSessionDesc, out IUnknown* outAuxAllocation);

    SlangResult GetSessionDescDigest(SessionDescription* sessionDesc, out ISlangBlob* outBlob);

    SlangResult CompileBuiltinModule(BuiltinModuleName module, CompileCoreModuleFlags flags);

    SlangResult LoadBuiltinModule(BuiltinModuleName module, void* moduleData, nuint sizeInBytes);

    SlangResult SaveBuiltinModule(BuiltinModuleName module, SlangArchiveType archiveType, out ISlangBlob* outBlob);
}
