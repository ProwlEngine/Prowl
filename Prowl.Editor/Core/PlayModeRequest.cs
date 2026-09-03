// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Editor;

/// <summary>
/// A request to start or stop play mode, handed to every subscriber of
/// <see cref="EditorApplication.PlayModeRequested"/> before anything happens.
/// </summary>
/// <remarks>
/// A handler holding work the user has to decide about first, unapplied asset edits being the case
/// this exists for, calls <see cref="Defer"/> and owns asking again once they have decided. Deciding
/// is what re-runs the request, so the transition happens through the same path either way rather
/// than through a stored continuation that has to be kept correct.
/// </remarks>
public sealed class PlayModeRequest
{
    internal PlayModeRequest(bool entering) => Entering = entering;

    /// <summary>True for a request to start playing, false for one to stop.</summary>
    public bool Entering { get; }

    /// <summary>Why the transition was held, empty when nothing held it.</summary>
    public string DeferredBy { get; private set; } = string.Empty;

    /// <summary>True once a handler has held this transition.</summary>
    public bool IsDeferred => DeferredBy.Length > 0;

    /// <summary>
    /// Holds the transition, naming what it is waiting on. The handler is responsible for asking
    /// again through <see cref="EditorApplication.RequestPlayMode"/> or
    /// <see cref="EditorApplication.RequestExitPlayMode"/> once it is finished.
    /// </summary>
    public void Defer(string reason)
    {
        if (IsDeferred) return;

        DeferredBy = string.IsNullOrWhiteSpace(reason) ? "an editor panel" : reason;
    }
}
