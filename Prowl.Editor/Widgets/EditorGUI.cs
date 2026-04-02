using System;
using System.Globalization;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Scribe;

using Color = System.Drawing.Color;

namespace Prowl.Editor.Widgets;

/// <summary>
/// Core editor widget library. Immediate-mode drawing, callback-based state updates.
/// Each widget draws itself and returns a WidgetResult for chaining OnValueChanged callbacks.
/// State is managed via Paper element storage — values persist across frames automatically.
/// </summary>
public static class EditorGUI
{
    private static FontFile? Font => EditorTheme.DefaultFont;
    private static float FontSz => EditorTheme.FontSize;
    private static float LabelW => EditorTheme.LabelWidth;

    // Shared callback storage: widgets register their callbacks here,
    // Paper's end-of-frame events invoke them.
    // We use a simple list that's drained each frame.
    private static readonly System.Collections.Generic.List<Action> _pendingCallbacks = new();

    /// <summary>Call at end of frame to flush any pending widget callbacks. Not needed
    /// if widgets use Paper's own OnClick/OnDrag callbacks (which fire automatically).</summary>
    public static void FlushCallbacks()
    {
        foreach (var cb in _pendingCallbacks) cb();
        _pendingCallbacks.Clear();
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
            .TextColor(color ?? EditorTheme.Text)
            .FontSize(FontSz);
    }

    // ================================================================
    //  Header
    // ================================================================

    public static void Header(Paper paper, string id, string text)
    {
        if (Font == null) return;
        paper.Box(id)
            .Height(EditorTheme.RowHeight + 4)
            .ChildLeft(4)
            .Margin(0, 10, 0, 2)
            .Text(text, Font)
            .TextColor(EditorTheme.Text)
            .FontSize(FontSz + 2);
    }

    // ================================================================
    //  Separator
    // ================================================================

    public static void Separator(Paper paper, string id)
    {
        paper.Box(id)
            .Height(1)
            .Margin(0, 6, 0, 6)
            .BackgroundColor(EditorTheme.Border);
    }

    // ================================================================
    //  Button
    // ================================================================

    public static WidgetResult<bool> Button(Paper paper, string id, string label, float width = 0)
    {
        Action<bool>? userCallback = null;

        var el = paper.Box(id)
            .Height(EditorTheme.RowHeight)
            .ChildLeft(12).ChildRight(12)
            .BackgroundColor(EditorTheme.ButtonNormal)
            .Hovered.BackgroundColor(EditorTheme.ButtonHovered).End()
            .Active.BackgroundColor(EditorTheme.ButtonActive).End()
            .Rounded(3)
            .BorderColor(EditorTheme.Border).BorderWidth(1)
            .OnClick(e => userCallback?.Invoke(true));

        if (width > 0) el.Width(width);
        if (Font != null) el.Text(label, Font).TextColor(EditorTheme.Text).FontSize(FontSz);

        return new WidgetResult<bool>(cb => userCallback = cb);
    }

    // ================================================================
    //  Toggle
    // ================================================================

    public static WidgetResult<bool> Toggle(Paper paper, string id, string label, bool value)
    {
        Action<bool>? userCallback = null;

        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .RowBetween(6)
            .OnClick(e => userCallback?.Invoke(!value))
            .Enter())
        {
            var box = paper.Box($"{id}_box")
                .Size(16, 16)
                .BackgroundColor(value ? EditorTheme.Accent : EditorTheme.InputBackground)
                .Hovered.BackgroundColor(value ? EditorTheme.AccentDim : EditorTheme.ButtonHovered).End()
                .Rounded(3)
                .BorderColor(EditorTheme.Border).BorderWidth(1);

            if (value && Font != null)
                box.Text("\u2713", Font).TextColor(EditorTheme.Text).FontSize(12f);

            if (Font != null)
                paper.Box($"{id}_lbl")
                    .Text(label, Font).TextColor(EditorTheme.Text).FontSize(FontSz);
        }

        return new WidgetResult<bool>(cb => userCallback = cb);
    }

    // ================================================================
    //  TextField
    // ================================================================

    public static WidgetResult<string> TextField(Paper paper, string id, string label, string value)
    {
        Action<string>? userCallback = null;

        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .RowBetween(6)
            .Enter())
        {
            if (Font != null && !string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(LabelW).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(label, Font).TextColor(EditorTheme.Text).FontSize(FontSz);

            using (paper.Box($"{id}_input")
                .Height(EditorTheme.RowHeight)
                .Width(UnitValue.Stretch())
                .BackgroundColor(EditorTheme.InputBackground)
                .Rounded(3)
                .BorderColor(EditorTheme.Border).BorderWidth(1)
                .Focused.BorderColor(EditorTheme.Accent).End()
                .TabIndex(0)
                .Enter())
            {
                paper.Box($"{id}_tf")
                    .Margin(4, UnitValue.Stretch())
                    .HookToParent()
                    .IsNotInteractable()
                    .Width(UnitValue.Stretch())
                    .Height(EditorTheme.RowHeight)
                    .FontSize(FontSz)
                    .TextField(value, Font!,
                        onChange: v => userCallback?.Invoke(v),
                        textColor: EditorTheme.Text,
                        placeholder: "",
                        placeholderColor: EditorTheme.TextDisabled,
                        intID: id.GetHashCode());
            }
        }

        return new WidgetResult<string>(cb => userCallback = cb);
    }

    // ================================================================
    //  FloatField
    // ================================================================

    public static WidgetResult<float> FloatField(Paper paper, string id, string label, float value)
    {
        Action<float>? userCallback = null;
        string textVal = value.ToString("G", CultureInfo.InvariantCulture);

        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .RowBetween(6)
            .Enter())
        {
            if (Font != null && !string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(LabelW).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(label, Font).TextColor(EditorTheme.Text).FontSize(FontSz);

            using (paper.Box($"{id}_input")
                .Height(EditorTheme.RowHeight)
                .Width(UnitValue.Stretch())
                .BackgroundColor(EditorTheme.InputBackground)
                .Rounded(3)
                .BorderColor(EditorTheme.Border).BorderWidth(1)
                .Focused.BorderColor(EditorTheme.Accent).End()
                .TabIndex(0)
                .Enter())
            {
                paper.Box($"{id}_tf")
                    .Margin(4, UnitValue.Stretch())
                    .HookToParent()
                    .IsNotInteractable()
                    .Width(UnitValue.Stretch())
                    .Height(EditorTheme.RowHeight)
                    .FontSize(FontSz)
                    .TextField(textVal, Font!,
                        onChange: v =>
                        {
                            if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                                userCallback?.Invoke(parsed);
                        },
                        textColor: EditorTheme.Text,
                        placeholder: "0",
                        placeholderColor: EditorTheme.TextDisabled,
                        intID: id.GetHashCode());
            }
        }

        return new WidgetResult<float>(cb => userCallback = cb);
    }

    // ================================================================
    //  IntField
    // ================================================================

    public static WidgetResult<int> IntField(Paper paper, string id, string label, int value)
    {
        Action<int>? userCallback = null;
        string textVal = value.ToString(CultureInfo.InvariantCulture);

        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .RowBetween(6)
            .Enter())
        {
            if (Font != null && !string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(LabelW).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(label, Font).TextColor(EditorTheme.Text).FontSize(FontSz);

            using (paper.Box($"{id}_input")
                .Height(EditorTheme.RowHeight)
                .Width(UnitValue.Stretch())
                .BackgroundColor(EditorTheme.InputBackground)
                .Rounded(3)
                .BorderColor(EditorTheme.Border).BorderWidth(1)
                .Focused.BorderColor(EditorTheme.Accent).End()
                .TabIndex(0)
                .Enter())
            {
                paper.Box($"{id}_tf")
                    .Margin(4, UnitValue.Stretch())
                    .HookToParent()
                    .IsNotInteractable()
                    .Width(UnitValue.Stretch())
                    .Height(EditorTheme.RowHeight)
                    .FontSize(FontSz)
                    .TextField(textVal, Font!,
                        onChange: v =>
                        {
                            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                                userCallback?.Invoke(parsed);
                        },
                        textColor: EditorTheme.Text,
                        placeholder: "0",
                        placeholderColor: EditorTheme.TextDisabled,
                        intID: id.GetHashCode());
            }
        }

        return new WidgetResult<int>(cb => userCallback = cb);
    }

    // ================================================================
    //  Slider
    // ================================================================

    public static WidgetResult<float> Slider(Paper paper, string id, string label, float value, float min, float max)
    {
        Action<float>? userCallback = null;
        float t = (max > min) ? Math.Clamp((value - min) / (max - min), 0, 1) : 0;

        void SetFromEvent(PaperUI.Events.ElementEvent e)
        {
            float v = min + Math.Clamp((float)e.NormalizedPosition.X, 0, 1) * (max - min);
            userCallback?.Invoke(v);
        }

        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .RowBetween(6)
            .Enter())
        {
            if (Font != null && !string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(LabelW).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(label, Font).TextColor(EditorTheme.Text).FontSize(FontSz);

            // Track with fill drawn via OnPostLayout
            paper.Box($"{id}_track")
                .Height(EditorTheme.RowHeight)
                .Width(UnitValue.Stretch())
                .BackgroundColor(EditorTheme.InputBackground)
                .Rounded(3)
                .BorderColor(EditorTheme.Border).BorderWidth(1)
                .OnClick(e => SetFromEvent(e))
                .OnDragStart(e => SetFromEvent(e))
                .OnDragging(e => SetFromEvent(e))
                .OnPostLayout((handle, rect) =>
                {
                    paper.AddActionElement(ref handle, (canvas, r) =>
                    {
                        if (t > 0)
                        {
                            float fillW = (float)(r.Size.X * t);
                            canvas.RoundedRectFilled(
                                (float)r.Min.X, (float)r.Min.Y,
                                fillW, (float)r.Size.Y,
                                3, 0, 0, 3,
                                new Prowl.Vector.Color(51/255f, 122/255f, 183/255f, 1f));
                        }
                    });
                });

            if (Font != null)
                paper.Box($"{id}_val")
                    .Width(50).Height(EditorTheme.RowHeight)
                    .Text(value.ToString("F2"), Font)
                    .TextColor(EditorTheme.Text).FontSize(FontSz);
        }

        return new WidgetResult<float>(cb => userCallback = cb);
    }

    // ================================================================
    //  Foldout (header only — content is drawn by the caller after this)
    // ================================================================

    public static WidgetResult<bool> Foldout(Paper paper, string id, string label, bool expanded)
    {
        Action<bool>? userCallback = null;

        // Just the clickable header bar — fixed height
        var el = paper.Box($"{id}_header")
            .Height(EditorTheme.RowHeight)
            .ChildLeft(4)
            .BackgroundColor(EditorTheme.HeaderBackground)
            .Hovered.BackgroundColor(EditorTheme.ButtonHovered).End()
            .Rounded(2)
            .OnClick(e => userCallback?.Invoke(!expanded));

        if (Font != null)
        {
            string arrow = expanded ? "\u25BC " : "\u25B6 ";
            el.Text(arrow + label, Font).TextColor(EditorTheme.Text).FontSize(FontSz);
        }

        return new WidgetResult<bool>(cb => userCallback = cb);
    }

    // ================================================================
    //  Dropdown
    // ================================================================

    public static WidgetResult<int> Dropdown(Paper paper, string id, string label, int selectedIndex, string[] options)
    {
        Action<int>? userCallback = null;
        string displayText = (selectedIndex >= 0 && selectedIndex < options.Length) ? options[selectedIndex] : "\u2014";

        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .RowBetween(6)
            .Enter())
        {
            if (Font != null && !string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(LabelW).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(label, Font).TextColor(EditorTheme.Text).FontSize(FontSz);

            PaperUI.LayoutEngine.ElementHandle btnHandle = default;

            using (paper.Box($"{id}_btn")
                .Height(EditorTheme.RowHeight)
                .Width(UnitValue.Stretch())
                .BackgroundColor(EditorTheme.InputBackground)
                .Rounded(3)
                .BorderColor(EditorTheme.Border).BorderWidth(1)
                .Hovered.BorderColor(EditorTheme.Accent).End()
                .ChildLeft(6).ChildRight(6)
                .OnClick(e =>
                {
                    bool cur = paper.GetElementStorage(btnHandle, "open", false);
                    paper.SetElementStorage(btnHandle, "open", !cur);
                })
                .Enter())
            {
                btnHandle = paper.CurrentParent;
                bool isOpen = paper.GetElementStorage(btnHandle, "open", false);

                // Close when mouse leaves the whole area (button + popup)
                if (isOpen && !paper.IsParentHovered)
                    paper.SetElementStorage(btnHandle, "open", false);

                if (Font != null)
                {
                    paper.Box($"{id}_txt")
                        .Width(UnitValue.Stretch())
                        .IsNotInteractable()
                        .Text(displayText, Font).TextColor(EditorTheme.Text).FontSize(FontSz);
                    paper.Box($"{id}_arr")
                        .Width(16)
                        .IsNotInteractable()
                        .Text(isOpen ? "\u25B2" : "\u25BC", Font).TextColor(EditorTheme.TextDim).FontSize(10f);
                }

                if (isOpen)
                {
                    using (paper.Column($"{id}_popup")
                        .PositionType(PositionType.SelfDirected)
                        .Position(0, EditorTheme.RowHeight - 1)
                        .Width(UnitValue.Stretch())
                        .Height(UnitValue.Auto)
                        .BackgroundColor(EditorTheme.PanelBackground)
                        .BorderColor(EditorTheme.Border).BorderWidth(1)
                        .Rounded(4)
                        .ChildTop(2).ChildBottom(2).ChildLeft(2).ChildRight(2)
                        .HookToParent()
                        .Layer(Layer.Topmost)
                        .Enter())
                    {
                        for (int i = 0; i < options.Length; i++)
                        {
                            int idx = i;
                            bool isSel = i == selectedIndex;

                            var opt = paper.Box($"{id}_o_{i}")
                                .Height(EditorTheme.RowHeight)
                                .ChildLeft(6)
                                .BackgroundColor(isSel ? EditorTheme.AccentDim : Color.Transparent)
                                .Hovered.BackgroundColor(EditorTheme.Accent).End()
                                .Rounded(3)
                                .HookToParent()
                                .OnClick(e =>
                                {
                                    userCallback?.Invoke(idx);
                                    paper.SetElementStorage(btnHandle, "open", false);
                                });

                            if (Font != null)
                                opt.Text(options[i], Font).TextColor(EditorTheme.Text).FontSize(FontSz);
                        }
                    }
                }
            }
        }

        return new WidgetResult<int>(cb => userCallback = cb);
    }

    // ================================================================
    //  ToggleButton
    // ================================================================

    public static WidgetResult<bool> ToggleButton(Paper paper, string id, string label, bool value)
    {
        Action<bool>? userCallback = null;

        var el = paper.Box(id)
            .Height(EditorTheme.RowHeight)
            .ChildLeft(10).ChildRight(10)
            .BackgroundColor(value ? EditorTheme.Accent : EditorTheme.ButtonNormal)
            .Hovered.BackgroundColor(value ? EditorTheme.AccentDim : EditorTheme.ButtonHovered).End()
            .Active.BackgroundColor(EditorTheme.ButtonActive).End()
            .Rounded(3)
            .BorderColor(EditorTheme.Border).BorderWidth(1)
            .OnClick(e => userCallback?.Invoke(!value));

        if (Font != null) el.Text(label, Font).TextColor(EditorTheme.Text).FontSize(FontSz);

        return new WidgetResult<bool>(cb => userCallback = cb);
    }

    // ================================================================
    //  SearchBar
    // ================================================================

    public static WidgetResult<string> SearchBar(Paper paper, string id, string value, string placeholder = "Search...")
    {
        Action<string>? userCallback = null;

        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .BackgroundColor(EditorTheme.InputBackground)
            .Rounded(3)
            .BorderColor(EditorTheme.Border).BorderWidth(1)
            .Focused.BorderColor(EditorTheme.Accent).End()
            .ChildLeft(6).ChildRight(4)
            .RowBetween(4)
            .TabIndex(0)
            .Enter())
        {
            if (Font != null)
                paper.Box($"{id}_icon")
                    .Width(16)
                    .Text("\u2315", Font).TextColor(EditorTheme.TextDim).FontSize(FontSz);

            paper.Box($"{id}_tf")
                .Height(EditorTheme.RowHeight)
                .Width(UnitValue.Stretch())
                .HookToParent()
                .IsNotInteractable()
                .FontSize(FontSz)
                .TextField(value, Font!,
                    onChange: v => userCallback?.Invoke(v),
                    textColor: EditorTheme.Text,
                    placeholder: placeholder,
                    placeholderColor: EditorTheme.TextDisabled,
                    intID: id.GetHashCode());

            if (!string.IsNullOrEmpty(value))
            {
                var clearBtn = paper.Box($"{id}_clear")
                    .Size(16, 16).Rounded(8)
                    .Hovered.BackgroundColor(EditorTheme.ButtonHovered).End()
                    .OnClick(e => userCallback?.Invoke(""));

                if (Font != null)
                    clearBtn.Text("\u2715", Font).TextColor(EditorTheme.TextDim).FontSize(10f);
            }
        }

        return new WidgetResult<string>(cb => userCallback = cb);
    }

    // ================================================================
    //  Enum Dropdown (generic)
    // ================================================================

    public static WidgetResult<T> EnumDropdown<T>(Paper paper, string id, string label, T value) where T : struct, Enum
    {
        Action<T>? userCallback = null;
        var names = Enum.GetNames<T>();
        var values = Enum.GetValues<T>();
        int selectedIndex = Array.IndexOf(values, value);

        Dropdown(paper, id, label, selectedIndex, names)
            .OnValueChanged(idx =>
            {
                if (idx >= 0 && idx < values.Length)
                    userCallback?.Invoke(values[idx]);
            });

        return new WidgetResult<T>(cb => userCallback = cb);
    }

    // ================================================================
    //  IntSlider
    // ================================================================

    public static WidgetResult<int> IntSlider(Paper paper, string id, string label, int value, int min, int max)
    {
        Action<int>? userCallback = null;

        Slider(paper, id, label, value, min, max)
            .OnValueChanged(v => userCallback?.Invoke((int)MathF.Round(v)));

        return new WidgetResult<int>(cb => userCallback = cb);
    }

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
            .Enter())
        {
            if (Font != null && !string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(LabelW).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(label, Font).TextColor(EditorTheme.Text).FontSize(FontSz);

            // X
            if (Font != null)
                paper.Box($"{id}_xl")
                    .Width(14).Height(EditorTheme.RowHeight)
                    .Text("X", Font).TextColor(Color.FromArgb(255, 200, 80, 80)).FontSize(FontSz);
            FloatField(paper, $"{id}_x", "", (float)current.X)
                .OnValueChanged(v => { current.X = v; userCallback?.Invoke(current); });

            // Y
            if (Font != null)
                paper.Box($"{id}_yl")
                    .Width(14).Height(EditorTheme.RowHeight)
                    .Text("Y", Font).TextColor(Color.FromArgb(255, 80, 200, 80)).FontSize(FontSz);
            FloatField(paper, $"{id}_y", "", (float)current.Y)
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
            .Enter())
        {
            if (Font != null && !string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(LabelW).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(label, Font).TextColor(EditorTheme.Text).FontSize(FontSz);

            // X
            if (Font != null)
                paper.Box($"{id}_xl")
                    .Width(14).Height(EditorTheme.RowHeight)
                    .Text("X", Font).TextColor(Color.FromArgb(255, 200, 80, 80)).FontSize(FontSz);
            FloatField(paper, $"{id}_x", "", (float)current.X)
                .OnValueChanged(v => { current.X = v; userCallback?.Invoke(current); });

            // Y
            if (Font != null)
                paper.Box($"{id}_yl")
                    .Width(14).Height(EditorTheme.RowHeight)
                    .Text("Y", Font).TextColor(Color.FromArgb(255, 80, 200, 80)).FontSize(FontSz);
            FloatField(paper, $"{id}_y", "", (float)current.Y)
                .OnValueChanged(v => { current.Y = v; userCallback?.Invoke(current); });

            // Z
            if (Font != null)
                paper.Box($"{id}_zl")
                    .Width(14).Height(EditorTheme.RowHeight)
                    .Text("Z", Font).TextColor(Color.FromArgb(255, 80, 80, 200)).FontSize(FontSz);
            FloatField(paper, $"{id}_z", "", (float)current.Z)
                .OnValueChanged(v => { current.Z = v; userCallback?.Invoke(current); });
        }

        return new WidgetResult<Prowl.Vector.Float3>(cb => userCallback = cb);
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
                    .Width(LabelW).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .IsNotInteractable()
                    .Text(label, Font).TextColor(EditorTheme.Text).FontSize(FontSz);

            // Color swatch (FocusWithin-based popup using cached ancestor set)
            using (paper.Box($"{id}_swatch")
                .Height(EditorTheme.RowHeight)
                .Width(UnitValue.Stretch())
                .BackgroundColor(Color.FromArgb(
                    (int)(value.A * 255),
                    (int)(value.R * 255),
                    (int)(value.G * 255),
                    (int)(value.B * 255)))
                .Rounded(3)
                .BorderColor(EditorTheme.Border).BorderWidth(1)
                .Hovered.BorderColor(EditorTheme.Accent).End()
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
                        .Width(UnitValue.Stretch()).Height(EditorTheme.RowHeight)
                        .ChildLeft(6)
                        .IsNotInteractable()
                        .Text($"#{r:X2}{g:X2}{b:X2}", Font)
                        .TextColor(EditorTheme.Text).FontSize(FontSz - 1);
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

    public static void ProgressBar(Paper paper, string id, string label, float progress)
    {
        progress = Math.Clamp(progress, 0, 1);

        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .RowBetween(6)
            .Enter())
        {
            if (Font != null && !string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(LabelW).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(label, Font).TextColor(EditorTheme.Text).FontSize(FontSz);

            // Track
            paper.Box($"{id}_track")
                .Height(EditorTheme.RowHeight)
                .Width(UnitValue.Stretch())
                .BackgroundColor(EditorTheme.InputBackground)
                .Rounded(3)
                .BorderColor(EditorTheme.Border).BorderWidth(1)
                .OnPostLayout((handle, rect) =>
                {
                    paper.AddActionElement(ref handle, (canvas, r) =>
                    {
                        if (progress > 0)
                        {
                            float fillW = (float)(r.Size.X * progress);
                            canvas.RoundedRectFilled(
                                (float)r.Min.X, (float)r.Min.Y,
                                fillW, (float)r.Size.Y,
                                3, 0, 0, 3,
                                new Prowl.Vector.Color(51/255f, 122/255f, 183/255f, 1f));
                        }
                    });
                });

            if (Font != null)
                paper.Box($"{id}_pct")
                    .Width(40).Height(EditorTheme.RowHeight)
                    .Text($"{(int)(progress * 100)}%", Font)
                    .TextColor(EditorTheme.Text).FontSize(FontSz);
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
