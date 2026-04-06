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

    // Char filters for numeric inputs
    private static readonly Func<char, string, bool> IntFilter = (c, current) =>
        char.IsDigit(c) || (c == '-' && current.Length == 0);

    private static readonly Func<char, string, bool> FloatFilter = (c, current) =>
        char.IsDigit(c) || (c == '-' && current.Length == 0) || (c == '.' && !current.Contains('.'));

    private static ElementBuilder.TextInputSettings MakeNumericSettings(Func<char, string, bool> filter)
    {
        var s = ElementBuilder.TextInputSettings.Default;
        if (Font != null) s.Font = Font;
        s.TextColor = EditorTheme.Text;
        s.Placeholder = "0";
        s.PlaceholderColor = EditorTheme.TextDisabled;
        s.CharFilter = filter;
        return s;
    }

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
            .Margin(8, 10, 0, 2)
            .Text(text, Font)
            .TextColor(EditorTheme.Text)
            .FontSize(FontSz + 2);
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

    // ================================================================
    //  Button
    // ================================================================

    public static WidgetResult<bool> Button(Paper paper, string id, string label, float width = 0)
    {
        Action<bool>? userCallback = null;

        var el = paper.Box(id)
            .Height(EditorTheme.RowHeight)
            .ChildLeft(EditorTheme.RowHeight/4).ChildRight(EditorTheme.RowHeight/4)
            .BackgroundColor(EditorTheme.ButtonNormal)
            .Hovered.BackgroundColor(EditorTheme.ButtonHovered).End()
            .Active.BackgroundColor(EditorTheme.ButtonActive).End()
            .Rounded(3)
            .BorderColor(EditorTheme.Border).BorderWidth(1)
            .OnClick(e => userCallback?.Invoke(true));

        if (width > 0) el.Width(width);
        using (el.Enter())
        {
            if (Font != null) 
                paper.Box($"{id}_label")
                    .Alignment(PaperUI.TextAlignment.MiddleLeft)
                    .Text(label, Font)
                    .TextColor(EditorTheme.Text)
                    .FontSize(FontSz);
        }

        return new WidgetResult<bool>(cb => userCallback = cb);
    }

    public static WidgetResult<bool> ButtonSquare(Paper paper, string id, string icon)
    {
        Action<bool>? userCallback = null;

        paper.Box(id)
            .Alignment(PaperUI.TextAlignment.MiddleCenter)
            .Text(icon, Font)
            .TextColor(EditorTheme.Text)
            .FontSize(FontSz)
            .Height(EditorTheme.RowHeight)
            .Width(EditorTheme.RowHeight)
            .BackgroundColor(EditorTheme.ButtonNormal)
            .Hovered.BackgroundColor(EditorTheme.ButtonHovered).End()
            .Active.BackgroundColor(EditorTheme.ButtonActive).End()
            .Rounded(3)
            .BorderColor(EditorTheme.Border).BorderWidth(1)
            .OnClick(e => userCallback?.Invoke(true));

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
            .Width(UnitValue.Auto)
            .RowBetween(6)
            .OnClick(e => userCallback?.Invoke(!value))
            .Enter())
        {
            var box = paper.Box($"{id}_box")
                .Size(16, 16)
                .BackgroundColor(value ? EditorTheme.Accent : EditorTheme.InputBackground)
                .Hovered.BackgroundColor(value ? EditorTheme.AccentDim : EditorTheme.ButtonHovered).End()
                .Rounded(3)
                .Alignment(PaperUI.TextAlignment.MiddleCenter)
                .BorderColor(EditorTheme.Border).BorderWidth(1);

            if (value && Font != null)
                box.Text(EditorIcons.Check, Font).TextColor(EditorTheme.Text).FontSize(12f);

            if (Font != null)
                paper.Box($"{id}_lbl")
                    .Width(UnitValue.Auto)
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

                .Rounded(3)
                .BorderWidth(1)
                
                .BackgroundColor(EditorTheme.Neutral200)
                .BorderColor(EditorTheme.Neutral100)
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

    public static WidgetResult<float> FloatField(Paper paper, string id, float value, string label = "", Vector.Color? textColor = null)
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
                    .Width(UnitValue.Auto).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(label, Font).TextColor(textColor ?? EditorTheme.Text).FontSize(FontSz);

            using (paper.Box($"{id}_input")
                .Height(EditorTheme.RowHeight)
                .Width(UnitValue.Stretch())

                .Rounded(3)
                .BorderWidth(1)
                
                .BackgroundColor(EditorTheme.Neutral200)
                .BorderColor(EditorTheme.Neutral100)
                .Focused.BorderColor(EditorTheme.Accent).End()

                .TabIndex(0)
                .Enter())
            {
                var settings = MakeNumericSettings(FloatFilter);
                paper.Box($"{id}_tf")
                    .Margin(4, UnitValue.Stretch())
                    .HookToParent()
                    .IsNotInteractable()
                    .Width(UnitValue.Stretch())
                    .Height(EditorTheme.RowHeight)
                    .FontSize(FontSz)
                    .Alignment(PaperUI.TextAlignment.MiddleLeft)
                    .TextField(textVal, settings,
                        onChange: v =>
                        {
                            if (float.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                                userCallback?.Invoke(parsed);
                        },
                        intID: id.GetHashCode());
            }
        }

        return new WidgetResult<float>(cb => userCallback = cb);
    }

    // ================================================================
    //  IntField
    // ================================================================

    public static WidgetResult<int> IntField(Paper paper, string id, int value, string label, Vector.Color? textColor = null)
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
                    .Width(UnitValue.Auto).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(label, Font).TextColor(textColor ?? EditorTheme.Text).FontSize(FontSz);


            using (paper.Box($"{id}_input")
                .Height(EditorTheme.RowHeight)
                .Width(UnitValue.Stretch())

                .Rounded(3)
                .BorderWidth(1)
                
                .BackgroundColor(EditorTheme.Neutral200)
                .BorderColor(EditorTheme.Neutral100)
                .Focused.BorderColor(EditorTheme.Accent).End()
                
                .TabIndex(0)
                .Enter())
            {
                var settings = MakeNumericSettings(IntFilter);
                paper.Box($"{id}_tf")
                    .Margin(4, UnitValue.Stretch())
                    .HookToParent()
                    .IsNotInteractable()
                    .Width(UnitValue.Stretch())
                    .Height(EditorTheme.RowHeight)
                    .FontSize(FontSz)
                    .Alignment(PaperUI.TextAlignment.MiddleLeft)
                    .TextField(textVal, settings,
                        onChange: v =>
                        {
                            if (int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                                userCallback?.Invoke(parsed);
                        },
                        intID: id.GetHashCode());
            }
        }

        return new WidgetResult<int>(cb => userCallback = cb);
    }

    // ================================================================
    //  Slider
    // ================================================================
    public static WidgetResult<float> Slider(Paper paper, string id, string label, float value, float min, float max, bool showField = true)
    {
        Action<float>? userCallback = null;
        float t = (max > min) ? Math.Clamp((value - min) / (max - min), 0, 1) : 0;

        using (paper.Row(id)
            .Height(EditorTheme.RowHeight)
            .RowBetween(6)
            .Enter())
        {
            if (Font != null && !string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(LabelW).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .IsNotInteractable()
                    .Text(label, Font)
                    .TextColor(EditorTheme.Text).FontSize(FontSz);

            paper.Box($"{id}_track")
                .Height(EditorTheme.RowHeight)
                .Width(UnitValue.Stretch(4f))
                .OnClick(e =>
                {
                    float v = min + Math.Clamp((float)e.NormalizedPosition.X, 0f, 1f) * (max - min);
                    userCallback?.Invoke(v);
                })
                .OnDragStart(e =>
                {
                    float v = min + Math.Clamp((float)e.NormalizedPosition.X, 0f, 1f) * (max - min);
                    userCallback?.Invoke(v);
                })
                .OnDragging(e =>
                {
                    float v = min + Math.Clamp((float)e.NormalizedPosition.X, 0f, 1f) * (max - min);
                    userCallback?.Invoke(v);
                })
                .OnPostLayout((handle, rect) => paper.Draw(ref handle, (canvas, r) =>
                {
                    float rx = (float)r.Min.X;
                    float ry = (float)r.Min.Y;
                    float rw = (float)r.Size.X;
                    float rh = (float)r.Size.Y;

                    float trackH = 4f;
                    float trackY = ry + rh * 0.5f - trackH * 0.5f;
                    float trackR = trackH * 0.5f;
                    float thumbCx = rx + rw * t;
                    float thumbCy = ry + rh * 0.5f;
                    float thumbR = rh * 0.36f;

                    // ── Track background ──────────────────────────────────
                    canvas.RoundedRectFilled(rx, trackY, rw, trackH, trackR, trackR, trackR, trackR,
                        EditorTheme.ButtonNormal);

                    // ── Track fill ────────────────────────────────────────
                    if (t > 0f)
                    {
                        canvas.RoundedRectFilled(rx, trackY, rw * t, trackH, trackR, trackR, trackR, trackR,
                            EditorTheme.Accent);
                    }

                    // ── Thumb body ────────────────────────────────────────
                    canvas.SetFillColor(EditorTheme.Text);
                    canvas.BeginPath();
                    canvas.Circle(thumbCx, thumbCy, thumbR, 24);
                    canvas.Fill();
                }));

            if (showField)
            {
                FloatField(paper, $"{id}_val", value)
                    .OnValueChanged(v => userCallback?.Invoke(Math.Clamp(v, min, max)));
            }
        }

        return new WidgetResult<float>(cb => userCallback = cb);
    }

    // ================================================================
    //  Foldout (self-contained state + content callback)
    // ================================================================

    public static void Foldout(Paper paper, string id, string label, Action drawContents, bool defaultValue = true)
    {
        // Header button — storage lives on the header element itself
        var header = paper
            .Row($"{id}_header")
            .Height(EditorTheme.RowHeight)
            .ChildLeft(4)
            .BackgroundColor(EditorTheme.HeaderBackground)
            .Hovered.BackgroundColor(EditorTheme.ButtonHovered).End()
            .Rounded(2);

        bool expanded = paper.GetElementStorage(header._handle, "exp", defaultValue);

        header.OnClick(e => paper.SetElementStorage(header._handle, "exp", !expanded));

        if (Font != null)
        {
            using (header.Enter())
            {
                if (expanded)
                {
                    paper.Box("{id}_arrow")
                        .Margin(EditorTheme.RowHeight / 4, 0, EditorTheme.RowHeight / 8, 0)
                        .Width(16)
                        .MaxWidth(16)
                        // .Text("\u25BC", Font)
                        .Text(EditorIcons.ChevronDown, Font)
                        .TextColor(EditorTheme.TextDim)
                        .FontSize(FontSz * 0.7f);
                }
                else
                {
                    paper.Box("{id}_arrow")
                        .Margin(EditorTheme.RowHeight / 3, (EditorTheme.RowHeight / 4) - (EditorTheme.RowHeight / 3), EditorTheme.RowHeight / 8, 0)
                        .Width(16)
                        .MaxWidth(16)
                        .Text(EditorIcons.ChevronRight, Font)
                        .TextColor(EditorTheme.TextDim)
                        .FontSize(FontSz * 0.7f);
                }
                paper.Box($"{id}_header_lbl")
                    .Text(label, Font)
                    .TextColor(EditorTheme.Text)
                    .FontSize(FontSz);
            }
        }

        if (expanded)
        {
            using (paper.Column($"{id}_content").Height(UnitValue.Auto).Enter())
            {
                drawContents();
            }
        }
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
                .BackgroundColor(EditorTheme.Neutral200)
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
                    using (paper.Row($"{id}_display")
                        .Height(EditorTheme.RowHeight)
                        .Width(UnitValue.Stretch())
                        .Enter())
                    {
                        paper.Box($"{id}_txt")
                            .Width(UnitValue.Stretch())
                            .IsNotInteractable()
                            .Text(displayText, Font).TextColor(EditorTheme.Text).FontSize(FontSz);
                        
                        // chevron down if open, right if closed
                        if (isOpen)
                        {
                            paper.Box("{id}_arrow")
                                .Margin(EditorTheme.RowHeight / 4, 0, EditorTheme.RowHeight / 8, 0)
                                .Width(16)
                                .MaxWidth(16)
                                // .Text("\u25BC", Font)
                                .Text(EditorIcons.ChevronUp, Font)
                                .TextColor(EditorTheme.TextDim)
                                .FontSize(FontSz * 0.7f);
                        }
                        else
                        {
                            paper.Box("{id}_arrow")
                                .Margin(EditorTheme.RowHeight / 3, (EditorTheme.RowHeight / 4) - (EditorTheme.RowHeight / 3), EditorTheme.RowHeight / 8, 0)
                                .Width(16)
                                .MaxWidth(16)
                                // .Text("\u25B6", Font)
                                .Text(EditorIcons.ChevronDown, Font)
                                .TextColor(EditorTheme.TextDim)
                                .FontSize(FontSz * 0.7f);
                        }
                    }
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
                        .ClampToScreen()
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
        
        using (paper.Box(id)
            .Height(EditorTheme.RowHeight)
            .ChildLeft(EditorTheme.RowHeight/4).ChildRight(EditorTheme.RowHeight/4)
            .BackgroundColor(value ? EditorTheme.Accent : EditorTheme.ButtonNormal)
            .Hovered.BackgroundColor(value ? EditorTheme.AccentDim : EditorTheme.ButtonHovered).End()
            .Active.BackgroundColor(EditorTheme.ButtonActive).End()
            .Rounded(3)
            .BorderColor(EditorTheme.Border).BorderWidth(1)
            .OnClick(e => userCallback?.Invoke(!value)).Enter())
        {
            if (Font != null)
                paper.Box($"{id}_label")
                    .Alignment(PaperUI.TextAlignment.MiddleLeft)
                    .Text(label, Font)
                    .TextColor(EditorTheme.Text)
                    .FontSize(FontSz);
        }

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
        
            .Rounded(3)
            .BorderWidth(1)
            
            .BackgroundColor(EditorTheme.Neutral200)
            .BorderColor(EditorTheme.Neutral100)
            .Focused.BorderColor(EditorTheme.Accent).End()
            
            .ChildLeft(6).ChildRight(4)
            .RowBetween(4)
            .TabIndex(0)
            .Enter())
        {
            if (Font != null)
                paper.Box($"{id}_icon")
                    .Width(16)
                    .Margin(EditorTheme.RowHeight / 4, 0, EditorTheme.RowHeight / 8, 0)
                    .Text(EditorIcons.MagnifyingGlass, Font).TextColor(EditorTheme.TextDim).FontSize(FontSz * 0.7f);

            paper.Box($"{id}_tf")
                .Height(EditorTheme.RowHeight)
                .Width(UnitValue.Stretch())
                .HookToParent()
                .IsNotInteractable()
                .FontSize(FontSz)
                .Alignment(PaperUI.TextAlignment.MiddleLeft)
                .TextField(value, Font,
                    onChange: v => userCallback?.Invoke(v),
                    textColor: EditorTheme.Text,
                    placeholder: placeholder,
                    placeholderColor: EditorTheme.TextDisabled,
                    intID: id.GetHashCode());

            if (!string.IsNullOrEmpty(value))
            {
                var clearBtn = paper.Box($"{id}_clear")
                    .Rounded(8)
                    .Size(16).Margin(2, UnitValue.StretchOne)
                    .Hovered.BackgroundColor(EditorTheme.ButtonHovered).End()
                    .Text(EditorIcons.Xmark, Font).TextColor(EditorTheme.TextDim).FontSize(14).Alignment(PaperUI.TextAlignment.MiddleCenter)
                    .OnClick(e => userCallback?.Invoke(""));
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
            FloatField(paper, $"{id}_x", (float)current.X, "X", Color.FromArgb(255, 200, 80, 80))
                .OnValueChanged(v => { current.X = v; userCallback?.Invoke(current); });

            // Y
            FloatField(paper, $"{id}_y", (float)current.Y, "Y", Color.FromArgb(255, 80, 200, 80))
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
            FloatField(paper, $"{id}_x", (float)current.X, "X", Color.FromArgb(255, 200, 80, 80))
                .OnValueChanged(v => { current.X = v; userCallback?.Invoke(current); });

            // Y
            FloatField(paper, $"{id}_y", (float)current.Y, "Y", Color.FromArgb(255, 80, 200, 80))
                .OnValueChanged(v => { current.Y = v; userCallback?.Invoke(current); });

            // Z
            FloatField(paper, $"{id}_z", (float)current.Z, "Z", Color.FromArgb(255, 80, 80, 200))
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
            .Enter())
        {
            if (Font != null && !string.IsNullOrEmpty(label))
                paper.Box($"{id}_lbl")
                    .Width(LabelW).Height(EditorTheme.RowHeight).ChildLeft(4)
                    .Text(label, Font).TextColor(EditorTheme.Text).FontSize(FontSz);

            // X
            FloatField(paper, $"{id}_x", (float)current.X, "X", Color.FromArgb(255, 200, 80, 80))
                .OnValueChanged(v => { current.X = v; userCallback?.Invoke(current); });

            // Y
            FloatField(paper, $"{id}_y", (float)current.Y, "Y", Color.FromArgb(255, 80, 200, 80))
                .OnValueChanged(v => { current.Y = v; userCallback?.Invoke(current); });

            // Z
            FloatField(paper, $"{id}_z", (float)current.Z, "Z", Color.FromArgb(255, 80, 80, 200))
                .OnValueChanged(v => { current.Z = v; userCallback?.Invoke(current); });

            // W
            FloatField(paper, $"{id}_w", (float)current.W, "W")
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
                    .TextColor(EditorTheme.Text)
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
                        .Width(UnitValue.Stretch())
                        .Height(EditorTheme.RowHeight)
                        .Margin(EditorTheme.RowHeight / 4, 0)
                        .IsNotInteractable()
                        .Alignment(PaperUI.TextAlignment.MiddleLeft)
                        .Text($"#{r:X2}{g:X2}{b:X2}", Font)
                        .TextColor(EditorTheme.Text)
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
                    .IsNotInteractable()
                    .Text(label, Font).TextColor(EditorTheme.Text).FontSize(FontSz);

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

                    float trackH = 4f;
                    float trackY = ry + rh * 0.5f - trackH * 0.5f;
                    float trackR = trackH * 0.5f;

                    // ── Track background ──────────────────────────────────
                    canvas.RoundedRectFilled(rx, trackY, rw, trackH, trackR, trackR, trackR, trackR,
                        EditorTheme.ButtonNormal);

                    // ── Track fill ────────────────────────────────────────
                    if (progress > 0f)
                        canvas.RoundedRectFilled(rx, trackY, rw * progress, trackH, trackR, trackR, trackR, trackR,
                            EditorTheme.Accent);
                }));

            if (Font != null)
                paper.Box($"{id}_pct")
                    .Width(40).Height(EditorTheme.RowHeight)
                    .IsNotInteractable()
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
