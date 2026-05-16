// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Thin wrapper around OrigamiModal for backward compatibility.

using System;

using Prowl.OrigamiUI;
using Prowl.PaperUI;

namespace Prowl.Editor.Widgets;

/// <summary>
/// Legacy modal dialog entry. Wraps <see cref="DialogModal"/>.
/// </summary>
public class ModalDialogEntry
{
    public string Title;
    public string Message;
    public Action<Paper> DrawContent;
    public float Width;
    public float Height;
    internal DialogModal _inner;

    public ModalDialogEntry(string title, Action<Paper> drawContent, float width = 400, float height = 0)
    {
        Title = title;
        DrawContent = drawContent;
        Width = width;
        Height = height;
        _inner = new DialogModal
        {
            Title = title,
            DrawContent = drawContent,
            Width = width,
            Height = height,
        };
    }

    public ModalDialogEntry(string title, string message, Action<Paper> drawContent, float width = 400, float height = 0)
        : this(title, drawContent, width, height)
    {
        Message = message;
    }

    public ModalDialogEntry Button(string label, Action onClick)
    {
        _inner.Button(label, onClick);
        return this;
    }
}

/// <summary>
/// Backward-compatible static API that delegates to <see cref="OrigamiModal"/>.
/// </summary>
public static class ModalDialog
{
    public static bool IsOpen => OrigamiModal.IsOpen;

    public static void Show(ModalDialogEntry dialog) => OrigamiModal.Push(dialog._inner);

    public static void Confirm(string title, string message, Action onYes, Action? onNo = null)
        => OrigamiModal.Confirm(title, message, onYes, onNo);

    public static void Message(string title, string message)
        => OrigamiModal.Message(title, message);

    public static void Close() => OrigamiModal.Pop();

    public static void Draw(Paper paper) => OrigamiModal.Draw(paper);
}
