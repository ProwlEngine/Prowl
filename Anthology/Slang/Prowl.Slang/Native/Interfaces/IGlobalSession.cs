using System;

namespace Prowl.Slang.Native;


/** A global session for interaction with the Slang library.

An application may create and re-use a single global session across
multiple sessions, in order to amortize startups costs (in current
Slang this is mostly the cost of loading the Slang standard library).

The global session is currently *not* thread-safe and objects created from
a single global session should only be used from a single thread at
a time.
*/
[UUID(0xc140b5fd, 0xc78, 0x452e, 0xba, 0x7c, 0x1a, 0x1e, 0x70, 0xc7, 0xf7, 0x1c)]
public unsafe interface IGlobalSession : IUnknown
{
    /** Create a new session for loading and compiling code.
     */
    SlangResult CreateSession(SessionDesc* desc, out ISession* outSession);

    /** Look up the internal ID of a profile by its `name`.

    Profile IDs are *not* guaranteed to be stable across versions
    of the Slang library, so clients are expected to look up
    profiles by name at runtime.
    */
    SlangProfileID FindProfile(ConstU8Str name);

    /** Set the path that downstream compilers (aka back end compilers) will
    be looked from.
    @param passThrough Identifies the downstream compiler
    @param path The path to find the downstream compiler (shared library/dll/executable)

    For back ends that are dlls/shared libraries, it will mean the path will
    be prefixed with the path when calls are made out to ISlangSharedLibraryLoader.
    For executables - it will look for executables along the path */
    void SetDownstreamCompilerPath(SlangPassThrough passThrough, ConstU8Str path);

    /** DEPRECATED: Use setLanguagePrelude

    Set the 'prelude' for generated code for a 'downstream compiler'.
    @param passThrough The downstream compiler for generated code that will have the prelude applied
    to it.
    @param preludeText The text added pre-pended verbatim before the generated source

    That for pass-through usage, prelude is not pre-pended, preludes are for code generation only.
    */
    [Obsolete("Method is deprecated")]
    void SetDownstreamCompilerPrelude(SlangPassThrough passThrough, ConstU8Str preludeText);

    /** DEPRECATED: Use getLanguagePrelude

    Get the 'prelude' for generated code for a 'downstream compiler'.
    @param passThrough The downstream compiler for generated code that will have the prelude applied
    to it.
    @param outPrelude  On exit holds a blob that holds the string of the prelude.
    */
    [Obsolete("Method is deprecated")]
    void GetDownstreamCompilerPrelude(SlangPassThrough passThrough, out ISlangBlob* outPrelude);

    /** Get the build version 'tag' string. The string is the same as produced via `git describe
    --tags` for the project. If Slang is built separately from the automated build scripts the
    contents will by default be 'unknown'. Any string can be set by changing the contents of
    'slang-tag-version.h' file and recompiling the project.

    This method will return exactly the same result as the free function spGetBuildTagString.

    @return The build tag string
    */
    ConstU8Str GetBuildTagString();

    /* For a given source language set the default compiler.
    If a default cannot be chosen (for example the target cannot be achieved by the default),
    the default will not be used.

    @param sourceLanguage the source language
    @param defaultCompiler the default compiler for that language
    @return
    */
    SlangResult SetDefaultDownstreamCompiler(SlangSourceLanguage sourceLanguage, SlangPassThrough defaultCompiler);

    /* For a source type get the default compiler

    @param sourceLanguage the source language
    @return The downstream compiler for that source language */
    SlangPassThrough GetDefaultDownstreamCompiler(SlangSourceLanguage sourceLanguage);

    /* Set the 'prelude' placed before generated code for a specific language type.

    @param sourceLanguage The language the prelude should be inserted on.
    @param preludeText The text added pre-pended verbatim before the generated source

    Note! That for pass-through usage, prelude is not pre-pended, preludes are for code generation
    only.
    */
    void SetLanguagePrelude(SlangSourceLanguage sourceLanguage, ConstU8Str preludeText);

    /** Get the 'prelude' associated with a specific source language.
    @param sourceLanguage The language the prelude should be inserted on.
    @param outPrelude  On exit holds a blob that holds the string of the prelude.
    */
    void GetLanguagePrelude(SlangSourceLanguage sourceLanguage, out ISlangBlob* outPrelude);

    /** Create a compile request.
     */
    [Obsolete("Method is deprecated")]
    SlangResult CreateCompileRequest(out /* ICompileRequest */ void* outCompileRequest);

    /** Add new builtin declarations to be used in subsequent compiles.
     */
    void AddBuiltins(ConstU8Str sourcePath, ConstU8Str sourceString);

    /** Set the session shared library loader. If this changes the loader, it may cause shared
    libraries to be unloaded
    @param loader The loader to set. Setting null sets the default loader.
    */
    void SetSharedLibraryLoader(ISlangSharedLibraryLoader* loader);

    /** Gets the currently set shared library loader
    @return Gets the currently set loader. If returns null, it's the default loader
    */
    ISlangSharedLibraryLoader* GetSharedLibraryLoader();

    /** Returns SLANG_OK if the compilation target is supported for this session

    @param target The compilation target to test
    @return SLANG_OK if the target is available
    SLANG_E_NOT_IMPLEMENTED if not implemented in this build
    SLANG_E_NOT_FOUND if other resources (such as shared libraries) required to make target work
    could not be found SLANG_FAIL other kinds of failures */
    SlangResult CheckCompileTargetSupport(SlangCompileTarget target);

    /** Returns SLANG_OK if the pass through support is supported for this session
    @param session Session
    @param target The compilation target to test
    @return SLANG_OK if the target is available
    SLANG_E_NOT_IMPLEMENTED if not implemented in this build
    SLANG_E_NOT_FOUND if other resources (such as shared libraries) required to make target work
    could not be found SLANG_FAIL other kinds of failures */
    SlangResult CheckPassThroughSupport(SlangPassThrough passThrough);

    /** Compile from (embedded source) the core module on the session.
    Will return a failure if there is already a core module available
    NOTE! API is experimental and not ready for production code
    @param flags to control compilation
    */
    SlangResult CompileCoreModule(CompileCoreModuleFlags flags);

    /** Load the core module. Currently loads modules from the file system.
    @param coreModule Start address of the serialized core module
    @param coreModuleSizeInBytes The size in bytes of the serialized core module

    NOTE! API is experimental and not ready for production code
    */
    SlangResult LoadCoreModule(void* coreModule, nuint coreModuleSizeInBytes);

    /** Save the core module to the file system
    @param archiveType The type of archive used to hold the core module
    @param outBlob The serialized blob containing the core module

    NOTE! API is experimental and not ready for production code  */
    SlangResult SaveCoreModule(SlangArchiveType archiveType, out ISlangBlob* outBlob);

    /** Look up the internal ID of a capability by its `name`.

    Capability IDs are *not* guaranteed to be stable across versions
    of the Slang library, so clients are expected to look up
    capabilities by name at runtime.
    */
    SlangCapabilityID FindCapability(ConstU8Str name);

    /** Set the downstream/pass through compiler to be used for a transition from the source type to
    the target type
    @param source The source 'code gen target'
    @param target The target 'code gen target'
    @param compiler The compiler/pass through to use for the transition from source to target
    */
    void SetDownstreamCompilerForTransition(SlangCompileTarget source, SlangCompileTarget target, SlangPassThrough compiler);

    /** Get the downstream/pass through compiler for a transition specified by source and target
    @param source The source 'code gen target'
    @param target The target 'code gen target'
    @return The compiler that is used for the transition. Returns NONE it is not
    defined
    */
    SlangPassThrough GetDownstreamCompilerForTransition(SlangCompileTarget source, SlangCompileTarget target);

    /** Get the time in seconds spent in the slang and downstream compiler.
     */
    void GetCompilerElapsedTime(out double outTotalTime, out double outDownstreamTime);

    /** Specify a spirv.core.grammar.json file to load and use when
     * parsing and checking any SPIR-V code
     */
    SlangResult SetSPIRVCoreGrammar(ConstU8Str jsonPath);

    /** Parse slangc command line options into a SessionDesc that can be used to create a session
     *   with all the compiler options specified in the command line.
     *   @param argc The number of command line arguments.
     *   @param argv An input array of command line arguments to parse.
     *   @param outSessionDesc A pointer to a SessionDesc struct to receive parsed session desc.
     *   @param outAuxAllocation Auxiliary memory allocated to hold data used in the session desc.
     */
    SlangResult ParseCommandLineArguments(int argc, ConstU8Str* argv, SessionDesc* outSessionDesc, out IUnknown* outAuxAllocation);

    /** Computes a digest that uniquely identifies the session description.
     */
    SlangResult GetSessionDescDigest(SessionDesc* sessionDesc, out ISlangBlob* outBlob);

    /** Compile from (embedded source) the builtin module on the session.
    Will return a failure if there is already a builtin module available.
    NOTE! API is experimental and not ready for production code.
    @param module The builtin module name.
    @param flags to control compilation
    */
    SlangResult CompileBuiltinModule(BuiltinModuleName module, CompileCoreModuleFlags flags);

    /** Load a builtin module. Currently loads modules from the file system.
    @param module The builtin module name
    @param moduleData Start address of the serialized core module
    @param sizeInBytes The size in bytes of the serialized builtin module

    NOTE! API is experimental and not ready for production code
    */
    SlangResult LoadBuiltinModule(BuiltinModuleName module, void* moduleData, nuint sizeInBytes);

    /** Save the builtin module to the file system
    @param module The builtin module name
    @param archiveType The type of archive used to hold the builtin module
    @param outBlob The serialized blob containing the builtin module

    NOTE! API is experimental and not ready for production code  */
    SlangResult SaveBuiltinModule(BuiltinModuleName module, SlangArchiveType archiveType, out ISlangBlob* outBlob);
}
