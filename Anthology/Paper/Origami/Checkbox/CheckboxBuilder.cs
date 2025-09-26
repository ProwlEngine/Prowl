using System.Drawing;
using System.Runtime.CompilerServices;

using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Scribe;

namespace Prowl.PaperUI.Themes.Origami.Checkbox;

/// <summary>
/// Builder class for creating checkboxes with the Origami design system.
/// Provides customizable appearance, state management, and event handling.
/// </summary>
public class CheckboxBuilder
{
    private readonly Paper _paper;
    private readonly string _stringId;
    private readonly int _intId;
    private readonly int _lineId;
    private bool _isChecked = false;
    private bool _isIndeterminate = false;
    private string _label = string.Empty;
    private FontFile? _labelFont;
    private string _icon = string.Empty;
    private FontFile? _iconFont;
    private OrigamiColor _color = OrigamiColor.Primary;
    private OrigamiSize _size = OrigamiSize.Medium;
    private OrigamiRadius _radius = OrigamiRadius.Small;
    private bool _isDisabled = false;
    private bool _isReadOnly = false;
    private Action<bool>? _onChange;
    private Action<ClickEvent>? _onCheck;
    private Action<ElementEvent>? _onHover;
    private Action<ElementEvent>? _onLeave;

    /// <summary>
    /// Initializes a new CheckboxBuilder with the specified Paper instance and unique identifier.
    /// </summary>
    /// <param name="paper">The Paper UI instance</param>
    /// <param name="stringID">String identifier for the element</param>
    /// <param name="intID">Integer identifier useful for when creating elements in loops</param>
    /// <param name="lineID">Line number based identifier (auto-provided as Source Line Number)</param>
    internal CheckboxBuilder(Paper paper, string stringID, int intID = 0, [CallerLineNumber] int lineID = 0)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _stringId = stringID ?? throw new ArgumentNullException(nameof(stringID));
        _intId = intID;
        _lineId = lineID;
    }

    #region State Configuration

    /// <summary>
    /// Sets whether the checkbox is checked.
    /// </summary>
    /// <param name="isChecked">True if the checkbox is checked</param>
    /// <returns>This builder for method chaining</returns>
    public CheckboxBuilder IsChecked(bool isChecked)
    {
        _isChecked = isChecked;
        _isIndeterminate = false; // Clear indeterminate when setting checked state
        return this;
    }

    /// <summary>
    /// Sets the checkbox to an indeterminate state (partially checked).
    /// </summary>
    /// <param name="isIndeterminate">True for indeterminate state</param>
    /// <returns>This builder for method chaining</returns>
    public CheckboxBuilder IsIndeterminate(bool isIndeterminate = true)
    {
        _isIndeterminate = isIndeterminate;
        if (isIndeterminate) _isChecked = false; // Clear checked when setting indeterminate
        return this;
    }

    /// <summary>
    /// Sets whether the checkbox is disabled.
    /// </summary>
    /// <param name="disabled">True to disable the checkbox</param>
    /// <returns>This builder for method chaining</returns>
    public CheckboxBuilder IsDisabled(bool disabled = true)
    {
        _isDisabled = disabled;
        return this;
    }

    /// <summary>
    /// Sets whether the checkbox is read-only.
    /// </summary>
    /// <param name="readOnly">True to make the checkbox read-only</param>
    /// <returns>This builder for method chaining</returns>
    public CheckboxBuilder IsReadOnly(bool readOnly = true)
    {
        _isReadOnly = readOnly;
        return this;
    }

    #endregion

    #region Content Configuration

    /// <summary>
    /// Sets the label text for the checkbox.
    /// </summary>
    /// <param name="label">The label text</param>
    /// <param name="font">The font to use for the label</param>
    /// <returns>This builder for method chaining</returns>
    public CheckboxBuilder Label(string label, FontFile font)
    {
        _label = label ?? string.Empty;
        _labelFont = font ?? throw new ArgumentNullException(nameof(font));
        return this;
    }

    /// <summary>
    /// Sets a custom icon for the checkbox when checked.
    /// </summary>
    /// <param name="icon">The icon string (typically from an icon font)</param>
    /// <param name="font">The icon font</param>
    /// <returns>This builder for method chaining</returns>
    public CheckboxBuilder Icon(string icon, FontFile font)
    {
        _icon = icon ?? string.Empty;
        _iconFont = font ?? throw new ArgumentNullException(nameof(font));
        return this;
    }

    #endregion

    #region Style Configuration

    /// <summary>
    /// Sets the color scheme of the checkbox.
    /// </summary>
    /// <param name="color">The color scheme</param>
    /// <returns>This builder for method chaining</returns>
    public CheckboxBuilder Color(OrigamiColor color)
    {
        _color = color;
        return this;
    }

    /// <summary>
    /// Sets the size of the checkbox.
    /// </summary>
    /// <param name="size">The checkbox size</param>
    /// <returns>This builder for method chaining</returns>
    public CheckboxBuilder Size(OrigamiSize size)
    {
        _size = size;
        return this;
    }

    /// <summary>
    /// Sets the border radius of the checkbox.
    /// </summary>
    /// <param name="radius">The border radius</param>
    /// <returns>This builder for method chaining</returns>
    public CheckboxBuilder Radius(OrigamiRadius radius)
    {
        _radius = radius;
        return this;
    }

    #endregion

    #region Event Configuration

    /// <summary>
    /// Sets the change handler for the checkbox.
    /// </summary>
    /// <param name="onChange">The change event handler</param>
    /// <returns>This builder for method chaining</returns>
    public CheckboxBuilder OnChange(Action<bool> onChange)
    {
        _onChange = onChange;
        return this;
    }

    /// <summary>
    /// Sets the click handler for the checkbox (in addition to change events).
    /// </summary>
    /// <param name="onCheck">The click event handler</param>
    /// <returns>This builder for method chaining</returns>
    public CheckboxBuilder OnClick(Action<ClickEvent> onCheck)
    {
        _onCheck = onCheck;
        return this;
    }

    /// <summary>
    /// Sets the hover handler for when the mouse enters the checkbox.
    /// </summary>
    /// <param name="onHover">The hover event handler</param>
    /// <returns>This builder for method chaining</returns>
    public CheckboxBuilder OnHover(Action<ElementEvent> onHover)
    {
        _onHover = onHover;
        return this;
    }

    /// <summary>
    /// Sets the leave handler for when the mouse leaves the checkbox.
    /// </summary>
    /// <param name="onLeave">The leave event handler</param>
    /// <returns>This builder for method chaining</returns>
    public CheckboxBuilder OnLeave(Action<ElementEvent> onLeave)
    {
        _onLeave = onLeave;
        return this;
    }

    #endregion

    #region Build Method

    /// <summary>
    /// Builds and returns the configured checkbox as an ElementBuilder.
    /// </summary>
    /// <returns>An ElementBuilder representing the checkbox</returns>
    public ElementBuilder Build()
    {
        var theme = Origami.Theme;
        var dimensions = GetCheckboxDimensions(_size);

        // Create main container
        var container = _paper.Box($"origami-checkbox-container-{_stringId}", _intId, _lineId)
            .LayoutType(LayoutType.Row)
            .Width(UnitValue.Auto)
            .Height(dimensions.Height);

        using (container.Enter())
        {
            // Create checkbox box
            var checkbox = _paper.Box($"origami-checkbox-box-{_stringId}")
                .Width(dimensions.CheckboxSize)
                .Height(dimensions.CheckboxSize)
                .Rounded(theme.GetRadius(_radius))
                .Transition(GuiProp.BackgroundColor, 0.15)
                .Transition(GuiProp.BorderColor, 0.15);

            // Apply checkbox styling
            ApplyCheckboxStyling(checkbox, theme, dimensions);

            // Handle interaction
            if (!_isDisabled && !_isReadOnly)
            {
                if (_onChange != null)
                {
                    checkbox.OnClick(_ =>
                    {
                        var newChecked = !(_isChecked || _isIndeterminate);
                        _onChange(newChecked);
                    });
                }

                if (_onCheck != null)
                {
                    checkbox.OnClick(_onCheck);
                }

                if (_onHover != null) checkbox.OnHover(_onHover);
                if (_onLeave != null) checkbox.OnLeave(_onLeave);
            }
            else
            {
                checkbox.IsNotInteractable();
            }

            if (_isDisabled)
            {
                checkbox.IsNotFocusable();
            }

            // Add checkmark/icon
            using (checkbox.Enter())
            {
                AddCheckboxIcon(theme, dimensions);
            }

            // Add label if provided
            if (!string.IsNullOrEmpty(_label) && _labelFont != null)
            {
                var fontSize = theme.GetFontSize(_size);
                var labelColor = GetLabelColor(theme);

                _paper.Box($"origami-checkbox-label-{_stringId}")
                    .Width(UnitValue.Auto)
                    .Text(_label, _labelFont)
                    .TextColor(labelColor)
                    .Alignment(TextAlignment.MiddleLeft)
                    .FontSize(fontSize)
                    .Margin(8, 0, 0, 0)
                    .IsNotInteractable()
                    .IsNotFocusable();
            }
        }

        return container;
    }

    #endregion

    #region Private Helper Methods

    private void ApplyCheckboxStyling(ElementBuilder checkbox, OrigamiTheme theme, CheckboxDimensions dimensions)
    {
        var themeColor = theme.GetColor(_color);
        double opacity = _isDisabled ? theme.DisableOpacity : 1.0;

        // Determine state colors
        if (_isChecked || _isIndeterminate)
        {
            // Checked/indeterminate state
            var bgColor = System.Drawing.Color.FromArgb((int)(255 * opacity), themeColor.Base);
            checkbox.BackgroundColor(bgColor);
            checkbox.BorderWidth(theme.GetBorderWidth(_size));
            checkbox.BorderColor(bgColor);

            if (!_isDisabled)
            {
                var hoverColor = System.Drawing.Color.FromArgb((int)(255 * theme.HoverOpacity), themeColor.Base);
                checkbox.Hovered.BackgroundColor(hoverColor);
                checkbox.Hovered.BorderColor(hoverColor);
            }
        }
        else
        {
            // Unchecked state
            checkbox.BackgroundColor(System.Drawing.Color.Transparent);
            checkbox.BorderWidth(theme.GetBorderWidth(_size));

            var borderColor = System.Drawing.Color.FromArgb((int)(255 * opacity), theme.Content3.Base);
            checkbox.BorderColor(borderColor);

            if (!_isDisabled)
            {
                checkbox.Hovered.BorderColor(System.Drawing.Color.FromArgb((int)(255 * theme.HoverOpacity), themeColor.Base));
            }
        }
    }

    private void AddCheckboxIcon(OrigamiTheme theme, CheckboxDimensions dimensions)
    {
        if (!_isChecked && !_isIndeterminate) return;

        var iconColor = theme.Foreground.Base;
        if (_isDisabled) iconColor = System.Drawing.Color.FromArgb((int)(255 * theme.DisableOpacity), iconColor);

        var iconText = _isIndeterminate ? "−" : (!string.IsNullOrEmpty(_icon) ? _icon : "✓");
        var font = _isIndeterminate || string.IsNullOrEmpty(_icon) ? _labelFont : _iconFont;

        if (font == null) return;

        _paper.Box($"origami-checkbox-icon-{_stringId}")
            .Text(iconText, font)
            .TextColor(iconColor)
            .Alignment(TextAlignment.MiddleCenter)
            .FontSize(dimensions.IconSize)
            .IsNotInteractable()
            .IsNotFocusable();
    }

    private Color GetLabelColor(OrigamiTheme theme)
    {
        var baseColor = theme.Foreground.Base;

        if (_isDisabled)
        {
            return System.Drawing.Color.FromArgb((int)(255 * theme.DisableOpacity), baseColor);
        }

        return baseColor;
    }

    private struct CheckboxDimensions
    {
        public double CheckboxSize;
        public double Height;
        public double IconSize;
    }

    private CheckboxDimensions GetCheckboxDimensions(OrigamiSize size)
    {
        return size switch
        {
            OrigamiSize.Small => new CheckboxDimensions
            {
                CheckboxSize = 16,
                Height = 20,
                IconSize = 10
            },
            OrigamiSize.Medium => new CheckboxDimensions
            {
                CheckboxSize = 20,
                Height = 24,
                IconSize = 12
            },
            OrigamiSize.Large => new CheckboxDimensions
            {
                CheckboxSize = 24,
                Height = 28,
                IconSize = 14
            },
            _ => throw new InvalidOperationException()
        };
    }

    #endregion
}
