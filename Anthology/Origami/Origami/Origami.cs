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

    // ── Read-only state ────────────────────────────────────────
    private static int _readOnlyDepth;

    /// <summary>True when inside a BeginReadOnly/EndReadOnly block.</summary>
    public static bool IsReadOnly => _readOnlyDepth > 0;

    /// <summary>Begin a read-only scope. All Origami widgets rendered between
    /// Begin/EndReadOnly will be disabled/non-interactive. Nestable.</summary>
    public static void BeginReadOnly() => _readOnlyDepth++;

    /// <summary>End a read-only scope.</summary>
    public static void EndReadOnly() { if (_readOnlyDepth > 0) _readOnlyDepth--; }

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

    // ── Frame lifecycle ─────────────────────────────────────────

    /// <summary>
    /// Call once at the start of each frame before any Origami widgets are drawn.
    /// Advances theme transitions and resets per-frame state.
    /// </summary>
    /// <param name="paper">Paper instance for this frame.</param>
    /// <param name="deltaSeconds">Frame delta time in seconds.</param>
    public static void BeginFrame(PaperUI.Paper paper, float deltaSeconds)
    {
        TickTransition(deltaSeconds);
    }

    /// <summary>
    /// Call once at the end of each frame after all user widgets are drawn.
    /// Renders overlay systems (drag-drop, context menus, modals, toasts, tooltips).
    /// </summary>
    /// <param name="paper">Paper instance for this frame.</param>
    public static void EndFrame(PaperUI.Paper paper)
    {
        DragDrop.Update(paper);
        DragDrop.DrawVisual(paper);
        OrigamiUI.ContextMenu.Tick();
        OrigamiUI.Modal.Draw(paper);
        OrigamiUI.Toasts.Draw(paper);
        TooltipSystem.Draw(paper);
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

    // ── Header / Separator factories ────────────────────────────

    /// <summary>Begin building a header or section divider. Chain style/variant modifiers, then call <see cref="HeaderBuilder.Show"/>.</summary>
    public static HeaderBuilder Header(Paper paper, string id, string label)
        => new HeaderBuilder(paper, id, label, Current);

    // ── Label factory ───────────────────────────────────────────

    /// <summary>
    /// Begin building a label. Chain variant/size/decoration modifiers and call
    /// <see cref="LabelBuilder.Show"/> to render.
    /// </summary>
    public static LabelBuilder Label(Paper paper, string id, string text)
        => new LabelBuilder(paper, id, text, Current);

    // ── Loading widgets ─────────────────────────────────────────

    /// <summary>
    /// Begin building a progress bar. Pass <paramref name="value"/> in [0,1] for
    /// determinate mode, or chain <see cref="ProgressBarBuilder.Indeterminate"/> for
    /// a sliding-band loading state.
    /// </summary>
    public static ProgressBarBuilder ProgressBar(Paper paper, string id, float value)
        => new ProgressBarBuilder(paper, id, value, Current);

    /// <summary>
    /// Begin building an animated spinner. Chain a style modifier
    /// (<see cref="SpinnerBuilder.Arc"/>, <see cref="SpinnerBuilder.Dots"/>,
    /// <see cref="SpinnerBuilder.Pulse"/>) and call <see cref="SpinnerBuilder.Show"/>.
    /// </summary>
    public static SpinnerBuilder Spinner(Paper paper, string id)
        => new SpinnerBuilder(paper, id, Current);

    /// <summary>
    /// Begin building a skeleton loading placeholder. Choose shape via
    /// <see cref="SkeletonBuilder.Rect"/> / <see cref="SkeletonBuilder.Pill"/> /
    /// <see cref="SkeletonBuilder.Circle"/>, set size, call <see cref="SkeletonBuilder.Show"/>.
    /// </summary>
    public static SkeletonBuilder Skeleton(Paper paper, string id)
        => new SkeletonBuilder(paper, id, Current);

    /// <summary>Shorthand for a text-less horizontal line separator.</summary>
    public static HeaderBuilder Separator(Paper paper, string id)
        => new HeaderBuilder(paper, id, "", Current).Separator();

    // ── Date picker factory ────────────────────────────────────

    /// <summary>Begin building a date picker field.</summary>
    public static DatePickerBuilder DatePicker(Paper paper, string id, DateTime value, Action<DateTime> setter)
        => new DatePickerBuilder(paper, id, value, setter, Current);

    // ── Table factory ──────────────────────────────────────────

    /// <summary>Begin building a data table. Declare columns with <c>.Column(...)</c>, add rows with
    /// <c>.Row().Cell(...)</c>, then call <c>.Show()</c>. Controlled — caller owns the selected index.</summary>
    public static TableBuilder Table(Paper paper, string id, int selectedIndex, Action<int> onSelect)
        => new TableBuilder(paper, id, selectedIndex, onSelect, Current);

    // ── Image diff factory ─────────────────────────────────────

    /// <summary>Begin building an image diff slider. Two images overlaid with a draggable split bar.</summary>
    public static ImageDiffBuilder ImageDiff(Paper paper, string id, object imageA, object imageB)
        => new ImageDiffBuilder(paper, id, imageA, imageB, Current);

    // ── Chat bubble factory ────────────────────────────────────

    /// <summary>Begin building a chat bubble. Provide content via the draw callback, then call .Show().</summary>
    public static ChatBubbleBuilder ChatBubble(Paper paper, string id, Action<Paper> content)
        => new ChatBubbleBuilder(paper, id, content, Current);

    // ── Breadcrumb factory ─────────────────────────────────────

    /// <summary>Begin building a breadcrumb navigation trail.</summary>
    public static BreadcrumbBuilder Breadcrumb(Paper paper, string id,
        IReadOnlyList<BreadcrumbItem> items, Action<BreadcrumbItem> onClick)
        => new BreadcrumbBuilder(paper, id, items, onClick, Current);

    // ── App bar factory ────────────────────────────────────────

    /// <summary>Begin building an app bar (menubar or footer). Chain .Menu(), .Center(), .Right(), then .Show().</summary>
    public static AppBarBuilder AppBar(Paper paper, string id)
        => new AppBarBuilder(paper, id, Current);

    // ── Menu bar factory ───────────────────────────────────────

    /// <summary>Begin building a menu bar (File / Edit / … dropdowns). Chain <c>.Menu(label, build)</c>
    /// per menu, then call <c>.Show()</c>. Click opens, hover switches, click-away closes.</summary>
    public static MenuBarBuilder MenuBar(Paper paper, string id)
        => new MenuBarBuilder(paper, id, Current);

    // ── Accordion factory ──────────────────────────────────────

    /// <summary>Begin building an accordion (single-open group of collapsible sections). Chain
    /// <c>.Section(id, title, body)</c> per section, then call <c>.Show()</c>.</summary>
    public static AccordionBuilder Accordion(Paper paper, string id)
        => new AccordionBuilder(paper, id, Current);

    // ── Tabs factory ───────────────────────────────────────────

    /// <summary>Begin building a tab strip. Controlled — caller owns the selected index; chain
    /// <c>.Underline()</c>/<c>.Pills()</c> and <c>.Tab(...)</c> per tab, then call <c>.Show()</c>.</summary>
    public static TabsBuilder Tabs(Paper paper, string id, int selectedIndex, Action<int> onSelect)
        => new TabsBuilder(paper, id, selectedIndex, onSelect, Current);

    // ── Modal helpers ────────────────────────────────────────

    /// <summary>Begin building a dialog modal. Chain content, buttons, then call .Show() to push.</summary>
    public static ModalBuilder Modal(string title) => new ModalBuilder(title);

    /// <summary>Push a confirmation modal (Yes/No). Shorthand for Modal().Message().Button().Show().</summary>
    public static void Confirm(string title, string message, Action onYes, Action? onNo = null)
        => OrigamiUI.Modal.Confirm(title, message, onYes, onNo);

    /// <summary>Push a message modal (OK). Shorthand for Modal().Message().Button().Show().</summary>
    public static void Message(string title, string message)
        => OrigamiUI.Modal.Message(title, message);

    /// <summary>Push a custom dialog modal. Returns the entry for chaining .Button().</summary>
    public static DialogModal Dialog(string title, Action<PaperUI.Paper> drawContent, float width = 400, float height = 0)
        => OrigamiUI.Modal.Custom(title, drawContent, width, height);

    /// <summary>Push a fully custom modal with caller-controlled rendering.</summary>
    public static CustomDrawModal PushModal(Action<PaperUI.Paper, int, int> draw, bool closeOnEscape = true, bool closeOnBackdrop = false)
        => OrigamiUI.Modal.PushCustomDraw(draw, closeOnEscape, closeOnBackdrop);

    // ── Toast helpers ────────────────────────────────────────

    /// <summary>Begin building a toast notification. Chain message, type, duration, then call .Show().</summary>
    public static Toasts Toast(string title) => new Toasts(title);

    // ── Context menu helpers ─────────────────────────────────

    /// <summary>Open a context menu at the given position.</summary>
    public static void ContextMenu(float x, float y, Action<ContextBuilder> build)
        => OrigamiUI.ContextMenu.Show(x, y, build);

    /// <summary>Attach a right-click context menu to the current parent element.</summary>
    public static void RightClickMenu(PaperUI.Paper paper, string id, Action<ContextBuilder> build)
        => OrigamiUI.ContextMenu.RightClickMenu(paper, id, build);

    // ── Property grid helpers ────────────────────────────────

    /// <summary>Create a property grid builder for the given object.</summary>
    public static PropertyGridBuilder PropertyGrid(PaperUI.Paper paper, string id, object target, PropertyGridConfig config)
        => new PropertyGridBuilder(paper, id, target, config);

    /// <summary>Create a property grid builder editing several objects at once. Fields shared by every
    /// target are shown; differing values are flagged as mixed and edits apply to all.</summary>
    public static PropertyGridBuilder PropertyGrid(PaperUI.Paper paper, string id, System.Collections.Generic.IReadOnlyList<object> targets, PropertyGridConfig config)
        => new PropertyGridBuilder(paper, id, targets, config);

    // ── File dialog helpers ──────────────────────────────────

    /// <summary>Open a file dialog.</summary>
    public static void OpenFileDialog(FileDialogMode mode, Action<string?> onComplete,
        string? startPath = null, string[]? filters = null, string[]? filterLabels = null,
        FileDialogConfig? config = null)
        => OrigamiUI.FileDialog.Open(mode, onComplete, startPath, filters, filterLabels, config);

    // ── Tooltip helpers ──────────────────────────────────────

    /// <summary>Notify the tooltip system that an element is hovered. Call from OnHover callbacks.</summary>
    public static void ShowTooltip(int elementId, string text) => TooltipSystem.Hover(elementId, text);

    /// <summary>Notify with rich tooltip content.</summary>
    public static void ShowTooltip(int elementId, TooltipContent content) => TooltipSystem.Hover(elementId, content);

    // ── Vector field factories ───────────────────────────────────

    // Float vectors
    public static VectorField2Builder<float> Float2Field(Paper paper, string id,
        Prowl.Vector.Float2 value, Action<Prowl.Vector.Float2> setter)
    {
        var v = value;
        return new VectorField2Builder<float>(paper, id, Current,
            (float)v.X, (float)v.Y,
            x => { v.X = x; setter(v); }, y => { v.Y = y; setter(v); });
    }

    public static VectorField3Builder<float> Float3Field(Paper paper, string id,
        Prowl.Vector.Float3 value, Action<Prowl.Vector.Float3> setter)
    {
        var v = value;
        return new VectorField3Builder<float>(paper, id, Current,
            (float)v.X, (float)v.Y, (float)v.Z,
            x => { v.X = x; setter(v); }, y => { v.Y = y; setter(v); }, z => { v.Z = z; setter(v); });
    }

    public static VectorField4Builder<float> Float4Field(Paper paper, string id,
        Prowl.Vector.Float4 value, Action<Prowl.Vector.Float4> setter)
    {
        var v = value;
        return new VectorField4Builder<float>(paper, id, Current,
            (float)v.X, (float)v.Y, (float)v.Z, (float)v.W,
            x => { v.X = x; setter(v); }, y => { v.Y = y; setter(v); },
            z => { v.Z = z; setter(v); }, w => { v.W = w; setter(v); });
    }

    // Double vectors
    public static VectorField2Builder<double> Double2Field(Paper paper, string id,
        Prowl.Vector.Double2 value, Action<Prowl.Vector.Double2> setter)
    {
        var v = value;
        return new VectorField2Builder<double>(paper, id, Current,
            (double)v.X, (double)v.Y,
            x => { v.X = x; setter(v); }, y => { v.Y = y; setter(v); });
    }

    public static VectorField3Builder<double> Double3Field(Paper paper, string id,
        Prowl.Vector.Double3 value, Action<Prowl.Vector.Double3> setter)
    {
        var v = value;
        return new VectorField3Builder<double>(paper, id, Current,
            (double)v.X, (double)v.Y, (double)v.Z,
            x => { v.X = x; setter(v); }, y => { v.Y = y; setter(v); }, z => { v.Z = z; setter(v); });
    }

    public static VectorField4Builder<double> Double4Field(Paper paper, string id,
        Prowl.Vector.Double4 value, Action<Prowl.Vector.Double4> setter)
    {
        var v = value;
        return new VectorField4Builder<double>(paper, id, Current,
            (double)v.X, (double)v.Y, (double)v.Z, (double)v.W,
            x => { v.X = x; setter(v); }, y => { v.Y = y; setter(v); },
            z => { v.Z = z; setter(v); }, w => { v.W = w; setter(v); });
    }

    // Int vectors
    public static VectorField2Builder<int> Int2Field(Paper paper, string id,
        Prowl.Vector.Int2 value, Action<Prowl.Vector.Int2> setter)
    {
        var v = value;
        return new VectorField2Builder<int>(paper, id, Current,
            v.X, v.Y,
            x => { v.X = x; setter(v); }, y => { v.Y = y; setter(v); });
    }

    public static VectorField3Builder<int> Int3Field(Paper paper, string id,
        Prowl.Vector.Int3 value, Action<Prowl.Vector.Int3> setter)
    {
        var v = value;
        return new VectorField3Builder<int>(paper, id, Current,
            v.X, v.Y, v.Z,
            x => { v.X = x; setter(v); }, y => { v.Y = y; setter(v); }, z => { v.Z = z; setter(v); });
    }

    public static VectorField4Builder<int> Int4Field(Paper paper, string id,
        Prowl.Vector.Int4 value, Action<Prowl.Vector.Int4> setter)
    {
        var v = value;
        return new VectorField4Builder<int>(paper, id, Current,
            v.X, v.Y, v.Z, v.W,
            x => { v.X = x; setter(v); }, y => { v.Y = y; setter(v); },
            z => { v.Z = z; setter(v); }, w => { v.W = w; setter(v); });
    }

    // ── Color field factory ─────────────────────────────────────

    /// <summary>Begin building a color field. Renders a swatch that opens a full picker popover.</summary>
    public static ColorFieldBuilder ColorField(Paper paper, string id,
        Prowl.Vector.Color value, Action<Prowl.Vector.Color> setter)
        => new ColorFieldBuilder(paper, id, value, setter, Current);

    // ── Button factories ─────────────────────────────────────────

    /// <summary>Begin building a button. Construct the click handler at the call site.</summary>
    public static ButtonBuilder Button(Paper paper, string id, string label, Action? onClick = null)
        => new ButtonBuilder(paper, id, label, onClick, Current);

    /// <summary>Square icon-only button. Sugar for <c>Button(...).IconOnly().LeadingIcon(glyph)</c>.</summary>
    public static ButtonBuilder IconButton(Paper paper, string id, string glyph, Action? onClick = null)
        => new ButtonBuilder(paper, id, string.Empty, onClick, Current).IconOnly().LeadingIcon(glyph);

    /// <summary>Square icon-only button drawn from an <see cref="IOrigamiIcon"/>.</summary>
    public static ButtonBuilder IconButton(Paper paper, string id, IOrigamiIcon? icon, Action? onClick = null)
        => new ButtonBuilder(paper, id, string.Empty, onClick, Current).IconOnly().LeadingIcon(icon);

    /// <summary>
    /// Begin building a segmented control. Caller supplies the current selected index and a setter;
    /// chain <see cref="ButtonGroupBuilder.Item"/> for each segment.
    /// </summary>
    public static ButtonGroupBuilder ButtonGroup(Paper paper, string id, int selectedIndex, Action<int> setter)
        => new ButtonGroupBuilder(paper, id, selectedIndex, setter, Current);

    /// <summary>
    /// A compact toolbar of square icon buttons (active = accent + glow). Horizontal or vertical,
    /// with optional glass container and tooltips. Chain <see cref="IconToolbarBuilder.Item"/> per button.
    /// </summary>
    public static IconToolbarBuilder IconToolbar(Paper paper, string id, int selected, Action<int> setter)
        => new IconToolbarBuilder(paper, id, selected, setter, Current);

    // ── Tree factories ─────────────────────────────────────────

    /// <summary>
    /// Begin building a tree view. Provide nodes as a flat depth-first list via
    /// <see cref="TreeBuilder.Nodes"/>; chain callbacks for selection, expand, checkbox,
    /// drag-drop, rename, context menu; call <see cref="TreeBuilder.Show"/> to render.
    /// </summary>
    public static TreeBuilder Tree(Paper paper, string id, float width, float height)
        => new TreeBuilder(paper, id, Current).Size(width, height);

    // ── Slider factories ─────────────────────────────────────────

    /// <summary>
    /// Begin building a generic slider. Track + thumb, click / drag / wheel / keyboard,
    /// optional log + bipolar mapping, ticks, tooltip, inline numeric. Generic on
    /// <typeparamref name="T"/> — any <see cref="System.Numerics.INumber{T}"/> works.
    /// </summary>
    public static SliderBuilder<T> Slider<T>(Paper paper, string id, T value, Action<T> setter, T min, T max)
        where T : struct, System.Numerics.INumber<T>
        => new SliderBuilder<T>(paper, id, value, setter, min, max, Current);

    /// <summary>Float convenience for the generic <see cref="Slider{T}"/>.</summary>
    public static SliderBuilder<float> Slider(Paper paper, string id, float value, Action<float> setter, float min, float max)
        => new SliderBuilder<float>(paper, id, value, setter, min, max, Current);

    /// <summary>Int convenience.</summary>
    public static SliderBuilder<int> IntSlider(Paper paper, string id, int value, Action<int> setter, int min, int max)
        => new SliderBuilder<int>(paper, id, value, setter, min, max, Current);

    /// <summary>
    /// Begin building a two-thumb range slider. Caller passes in <paramref name="low"/> and
    /// <paramref name="high"/> and gets both back through <paramref name="setter"/> on every
    /// change, ordered low &lt;= high.
    /// </summary>
    public static RangeSliderBuilder<T> RangeSlider<T>(Paper paper, string id, T low, T high,
        Action<T, T> setter, T min, T max)
        where T : struct, System.Numerics.INumber<T>
        => new RangeSliderBuilder<T>(paper, id, low, high, setter, min, max, Current);

    /// <summary>Float convenience for the generic <see cref="RangeSlider{T}"/>.</summary>
    public static RangeSliderBuilder<float> RangeSlider(Paper paper, string id, float low, float high,
        Action<float, float> setter, float min, float max)
        => new RangeSliderBuilder<float>(paper, id, low, high, setter, min, max, Current);

    /// <summary>Int convenience for the range slider.</summary>
    public static RangeSliderBuilder<int> IntRangeSlider(Paper paper, string id, int low, int high,
        Action<int, int> setter, int min, int max)
        => new RangeSliderBuilder<int>(paper, id, low, high, setter, min, max, Current);

    // ── Toggle factories ─────────────────────────────────────────

    /// <summary>
    /// Begin building a generic toggle. Defaults to switch style — chain
    /// <see cref="ToggleBuilder.AsCheckbox"/> / <see cref="ToggleBuilder.AsRadio"/> to switch style.
    /// </summary>
    public static ToggleBuilder Toggle(Paper paper, string id, bool value, Action<bool> setter)
        => new ToggleBuilder(paper, id, value, setter, Current);

    /// <summary>Sliding-pill switch — alias for <see cref="Toggle"/> defaults.</summary>
    public static ToggleBuilder Switch(Paper paper, string id, bool value, Action<bool> setter)
        => new ToggleBuilder(paper, id, value, setter, Current).AsSwitch();

    /// <summary>Square checkbox. Use <see cref="ToggleBuilder.Indeterminate"/> for tri-state.</summary>
    public static ToggleBuilder Checkbox(Paper paper, string id, bool value, Action<bool> setter)
        => new ToggleBuilder(paper, id, value, setter, Current).AsCheckbox();

    /// <summary>Single circular radio. For "pick one of N" use <see cref="RadioGroup{T}"/> instead.</summary>
    public static ToggleBuilder Radio(Paper paper, string id, bool value, Action<bool> setter)
        => new ToggleBuilder(paper, id, value, setter, Current).AsRadio();

    /// <summary>Begin building a typed radio group bound to a list of items.</summary>
    public static RadioGroupBuilder<T> RadioGroup<T>(Paper paper, string id, T value,
        Action<T> setter, IReadOnlyList<T> items)
        => new RadioGroupBuilder<T>(paper, id, value, setter, items, Current);

    /// <summary>Enum convenience for a single-value radio group. Renders <see cref="Enum.GetNames{T}"/> as labels.</summary>
    public static RadioGroupBuilder<TEnum> EnumRadioGroup<TEnum>(Paper paper, string id,
        TEnum value, Action<TEnum> setter)
        where TEnum : struct, Enum
        => new RadioGroupBuilder<TEnum>(paper, id, value, setter, Enum.GetValues<TEnum>(), Current);

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
