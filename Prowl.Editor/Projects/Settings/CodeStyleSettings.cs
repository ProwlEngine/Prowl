// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Formatting;
using Microsoft.CodeAnalysis.Formatting;
using Microsoft.CodeAnalysis.Options;

using Prowl.Editor.GUI;
using Prowl.Editor.GUI.ScriptEditor;
using Prowl.Editor.Projects.Scripting;
using Prowl.Editor.Theming;
using Prowl.PaperUI;
using Prowl.Runtime;

namespace Prowl.Editor.Projects.Settings;

public enum VarPreference { Always, WhenTypeIsApparent, Never }
public enum NamespaceStyle { FileScoped, BlockScoped }
public enum UsingPlacement { OutsideNamespace, InsideNamespace }
public enum RuleSeverity { None, Suggestion, Warning, Error }

/// <summary>A severity override for one diagnostic, such as CS0168 or IDE0005.</summary>
public class DiagnosticRule
{
    public string Id = "";
    public RuleSeverity Severity = RuleSeverity.Warning;
}

/// <summary>
/// How the project's C# is formatted and checked. Formatting drives the built in formatter, severities apply to
/// compiles and the code editor, and everything is written to the project's .editorconfig so external IDEs agree.
/// </summary>
[ProjectSettings("Code Style", EditorIcons.Code, order: 30, exportToBuild: false)]
public class CodeStyleSettings : ProjectSettingsBase
{
    [Header("Formatting")]
    public bool IndentWithTabs;
    [Range(1, 8)] public int IndentSize = 4;
    [Tooltip("Opening braces on their own line.")] public bool BracesOnNewLine = true;
    public bool NewLineBeforeElse = true;
    public bool NewLineBeforeCatch = true;
    public bool NewLineBeforeFinally = true;
    [Tooltip("A space after a cast, as in (int) value.")] public bool SpaceAfterCast;
    [Tooltip("A space after if, for, while and the like, as in if (x).")] public bool SpaceAfterControlFlowKeyword = true;

    [Header("Style (external IDEs)")]
    public VarPreference PreferVar = VarPreference.WhenTypeIsApparent;
    public NamespaceStyle Namespaces = NamespaceStyle.FileScoped;
    public UsingPlacement Usings = UsingPlacement.OutsideNamespace;
    public bool SystemUsingsFirst = true;

    [Header("Diagnostics")]
    [Tooltip("Severity overrides by diagnostic id, used when compiling and in the code editor.")]
    public List<DiagnosticRule> Severities = [];

    [Header(".editorconfig")]
    [Tooltip("Keeps a Prowl section in the project's .editorconfig in step with these settings. The rest of the file is left alone.")]
    public bool WriteEditorConfig = true;

    private const string BlockStart = "# Prowl code style begin";
    private const string BlockEnd = "# Prowl code style end";

    public override void Apply()
    {
        RoslynScriptBackend.DiagnosticOverrides = [.. DiagnosticOptions()];
        ScriptWorkspaces.SetStyle(ApplyTo);
        if (WriteEditorConfig && Project.Current is { } project) WriteEditorConfigFile(project.RootPath);
    }

    public override void ResetToDefaults() => EditorRegistries.CopySettingsFields(new CodeStyleSettings(), this);

    public override void OnGUI(Paper paper, float width)
        => PropertyGridUtils.Draw(paper, "code_style", this, _ =>
        {
            Apply();
            EditorRegistries.SaveSettings();
        });

    /// <summary>The formatting part of these settings, as Roslyn options.</summary>
    public OptionSet ApplyTo(OptionSet options) => options
        .WithChangedOption(FormattingOptions.UseTabs, LanguageNames.CSharp, IndentWithTabs)
        .WithChangedOption(FormattingOptions.IndentationSize, LanguageNames.CSharp, IndentSize)
        .WithChangedOption(FormattingOptions.TabSize, LanguageNames.CSharp, IndentSize)
        .WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInTypes, BracesOnNewLine)
        .WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInMethods, BracesOnNewLine)
        .WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInProperties, BracesOnNewLine)
        .WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInAccessors, BracesOnNewLine)
        .WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInAnonymousMethods, BracesOnNewLine)
        .WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInControlBlocks, BracesOnNewLine)
        .WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInAnonymousTypes, BracesOnNewLine)
        .WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInObjectCollectionArrayInitializers, BracesOnNewLine)
        .WithChangedOption(CSharpFormattingOptions.NewLinesForBracesInLambdaExpressionBody, BracesOnNewLine)
        .WithChangedOption(CSharpFormattingOptions.NewLineForElse, NewLineBeforeElse)
        .WithChangedOption(CSharpFormattingOptions.NewLineForCatch, NewLineBeforeCatch)
        .WithChangedOption(CSharpFormattingOptions.NewLineForFinally, NewLineBeforeFinally)
        .WithChangedOption(CSharpFormattingOptions.SpaceAfterCast, SpaceAfterCast)
        .WithChangedOption(CSharpFormattingOptions.SpaceAfterControlFlowStatementKeyword, SpaceAfterControlFlowKeyword);

    private IEnumerable<KeyValuePair<string, ReportDiagnostic>> DiagnosticOptions()
    {
        foreach (DiagnosticRule rule in Severities)
        {
            if (string.IsNullOrWhiteSpace(rule.Id)) continue;
            ReportDiagnostic report = rule.Severity switch
            {
                RuleSeverity.None => ReportDiagnostic.Suppress,
                RuleSeverity.Suggestion => ReportDiagnostic.Info,
                RuleSeverity.Error => ReportDiagnostic.Error,
                _ => ReportDiagnostic.Warn,
            };
            yield return new(rule.Id.Trim(), report);
        }
    }

    /// <summary>The Prowl section of .editorconfig, between its begin and end markers.</summary>
    public string EditorConfigBlock()
    {
        static string B(bool v) => v ? "true" : "false";
        bool builtIn = PreferVar == VarPreference.Always, apparent = PreferVar != VarPreference.Never, elsewhere = PreferVar == VarPreference.Always;

        var sb = new StringBuilder();
        sb.AppendLine(BlockStart);
        sb.AppendLine("# Written from Project Settings > Code Style. Change it there, this section is rewritten.");
        sb.AppendLine("[*.cs]");
        sb.AppendLine($"indent_style = {(IndentWithTabs ? "tab" : "space")}");
        sb.AppendLine($"indent_size = {IndentSize}");
        sb.AppendLine($"csharp_new_line_before_open_brace = {(BracesOnNewLine ? "all" : "none")}");
        sb.AppendLine($"csharp_new_line_before_else = {B(NewLineBeforeElse)}");
        sb.AppendLine($"csharp_new_line_before_catch = {B(NewLineBeforeCatch)}");
        sb.AppendLine($"csharp_new_line_before_finally = {B(NewLineBeforeFinally)}");
        sb.AppendLine($"csharp_space_after_cast = {B(SpaceAfterCast)}");
        sb.AppendLine($"csharp_space_after_keywords_in_control_flow_statements = {B(SpaceAfterControlFlowKeyword)}");
        sb.AppendLine($"csharp_style_var_for_built_in_types = {B(builtIn)}");
        sb.AppendLine($"csharp_style_var_when_type_is_apparent = {B(apparent)}");
        sb.AppendLine($"csharp_style_var_elsewhere = {B(elsewhere)}");
        sb.AppendLine($"csharp_style_namespace_declarations = {(Namespaces == NamespaceStyle.FileScoped ? "file_scoped" : "block_scoped")}");
        sb.AppendLine($"csharp_using_directive_placement = {(Usings == UsingPlacement.OutsideNamespace ? "outside_namespace" : "inside_namespace")}");
        sb.AppendLine($"dotnet_sort_system_directives_first = {B(SystemUsingsFirst)}");
        foreach (DiagnosticRule rule in Severities.Where(r => !string.IsNullOrWhiteSpace(r.Id)))
            sb.AppendLine($"dotnet_diagnostic.{rule.Id.Trim()}.severity = {rule.Severity.ToString().ToLowerInvariant()}");
        sb.Append(BlockEnd);
        return sb.ToString().Replace("\r\n", "\n");
    }

    /// <summary>The .editorconfig text with the Prowl section replaced, or added when the file has none.</summary>
    public static string MergeEditorConfig(string? existing, string block)
    {
        if (string.IsNullOrEmpty(existing)) return "root = true\n\n" + block + "\n";

        string text = existing.Replace("\r\n", "\n");
        int start = text.IndexOf(BlockStart, StringComparison.Ordinal);
        int end = start < 0 ? -1 : text.IndexOf(BlockEnd, start, StringComparison.Ordinal);
        if (start >= 0 && end >= 0)
            return text[..start] + block + text[(end + BlockEnd.Length)..];
        return text.TrimEnd('\n') + "\n\n" + block + "\n";
    }

    private void WriteEditorConfigFile(string root)
    {
        string path = Path.Combine(root, ".editorconfig");
        try
        {
            string? existing = File.Exists(path) ? File.ReadAllText(path) : null;
            string merged = MergeEditorConfig(existing, EditorConfigBlock());
            if (merged != existing?.Replace("\r\n", "\n")) File.WriteAllText(path, merged);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[Code Style] Could not write {path}: {ex.Message}");
        }
    }
}
