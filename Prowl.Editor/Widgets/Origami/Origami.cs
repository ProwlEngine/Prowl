// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;

namespace Prowl.OrigamiUI;

/// <summary>
/// Static entry-point for the Origami widget library.
///
/// <para>Origami stays self-contained — it never references the host's theme system
/// directly. Hosts (e.g. Prowl's editor) sync their palette via <see cref="SetTheme"/>.
/// Multiple consumers in the same process scope their styling with <see cref="PushTheme"/>;
/// a game running inside the editor can theme its own UI without disturbing the editor's.</para>
/// </summary>
public static class Origami
{
    [ThreadStatic] private static Stack<OrigamiTheme>? t_stack;

    private static OrigamiTheme _root = OrigamiTheme.CreateDefaults();

    // Transition state (lerp from start → target over duration).
    private static OrigamiTheme? _transitionStart;
    private static OrigamiTheme? _transitionTarget;
    private static float _transitionDuration;
    private static float _transitionElapsed;

    /// <summary>
    /// The active root theme. During a transition this is a frame-fresh interpolated
    /// theme; otherwise it's whatever was last assigned via <see cref="SetTheme"/>.
    /// </summary>
    public static OrigamiTheme Root => _root;

    /// <summary>The currently active theme — top of the push stack, or <see cref="Root"/> if empty.</summary>
    public static OrigamiTheme Current => (t_stack is { Count: > 0 } s) ? s.Peek() : _root;

    /// <summary>True while a <see cref="SetTheme"/> transition is in progress.</summary>
    public static bool IsTransitioning => _transitionTarget != null;

    /// <summary>
    /// Replace the root theme. When <paramref name="transitionSeconds"/> is &gt; 0, the change
    /// animates over that duration — colours and metric numbers lerp smoothly; font and icons
    /// snap to the target at frame zero.
    /// </summary>
    /// <remarks>
    /// While a transition is running, the host must call <see cref="TickTransition"/> once per
    /// frame for the lerp to advance. Calling <c>SetTheme</c> again mid-transition restarts
    /// from the current interpolated state.
    /// </remarks>
    public static void SetTheme(OrigamiTheme target, float transitionSeconds = 0f)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (transitionSeconds <= 0f)
        {
            _root = target;
            _transitionStart = null;
            _transitionTarget = null;
            _transitionElapsed = 0f;
            _transitionDuration = 0f;
            return;
        }

        // Snapshot the present root as the lerp source — handles re-targeting mid-transition.
        _transitionStart = _root.Clone();
        _transitionTarget = target;
        _transitionDuration = transitionSeconds;
        _transitionElapsed = 0f;
    }

    /// <summary>
    /// Advance an in-progress theme transition. Call once per frame from the host's render
    /// loop; safe to call when no transition is active (no-op).
    /// </summary>
    public static void TickTransition(float deltaSeconds)
    {
        if (_transitionTarget == null || _transitionStart == null) return;

        _transitionElapsed += deltaSeconds;
        float t = _transitionDuration > 0f ? Math.Clamp(_transitionElapsed / _transitionDuration, 0f, 1f) : 1f;

        if (t >= 1f)
        {
            _root = _transitionTarget;
            _transitionStart = null;
            _transitionTarget = null;
            _transitionElapsed = 0f;
            _transitionDuration = 0f;
            return;
        }

        _root = OrigamiTheme.Lerp(_transitionStart, _transitionTarget, t);
    }

    /// <summary>
    /// Push a theme for the duration of the returned scope. Disposing the scope pops it.
    /// Stack is per-thread so different threads (rare in UI but possible in headless tests)
    /// don't collide.
    /// </summary>
    /// <example>
    /// <code>
    /// using (Origami.PushTheme(myGameTheme))
    /// {
    ///     Origami.Foldout(paper, "stats", "Stats").Body(() => { ... });
    /// }
    /// </code>
    /// </example>
    public static IDisposable PushTheme(OrigamiTheme theme)
    {
        ArgumentNullException.ThrowIfNull(theme);
        (t_stack ??= new Stack<OrigamiTheme>()).Push(theme);
        return new Scope();
    }

    private sealed class Scope : IDisposable
    {
        private bool _popped;
        public void Dispose()
        {
            if (_popped) return;
            _popped = true;
            t_stack?.Pop();
        }
    }

    // ── Widget factories ─────────────────────────────────────────

    /// <summary>Begin building a foldout. Terminate with <c>.Body(...)</c> to render.</summary>
    public static FoldoutBuilder Foldout(Paper paper, string id, string label)
        => new FoldoutBuilder(paper, id, label, Current);
}
