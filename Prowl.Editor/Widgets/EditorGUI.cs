using System;
using System.Globalization;

using Prowl.OrigamiUI;
using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Scribe;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Widgets;

/// <summary>
/// Core editor widget library. Immediate-mode drawing, callback-based state updates.
/// Each widget draws itself and returns a WidgetResult for chaining OnValueChanged callbacks.
/// State is managed via Paper element storage values persist across frames automatically.
/// </summary>
public static class EditorGUI
{
    private static FontFile? Font => EditorTheme.DefaultFont;
    private static float FontSz => EditorTheme.FontSize;
    private static float LabelW => EditorTheme.LabelWidth;

    /// <summary>Foldout disclosure icon: AngleDown when expanded, AngleRight when collapsed.</summary>
    public static string FoldoutIcon(bool expanded) => expanded ? EditorIcons.AngleDown : EditorIcons.AngleRight;

    /// <summary>
    /// Fullscreen backdrop element. Two flavours:
    /// <list type="bullet">
    /// <item><description><paramref name="dim"/>=true (default): semi-transparent black on <c>Layer.Overlay</c> — modal-style darken.</description></item>
    /// <item><description><paramref name="dim"/>=false: invisible click-blocker on <c>Layer.Topmost</c> — popup-style click-outside-to-close.</description></item>
    /// </list>
    /// <paramref name="onClose"/> fires on click; pass <c>null</c> for a non-dismissable backdrop (e.g. a modal that requires a button).
    /// </summary>
    public static void Backdrop(Paper paper, string id, Action? onClose = null, bool dim = true)
    {
        // Both flavours use a huge SelfDirected rectangle so the backdrop covers the entire
        // screen regardless of where in the layout tree it's emitted from. Size(Stretch)
        // would only fill the parent, which can be just a panel (e.g. the graph editor).
        var box = paper.Box(id)
            .PositionType(PositionType.SelfDirected)
            .Position(-9999, -9999)
            .Size(99999, 99999)
            .Layer(dim ? Layer.Overlay : Layer.Topmost);

        if (dim)
            box.BackgroundColor(Color.FromArgb(120, 0, 0, 0));

        // Backdrops absorb interaction with whatever is behind them — stop events from
        // bubbling to ancestors, so e.g. scrolling over a popup doesn't pan the canvas.
        box.StopEventPropagation();

        if (onClose != null)
            box.OnClick(0, (_, _) => onClose());
    }

    // Char filters for numeric inputs
    private static readonly Func<char, string, bool> IntFilter = (c, current) =>
        char.IsDigit(c) || (c == '-' && !current.Contains('-'));

    private static readonly Func<char, string, bool> FloatFilter = (c, current) =>
        char.IsDigit(c) || (c == '-' && !current.Contains('-')) || (c == '.' && !current.Contains('.'));

    private static ElementBuilder.TextInputSettings MakeNumericSettings(Func<char, string, bool> filter)
    {
        var s = ElementBuilder.TextInputSettings.Default;
        if (Font != null) s.Font = Font;
        s.TextColor = EditorTheme.Ink500;
        s.Placeholder = "0";
        s.PlaceholderColor = EditorTheme.Ink300;
        s.CharFilter = filter;
        return s;
    }

    // ================================================================
    //  Label
    // ================================================================

    public static void Label(Paper paper, string id, string text, Color? color = null)
    {
        if (Font == null) return;
        paper.Box(id)
            .Height(EditorTheme.RowHeight)
            .ChildLeft(4)
            .Text(text, Font)
            .TextColor(color ?? EditorTheme.Ink500)
            .Alignment(PaperUI.TextAlignment.MiddleLeft)
            .ChildBottom(EditorTheme.Spacing)
            .FontSize(FontSz);
    }


    // ================================================================
    //  Separator
    // ================================================================

    public static void Separator(Paper paper, string id)
{
        paper.Box(id + "_line")
            .Height(1)
            .Margin(6, 6)
            .BackgroundColor(EditorTheme.Ink100);
    }

    // Button removed (use Origami.Button).

    // Toggle removed (use Origami.Checkbox / Origami.Switch with .LabelRight(...)).

    // TextField, FloatField, IntField, Slider, IntSlider, Dropdown, EnumDropdown,
    // and SearchBar were removed in favour of the Origami widgets
    // (Origami.TextField / NumericField / Dropdown / SearchField etc.). Inspector rows
    // wrap them with Inspector.InspectorRow.Draw for the label-on-left convention.
    // FloatFieldWithInternalLabel is retained as the internal helper used by the
    // colour-coded Vector2/3/4 field rows below.

    public static Color LerpRGB(Color a, Color b, float t)
    {
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t),
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t)
        );
    }


    // ================================================================
    //  FloatField (helper state retained for FloatFieldWithInternalLabel below)
    // ================================================================

    private static bool s_floatDragging = false;
    private static float s_floatDragValue;

    public static float ProcessFloatDrag(PaperUI.Events.DragEvent dragEvent)
    {
        float multiplier = 0.1f;
        if (Runtime.Input.IsCtrlPressed)
            multiplier *= 10f;
        else if (Runtime.Input.IsShiftPressed)
            multiplier *= 0.01f;
        float finalValue = s_floatDragValue + dragEvent.TotalDelta.X * multiplier;
        s_floatDragValue = finalValue;
        return finalValue;
    }

    public static WidgetResult<float> FloatFieldWithInternalLabel(Paper paper, string id, float value, string label = "", Vector.Color? textColor = null)
    {
        Action<float>? userCallback = null;
        string textVal = value.ToString("G", CultureInfo.InvariantCulture);

        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .RowBetween(6)
            .Margin(UnitValue.Auto, EditorTheme.Spacing)
            .Enter())
        {

            using (paper.Box($"{id}_input")
                .Height(EditorTheme.RowHeight)
                .Width(UnitValue.Stretch())

                .Rounded(3)
                .BorderWidth(1)

                .BackgroundColor(EditorTheme.Neutral200)
                .BorderColor(EditorTheme.Neutral100)
                .Focused.BorderColor(EditorTheme.Purple400).End()

                .TabIndex(0)
                .Enter())
            {

                using (paper.Row(id)
                    .Height(EditorTheme.RowHeight)
                    .RowBetween(6)
                    .Margin(UnitValue.Auto)
                    .Enter())
                {

                    if (Font != null && !string.IsNullOrEmpty(label))
                    {
                        paper.Box($"{id}_lbl")
                            .Height(EditorTheme.RowHeight - 2)
                            .Width(EditorTheme.RowHeight - 2)
                            .Margin(1, UnitValue.Auto, 1, UnitValue.Auto)
                            .ChildLeft(4)
                            .Alignment(PaperUI.TextAlignment.MiddleCenter)
                            .Rounded(5)
                            .BackgroundColor(textColor.HasValue ? LerpRGB(EditorTheme.Neutral300, textColor.Value, 0.05f) : EditorTheme.Neutral400)
                            .Text(label, Font)
                            .TextColor(textColor ?? EditorTheme.Ink500).FontSize(FontSz)
                            .OnDragStart((dragEvent) =>
                            {
                                s_floatDragValue = value;
                                s_floatDragging = true;
                            })
                            .OnDragging((dragEvent)=>
                            {
                                if (!s_floatDragging) return;
                                userCallback?.Invoke(ProcessFloatDrag(dragEvent));
                            })
                            .OnDragEnd((dragEvent)=>
                            {
                                s_floatDragging = false;
                            });
                    }

                    var yOffset = (EditorTheme.RowHeight - FontSz) / 2.0f;

                    var settings = MakeNumericSettings(FloatFilter);
                    paper.Box($"{id}_tf")
                        .Margin(4, yOffset)
                        .HookToParent()
                        .IsNotInteractable()
                        .Width(UnitValue.Stretch())
                        .Height(EditorTheme.RowHeight)
                        .FontSize(FontSz)
                        .TextField(textVal, settings,
                            onChange: v =>
                            {
                                if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                                    userCallback?.Invoke(parsed);
                            },
                            intID: id.GetHashCode());
                }
            }
        }

        return new WidgetResult<float>(cb => userCallback = cb);
    }

    // IntField removed (use Origami.NumericField<int> wrapped in Inspector.InspectorRow.Draw).

    // Slider removed (use Origami.NumericField<float> with .Min/.Max).

    /// <summary>
    /// Section gated by an enable toggle, with no separate expand state.
    /// Body is drawn whenever <paramref name="enabled"/> is true.
    /// Useful for "feature flag"-style groups where collapsed-but-enabled doesn't make sense.
    /// </summary>
    public static void ToggleSection(Paper paper, string id, string label,
        bool enabled, Action<bool> setEnabled, Action drawContents)
    {
        Separator(paper, $"{id}_sep");

        Origami.Checkbox(paper, $"{id}_tog", enabled, v => setEnabled(v))
            .LabelRight(label).Show();

        if (enabled)
        {
            using (paper.Column($"{id}_body").Height(UnitValue.Auto).Enter())
            {
                drawContents();
            }
        }
    }

    // Dropdown removed (use Origami.Dropdown / Origami.EnumDropdown wrapped in InspectorRow.Draw).

    // ================================================================
    //  ToggleButton
    // ================================================================

    // ToggleButton removed (use Origami.Switch / Origami.Checkbox / Origami.Radio
    // depending on the semantic — most call sites preferred a Switch-with-LabelRight).

    // SearchBar removed (use Origami.SearchField).
    // EnumDropdown removed (use Origami.EnumDropdown).
    // IntSlider removed (use Origami.NumericField<int> with .Min/.Max).

    // ================================================================
    //  Vector2 Field
    // ================================================================

    public static WidgetResult<Prowl.Vector.Float2> Vector2Field(Paper paper, string id, string label, Prowl.Vector.Float2 value)
    {
        Action<Prowl.Vector.Float2>? userCallback = null;
        var current = value;

        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .RowBetween(4)
            .Margin(UnitValue.Auto, EditorTheme.Spacing)
            .Enter())
        {
            if (Font != null && !string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(LabelW).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Alignment(PaperUI.TextAlignment.MiddleLeft)
                    .Text(label, Font).TextColor(EditorTheme.Ink500).FontSize(FontSz);

            // X
            FloatFieldWithInternalLabel(paper, $"{id}_x", (float)current.X, "X", Color.FromArgb(255, 200, 80, 80))
                .OnValueChanged(v => { current.X = v; userCallback?.Invoke(current); });

            // Y
            FloatFieldWithInternalLabel(paper, $"{id}_y", (float)current.Y, "Y", Color.FromArgb(255, 80, 200, 80))
                .OnValueChanged(v => { current.Y = v; userCallback?.Invoke(current); });
        }

        return new WidgetResult<Prowl.Vector.Float2>(cb => userCallback = cb);
    }

    // ================================================================
    //  Vector3 Field
    // ================================================================

    public static WidgetResult<Prowl.Vector.Float3> Vector3Field(Paper paper, string id, string label, Prowl.Vector.Float3 value)
    {
        Action<Prowl.Vector.Float3>? userCallback = null;
        var current = value;

        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .RowBetween(4)
            .Margin(UnitValue.Auto, EditorTheme.Spacing)
            .Enter())
        {
            if (Font != null && !string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(LabelW).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Alignment(PaperUI.TextAlignment.MiddleLeft)
                    .Text(label, Font).TextColor(EditorTheme.Ink500).FontSize(FontSz);

            // X
            FloatFieldWithInternalLabel(paper, $"{id}_x", (float)current.X, "X", Color.FromArgb(255, 200, 80, 80))
                .OnValueChanged(v => { current.X = v; userCallback?.Invoke(current); });

            // Y
            FloatFieldWithInternalLabel(paper, $"{id}_y", (float)current.Y, "Y", Color.FromArgb(255, 80, 200, 80))
                .OnValueChanged(v => { current.Y = v; userCallback?.Invoke(current); });

            // Z
            FloatFieldWithInternalLabel(paper, $"{id}_z", (float)current.Z, "Z", Color.FromArgb(255, 80, 80, 200))
                .OnValueChanged(v => { current.Z = v; userCallback?.Invoke(current); });
        }

        return new WidgetResult<Prowl.Vector.Float3>(cb => userCallback = cb);
    }

    // ================================================================
    //  Vector4 Field
    // ================================================================

    public static WidgetResult<Prowl.Vector.Float4> Vector4Field(Paper paper, string id, string label, Prowl.Vector.Float4 value)
    {
        Action<Prowl.Vector.Float4>? userCallback = null;
        var current = value;

        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .RowBetween(4)
            .Margin(UnitValue.Auto, EditorTheme.Spacing)
            .Enter())
        {
            if (Font != null && !string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(LabelW).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Alignment(PaperUI.TextAlignment.MiddleLeft)
                    .Text(label, Font).TextColor(EditorTheme.Ink500).FontSize(FontSz);

            // X
            FloatFieldWithInternalLabel(paper, $"{id}_x", (float)current.X, "X", Color.FromArgb(255, 200, 80, 80))
                .OnValueChanged(v => { current.X = v; userCallback?.Invoke(current); });

            // Y
            FloatFieldWithInternalLabel(paper, $"{id}_y", (float)current.Y, "Y", Color.FromArgb(255, 80, 200, 80))
                .OnValueChanged(v => { current.Y = v; userCallback?.Invoke(current); });

            // Z
            FloatFieldWithInternalLabel(paper, $"{id}_z", (float)current.Z, "Z", Color.FromArgb(255, 80, 80, 200))
                .OnValueChanged(v => { current.Z = v; userCallback?.Invoke(current); });

            // W
            FloatFieldWithInternalLabel(paper, $"{id}_w", (float)current.W, "W")
                .OnValueChanged(v => { current.W = v; userCallback?.Invoke(current); });
        }

        return new WidgetResult<Prowl.Vector.Float4>(cb => userCallback = cb);
    }

    // ================================================================
    //  Color Field (basic swatch, no picker yet)
    // ================================================================

    public static WidgetResult<Prowl.Vector.Color> ColorField(Paper paper, string id, string label, Prowl.Vector.Color value)
    {
        Action<Prowl.Vector.Color>? userCallback = null;

        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .RowBetween(6)
            .Enter())
        {
            if (Font != null && !string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(LabelW)
                    .Height(EditorTheme.RowHeight)
                    .IsNotInteractable()
                    .Alignment(PaperUI.TextAlignment.MiddleLeft)
                    .Text(label, Font)
                    .TextColor(EditorTheme.Ink500)
                    .FontSize(FontSz);

            // Color swatch (FocusWithin-based popup using cached ancestor set)
            using (paper.Box($"{id}_swatch")
                .Height(EditorTheme.RowHeight)
                .Width(UnitValue.Stretch())
                .BackgroundColor(Color.FromArgb(
                    (int)(value.A * 255),
                    (int)(value.R * 255),
                    (int)(value.G * 255),
                    (int)(value.B * 255)))
                .Rounded(4)
                .BorderColor(EditorTheme.Ink200).BorderWidth(1)
                .Hovered.BorderColor(EditorTheme.Purple400).End()
                .Enter())
            {
                bool isOpen = paper.IsParentFocusWithin;

                // Hex display
                if (Font != null)
                {
                    int r = (int)(value.R * 255);
                    int g = (int)(value.G * 255);
                    int b = (int)(value.B * 255);
                    paper.Box($"{id}_hex")
                        .Width(UnitValue.Stretch())
                        .Height(EditorTheme.RowHeight)
                        .Margin(EditorTheme.RowHeight / 4, 0)
                        .IsNotInteractable()
                        .Alignment(PaperUI.TextAlignment.MiddleLeft)
                        .Text($"#{r:X2}{g:X2}{b:X2}", Font)
                        .TextColor(EditorTheme.Ink500)
                        .FontSize(FontSz - 1);
                }

                if (isOpen)
                {
                    using (paper.Box($"{id}_picker_wrap")
                        .PositionType(PositionType.SelfDirected)
                        .Position(0, EditorTheme.RowHeight + 2)
                        .Width(UnitValue.Auto).Height(UnitValue.Auto)
                        .Enter())
                    {
                        ColorPicker.Draw(paper, $"{id}_picker", value, c => userCallback?.Invoke(c));
                    }
                }
            }
        }

        return new WidgetResult<Prowl.Vector.Color>(cb => userCallback = cb);
    }

    // ================================================================
    //  Progress Bar
    // ================================================================
    public static void ProgressBar(Paper paper, string id, string label, float progress, float? height = null)
    {
        progress = Math.Clamp(progress, 0, 1);

        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .RowBetween(6)
            .Margin(UnitValue.Auto, EditorTheme.Spacing)
            .Enter())
        {
            if (Font != null && !string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(LabelW).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Alignment(PaperUI.TextAlignment.MiddleLeft)
                    .IsNotInteractable()
                    .Text(label, Font).TextColor(EditorTheme.Ink500).FontSize(FontSz);

            paper.Box($"{id}_track")
                .Height(EditorTheme.RowHeight)
                .Width(UnitValue.Stretch())
                .IsNotInteractable()
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                {
                    float rx = (float)r.Min.X;
                    float ry = (float)r.Min.Y;
                    float rw = (float)r.Size.X;
                    float rh = (float)r.Size.Y;

                    float trackH = height ?? 4f;
                    float trackY = ry + rh * 0.5f - trackH * 0.5f;
                    float trackR = trackH * 0.5f;

                    // ── Track background ──────────────────────────────────
                    canvas.RoundedRect(rx, trackY, rw, trackH, trackR, trackR, trackR, trackR);
                    canvas.SetFillColor(EditorTheme.Ink100);
                    canvas.Fill();

                    // ── Track fill ────────────────────────────────────────
                    if (progress > 0f)
                    {
                        canvas.RoundedRect(rx, trackY, rw * progress, trackH, trackR, trackR, trackR, trackR);
                        canvas.SetFillColor(EditorTheme.Purple400);
                        canvas.Fill();
                    }
                }));

            if (Font != null)
                paper.Box($"{id}_pct")
                    .Width(40).Height(EditorTheme.RowHeight)
                    .IsNotInteractable()
                    .Alignment(PaperUI.TextAlignment.MiddleCenter)
                    .Text($"{(int)(progress * 100)}%", Font)
                    .Alignment(PaperUI.TextAlignment.MiddleLeft)
                    .TextColor(EditorTheme.Ink500).FontSize(FontSz);
        }
    }

    // ================================================================
    //  Context Menu
    // ================================================================

    public static void ContextMenu(Paper paper, string id, Action<ContextMenuBuilder> build)
    {
        // Show on right-click of the parent element
        if (paper.IsParentActive)
        {
            // Check for right-click via Paper
            // For now, context menus are triggered by the caller
        }
    }
}
