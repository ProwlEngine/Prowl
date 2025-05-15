using Prowl.Slang.Native;


namespace Prowl.Slang;


/** Description of a code generation target.
 */
public struct TargetDescription()
{
    /** The target format to generate code for (e.g., SPIR-V, DXIL, etc.)
     */
    public SlangCompileTarget format = SlangCompileTarget.Unknown;

    /** The compilation profile supported by the target (e.g., "Shader Model 5.1")
     */
    public SlangProfileID profile = SlangProfileID.UNKNOWN;

    /** Flags for the code generation target. Currently unused. */
    public SlangTargetFlags flags = SlangTargetFlags.Default;

    /** Default mode to use for floating-point operations on the target.
     */
    public SlangFloatingPointMode floatingPointMode = SlangFloatingPointMode.DEFAULT;

    /** The line directive mode for output source code.
     */
    public SlangLineDirectiveMode lineDirectiveMode = SlangLineDirectiveMode.DEFAULT;

    /** Whether to force `scalar` layout for glsl shader storage buffers.
     */
    public bool forceGLSLScalarBufferLayout = false;

    public CompilerOptionEntry[]? compilerOptionEntries;
};