// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Editor;

/// <summary>
/// A request to start or stop play mode, handed to every subscriber of EditorApplication.PlayModeRequested before anything happens.
/// 
/// A handler that needs user input before the transition can proceed (for example, because of unapplied asset edits) calls Defer and is responsible for re-issuing the request once the user has decided. Re-issuing runs the request through the same path, avoiding the need for a stored continuation.
/// </summary>
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
