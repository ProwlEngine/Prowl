using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Collections.Generic;

using Prowl.Slang.Native;


namespace Prowl.Slang;


public struct CompilerOptionValue
{
    public CompilerOptionValueKind Kind;

    public int IntValue0;
    public int IntValue1;

    public string? StringValue0;
    public string? StringValue1;
}


public struct CompilerOptionEntry
{
    public CompilerOptionName Name;
    public CompilerOptionValue Value;
}


/** Description of a code generation target.
 */
public struct TargetDescription()
{
    /** The target format to generate code for (e.g., SPIR-V, DXIL, etc.)
     */
    public SlangCompileTarget Format = SlangCompileTarget.Unknown;

    /** The compilation profile supported by the target (e.g., "Shader Model 5.1")
     */
    public SlangProfileID Profile = SlangProfileID.UNKNOWN;

    /** Flags for the code generation target. Currently unused. */
    public SlangTargetFlags Flags = SlangTargetFlags.Default;

    /** Default mode to use for floating-point operations on the target.
     */
    public SlangFloatingPointMode FloatingPointMode = SlangFloatingPointMode.DEFAULT;

    /** The line directive mode for output source code.
     */
    public SlangLineDirectiveMode LineDirectiveMode = SlangLineDirectiveMode.DEFAULT;

    /** Whether to force `scalar` layout for glsl shader storage buffers.
     */
    public bool ForceGLSLScalarBufferLayout = false;


    public CompilerOptionEntry[]? CompilerOptionEntries;
}



public struct PreprocessorMacroDescription
{
    public string Name;
    public string Value;
}


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

    public PreprocessorMacroDescription[]? PreprocessorMacros;

    public IFileProvider? FileProvider;

    public bool EnableEffectAnnotations;
    public bool AllowGLSLSyntax;

    public CompilerOptionEntry[]? CompilerOptionEntries;
}