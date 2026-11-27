// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Prowl.OrigamiUI;

/// <summary>Per frame builder for a <see cref="CodeEditor"/>. Construct via <see cref="Origami.CodeEditor"/> and finish with <see cref="Show"/>.</summary>
public sealed class CodeEditorBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly CodeEditor _editor;
    private UnitValue _width = UnitValue.Stretch();
    private UnitValue _height = UnitValue.Stretch();

    internal CodeEditorBuilder(Paper paper, string id, CodeEditor editor)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _editor = editor ?? throw new ArgumentNullException(nameof(editor));
    }

    public CodeEditorBuilder Width(UnitValue width) { _width = width; return this; }
    public CodeEditorBuilder Height(UnitValue height) { _height = height; return this; }
    public CodeEditorBuilder Size(UnitValue width, UnitValue height) { _width = width; _height = height; return this; }

    public CodeEditorBuilder ReadOnly(bool readOnly = true) { _editor.Options.ReadOnly = readOnly; return this; }

    public void Show()
    {
        bool readOnly = Origami.IsReadOnly;
        bool saved = _editor.Options.ReadOnly;
        if (readOnly) _editor.Options.ReadOnly = true;
        try { _editor.Build(_paper, _id, _width, _height); }
        finally { if (readOnly) _editor.Options.ReadOnly = saved; }
    }
}
