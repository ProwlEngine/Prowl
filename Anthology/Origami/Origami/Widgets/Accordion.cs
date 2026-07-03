// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;
using Prowl.Vector;

namespace Prowl.OrigamiUI;

/// <summary>
/// Fluent builder for an accordion: a stack of collapsible sections where opening one collapses
/// the others (single-open). Each section is an Origami <see cref="FoldoutBuilder"/> under the hood.
/// Construct via <see cref="Origami.Accordion(Paper, string)"/>, add sections, then call <see cref="Show"/>.
/// </summary>
/// <remarks>The open section is tracked per-instance in element storage, so the caller holds no
/// state. Pass <see cref="AllowAllClosed"/> = false to keep exactly one section open at all times.</remarks>
public sealed class AccordionBuilder
{
    private sealed class SectionDef
    {
        public string Id = "";
        public string Title = "";
        public IOrigamiIcon? Icon;
        public Action Body = static () => { };
    }

    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly List<SectionDef> _sections = new();
    private float _spacing = 7f;
    private string? _defaultOpen;
    private bool _allowAllClosed = true;

    internal AccordionBuilder(Paper paper, string id, OrigamiTheme theme)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    /// <summary>Add a section with a leading icon.</summary>
    public AccordionBuilder Section(string id, string title, IOrigamiIcon? icon, Action body)
    {
        _sections.Add(new SectionDef { Id = id, Title = title ?? "", Icon = icon, Body = body ?? (static () => { }) });
        return this;
    }

    /// <summary>Add a section with no icon.</summary>
    public AccordionBuilder Section(string id, string title, Action body) => Section(id, title, null, body);

    /// <summary>Which section is open on first render (default none).</summary>
    public AccordionBuilder DefaultOpen(string id) { _defaultOpen = id; return this; }

    /// <summary>When false, clicking the open section keeps it open (one section is always expanded).</summary>
    public AccordionBuilder AllowAllClosed(bool allow = true) { _allowAllClosed = allow; return this; }

    /// <summary>Vertical gap between sections (default 7).</summary>
    public AccordionBuilder Spacing(float spacing) { _spacing = MathF.Max(0f, spacing); return this; }

    /// <summary>Render the accordion.</summary>
    public void Show()
    {
        if (_sections.Count == 0) return;

        var parentH = _paper.CurrentParent;
        string key = $"{_id}_open";
        string openId = _paper.GetElementStorage(parentH, key, _defaultOpen ?? "");

        using (_paper.Column(_id).Width(UnitValue.Stretch()).Height(UnitValue.Auto).ColBetween(_spacing).Enter())
        {
            foreach (var s in _sections)
            {
                string sid = s.Id;
                bool isOpen = openId == sid;

                var fold = Origami.Foldout(_paper, $"{_id}_{sid}", s.Title)
                    .Expanded(isOpen, expand =>
                    {
                        // Single-open: expanding a section becomes the open one; collapsing clears it
                        // (unless one must always stay open).
                        string next = expand ? sid : (_allowAllClosed ? "" : sid);
                        _paper.SetElementStorage(parentH, key, next);
                    });

                if (s.Icon != null) fold.Icon(s.Icon);
                fold.Body(s.Body);
            }
        }
    }
}
