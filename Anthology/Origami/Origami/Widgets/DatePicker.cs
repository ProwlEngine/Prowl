// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

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
    public DatePickerBuilder Range(System.DateTime rangeEnd, Action<System.DateTime> rangeEndSetter)
    {
        _rangeEnd = rangeEnd;
        _rangeEndSetter = rangeEndSetter;
        return this;
    }

    public void Show()
    {
        if (Origami.IsReadOnly) _readOnly = true;
        var m = _theme.Metrics;
        var font = _theme.Font;
        var ink = _theme.Ink;
        var ramp = _theme.Get(_variant);

        string displayText = FormatDisplay();

        Color borderColor = _error != null ? _theme.Red.C400
            : _success ? _theme.Green.C400
            : (_variant == OrigamiVariant.Default ? _theme.Neutral.C400 : ramp.C400);
        Color hoverBorder = _error != null ? _theme.Red.C500
            : _success ? _theme.Green.C500
            : (_variant == OrigamiVariant.Default ? ramp.C500 : ramp.C500);

        var trigger = _paper.Row(_id)
            .Width(_width).Height(m.RowHeight)
            .BackgroundColor(_theme.Neutral.C200)
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

                string icon = _mode == DatePickerMode.Time ? _theme.Icons.Clock : _theme.Icons.ChevronDown;
                if (!string.IsNullOrEmpty(icon))
                    _paper.Box($"{_id}_ico")
                        .Width(m.RowHeight).Height(m.RowHeight)
                        .IsNotInteractable()
                        .Text(icon, font).TextColor(ink.C300)
                        .FontSize(m.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
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

        float popW = 280f;

        using (paper.Column($"{_id}_dpop")
            .PositionType(PositionType.SelfDirected)
            .Position(_anchorX, _anchorY)
            .Width(popW).Height(UnitValue.Auto)
            .BackgroundColor(theme.Neutral.C300)
            .BorderColor(theme.Ink.C200).BorderWidth(1)
            .Rounded(m.ContainerRounding)
            .BoxShadow(0, 4, 20, 0, Color.FromArgb(90, 0, 0, 0))
            .Padding(m.PaddingLarge, m.PaddingLarge, m.PaddingLarge, m.PaddingLarge)
            .ColBetween(m.Spacing)
            .Layer(layer)
            .ClampToScreen()
            .StopEventPropagation()
            .Enter())
        {
            if (_yearPickerOpen)
            {
                DrawYearPicker(paper, font, theme, m, popW);
                return;
            }

            if (_mode != DatePickerMode.Time)
                DrawCalendar(paper, font, theme, m, popW);

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

    private void DrawCalendar(Paper paper, Scribe.FontFile font, OrigamiTheme theme, OrigamiMetrics m, float popW)
    {
        var ink = theme.Ink;
        var primary = theme.Primary;

        // Month/Year navigation header
        using (paper.Row($"{_id}_nav").Height(m.RowHeight).RowBetween(m.Spacing).Enter())
        {
            Origami.IconButton(paper, $"{_id}_prev", theme.Icons.ChevronLeft, () =>
            {
                _viewMonth--;
                if (_viewMonth < 1) { _viewMonth = 12; _viewYear--; }
            }).Height(m.CompactHeight).Show();

            // Clickable month/year label - click year to open year picker
            _paper_Box(paper, $"{_id}_myr", $"{MonthName(_viewMonth)} {_viewYear}", font,
                ink.C500, m.FontSize, TextAlignment.MiddleCenter, () => _yearPickerOpen = true);

            Origami.IconButton(paper, $"{_id}_next", theme.Icons.ChevronRight, () =>
            {
                _viewMonth++;
                if (_viewMonth > 12) { _viewMonth = 1; _viewYear++; }
            }).Height(m.CompactHeight).Show();
        }

        // Day-of-week headers
        float cellW = (popW - m.PaddingLarge * 2) / 7f;
        float cellH = m.RowHeight;
        string[] days = { "Su", "Mo", "Tu", "We", "Th", "Fr", "Sa" };

        using (paper.Row($"{_id}_dow").Height(cellH).Enter())
        {
            for (int i = 0; i < 7; i++)
                paper.Box($"{_id}_dh_{i}")
                    .Width(cellW).Height(cellH).IsNotInteractable()
                    .Text(days[i], font).TextColor(ink.C300)
                    .FontSize(m.FontSizeSmall).Alignment(TextAlignment.MiddleCenter);
        }

        // Calendar grid
        int daysInMonth = System.DateTime.DaysInMonth(_viewYear, _viewMonth);
        int firstDay = (int)new System.DateTime(_viewYear, _viewMonth, 1).DayOfWeek;
        int totalCells = firstDay + daysInMonth;
        int rows = (totalCells + 6) / 7;
        bool isRange = _rangeEndSetter != null;
        float pill = cellH * 0.5f;

        // Compute the effective range for display (including hover preview)
        System.DateTime? rangeStart = null, rangeEndDisp = null;
        if (isRange)
        {
            if (_selectingEnd && _hoverDate.HasValue)
            {
                // Previewing: show range from selected start to hover
                rangeStart = _value.Date;
                rangeEndDisp = _hoverDate.Value.Date;
                if (rangeEndDisp < rangeStart)
                    (rangeStart, rangeEndDisp) = (rangeEndDisp, rangeStart);
            }
            else if (_rangeEnd.HasValue)
            {
                rangeStart = _value.Date;
                rangeEndDisp = _rangeEnd.Value.Date;
                if (rangeEndDisp < rangeStart)
                    (rangeStart, rangeEndDisp) = (rangeEndDisp, rangeStart);
            }
        }

        for (int row = 0; row < rows; row++)
        {
            using (paper.Row($"{_id}_r_{row}").Height(cellH).Enter())
            {
                for (int col = 0; col < 7; col++)
                {
                    int cellIdx = row * 7 + col;
                    int day = cellIdx - firstDay + 1;

                    if (day < 1 || day > daysInMonth)
                    {
                        paper.Box($"{_id}_c_{row}_{col}").Width(cellW).Height(cellH);
                        continue;
                    }

                    var date = new System.DateTime(_viewYear, _viewMonth, day);
                    bool isDisabled = _disabledDate?.Invoke(date) ?? false;
                    bool isToday = date.Date == System.DateTime.Now.Date;

                    // Range state
                    bool isRangeStart = rangeStart.HasValue && date.Date == rangeStart.Value;
                    bool isRangeEnd = rangeEndDisp.HasValue && date.Date == rangeEndDisp.Value;
                    bool isInRange = rangeStart.HasValue && rangeEndDisp.HasValue
                        && date.Date >= rangeStart.Value && date.Date <= rangeEndDisp.Value;
                    bool isSelected = !isRange && date.Date == _value.Date;
                    bool isEndpoint = isRangeStart || isRangeEnd;

                    // Background + rounding for pill-shaped range
                    Color bg;
                    float roundTL = 0, roundTR = 0, roundBR = 0, roundBL = 0;

                    if (isSelected)
                    {
                        bg = primary.C400;
                        roundTL = roundTR = roundBR = roundBL = pill;
                    }
                    else if (isRangeStart && isRangeEnd)
                    {
                        // Single day range (start == end)
                        bg = primary.C400;
                        roundTL = roundTR = roundBR = roundBL = pill;
                    }
                    else if (isRangeStart)
                    {
                        // Pill on left, flat on right to connect to range
                        bg = primary.C400;
                        roundTL = roundBL = pill;
                        roundTR = roundBR = 0;
                    }
                    else if (isRangeEnd)
                    {
                        // Flat on left, pill on right
                        bg = primary.C400;
                        roundTL = roundBL = 0;
                        roundTR = roundBR = pill;
                    }
                    else if (isInRange)
                    {
                        bg = Color.FromArgb(50, primary.C400.R, primary.C400.G, primary.C400.B);
                    }
                    else
                    {
                        bg = Color.Transparent;
                    }

                    Color fg = isDisabled ? ink.C200
                        : (isSelected || isEndpoint) ? Color.White
                        : isToday ? primary.C500
                        : ink.C500;

                    int d = day;
                    var cell = paper.Box($"{_id}_c_{row}_{col}")
                        .Width(cellW).Height(cellH)
                        .BackgroundColor(bg)
                        .Rounded(roundTL, roundTR, roundBR, roundBL)
                        .Text(day.ToString(), font).TextColor(fg)
                        .FontSize(m.FontSize).Alignment(TextAlignment.MiddleCenter);

                    if (!isDisabled)
                    {
                        cell.Hovered.BackgroundColor(isSelected || isEndpoint ? primary.C500 : theme.Neutral.C500).End();

                        // Track hover for range preview
                        if (isRange && _selectingEnd)
                        {
                            cell.OnHover(d, (dd, _) =>
                            {
                                _hoverDate = new System.DateTime(_viewYear, _viewMonth, dd);
                            });
                        }

                        cell.OnClick(d, (dd, _) =>
                        {
                            var newDate = new System.DateTime(_viewYear, _viewMonth, dd,
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
                        });
                    }
                }
            }
        }

        // Clear hover when mouse leaves the calendar area
        if (isRange && _selectingEnd)
        {
            // If pointer isn't over any day cell this frame, the hover callbacks won't fire
            // and _hoverDate stays from last frame - that's fine for smooth preview
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
        var primary = theme.Primary;

        // Header with back button
        using (paper.Row($"{_id}_yh").Height(m.RowHeight).Enter())
        {
            Origami.IconButton(paper, $"{_id}_yback", theme.Icons.ChevronLeft, () =>
                _yearPickerOpen = false).Height(m.CompactHeight).Show();
            paper.Box($"{_id}_ytitle").Width(UnitValue.Stretch()).Height(m.RowHeight)
                .Text("Select Year", font).TextColor(ink.C500)
                .FontSize(m.FontSize).Alignment(TextAlignment.MiddleCenter)
                .IsNotInteractable();
        }

        // Year grid (4 columns, showing +/- 6 years from current view)
        int startYear = _viewYear - 6;
        float cellW = (popW - m.PaddingLarge * 2) / 4f;

        for (int row = 0; row < 3; row++)
        {
            using (paper.Row($"{_id}_yr_{row}").Height(m.RowHeight + 4).Enter())
            {
                for (int col = 0; col < 4; col++)
                {
                    int yr = startYear + row * 4 + col;
                    bool isCurrent = yr == _viewYear;
                    bool isThisYear = yr == System.DateTime.Now.Year;

                    Color bg = isCurrent ? primary.C400 : Color.Transparent;
                    Color fg = isCurrent ? Color.White : isThisYear ? primary.C500 : ink.C500;

                    int capturedYr = yr;
                    paper.Box($"{_id}_y_{yr}")
                        .Width(cellW).Height(m.RowHeight + 4)
                        .BackgroundColor(bg).Rounded(m.Rounding)
                        .Hovered.BackgroundColor(isCurrent ? primary.C500 : theme.Neutral.C500).End()
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

    // Helper to create a clickable text box
    private static void _paper_Box(Paper paper, string id, string text, Scribe.FontFile font,
        Color color, float fontSize, TextAlignment align, Action onClick)
    {
        paper.Box(id)
            .Width(UnitValue.Stretch()).Height(Origami.Current.Metrics.RowHeight)
            .Text(text, font).TextColor(color)
            .FontSize(fontSize).Alignment(align)
            .OnClick(_ => onClick());
    }

    private static string MonthName(int month) => month switch
    {
        1 => "January", 2 => "February", 3 => "March", 4 => "April",
        5 => "May", 6 => "June", 7 => "July", 8 => "August",
        9 => "September", 10 => "October", 11 => "November", 12 => "December",
        _ => "",
    };
}
