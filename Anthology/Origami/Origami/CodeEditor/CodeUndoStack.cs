// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.OrigamiUI;

/// <summary>A caret and the other end of its selection. Equal ends mean no selection.</summary>
public readonly struct CodeSelection : IEquatable<CodeSelection>
{
    public readonly TextPosition Anchor;
    public readonly TextPosition Caret;

    public CodeSelection(TextPosition anchor, TextPosition caret)
    {
        Anchor = anchor;
        Caret = caret;
    }

    public CodeSelection(TextPosition caret) : this(caret, caret) { }

    public bool IsEmpty => Anchor == Caret;
    public TextRange Range => new(Anchor, Caret);
    public TextPosition Start => TextPosition.Min(Anchor, Caret);
    public TextPosition End => TextPosition.Max(Anchor, Caret);
    public bool IsReversed => Caret < Anchor;

    public bool Equals(CodeSelection other) => Anchor == other.Anchor && Caret == other.Caret;
    public override bool Equals(object? obj) => obj is CodeSelection s && Equals(s);
    public override int GetHashCode() => HashCode.Combine(Anchor, Caret);
}

/// <summary>Why an edit happened. Consecutive edits of the same typing kind merge into one undo step.</summary>
public enum CodeEditKind : byte { Other, Typing, Deleting }

internal sealed class CodeUndoStep
{
    public readonly List<TextChange> Changes = new();
    public CodeSelection[] Before = Array.Empty<CodeSelection>();
    public CodeSelection[] After = Array.Empty<CodeSelection>();
    public CodeEditKind Kind;
    public double Time;
}

/// <summary>Undo history of a <see cref="CodeEditor"/>. Steps hold the applied changes so undo replays them backwards.</summary>
public sealed class CodeUndoStack
{
    private readonly List<CodeUndoStep> _undo = new();
    private readonly List<CodeUndoStep> _redo = new();
    private CodeUndoStep? _open;
    private int _depth;
    private int _savePoint;

    /// <summary>Steps kept before the oldest are dropped.</summary>
    public int Limit { get; set; } = 2000;

    /// <summary>Typing that pauses longer than this starts a new undo step.</summary>
    public double MergeWindow { get; set; } = 1.0;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    /// <summary>True when the document differs from the last <see cref="MarkSaved"/>.</summary>
    public bool IsDirty => _savePoint != _undo.Count;

    public void MarkSaved() => _savePoint = _undo.Count;

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
        _open = null;
        _depth = 0;
        _savePoint = 0;
    }

    internal bool IsRecording => _open != null;

    internal void Begin(CodeSelection[] before, CodeEditKind kind, double time)
    {
        if (_depth++ > 0) return;
        _open = new CodeUndoStep { Before = before, Kind = kind, Time = time };
    }

    internal void Record(TextChange change) => _open?.Changes.Add(change);

    internal void End(CodeSelection[] after)
    {
        if (--_depth > 0 || _open == null) return;
        var step = _open;
        _open = null;
        if (step.Changes.Count == 0) return;
        step.After = after;

        _redo.Clear();
        if (_savePoint > _undo.Count) _savePoint = -1;

        if (_undo.Count > 0 && CanMerge(_undo[^1], step))
        {
            var last = _undo[^1];
            last.Changes.AddRange(step.Changes);
            last.After = step.After;
            last.Time = step.Time;
            return;
        }

        _undo.Add(step);
        if (_undo.Count > Limit)
        {
            _undo.RemoveAt(0);
            _savePoint--;
        }
    }

    /// <summary>Forces the next edit into a new step, such as after the caret was moved by hand.</summary>
    public void Break()
    {
        if (_undo.Count > 0) _undo[^1].Kind = CodeEditKind.Other;
    }

    private bool CanMerge(CodeUndoStep last, CodeUndoStep next)
    {
        if (next.Kind == CodeEditKind.Other || last.Kind != next.Kind) return false;
        if (next.Time - last.Time > MergeWindow) return false;
        if (_savePoint == _undo.Count) return false;
        if (last.After.Length != next.Before.Length) return false;
        for (int i = 0; i < last.After.Length; i++)
            if (!last.After[i].Equals(next.Before[i])) return false;

        // A word boundary after typed text starts a new step, so undo removes a word at a time.
        if (next.Kind == CodeEditKind.Typing && last.Changes.Count > 0 && next.Changes.Count > 0)
        {
            string prev = last.Changes[^1].NewText;
            string cur = next.Changes[0].NewText;
            if (cur.Contains('\n')) return false;
            if (prev.Length > 0 && cur.Length > 0 && char.IsWhiteSpace(cur[0]) && !char.IsWhiteSpace(prev[^1])) return false;
        }
        return true;
    }

    internal CodeUndoStep? PopUndo()
    {
        if (_undo.Count == 0) return null;
        var step = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        _redo.Add(step);
        return step;
    }

    internal CodeUndoStep? PopRedo()
    {
        if (_redo.Count == 0) return null;
        var step = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        _undo.Add(step);
        return step;
    }
}
