// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Projects.Settings;

using Xunit;

namespace Prowl.Editor.Test;

public class CodeStyleTests
{
    [Fact]
    public void ANewEditorConfigIsRootWithTheProwlSection()
    {
        string block = new CodeStyleSettings().EditorConfigBlock();

        string text = CodeStyleSettings.MergeEditorConfig(null, block);

        Assert.StartsWith("root = true\n\n# Prowl code style begin", text);
        Assert.Contains("csharp_new_line_before_open_brace = all", text);
    }

    [Fact]
    public void MergingReplacesOnlyTheProwlSection()
    {
        var style = new CodeStyleSettings { BracesOnNewLine = false };
        string existing = "root = true\n\n[*.md]\nindent_size = 2\n\n" + new CodeStyleSettings().EditorConfigBlock() + "\n\n[*.json]\nindent_size = 2\n";

        string text = CodeStyleSettings.MergeEditorConfig(existing, style.EditorConfigBlock());

        Assert.Contains("[*.md]\nindent_size = 2", text);
        Assert.Contains("[*.json]\nindent_size = 2", text);
        Assert.Contains("csharp_new_line_before_open_brace = none", text);
        Assert.DoesNotContain("csharp_new_line_before_open_brace = all", text);
        Assert.Equal(2, text.Split("# Prowl code style begin").Length);
    }

    [Fact]
    public void AFileWithoutTheSectionKeepsItsContentAndGainsIt()
    {
        string existing = "root = true\r\n\r\n[*]\r\ncharset = utf-8\r\n";

        string text = CodeStyleSettings.MergeEditorConfig(existing, new CodeStyleSettings().EditorConfigBlock());

        Assert.StartsWith("root = true\n\n[*]\ncharset = utf-8\n\n# Prowl code style begin", text);
        Assert.EndsWith("# Prowl code style end\n", text);
    }

    [Fact]
    public void SeverityRulesBecomeDiagnosticEntries()
    {
        var style = new CodeStyleSettings();
        style.Severities.Add(new DiagnosticRule { Id = " CS0168 ", Severity = RuleSeverity.None });

        Assert.Contains("dotnet_diagnostic.CS0168.severity = none", style.EditorConfigBlock());
    }
}
