using System.Drawing;

using Prowl.PaperUI.Events;
using Prowl.PaperUI.LayoutEngine;
using Prowl.Scribe;

namespace Prowl.PaperUI.Themes.Origami.Button;

/// <summary>
/// Builder class for creating highly customizable buttons with the Origami design system.
/// Provides a fluent API for setting text, icons, styles, and behaviors.
/// </summary>
public class ButtonBuilder
{
    private readonly Paper _paper;
    private readonly string _id;
    private readonly int _intId;
    private string _text = string.Empty;
    private string _icon = string.Empty;
    private FontFile? _font;
    private FontFile? _iconFont;
    private ButtonVariant _variant = ButtonVariant.Solid;
    private OrigamiColor _color = OrigamiColor.Primary;
    private OrigamiSize _size = OrigamiSize.Medium;
    private OrigamiRadius _radius = OrigamiRadius.Small;
    private bool _fullWidth = false;
    private bool _isIconOnly = false;
    private bool _isDisabled = false;
    private bool _isLoading = false;
    private SpinnerPosition _spinnerPlacement = SpinnerPosition.Replace;
    private Action<ClickEvent>? _onClick;
    private Action<ElementEvent>? _onHover;
    private Action<ElementEvent>? _onLeave;

    /// <summary>
    /// Initializes a new ButtonBuilder with the specified Paper instance and unique identifier.
    /// </summary>
    /// <param name="paper">The Paper UI instance</param>
    /// <param name="id">Unique identifier for this button</param>
    internal ButtonBuilder(Paper paper, string id, int intID)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _id = id ?? throw new ArgumentNullException(nameof(id));
        _intId = intID;
    }

    #region Content Configuration

    /// <summary>
    /// Sets the text content of the button.
    /// </summary>
    /// <param name="text">The text to display</param>
    /// <returns>This builder for method chaining</returns>
    public ButtonBuilder Text(string text, FontFile font)
    {
        _text = text ?? string.Empty;
        _font = font ?? throw new ArgumentNullException(nameof(font));
        return this;
    }

    /// <summary>
    /// Sets an icon for the button. The icon will be displayed before the text.
    /// </summary>
    /// <param name="icon">The icon string (typically from an icon font)</param>
    /// <returns>This builder for method chaining</returns>
    public ButtonBuilder Icon(string icon, FontFile font)
    {
        _icon = icon ?? string.Empty;
        _iconFont = font ?? throw new ArgumentNullException(nameof(font));
        return this;
    }

    #endregion

    #region Style Configuration

    /// <summary>
    /// Sets the visual variant of the button.
    /// </summary>
    /// <param name="variant">The button variant</param>
    /// <returns>This builder for method chaining</returns>
    public ButtonBuilder Variant(ButtonVariant variant)
    {
        _variant = variant;
        return this;
    }

    /// <summary>
    /// Sets the color scheme of the button.
    /// </summary>
    /// <param name="color">The color scheme</param>
    /// <returns>This builder for method chaining</returns>
    public ButtonBuilder Color(OrigamiColor color)
    {
        _color = color;
        return this;
    }

    /// <summary>
    /// Sets the size of the button.
    /// </summary>
    /// <param name="size">The button size</param>
    /// <returns>This builder for method chaining</returns>
    public ButtonBuilder Size(OrigamiSize size)
    {
        _size = size;
        return this;
    }

    /// <summary>
    /// Sets the border radius of the button.
    /// </summary>
    /// <param name="radius">The border radius</param>
    /// <returns>This builder for method chaining</returns>
    public ButtonBuilder Radius(OrigamiRadius radius)
    {
        _radius = radius;
        return this;
    }

    /// <summary>
    /// Makes the button take the full width of its container.
    /// </summary>
    /// <param name="fullWidth">True to make the button full width</param>
    /// <returns>This builder for method chaining</returns>
    public ButtonBuilder FullWidth(bool fullWidth = true)
    {
        _fullWidth = fullWidth;
        return this;
    }

    /// <summary>
    /// Configures the button as icon-only (no text, just icon).
    /// </summary>
    /// <param name="isIconOnly">True to make the button icon-only</param>
    /// <returns>This builder for method chaining</returns>
    public ButtonBuilder IsIconOnly(bool isIconOnly = true)
    {
        _isIconOnly = isIconOnly;
        return this;
    }

    #endregion

    #region State Configuration

    /// <summary>
    /// Sets whether the button is disabled. Disabled buttons cannot be clicked and have reduced opacity.
    /// </summary>
    /// <param name="disabled">True to disable the button</param>
    /// <returns>This builder for method chaining</returns>
    public ButtonBuilder IsDisabled(bool disabled = true)
    {
        _isDisabled = disabled;
        return this;
    }

    /// <summary>
    /// Shows a loading spinner on the button.
    /// </summary>
    /// <param name="isLoading">True to show the spinner</param>
    /// <returns>This builder for method chaining</returns>
    public ButtonBuilder IsLoading(bool isLoading)
    {
        _isLoading = isLoading;
        return this;
    }

    /// <summary>
    /// Sets where the spinner should be placed relative to the button content.
    /// </summary>
    /// <param name="placement">The spinner placement</param>
    /// <returns>This builder for method chaining</returns>
    public ButtonBuilder SpinnerPlacement(SpinnerPosition placement)
    {
        _spinnerPlacement = placement;
        return this;
    }

    #endregion

    #region Event Handlers

    /// <summary>
    /// Sets the click handler for the button.
    /// </summary>
    /// <param name="onClick">The click event handler</param>
    /// <returns>This builder for method chaining</returns>
    public ButtonBuilder OnClick(Action<ClickEvent> onClick)
    {
        _onClick = onClick;
        return this;
    }

    /// <summary>
    /// Sets the click handler for the button with no parameters.
    /// </summary>
    /// <param name="onClick">The click event handler</param>
    /// <returns>This builder for method chaining</returns>
    public ButtonBuilder OnClick(Action onClick)
    {
        _onClick = onClick != null ? _ => onClick() : null;
        return this;
    }

    /// <summary>
    /// Sets the hover handler for when the mouse enters the button.
    /// </summary>
    /// <param name="onHover">The hover event handler</param>
    /// <returns>This builder for method chaining</returns>
    public ButtonBuilder OnHover(Action<ElementEvent> onHover)
    {
        _onHover = onHover;
        return this;
    }

    /// <summary>
    /// Sets the leave handler for when the mouse leaves the button.
    /// </summary>
    /// <param name="onLeave">The leave event handler</param>
    /// <returns>This builder for method chaining</returns>
    public ButtonBuilder OnLeave(Action<ElementEvent> onLeave)
    {
        _onLeave = onLeave;
        return this;
    }

    #endregion

    #region Build Method

    /// <summary>
    /// Builds and returns the configured button as an ElementBuilder.
    /// This method creates the actual UI element with all the specified properties.
    /// </summary>
    /// <returns>An ElementBuilder representing the button</returns>
    public ElementBuilder Build()
    {
        var theme = Origami.Theme;

        // Create the button element
        var button = _paper.Box($"origami-btn-{_id}", _intId)
            .LayoutType(LayoutType.Row)
            .Transition(GuiProp.BackgroundColor, 0.2)
            .Transition(GuiProp.ScaleX, 0.1)
            .Transition(GuiProp.ScaleY, 0.1)
            .Active
                .Scale(0.95)
                .End();

        // Apply size-specific height
        var height = GetHeightForSize(_size);
        button.Height(height);

        // Apply width settings
        if (_fullWidth)
        {
            button.Width(UnitValue.StretchOne);
        }
        else if (_isIconOnly || _isLoading && _spinnerPlacement == SpinnerPosition.Replace)
        {
            button.Width(height); // Square for icon-only buttons
        }
        else
        {
            button.MinWidth(GetMinWidthForSize(_size));
            button.Width(UnitValue.Auto);
        }

        // Apply border radius
        button.Rounded(theme.GetRadius(_radius));

        ApplyBackground(theme, _variant, button);

        // Apply disabled state
        if (_isDisabled || _isLoading)
        {
            button.IsNotFocusable();
            button.IsNotInteractable();
        }
        else
        {
            if (_onClick != null) button.OnClick(_onClick);
            if (_onHover != null) button.OnHover(_onHover);
            if (_onLeave != null) button.OnLeave(_onLeave);
        }

        // Add content (text and/or icon and/or spinner)
        SetupButtonContent(button, theme);

        return button;
    }

    #endregion

    #region Private Helper Methods

    private void ApplyBackground(OrigamiTheme theme, ButtonVariant variant, ElementBuilder button)
    {
        var bg = theme.GetColor(_color).Base;
        double opacity = _isDisabled || _isLoading ? theme.DisableOpacity : 1.0;
        bg = System.Drawing.Color.FromArgb((int)(255 * opacity), bg);

        if (variant == ButtonVariant.Solid)
        {
            button.BackgroundColor(bg);
            button.Hovered.BackgroundColor(System.Drawing.Color.FromArgb((int)(255 * theme.HoverOpacity), bg));
        }
        else if (variant == ButtonVariant.Faded)
        {
            button.BorderWidth(theme.GetBorderWidth(_size));
            button.BorderColor(theme.Content3.Base700);

            bg = theme.Content3.Base800;
            bg = System.Drawing.Color.FromArgb((int)(255 * opacity), bg);
            button.BackgroundColor(bg);

            button.Hovered.BorderColor(System.Drawing.Color.FromArgb((int)(255 * theme.HoverOpacity), theme.Content3.Base700));
            button.Hovered.BackgroundColor(System.Drawing.Color.FromArgb((int)(255 * theme.HoverOpacity), bg));
        }
        else if (variant == ButtonVariant.Bordered)
        {
            button.BorderWidth(theme.GetBorderWidth(_size));
            button.BorderColor(bg);

            button.Hovered.BorderColor(System.Drawing.Color.FromArgb((int)(255 * theme.HoverOpacity), bg));
        }
        else if (variant == ButtonVariant.Light)
        {
            button.Hovered.BackgroundColor(System.Drawing.Color.FromArgb(50, bg));
            button.Active.BackgroundColor(bg);
        }
        else if (variant == ButtonVariant.Ghost)
        {
            button.BorderWidth(theme.GetBorderWidth(_size));
            button.BorderColor(bg);
            button.Hovered.BorderColor(System.Drawing.Color.FromArgb((int)(255 * theme.HoverOpacity), bg));
            button.Hovered.BackgroundColor(System.Drawing.Color.FromArgb((int)(255 * theme.HoverOpacity), bg));
            button.Active.BorderColor(bg);
            button.Active.BackgroundColor(bg);
        }
        else if (variant == ButtonVariant.Shadow)
        {
            button.BackgroundColor(bg);
            button.BoxShadow(0, 5, 52, -40, bg);

            button.Hovered.BackgroundColor(System.Drawing.Color.FromArgb((int)(255 * theme.HoverOpacity), bg));
            button.Hovered.BoxShadow(0, 5, 52, -40, System.Drawing.Color.FromArgb((int)(255 * theme.HoverOpacity), bg));
        }


    }

    /// <summary>
    /// Sets up the button's content including text, icon, and spinner.
    /// </summary>
    private void SetupButtonContent(ElementBuilder button, OrigamiTheme theme)
    {
        var hasText = !string.IsNullOrEmpty(_text) && !_isIconOnly;
        var hasIcon = !string.IsNullOrEmpty(_icon);

        if (!hasText && !hasIcon && !_isLoading)
        {
            return; // Nothing to display
        }

        var textColor = theme.Foreground.Base;

        double opacity = _isDisabled || _isLoading ? theme.DisableOpacity : 1.0;
        textColor = System.Drawing.Color.FromArgb((int)(255 * opacity), textColor);

        // Handle spinner placement
        if (_isLoading && _spinnerPlacement == SpinnerPosition.Replace)
        {
            using (button.Enter())
            {
                AddSpinnerElement(theme, textColor).Margin(UnitValue.StretchOne);
            }
            return;
        }

        // Content layout
        using (button.Enter())
        {
            var fontSize = theme.GetFontSize(_size);

            if (_isLoading && (_spinnerPlacement == SpinnerPosition.Before || _spinnerPlacement == SpinnerPosition.After))
            {
                var halfSize = fontSize / 2;
                if (_spinnerPlacement == SpinnerPosition.Before)
                {
                    AddSpinnerElement(theme, textColor).Margin(halfSize, halfSize, UnitValue.StretchOne, UnitValue.StretchOne);
                }

                AddContentElements(hasText, hasIcon, textColor, theme);

                if (_spinnerPlacement == SpinnerPosition.After)
                {
                    AddSpinnerElement(theme, textColor).Margin(halfSize, halfSize, UnitValue.StretchOne, UnitValue.StretchOne);
                }
            }
            else
            {
                AddContentElements(hasText, hasIcon, textColor, theme);
            }
        }
    }

    /// <summary>
    /// Adds content elements (text and/or icon) to the button.
    /// </summary>
    private void AddContentElements(bool hasText, bool hasIcon, Color textColor, OrigamiTheme theme)
    {
        var fontSize = theme.GetFontSize(_size);

        if (hasIcon)
        {
            if (_isIconOnly)
            {
                _paper.Box($"origami-btn-icon-{_id}")
                    .IsNotInteractable().IsNotFocusable()
                    .Text(_icon, _iconFont ?? throw new InvalidOperationException("Font is required for button with icon"))
                    .TextColor(textColor)
                    .Alignment(TextAlignment.MiddleCenter)
                    .FontSize(fontSize);
            }
            else
            {
                _paper.Box($"origami-btn-icon-{_id}")
                    .IsNotInteractable().IsNotFocusable()
                    .MinWidth(fontSize)
                    .Width(UnitValue.Auto)
                    .Margin(fontSize, 0, 0, 0)
                    .Text(_icon, _iconFont ?? throw new InvalidOperationException("Font is required for button with icon"))
                    .TextColor(textColor)
                    .Alignment(TextAlignment.MiddleCenter)
                    .FontSize(fontSize);
            }
        }

        if (hasText)
        {
            _paper.Box($"origami-btn-text-{_id}")
                .IsNotInteractable() .IsNotFocusable()
                .MinWidth(UnitValue.Auto)
                .Width(UnitValue.StretchOne)
                .Margin(_isLoading ? fontSize / 2 : fontSize, 0)
                .Text(_isLoading ? "Loading" : _text, _font ?? throw new InvalidOperationException("Font is required for button with text"))
                .TextColor(textColor)
                .Alignment(TextAlignment.MiddleCenter)
                .FontSize(fontSize);
        }
    }

    /// <summary>
    /// Adds a spinner element.
    /// </summary>
    private ElementBuilder AddSpinnerElement(OrigamiTheme theme, Color textColor)
    {
        var spinnerSize = theme.GetFontSize(_size);// SpinnerUtil.GetSpinnerSize(_size);
        return _paper.Box($"origami-btn-spinner-{_id}")
            .IsNotInteractable().IsNotFocusable()
            .Size(spinnerSize)
            .OnPostLayout((handle, rect) => {
                _paper.AddActionElement(ref handle, SpinnerUtil.CreateColoredSpinner(_paper, textColor, _size));
            });
    }

    /// <summary>
    /// Gets the height for the specified size.
    /// </summary>
    private static double GetHeightForSize(OrigamiSize size)
    {
        return size switch
        {
            OrigamiSize.Small => 32,
            OrigamiSize.Medium => 40,
            OrigamiSize.Large => 48,
            _ => 40
        };
    }

    /// <summary>
    /// Gets the min width for the specified size.
    /// </summary>
    private static double GetMinWidthForSize(OrigamiSize size)
    {
        return size switch
        {
            OrigamiSize.Small => 64,
            OrigamiSize.Medium => 80,
            OrigamiSize.Large => 96,
            _ => 80
        };
    }

    #endregion
}
