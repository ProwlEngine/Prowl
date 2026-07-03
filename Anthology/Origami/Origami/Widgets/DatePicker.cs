// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Quill;

using Color = System.Drawing.Color;
using TextAlignment = Prowl.PaperUI.TextAlignment;

namespace Prowl.OrigamiUI;

/// <summary>What the date picker shows.</summary>
public enum DatePickerMode
{
    /// <summary>Date only (no time).</summary>
    Date,
    /// <summary>Time only (no date).</summary>
    Time,
    /// <summary>Both date and time.</summary>
    DateTime,
}

/// <summary>
/// Fluent builder for a date/time picker. Renders an inline field that opens a
/// calendar/time popover on click via the modal system.
/// </summary>
public sealed class DatePickerBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly OrigamiTheme _theme;
    private readonly DateTime _value;
    private readonly Action<DateTime> _setter;

    private DatePickerMode _mode = DatePickerMode.Date;
    private UnitValue _width = UnitValue.Stretch();
    private bool _readOnly;
    private bool _use24Hour;
    private string? _format;
    private Func<DateTime, bool>? _disabledDate;
    private OrigamiVariant _variant = OrigamiVariant.Default;
    private string? _error;
    private bool _success;

    // Range
    private DateTime? _rangeEnd;
    private Action<DateTime>? _rangeEndSetter;

    // Inline (embedded) mode
    private bool _inline;
    private float _inlineWidth = 240f;

    internal DatePickerBuilder(Paper paper, string id, DateTime value, Action<DateTime> setter, OrigamiTheme theme)
    {
        _paper = paper;
        _id = id;
        _value = value;
        _setter = setter;
        _theme = theme;
    }

    public DatePickerBuilder Mode(DatePickerMode mode) { _mode = mode; return this; }
    public DatePickerBuilder DateOnly() => Mode(DatePickerMode.Date);
    public DatePickerBuilder TimeOnly() => Mode(DatePickerMode.Time);
    public DatePickerBuilder DateTime() => Mode(DatePickerMode.DateTime);
    public DatePickerBuilder Width(UnitValue width) { _width = width; return this; }
    public DatePickerBuilder ReadOnly(bool ro = true) { _readOnly = ro; return this; }
    public DatePickerBuilder Use24Hour(bool use = true) { _use24Hour = use; return this; }
    public DatePickerBuilder Format(string fmt) { _format = fmt; return this; }
    public DatePickerBuilder DisabledDates(Func<System.DateTime, bool> predicate) { _disabledDate = predicate; return this; }
    public DatePickerBuilder Variant(OrigamiVariant v) { _variant = v; return this; }
    public DatePickerBuilder Error(string msg) { _error = msg; return this; }
    public DatePickerBuilder Success(bool s = true) { _success = s; return this; }

    /// <summary>Enable range picking. The builder's value is the start, rangeEnd is the end.</summary>
    /// <summary>Render the calendar inline (embedded, no field/popup). Fully functional — single
    /// date or, with <see cref="Range"/>, a two-click date range. Width is a fixed pixel value.</summary>
    public DatePickerBuilder Inline(float width = 240f) { _inline = true; _inlineWidth = MathF.Max(180f, width); return this; }

    public DatePickerBuilder Range(System.DateTime rangeEnd, Action<System.DateTime> rangeEndSetter)
    {
        _rangeEnd = rangeEnd;
        _rangeEndSetter = rangeEndSetter;
        return this;
    }

    public void Show()
    {
        if (_inline) { ShowInline(); return; }

        if (Origami.IsReadOnly) _readOnly = true;
        var m = _theme.Metrics;
        var font = _theme.Font;
        var ink = _theme.Ink;
        var ramp = _theme.Get(_variant);

        string displayText = FormatDisplay();

        Color borderColor = _error != null ? _theme.Red.C400
            : _success ? _theme.Green.C400
            : (_variant == OrigamiVariant.Default ? _theme.BorderSoft : ramp.C400);
        Color hoverBorder = _error != null ? _theme.Red.C500
            : _success ? _theme.Green.C500
            : ramp.C500;

        var trigger = _paper.Row(_id)
            .Width(_width).Height(m.RowHeight)
            .BackgroundColor(_theme.Glass)
            .BorderColor(borderColor).BorderWidth(1)
            .Hovered.BorderColor(hoverBorder).End()
            .Rounded(m.Rounding);

        if (!_readOnly)
        {
            var value = _value;
            var setter = _setter;
            var id = _id;
            var mode = _mode;
            var use24 = _use24Hour;
            var disabled = _disabledDate;
            var rangeEnd = _rangeEnd;
            var rangeEndSetter = _rangeEndSetter;

            trigger.OnClick(e =>
            {
                float ax = (float)e.ElementRect.Min.X;
                float ay = (float)e.ElementRect.Max.Y + 2;
                Modal.Push(new DatePickerModal(id, value, setter, mode, use24, disabled,
                    rangeEnd, rangeEndSetter, ax, ay));
            });
        }

        using (trigger.Enter())
        {
            if (font != null)
            {
                _paper.Box($"{_id}_txt")
                    .Width(UnitValue.Stretch()).Height(m.RowHeight)
                    .Margin(m.Padding, 0, 0, 0)
                    .IsNotInteractable()
                    .Text(displayText, font).TextColor(ink.C500)
                    .FontSize(m.FontSize).Alignment(TextAlignment.MiddleLeft);

                // Trailing affordance — drawn as a vector (the icon font ships no glyph).
                bool clock = _mode == DatePickerMode.Time;
                Color icoColor = ink.C300;
                using (_paper.Box($"{_id}_ico")
                    .Width(m.RowHeight).Height(m.RowHeight)
                    .IsNotInteractable().Enter())
                {
                    _paper.Draw((canvas, rect) =>
                    {
                        float cx = (float)(rect.Min.X + rect.Size.X * 0.5);
                        float cy = (float)(rect.Min.Y + rect.Size.Y * 0.5);
                        if (clock) OrigamiCalendar.DrawClock(canvas, cx, cy, icoColor);
                        else OrigamiCalendar.DrawCaretDown(canvas, cx, cy, icoColor);
                    });
                }
            }
        }

        // Error text
        if (_error != null && font != null)
        {
            _paper.Box($"{_id}_err")
                .Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .Margin(2, 2, 2, 0).IsNotInteractable()
                .Text(_error, font).TextColor(_theme.Red.C500)
                .FontSize(m.FontSizeSmall).Alignment(TextAlignment.Left);
        }
    }

    private void ShowInline()
    {
        var m = _theme.Metrics;
        var font = _theme.Font;
        if (font == null) return;
        bool isRange = _rangeEndSetter != null;

        using (_paper.Column(_id)
            .Width(UnitValue.Pixels(_inlineWidth)).Height(UnitValue.Auto)
            .BackgroundColor(_theme.Glass)
            .BorderColor(_theme.BorderSoft).BorderWidth(1)
            .Rounded(m.ContainerRounding)
            .Padding(11, 11, 11, 11).ColBetween(m.SpacingMedium)
            .Enter())
        {
            var h = _paper.CurrentParent;
            int baseCode = _value.Year * 12 + (_value.Month - 1);
            int viewCode = _paper.GetElementStorage(h, "vm", baseCode);
            int vy = viewCode / 12, vm = viewCode % 12 + 1;
            bool selectingEnd = isRange && _paper.GetElementStorage(h, "selEnd", false);

            System.DateTime? rStart = null, rEnd = null;
            if (isRange)
            {
                rStart = _value.Date;
                if (_rangeEnd.HasValue) rEnd = _rangeEnd.Value.Date;
                if (rStart.HasValue && rEnd.HasValue && rEnd < rStart) (rStart, rEnd) = (rEnd, rStart);
            }

            OrigamiCalendar.DrawBody(_paper, _id, font, _theme, _inlineWidth - 22f, vy, vm,
                onPrev: () => _paper.SetElementStorage(h, "vm", viewCode - 1),
                onNext: () => _paper.SetElementStorage(h, "vm", viewCode + 1),
                onTitle: null,
                stateOf: date =>
                {
                    var f = new CalDayFlags
                    {
                        Disabled = _disabledDate?.Invoke(date) ?? false,
                        Today = date.Date == System.DateTime.Now.Date,
                    };
                    if (isRange)
                    {
                        f.RangeStart = rStart.HasValue && date.Date == rStart.Value;
                        f.RangeEnd = rEnd.HasValue && date.Date == rEnd.Value;
                        f.InRange = rStart.HasValue && rEnd.HasValue
                            && date.Date > rStart.Value && date.Date < rEnd.Value;
                    }
                    else f.Selected = date.Date == _value.Date;
                    return f;
                },
                onHover: null,
                onPick: date =>
                {
                    var picked = new System.DateTime(date.Year, date.Month, date.Day,
                        _value.Hour, _value.Minute, _value.Second);
                    if (isRange)
                    {
                        if (!selectingEnd)
                        {
                            _setter(picked);
                            _rangeEndSetter!(picked);
                            _paper.SetElementStorage(h, "selEnd", true);
                        }
                        else
                        {
                            var start = _value; var end = picked;
                            if (end < start) (start, end) = (end, start);
                            _setter(start);
                            _rangeEndSetter!(end);
                            _paper.SetElementStorage(h, "selEnd", false);
                        }
                    }
                    else _setter(picked);
                });
        }
    }

    private string FormatDisplay()
    {
        if (_format != null) return _value.ToString(_format);
        if (_rangeEnd.HasValue)
        {
            string df = "MMM d, yyyy";
            return $"{_value.ToString(df)} - {_rangeEnd.Value.ToString(df)}";
        }
        return _mode switch
        {
            DatePickerMode.Date => _value.ToString("MMM d, yyyy"),
            DatePickerMode.Time => _use24Hour ? _value.ToString("HH:mm") : _value.ToString("h:mm tt"),
            DatePickerMode.DateTime => _use24Hour
                ? _value.ToString("MMM d, yyyy  HH:mm")
                : _value.ToString("MMM d, yyyy  h:mm tt"),
            _ => _value.ToString(),
        };
    }
}

// ════════════════════════════════════════════════════════════════
//  Standalone / inline calendar
// ════════════════════════════════════════════════════════════════

/// <summary>
/// Static entry points for rendering the Nebula <c>.w2cal</c> calendar directly in the
/// normal layout flow (a showcase or an embedded date panel), reusing the exact grid the
/// popup date picker draws.
/// </summary>
public static class DatePicker
{
    /// <summary>
    /// Draw a <c>.w2cal</c> calendar card inline. The displayed month starts at
    /// <paramref name="month"/> and the header chevrons navigate from there (view state is
    /// kept in element storage keyed by <paramref name="id"/>). <paramref name="selectedDay"/>
    /// and <paramref name="today"/> are day-of-month numbers that highlight only while the view
    /// is on <paramref name="month"/>. <paramref name="onPick"/> receives the clicked day.
    /// </summary>
    public static void Calendar(Paper paper, string id, DateTime month,
        int? selectedDay = null, Action<int>? onPick = null,
        int? today = null, float width = 260f)
    {
        var theme = Origami.Current;
        var font = theme.Font;
        if (font == null) return;
        var m = theme.Metrics;

        int baseCode = month.Year * 12 + (month.Month - 1);

        using (paper.Column(id)
            .Width(width).Height(UnitValue.Auto)
            .BackgroundColor(theme.Glass)
            .BorderColor(theme.BorderSoft).BorderWidth(1)
            .Rounded(m.ContainerRounding)
            .Padding(11, 11, 11, 11)
            .Enter())
        {
            int code = paper.GetRootStorage<int>($"{id}_cm");
            if (code <= 0) code = baseCode;
            int viewYear = code / 12;
            int viewMonth = code % 12 + 1;
            bool onBase = viewYear == month.Year && viewMonth == month.Month;
            int captured = code;

            OrigamiCalendar.DrawBody(paper, id, font, theme, width - 22f, viewYear, viewMonth,
                onPrev: () => paper.SetRootStorage($"{id}_cm", captured - 1),
                onNext: () => paper.SetRootStorage($"{id}_cm", captured + 1),
                onTitle: null,
                stateOf: date => new CalDayFlags
                {
                    Selected = onBase && selectedDay.HasValue && date.Day == selectedDay.Value,
                    Today = onBase && today.HasValue && date.Day == today.Value,
                },
                onHover: null,
                onPick: onPick != null ? d => onPick(d.Day) : null);
        }
    }
}

// ════════════════════════════════════════════════════════════════
//  Shared calendar renderer (.w2cal)
// ════════════════════════════════════════════════════════════════

/// <summary>Per-day visual state fed to the shared calendar grid.</summary>
internal struct CalDayFlags
{
    public bool Disabled;
    public bool Today;
    public bool Selected;
    public bool InRange;
    public bool RangeStart;
    public bool RangeEnd;
}

/// <summary>
/// Draws the Nebula <c>.w2cal</c> calendar body (header + weekday row + day grid). Shared by
/// the popup <see cref="DatePickerModal"/> and the inline <see cref="DatePicker.Calendar"/>.
/// Callers own the surrounding container and all state; this class only paints and reports
/// clicks/hovers.
/// </summary>
internal static class OrigamiCalendar
{
    // ── Nebula literals ──────────────────────────────────────────
    /// <summary>glass-in — the translucent card fill.</summary>
    /// <summary>bd-soft — the hairline lavender border.</summary>
    /// <summary>hover wash — rgba(168,85,247,0.12).</summary>

    private static readonly string[] DowLetters = { "S", "M", "T", "W", "T", "F", "S" };

    private const float Gap = 2f;
    private const float DayRound = 6f;

    public static void DrawBody(Paper paper, string id, Scribe.FontFile font, OrigamiTheme theme,
        float contentWidth, int viewYear, int viewMonth,
        Action? onPrev, Action? onNext, Action? onTitle,
        Func<DateTime, CalDayFlags> stateOf,
        Action<DateTime>? onHover, Action<DateTime>? onPick)
    {
        var m = theme.Metrics;
        var ink = theme.Ink;
        var accent = theme.Primary.C500;

        float cell = (contentWidth - Gap * 6f) / 7f;
        float titleSize = m.FontSize - 1f;
        float dowSize = MathF.Max(9f, m.FontSizeSmall - 2f);
        float daySize = m.FontSizeSmall;
        var titleFont = theme.SemiBold ?? font;

        using (paper.Column($"{id}_cal").Width(contentWidth).Height(UnitValue.Auto).Enter())
        {
            // ── Header: ‹  Month Year  › ──
            using (paper.Row($"{id}_head").Width(UnitValue.Stretch()).Height(m.HeaderHeight)
                .Margin(0, 0, 0, 9).Enter())
            {
                Chevron(paper, $"{id}_prev", true, m.HeaderHeight, ink.C300, onPrev);

                var title = paper.Box($"{id}_title")
                    .Width(UnitValue.Stretch()).Height(m.HeaderHeight)
                    .Text($"{MonthName(viewMonth)} {viewYear}", titleFont)
                    .TextColor(ink.C500).FontSize(titleSize)
                    .Alignment(TextAlignment.MiddleCenter);
                if (onTitle != null) title.OnClick(_ => onTitle());
                else title.IsNotInteractable();

                Chevron(paper, $"{id}_next", false, m.HeaderHeight, ink.C300, onNext);
            }

            // ── Weekday row ──
            using (paper.Row($"{id}_dow").Width(UnitValue.Stretch()).Height(cell * 0.62f)
                .Margin(0, 0, 0, 2).RowBetween(Gap).Enter())
            {
                for (int i = 0; i < 7; i++)
                    paper.Box($"{id}_dow_{i}").Width(cell).Height(cell * 0.62f)
                        .IsNotInteractable()
                        .Text(DowLetters[i], font).TextColor(ink.C200)
                        .FontSize(dowSize).Alignment(TextAlignment.MiddleCenter);
            }

            // ── Day grid ──
            int daysInMonth = DateTime.DaysInMonth(viewYear, viewMonth);
            var first = new DateTime(viewYear, viewMonth, 1);
            int firstDow = (int)first.DayOfWeek; // Sunday = 0
            int rows = (firstDow + daysInMonth + 6) / 7;

            using (paper.Column($"{id}_grid").Width(UnitValue.Stretch()).Height(UnitValue.Auto)
                .ColBetween(Gap).Enter())
            {
                for (int r = 0; r < rows; r++)
                {
                    using (paper.Row($"{id}_r{r}").Width(UnitValue.Stretch()).Height(cell)
                        .RowBetween(Gap).Enter())
                    {
                        for (int c = 0; c < 7; c++)
                        {
                            int idx = r * 7 + c;
                            var date = first.AddDays(idx - firstDow);
                            string cid = $"{id}_d{r}_{c}";
                            bool inMonth = date.Month == viewMonth && date.Year == viewYear;

                            // Leading / trailing days from adjacent months — dimmed, inert.
                            if (!inMonth)
                            {
                                paper.Box(cid).Width(cell).Height(cell).IsNotInteractable()
                                    .Text(date.Day.ToString(), font).TextColor(ink.C100)
                                    .FontSize(daySize).Alignment(TextAlignment.MiddleCenter);
                                continue;
                            }

                            var f = stateOf(date);
                            bool onAccent = f.Selected || f.RangeStart || f.RangeEnd;

                            Color bg = onAccent ? accent
                                : f.InRange ? Color.FromArgb(40, accent.R, accent.G, accent.B)
                                : Color.Transparent;
                            Color fg = f.Disabled ? ink.C100
                                : onAccent ? Color.White
                                : ink.C400;

                            var day = paper.Box(cid).Width(cell).Height(cell)
                                .BackgroundColor(bg).Rounded(DayRound)
                                .Text(date.Day.ToString(), font).TextColor(fg)
                                .FontSize(daySize).Alignment(TextAlignment.MiddleCenter);

                            // Today ring — only when the cell isn't already filled.
                            if (f.Today && !onAccent)
                                day.BorderColor(accent).BorderWidth(1);

                            if (f.Disabled) { day.IsNotInteractable(); continue; }

                            day.Hovered.BackgroundColor(onAccent ? accent : theme.Hover).End();

                            if (onHover != null) day.OnHover(date, (d, _) => onHover(d));
                            if (onPick != null) day.OnClick(date, (d, _) => onPick(d));
                        }
                    }
                }
            }
        }
    }

    // ── Vector glyphs (icon font ships empty) ────────────────────

    private static void Chevron(Paper paper, string id, bool left, float size, Color color, Action? onClick)
    {
        var b = paper.Box(id).Width(size).Height(size).Rounded(DayRound);
        if (onClick != null) b.Hovered.BackgroundColor(Origami.Current.Hover).End().OnClick(_ => onClick());
        else b.IsNotInteractable();

        using (b.Enter())
            paper.Draw((canvas, rect) =>
            {
                float cx = (float)(rect.Min.X + rect.Size.X * 0.5);
                float cy = (float)(rect.Min.Y + rect.Size.Y * 0.5);
                canvas.SaveState();
                canvas.BeginPath();
                canvas.SetStrokeColor(color);
                canvas.SetStrokeWidth(1.4f);
                canvas.SetStrokeCap(EndCapStyle.Round);
                canvas.SetStrokeJoint(JointStyle.Round);
                float dx = left ? 1.8f : -1.8f;
                canvas.MoveTo(cx + dx, cy - 3.6f);
                canvas.LineTo(cx - dx, cy);
                canvas.LineTo(cx + dx, cy + 3.6f);
                canvas.Stroke();
                canvas.RestoreState();
            });
    }

    internal static void DrawCaretDown(Canvas canvas, float cx, float cy, Color color)
    {
        canvas.SaveState();
        canvas.BeginPath();
        canvas.SetStrokeColor(color);
        canvas.SetStrokeWidth(1.4f);
        canvas.SetStrokeCap(EndCapStyle.Round);
        canvas.SetStrokeJoint(JointStyle.Round);
        canvas.MoveTo(cx - 3.2f, cy - 1.6f);
        canvas.LineTo(cx, cy + 1.8f);
        canvas.LineTo(cx + 3.2f, cy - 1.6f);
        canvas.Stroke();
        canvas.RestoreState();
    }

    internal static void DrawClock(Canvas canvas, float cx, float cy, Color color)
    {
        canvas.SaveState();
        canvas.BeginPath();
        canvas.Arc(cx, cy, 4.2f, 0f, MathF.PI * 2f);
        canvas.SetStrokeColor(color);
        canvas.SetStrokeWidth(1.3f);
        canvas.Stroke();
        canvas.BeginPath();
        canvas.SetStrokeCap(EndCapStyle.Round);
        canvas.MoveTo(cx, cy - 2.4f);
        canvas.LineTo(cx, cy);
        canvas.LineTo(cx + 2.2f, cy + 1.2f);
        canvas.Stroke();
        canvas.RestoreState();
    }

    internal static string MonthName(int month) => month switch
    {
        1 => "January", 2 => "February", 3 => "March", 4 => "April",
        5 => "May", 6 => "June", 7 => "July", 8 => "August",
        9 => "September", 10 => "October", 11 => "November", 12 => "December",
        _ => "",
    };
}

// ════════════════════════════════════════════════════════════════
//  Date Picker Modal
// ════════════════════════════════════════════════════════════════

internal sealed class DatePickerModal : IModal
{
    private readonly string _id;
    private System.DateTime _value;
    private readonly Action<System.DateTime> _setter;
    private readonly DatePickerMode _mode;
    private readonly bool _use24Hour;
    private readonly Func<System.DateTime, bool>? _disabledDate;
    private readonly float _anchorX, _anchorY;

    // Range
    private System.DateTime? _rangeEnd;
    private readonly Action<System.DateTime>? _rangeEndSetter;
    private bool _selectingEnd;
    private System.DateTime? _hoverDate; // for range hover preview

    // View state
    private int _viewYear;
    private int _viewMonth;
    private bool _yearPickerOpen;

    private const float PopWidth = 280f;
    private const float Pad = 11f;

    public bool CloseOnBackdrop => true;
    public bool CloseOnEscape => true;

    public DatePickerModal(string id, System.DateTime value, Action<System.DateTime> setter,
        DatePickerMode mode, bool use24Hour, Func<System.DateTime, bool>? disabledDate,
        System.DateTime? rangeEnd, Action<System.DateTime>? rangeEndSetter,
        float anchorX, float anchorY)
    {
        _id = id;
        _value = value;
        _setter = setter;
        _mode = mode;
        _use24Hour = use24Hour;
        _disabledDate = disabledDate;
        _rangeEnd = rangeEnd;
        _rangeEndSetter = rangeEndSetter;
        _anchorX = anchorX;
        _anchorY = anchorY;
        _viewYear = value.Year;
        _viewMonth = value.Month;
    }

    public void Draw(Paper paper, int layer, int stackIndex)
    {
        var theme = Origami.Current;
        var m = theme.Metrics;
        var font = theme.Font;
        if (font == null) return;

        using (paper.Column($"{_id}_dpop")
            .PositionType(PositionType.SelfDirected)
            .Position(_anchorX, _anchorY)
            .Width(PopWidth).Height(UnitValue.Auto)
            .BackgroundColor(theme.Popover)                 // solid popover, crisp over content
            .BorderColor(theme.BorderStrong).BorderWidth(1)    // bd-strong
            .Rounded(m.ContainerRounding)
            .BoxShadow(0, 14, 40, 0, theme.Shadow)
            .Padding(Pad, Pad, Pad, Pad)
            .ColBetween(m.SpacingMedium)
            .Layer(layer)
            .ClampToScreen()
            .StopEventPropagation()
            .Enter())
        {
            if (_yearPickerOpen)
            {
                DrawYearPicker(paper, font, theme, m, PopWidth);
                return;
            }

            if (_mode != DatePickerMode.Time)
                DrawCalendar(paper, font, theme, m);

            if (_mode != DatePickerMode.Date)
                DrawTimePicker(paper, font, theme, m);

            // Today button
            if (_mode != DatePickerMode.Time)
            {
                Origami.Button(paper, $"{_id}_today", "Today", () =>
                {
                    var now = System.DateTime.Now;
                    _value = new System.DateTime(now.Year, now.Month, now.Day,
                        _value.Hour, _value.Minute, _value.Second);
                    _viewYear = now.Year;
                    _viewMonth = now.Month;
                    _setter(_value);
                }).Subtle().Show();
            }
        }
    }

    private void DrawCalendar(Paper paper, Scribe.FontFile font, OrigamiTheme theme, OrigamiMetrics m)
    {
        bool isRange = _rangeEndSetter != null;

        // Effective range for display (including hover preview), lo <= hi.
        System.DateTime? rangeStart = null, rangeEndDisp = null;
        if (isRange)
        {
            if (_selectingEnd && _hoverDate.HasValue)
            {
                rangeStart = _value.Date;
                rangeEndDisp = _hoverDate.Value.Date;
            }
            else if (_rangeEnd.HasValue)
            {
                rangeStart = _value.Date;
                rangeEndDisp = _rangeEnd.Value.Date;
            }
            if (rangeStart.HasValue && rangeEndDisp < rangeStart)
                (rangeStart, rangeEndDisp) = (rangeEndDisp, rangeStart);
        }

        OrigamiCalendar.DrawBody(paper, _id, font, theme, PopWidth - Pad * 2f, _viewYear, _viewMonth,
            onPrev: () => { _viewMonth--; if (_viewMonth < 1) { _viewMonth = 12; _viewYear--; } },
            onNext: () => { _viewMonth++; if (_viewMonth > 12) { _viewMonth = 1; _viewYear++; } },
            onTitle: () => _yearPickerOpen = true,
            stateOf: date =>
            {
                var f = new CalDayFlags
                {
                    Disabled = _disabledDate?.Invoke(date) ?? false,
                    Today = date.Date == System.DateTime.Now.Date,
                };
                if (isRange)
                {
                    f.RangeStart = rangeStart.HasValue && date.Date == rangeStart.Value;
                    f.RangeEnd = rangeEndDisp.HasValue && date.Date == rangeEndDisp.Value;
                    f.InRange = rangeStart.HasValue && rangeEndDisp.HasValue
                        && date.Date > rangeStart.Value && date.Date < rangeEndDisp.Value;
                }
                else f.Selected = date.Date == _value.Date;
                return f;
            },
            onHover: (isRange && _selectingEnd) ? new Action<System.DateTime>(d => _hoverDate = d) : null,
            onPick: date => OnDayPicked(date, isRange));
    }

    private void OnDayPicked(System.DateTime date, bool isRange)
    {
        var newDate = new System.DateTime(date.Year, date.Month, date.Day,
            _value.Hour, _value.Minute, _value.Second);

        if (isRange)
        {
            if (!_selectingEnd)
            {
                _value = newDate;
                _setter(newDate);
                _rangeEnd = null;
                _hoverDate = null;
                _selectingEnd = true;
            }
            else
            {
                var start = _value;
                var end = newDate;
                if (end < start) (start, end) = (end, start);
                _value = start;
                _setter(start);
                _rangeEnd = end;
                _rangeEndSetter!(end);
                _selectingEnd = false;
                _hoverDate = null;
                Modal.Pop();
            }
        }
        else
        {
            _value = newDate;
            _setter(newDate);
            if (_mode == DatePickerMode.Date)
                Modal.Pop();
        }
    }

    private void DrawTimePicker(Paper paper, Scribe.FontFile font, OrigamiTheme theme, OrigamiMetrics m)
    {
        var ink = theme.Ink;

        paper.Box($"{_id}_tsep").Height(1).Margin(0, 0, m.Spacing, m.Spacing).BackgroundColor(ink.C200);

        using (paper.Row($"{_id}_time").Height(m.RowHeight + 4).RowBetween(m.Spacing)
            .ChildLeft(UnitValue.StretchOne).ChildRight(UnitValue.StretchOne).Enter())
        {
            int hour = _value.Hour;
            int minute = _value.Minute;
            bool isPM = hour >= 12;
            int displayHour = _use24Hour ? hour : (hour % 12 == 0 ? 12 : hour % 12);

            // Hour
            Origami.NumericField<int>(paper, $"{_id}_hr", displayHour, v =>
            {
                int h = _use24Hour ? Math.Clamp(v, 0, 23)
                    : Math.Clamp(v, 1, 12);
                if (!_use24Hour)
                    h = isPM ? (h == 12 ? 12 : h + 12) : (h == 12 ? 0 : h);
                _value = new System.DateTime(_value.Year, _value.Month, _value.Day, h, minute, 0);
                _setter(_value);
            }).Min(_use24Hour ? 0 : 1).Max(_use24Hour ? 23 : 12).Width(50).Show();

            paper.Box($"{_id}_colon").Width(10).Height(m.RowHeight)
                .Text(":", font).TextColor(ink.C400)
                .FontSize(m.FontSize + 2).Alignment(TextAlignment.MiddleCenter)
                .IsNotInteractable();

            // Minute
            Origami.NumericField<int>(paper, $"{_id}_min", minute, v =>
            {
                int mi = Math.Clamp(v, 0, 59);
                _value = new System.DateTime(_value.Year, _value.Month, _value.Day, _value.Hour, mi, 0);
                _setter(_value);
            }).Min(0).Max(59).Width(50).Show();

            // AM/PM toggle
            if (!_use24Hour)
            {
                Origami.Button(paper, $"{_id}_ampm", isPM ? "PM" : "AM", () =>
                {
                    int h = _value.Hour;
                    h = isPM ? h - 12 : h + 12;
                    h = Math.Clamp(h, 0, 23);
                    _value = new System.DateTime(_value.Year, _value.Month, _value.Day, h, _value.Minute, 0);
                    _setter(_value);
                }).Width(40).Subtle().Show();
            }
        }
    }

    private void DrawYearPicker(Paper paper, Scribe.FontFile font, OrigamiTheme theme, OrigamiMetrics m, float popW)
    {
        var ink = theme.Ink;
        var accent = theme.Primary.C500;

        // Header with back button
        using (paper.Row($"{_id}_yh").Height(m.RowHeight).Enter())
        {
            Origami.IconButton(paper, $"{_id}_yback", theme.Icons.ChevronLeft, () =>
                _yearPickerOpen = false).Height(m.CompactHeight).Show();
            paper.Box($"{_id}_ytitle").Width(UnitValue.Stretch()).Height(m.RowHeight)
                .Text("Select Year", theme.SemiBold ?? font).TextColor(ink.C500)
                .FontSize(m.FontSize - 1f).Alignment(TextAlignment.MiddleCenter)
                .IsNotInteractable();
        }

        // Year grid (4 columns, showing +/- 6 years from current view)
        int startYear = _viewYear - 6;
        float cellW = (popW - Pad * 2) / 4f;

        for (int row = 0; row < 3; row++)
        {
            using (paper.Row($"{_id}_yr_{row}").Height(m.RowHeight + 4).Enter())
            {
                for (int col = 0; col < 4; col++)
                {
                    int yr = startYear + row * 4 + col;
                    bool isCurrent = yr == _viewYear;
                    bool isThisYear = yr == System.DateTime.Now.Year;

                    Color bg = isCurrent ? accent : Color.Transparent;
                    Color fg = isCurrent ? Color.White : isThisYear ? accent : ink.C400;

                    int capturedYr = yr;
                    paper.Box($"{_id}_y_{yr}")
                        .Width(cellW).Height(m.RowHeight + 4)
                        .BackgroundColor(bg).Rounded(m.Rounding)
                        .Hovered.BackgroundColor(isCurrent ? accent : theme.Hover).End()
                        .Text(yr.ToString(), font).TextColor(fg)
                        .FontSize(m.FontSize).Alignment(TextAlignment.MiddleCenter)
                        .OnClick(capturedYr, (y, _) =>
                        {
                            _viewYear = y;
                            _yearPickerOpen = false;
                        });
                }
            }
        }
    }
}
