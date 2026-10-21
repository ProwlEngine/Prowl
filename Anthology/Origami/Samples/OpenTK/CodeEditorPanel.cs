// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO;
using System.Text;

using Prowl.OrigamiUI;
using Prowl.OrigamiUI.CSharp;
using Prowl.PaperUI;

namespace OrigamiSample;

// A C# editor backed by Roslyn. Pass --huge N to append N generated lines and try the virtualization.
public sealed class CodeEditorPanel : DockPanel
{
    public static int HugeLines;

    private readonly CSharpWorkspace _workspace = new();
    private readonly CodeEditor _editor;

    public override string Title => _editor.IsDirty ? "PlayerController.cs *" : "PlayerController.cs";

    public CodeEditorPanel()
    {
        string text = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Scripts", "PlayerController.cs.txt"));
        if (HugeLines > 0) text += Generate(HugeLines);

        CodeLanguage language = Environment.GetEnvironmentVariable("PROWL_LEXICAL") != null
            ? new CSharpLanguage()
            : new RoslynCSharpLanguage(_workspace, "PlayerController.cs");
        _editor = new CodeEditor(text, language) { FilePath = "PlayerController.cs" };
        _editor.Options.Rulers = new[] { 120 };
        _editor.Markers.Add(new CodeLineMarker { Line = 34, Kind = CodeLineMarkerKind.Breakpoint });

        _editor.GlyphMarginClicked += line =>
        {
            var existing = _editor.Markers.FirstOrDefault(m => m.Line == line && m.Kind == CodeLineMarkerKind.Breakpoint);
            if (existing != null) _editor.Markers.Remove(existing);
            else _editor.Markers.Add(new CodeLineMarker { Line = line, Kind = CodeLineMarkerKind.Breakpoint });
        };
        _editor.SaveRequested += () => _editor.Undo.MarkSaved();
    }

    private static string Generate(int lines)
    {
        var sb = new StringBuilder();
        sb.Append("\n\npublic static class Generated\n{\n");
        int methods = Math.Max(1, lines / 8);
        for (int i = 0; i < methods; i++)
        {
            sb.Append("    /// <summary>Generated method ").Append(i).Append(".</summary>\n");
            sb.Append("    public static int Method").Append(i).Append("(int value)\n    {\n");
            sb.Append("        var text = $\"value {value} at ").Append(i).Append("\"; // comment\n");
            sb.Append("        return value * ").Append(i % 97).Append(" + text.Length;\n");
            sb.Append("    }\n\n");
        }
        sb.Append("}\n");
        return sb.ToString();
    }

    public override void OnGUI(Paper paper, float width, float height)
    {
        Origami.CodeEditor(paper, "sample_code", _editor).Size(paper.Percent(100), paper.Percent(100)).Show();
    }

    public override void OnClosed()
    {
        _editor.Dispose();
        _workspace.Dispose();
    }
}
