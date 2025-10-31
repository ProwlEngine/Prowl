using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Scribe;
using Prowl.Vector;

namespace Prowl.PaperUI.Themes.Origami.Switch;

/// <summary>
/// Builder class for creating switches with the Origami design system.
/// Provides toggleable switches with customizable appearance and behavior.
/// </summary>
public class SwitchBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly int _intId;
    private bool _isOn = false;
    private string _label = string.Empty;
    private FontFile? _labelFont;
    private string _onIcon = string.Empty;
    private string _offIcon = string.Empty;
    private string _thumbIcon = string.Empty;
    private FontFile? _iconFont;
    private OrigamiColor _color = OrigamiColor.Primary;
    private OrigamiSize _size = OrigamiSize.Medium;
    private bool _isDisabled = false;
    private bool _isReadOnly = false;
    private Action<bool>? _onChange;

    /// <summary>
    /// Initializes a new SwitchBuilder with the specified Paper instance and unique identifier.
    /// </summary>
    /// <param name="paper">The Paper UI instance</param>
    /// <param name="id">Unique identifier for this switch</param>
    internal SwitchBuilder(Paper paper, string id, int intID)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _intId = intID;
    }

    #region State Configuration

    /// <summary>
    /// Sets the current state of the switch.
    /// </summary>
    /// <param name="isOn">True if the switch is on</param>
    /// <returns>This builder for method chaining</returns>
    public SwitchBuilder IsOn(bool isOn)
    {
        _isOn = isOn;
        return this;
    }

    /// <summary>
    /// Sets whether the switch is disabled.
    /// </summary>
    /// <param name="disabled">True to disable the switch</param>
    /// <returns>This builder for method chaining</returns>
    public SwitchBuilder IsDisabled(bool disabled = true)
    {
        _isDisabled = disabled;
        return this;
    }

    /// <summary>
    /// Sets whether the switch is read-only.
    /// </summary>
    /// <param name="readOnly">True to make the switch read-only</param>
    /// <returns>This builder for method chaining</returns>
    public SwitchBuilder IsReadOnly(bool readOnly = true)
    {
        _isReadOnly = readOnly;
        return this;
    }

    #endregion

    #region Content Configuration

    /// <summary>
    /// Sets the label text for the switch.
    /// </summary>
    /// <param name="label">The label text</param>
    /// <param name="font">The font to use for the label</param>
    /// <returns>This builder for method chaining</returns>
    public SwitchBuilder Label(string label, FontFile font)
    {
        _label = label ?? string.Empty;
        _labelFont = font;
        return this;
    }

    /// <summary>
    /// Sets icons that appear in the switch track when on/off.
    /// </summary>
    /// <param name="onIcon">Icon to show when switch is on</param>
    /// <param name="offIcon">Icon to show when switch is off</param>
    /// <param name="font">The icon font</param>
    /// <returns>This builder for method chaining</returns>
    public SwitchBuilder Icons(string onIcon, string offIcon, FontFile font)
    {
        _onIcon = onIcon ?? string.Empty;
        _offIcon = offIcon ?? string.Empty;
        _iconFont = font ?? throw new ArgumentNullException(nameof(font));
        return this;
    }

    /// <summary>
    /// Sets an icon that appears on the switch thumb.
    /// </summary>
    /// <param name="thumbIcon">Icon to show on the thumb</param>
    /// <param name="font">The icon font</param>
    /// <returns>This builder for method chaining</returns>
    public SwitchBuilder ThumbIcon(string thumbIcon, FontFile font)
    {
        _thumbIcon = thumbIcon ?? string.Empty;
        _iconFont = font ?? throw new ArgumentNullException(nameof(font));
        return this;
    }

    #endregion

    #region Style Configuration

    /// <summary>
    /// Sets the color scheme of the switch.
    /// </summary>
    /// <param name="color">The color scheme</param>
    /// <returns>This builder for method chaining</returns>
    public SwitchBuilder Color(OrigamiColor color)
    {
        _color = color;
        return this;
    }

    /// <summary>
    /// Sets the size of the switch.
    /// </summary>
    /// <param name="size">The switch size</param>
    /// <returns>This builder for method chaining</returns>
    public SwitchBuilder Size(OrigamiSize size)
    {
        _size = size;
        return this;
    }

    #endregion

    #region Event Configuration

    /// <summary>
    /// Sets the change handler for the switch.
    /// </summary>
    /// <param name="onChange">The change event handler</param>
    /// <returns>This builder for method chaining</returns>
    public SwitchBuilder OnChange(Action<bool> onChange)
    {
        _onChange = onChange;
        return this;
    }

    #endregion

    #region Build Method

    /// <summary>
    /// Builds and returns the configured switch as an ElementBuilder.
    /// </summary>
    /// <returns>An ElementBuilder representing the switch</returns>
    public ElementBuilder Build()
    {
        var theme = Origami.Theme;
        var dimensions = GetSwitchDimensions(_size);

        // Create main container
        var container = _paper.Box($"origami-switch-container-{_id}", _intId)
            .LayoutType(LayoutType.Row)
            .Width(UnitValue.Auto)
            .Height(dimensions.Height);

        using (container.Enter())
        {
            // Add label if provided
            if (!string.IsNullOrEmpty(_label) && _labelFont != null)
            {
                var fontSize = theme.GetFontSize(_size);
                var labelColor = _isDisabled ? 
                    Color32.FromArgb((int)(255 * theme.DisableOpacity), theme.Foreground.Base) : 
                    theme.Foreground.Base;

                _paper.Box($"origami-switch-label-{_id}")
                    .Width(UnitValue.Auto)
                    .Text(_label, _labelFont)
                    .TextColor(labelColor)
                    .Alignment(TextAlignment.MiddleLeft)
                    .FontSize(fontSize)
                    .Margin(0, 8, 0, 0)
                    .IsNotInteractable()
                    .IsNotFocusable();
            }

            // Create switch track
            var track = _paper.Box($"origami-switch-track-{_id}")
                .Width(dimensions.TrackWidth)
                .Height(dimensions.TrackHeight)
                .Rounded(dimensions.TrackHeight / 2)
                .LayoutType(LayoutType.Row)
                .Transition(GuiProp.BackgroundColor, 0.25f, Easing.CubicInOut);

            // Apply track styling
            ApplyTrackStyling(track, theme);

            // Handle interaction
            if (!_isDisabled && !_isReadOnly && _onChange != null)
            {
                track.OnClick(_ => _onChange(!_isOn));
            }
            else
            {
                track.IsNotInteractable();
            }

            if (_isDisabled)
            {
                track.IsNotFocusable();
            }

            using (track.Enter())
            {
                // Add track icons if provided
                if (!string.IsNullOrEmpty(_onIcon) || !string.IsNullOrEmpty(_offIcon))
                {
                    AddTrackIcons(theme, dimensions);
                }

                // Add thumb
                AddThumb(theme, dimensions);
            }
        }

        return container;
    }

    #endregion

    #region Private Helper Methods

    private void ApplyTrackStyling(ElementBuilder track, OrigamiTheme theme)
    {
        var themeColor = theme.GetColor(_color);
        float opacity = _isDisabled ? theme.DisableOpacity : 1.0f;

        if (_isOn)
        {
            var onColor = System.Drawing.Color.FromArgb((int)(255 * opacity), themeColor.Base);
            track.BackgroundColor(onColor);
            
            if (!_isDisabled)
            {
                track.Hovered.BackgroundColor(System.Drawing.Color.FromArgb((int)(255 * theme.HoverOpacity), onColor));
            }
        }
        else
        {
            var offColor = System.Drawing.Color.FromArgb((int)(255 * opacity), theme.Content3.Base);
            track.BackgroundColor(offColor);
            
            if (!_isDisabled)
            {
                track.Hovered.BackgroundColor(System.Drawing.Color.FromArgb((int)(255 * theme.HoverOpacity), offColor));
            }
        }
    }

    private void AddTrackIcons(OrigamiTheme theme, SwitchDimensions dimensions)
    {
        if (!string.IsNullOrEmpty(_onIcon) && _iconFont != null)
        {
            var iconColor = _isOn ? theme.Foreground.Base : Prowl.Vector.Color.Transparent;
            if (_isDisabled) iconColor = Prowl.Vector.Color32.FromArgb((int)(255 * theme.DisableOpacity), iconColor);

            _paper.Box($"origami-switch-on-icon-{_id}")
                .Text(_onIcon, _iconFont)
                .TextColor(iconColor)
                .Alignment(TextAlignment.MiddleCenter)
                .FontSize(dimensions.IconSize)
                .Transition(GuiProp.TextColor, 0.25f, Easing.CubicInOut)
                .IsNotInteractable()
                .IsNotFocusable();
        }

        if (!string.IsNullOrEmpty(_offIcon) && _iconFont != null)
        {
            var iconColor = !_isOn ? theme.Content4.Base : Prowl.Vector.Color.Transparent;
            if (_isDisabled) iconColor = Prowl.Vector.Color32.FromArgb((int)(255 * theme.DisableOpacity), iconColor);

            _paper.Box($"origami-switch-off-icon-{_id}")
                .Text(_offIcon, _iconFont)
                .TextColor(iconColor)
                .Alignment(TextAlignment.MiddleCenter)
                .FontSize(dimensions.IconSize)
                .Transition(GuiProp.TextColor, 0.25f, Easing.CubicInOut)
                .IsNotInteractable()
                .IsNotFocusable();
        }
    }

    private void AddThumb(OrigamiTheme theme, SwitchDimensions dimensions)
    {
        var thumb = _paper.Box($"origami-switch-thumb-{_id}")
            .Width(dimensions.ThumbSize)
            .Height(dimensions.ThumbSize)
            .Rounded(dimensions.ThumbSize / 2)
            .BackgroundColor(theme.Foreground.Base)
            .PositionType(PositionType.SelfDirected)
            .Top((dimensions.TrackHeight - dimensions.ThumbSize) / 2)
            .Left(_isOn ? dimensions.TrackWidth - dimensions.ThumbSize - dimensions.ThumbMargin : dimensions.ThumbMargin)
            .Transition(GuiProp.Left, 0.25f, Easing.CubicInOut)
            .IsNotInteractable()
            .IsNotFocusable();

        if (_isDisabled)
        {
            thumb.BackgroundColor(Prowl.Vector.Color32.FromArgb((int)(255 * theme.DisableOpacity), theme.Foreground.Base));
        }

        // Add thumb icon if provided
        if (!string.IsNullOrEmpty(_thumbIcon) && _iconFont != null)
        {
            using (thumb.Enter())
            {
                var thumbIconColor = _isDisabled ?
                    Prowl.Vector.Color32.FromArgb((int)(255 * theme.DisableOpacity), theme.Content1.Base) : 
                    theme.Content1.Base;

                _paper.Box($"origami-switch-thumb-icon-{_id}")
                    .Text(_thumbIcon, _iconFont)
                    .TextColor(thumbIconColor)
                    .Alignment(TextAlignment.MiddleCenter)
                    .FontSize(dimensions.IconSize)
                    .IsNotInteractable()
                    .IsNotFocusable();
            }
        }
    }

    private struct SwitchDimensions
    {
        public float TrackWidth;
        public float TrackHeight;
        public float ThumbSize;
        public float ThumbMargin;
        public float Height;
        public float IconSize;
    }

    private SwitchDimensions GetSwitchDimensions(OrigamiSize size)
    {
        return size switch
        {
            OrigamiSize.Small => new SwitchDimensions
            {
                TrackWidth = 44,
                TrackHeight = 24,
                ThumbSize = 18,
                ThumbMargin = 4,
                Height = 24,
                IconSize = 10
            },
            OrigamiSize.Medium => new SwitchDimensions
            {
                TrackWidth = 52,
                TrackHeight = 30,
                ThumbSize = 24,
                ThumbMargin = 4,
                Height = 30,
                IconSize = 12
            },
            OrigamiSize.Large => new SwitchDimensions
            {
                TrackWidth = 60,
                TrackHeight = 36,
                ThumbSize = 30,
                ThumbMargin = 4,
                Height = 36,
                IconSize = 14
            },
            _ => throw new InvalidOperationException()
        };
    }

    #endregion
}
