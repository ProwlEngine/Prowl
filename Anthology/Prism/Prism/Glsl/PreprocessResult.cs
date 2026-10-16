namespace Prowl.Prism.Glsl;

/// <summary>
/// Resolves an <c>#include</c>. <paramref name="headerName"/> is the text between the quotes or angle brackets,
/// <paramref name="isSystem"/> is true for the <c>&lt;name&gt;</c> form, and <paramref name="requestingName"/> is the
/// <see cref="IncludeResult.Name"/> of the file holding the directive ("" for the main source). Returns null when the
/// header cannot be found, which is a compile error.
/// </summary>
public delegate IncludeResult? IncludeCallback(string headerName, bool isSystem, string requestingName);

/// <summary>
/// An included file. <see cref="Name"/> identifies the file for include-once: a file is entered the first time its
/// name is resolved, and later includes of the same name are skipped. <see cref="Content"/> is its GLSL source.
/// </summary>
public readonly record struct IncludeResult(string Name, string Content);

/// <summary>A macro as it stands at the end of preprocessing. <see cref="Parameters"/> is null for object-like macros.</summary>
public sealed record MacroDefinition(string Name, IReadOnlyList<string>? Parameters, string Body);

/// <summary>
/// The output of the preprocessor. <see cref="Text"/> is GLSL with no directives left: macros are expanded, inactive
/// <c>#if</c> blocks are removed, and each <c>#include</c> is replaced by its file's contents. The version, extensions,
/// pragmas, and macros are the state the preprocessor found; <see cref="IncludedFiles"/> lists the files entered, in order.
/// </summary>
public sealed record PreprocessResult(
    string Text,
    int Version,
    bool IsEs,
    IReadOnlyList<string> Extensions,
    IReadOnlyList<string> Pragmas,
    IReadOnlyList<MacroDefinition> Defines,
    IReadOnlyList<string> IncludedFiles);
