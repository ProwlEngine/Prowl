using Prowl.Slang.Native;


namespace Prowl.Slang;


public struct SessionDescription()
{
    /** Code generation targets to include in the session.
     */
    public TargetDescription[]? Targets;

    /** Flags to configure the session.
     */
    public SessionFlags Flags = SessionFlags.None;

    /** Default layout to assume for variables with matrix types.
     */
    public SlangMatrixLayoutMode DefaultMatrixLayoutMode = SlangMatrixLayoutMode.ROW_MAJOR;

    /** Paths to use when searching for `#include`d or `import`ed files.
     */
    public string[]? SearchPaths;

    public PreprocessorMacroDesc[]? PreprocessorMacros;

    public ManagedComProxy<ISlangFileSystem>? FileSystem;

    public bool EnableEffectAnnotations = false;
    public bool AllowGLSLSyntax = false;

    public CompilerOptionEntry[]? CompilerOptionEntries;
}