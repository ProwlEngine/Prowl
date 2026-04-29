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

    /// <summary>
    /// Begin building a scroll view of the given fixed viewport size. Terminate with
    /// <c>.Body(...)</c> to render. State (scroll offsets, content dims) is persisted in
    /// element storage on the outer box, keyed by <paramref name="id"/>.
    /// </summary>
    public static ScrollViewBuilder ScrollView(Paper paper, string id, float width, float height)
        => new ScrollViewBuilder(paper, id, width, height, Current);

    /// <summary>
    /// Programmatically request a scroll view to scroll to the given offset on its next render.
    /// Useful for "jump to selection" interactions where the caller doesn't have a builder handle.
    /// </summary>
    /// <param name="scrollViewId">The id passed to <see cref="ScrollView"/>.</param>
    /// <param name="offset">Target offset in pixels — X for horizontal, Y for vertical.</param>
    public static void ScrollTo(string scrollViewId, Prowl.Vector.Float2 offset)
    {
        ArgumentNullException.ThrowIfNull(scrollViewId);
        ScrollViewBuilder.s_pendingScrollTo[scrollViewId] = offset;
    }

    // ── Dropdown factories ───────────────────────────────────────

    /// <summary>
    /// Begin building a single-select dropdown over a typed item list. Caller owns the value
    /// and supplies a setter; Origami calls it when the user picks a different item. Equality
    /// is determined by <see cref="EqualityComparer{T}.Default"/>; override via
    /// <see cref="DropdownBuilder{T}.Comparer"/> if needed.
    /// </summary>
    public static DropdownBuilder<T> Dropdown<T>(Paper paper, string id, T value, Action<T> setter,
        IReadOnlyList<T> items)
        => new DropdownBuilder<T>(paper, id, value, setter, items, Current);

    /// <summary>
    /// String-array convenience: pick from a fixed list, with the caller tracking the index.
    /// </summary>
    public static DropdownBuilder<int> Dropdown(Paper paper, string id, int selectedIndex,
        Action<int> setter, IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var indices = new int[options.Count];
        for (int i = 0; i < options.Count; i++) indices[i] = i;
        return new DropdownBuilder<int>(paper, id, selectedIndex, setter, indices, Current)
            .Display(i => (uint)i < (uint)options.Count ? options[i] : string.Empty);
    }

    /// <summary>
    /// Enum convenience for a single-value enum. Renders <see cref="Enum.GetNames{T}"/> as labels.
    /// </summary>
    public static DropdownBuilder<TEnum> EnumDropdown<TEnum>(Paper paper, string id, TEnum value,
        Action<TEnum> setter)
        where TEnum : struct, Enum
        => new DropdownBuilder<TEnum>(paper, id, value, setter, Enum.GetValues<TEnum>(), Current);

    /// <summary>
    /// Begin building a multi-select dropdown. The selection set is passed in and a fresh list
    /// is delivered to <paramref name="setter"/> on every toggle.
    /// </summary>
    public static MultiDropdownBuilder<T> MultiDropdown<T>(Paper paper, string id,
        IEnumerable<T> selected, Action<IReadOnlyList<T>> setter, IReadOnlyList<T> items)
        => new MultiDropdownBuilder<T>(paper, id, selected, setter, items, Current);

    // ── TextField factories ──────────────────────────────────────

    /// <summary>Begin building a single-line text field.</summary>
    public static TextFieldBuilder TextField(Paper paper, string id, string value, Action<string> setter)
        => new TextFieldBuilder(paper, id, value, setter, Current);

    /// <summary>Search field: leading magnifier glyph, default "Search..." placeholder, clear button.</summary>
    public static TextFieldBuilder SearchField(Paper paper, string id, string value, Action<string> setter,
        string placeholder = "Search...")
        => new TextFieldBuilder(paper, id, value, setter, Current).Search(placeholder);

    /// <summary>Password field: masks the value, adds a show/hide eye toggle.</summary>
    public static TextFieldBuilder PasswordField(Paper paper, string id, string value, Action<string> setter,
        char maskChar = '●')
        => new TextFieldBuilder(paper, id, value, setter, Current).Password(maskChar);

    /// <summary>Multi-line text area sized to <paramref name="rows"/> rows.</summary>
    public static TextFieldBuilder TextArea(Paper paper, string id, string value, Action<string> setter, int rows = 4)
        => new TextFieldBuilder(paper, id, value, setter, Current).MultiLine(rows);

    /// <summary>
    /// Begin building a numeric field. Generic on <typeparamref name="T"/> — works with
    /// any <see cref="System.Numerics.INumber{T}"/> (float, double, decimal, int, uint,
    /// long, short, byte, sbyte, etc.). Culture-aware by default
    /// (<see cref="System.Globalization.CultureInfo.CurrentCulture"/>).
    /// </summary>
    public static NumericFieldBuilder<T> NumericField<T>(Paper paper, string id, T value, Action<T> setter)
        where T : struct, System.Numerics.INumber<T>
        => new NumericFieldBuilder<T>(paper, id, value, setter, Current);

    /// <summary>
    /// Convenience for a <c>[Flags]</c> enum: each non-zero flag becomes a checkbox row,
    /// and the OR of the checked flags is delivered to <paramref name="setter"/>.
    /// Zero (<c>None</c>) entries are skipped — clearing the selection clears all bits.
    /// </summary>
    public static MultiDropdownBuilder<TEnum> FlagsDropdown<TEnum>(Paper paper, string id,
        TEnum value, Action<TEnum> setter)
        where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(setter);

        var all = Enum.GetValues<TEnum>();
        var nonZero = new List<TEnum>(all.Length);
        var valueU = Convert.ToUInt64(value);
        var current = new List<TEnum>(all.Length);
        foreach (var v in all)
        {
            ulong u = Convert.ToUInt64(v);
            if (u == 0) continue;
            nonZero.Add(v);
            if ((valueU & u) == u) current.Add(v);
        }

        return new MultiDropdownBuilder<TEnum>(paper, id, current, list =>
        {
            ulong combined = 0;
            foreach (var f in list) combined |= Convert.ToUInt64(f);
            setter((TEnum)Enum.ToObject(typeof(TEnum), combined));
        }, nonZero, Current);
    }
}
