namespace Prowl.Slang;


/// <summary>
/// Represents a value used for a compiler option. Supports integer and string-based values.
/// </summary>
public struct CompilerOptionValue
{
    /// <summary>
    /// Specifies the kind of this value.
    /// </summary>
    public CompilerOptionValueKind Kind;

    /// <summary>
    /// First integer value.
    /// </summary>
    public int IntValue0;

    /// <summary>
    /// Second integer value.
    /// </summary>
    public int IntValue1;

    /// <summary>
    /// First integer value. Null if unused.
    /// </summary>
    public string? StringValue0;

    /// <summary>
    /// Second integer value. Null if unused.
    /// </summary>
    public string? StringValue1;
}


/// <summary>
/// A name-value pair for a compiler option.
/// </summary>
public struct CompilerOptionEntry
{
    /// <summary>
    /// The compiler option kind.
    /// </summary>
    public CompilerOptionName Name;

    /// <summary>
    /// The compiler option value.
    /// </summary>
    public CompilerOptionValue Value;
}


/// <summary>
/// Description of a code generation target.
/// </summary>
public struct TargetDescription()
{
    /// <summary>
    /// The target format to generate code for (e.g., SPIR-V, DXIL, etc.)
    /// </summary>
    public SlangCompileTarget Format = SlangCompileTarget.Unknown;

    /// <summary>
    /// The compilation profile supported by the target (e.g., "Shader Model 5.1")
    /// </summary>
    public SlangProfileID Profile = SlangProfileID.UNKNOWN;

    /// <summary>
    /// Flags for the code generation target. Currently unused.
    /// </summary>
    public SlangTargetFlags Flags = SlangTargetFlags.Default;

    /// <summary>
    /// Default mode to use for floating-point operations on the target.
    /// </summary>
    public SlangFloatingPointMode FloatingPointMode = SlangFloatingPointMode.DEFAULT;

    /// <summary>
    /// The line directive mode for output source code.
    /// </summary>
    public SlangLineDirectiveMode LineDirectiveMode = SlangLineDirectiveMode.DEFAULT;

    /// <summary>
    /// Whether to force `scalar` layout for glsl shader storage buffers.
    /// </summary>
    public bool ForceGLSLScalarBufferLayout = false;

    /// <summary>
    /// Compiler options for this target.
    /// </summary>
    public CompilerOptionEntry[]? CompilerOptionEntries;
}


/// <summary>
/// A preprocessor macro definition split into name and value
/// </summary>
public struct PreprocessorMacroDescription
{
    /// <summary>
    /// The name of the macro.
    /// </summary>
    public string Name;

    /// <summary>
    /// The value of the macro.
    /// </summary>
    public string Value;
}


/// <summary>
///
/// </summary>
public struct SessionDescription()
{
    /// <summary>
    /// Code generation targets to include in the session.
    /// </summary>
    public TargetDescription[]? Targets;

    /// <summary>
    /// Flags to configure the session.
    /// </summary>
    public SessionFlags Flags = SessionFlags.None;

    /// <summary>
    /// Default layout to assume for variables with matrix types.
    /// </summary>
    public SlangMatrixLayoutMode DefaultMatrixLayoutMode = SlangMatrixLayoutMode.ROW_MAJOR;

    /// <summary>
    /// Paths to use when searching for `#include`d or `import`ed files.
    /// </summary>
    public string[]? SearchPaths;

    /// <summary>
    /// Preprocessor macros to apply during compilation.
    /// </summary>
    public PreprocessorMacroDescription[]? PreprocessorMacros;

    /// <summary>
    /// The file provider used to load module files during compilation.
    /// </summary>
    public IFileProvider? FileProvider;

    /// <summary>
    /// Indicates whether or not effect annotations will be enabled during compilation.
    /// </summary>
    public bool EnableEffectAnnotations;

    /// <summary>
    /// Indicates if the compiler should be able to consume GLSL-style syntax (e.g `vec3`, `vec4`).
    /// </summary>
    public bool AllowGLSLSyntax;

    /// <summary>
    /// Top-level compiler options applied to all code generation targets.
    /// </summary>
    public CompilerOptionEntry[]? CompilerOptionEntries;
}
