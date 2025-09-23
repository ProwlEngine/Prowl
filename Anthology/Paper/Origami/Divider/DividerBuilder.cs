using System.Drawing;
using System.Runtime.CompilerServices;

using Prowl.PaperUI.LayoutEngine;

namespace Prowl.PaperUI.Themes.Origami.Divider;

/// <summary>
/// Builder class for creating dividers with the Origami design system.
/// Provides simple horizontal and vertical dividers with customizable thickness and colors.
/// </summary>
public class DividerBuilder
{
    private readonly Paper _paper;
    private readonly string _stringId;
    private readonly int _intId;
    private readonly int _lineId;
    private OrigamiColor _color = OrigamiColor.Primary;
    private int _thickness = 1;
    private bool _isVertical = false;

    /// <summary>
    /// Initializes a new DividerBuilder with the specified Paper instance and unique identifier.
    /// </summary>
    /// <param name="paper">The Paper UI instance</param>
    /// <param name="stringID">String identifier for the element</param>
    /// <param name="intID">Integer identifier useful for when creating elements in loops</param>
    /// <param name="lineID">Line number based identifier (auto-provided as Source Line Number)</param>
    internal DividerBuilder(Paper paper, string stringID, int intID = 0, [CallerLineNumber] int lineID = 0)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
        _stringId = _stringId ?? throw new ArgumentNullException(nameof(_stringId));
        _intId = intID;
        _lineId = lineID;
    }

    #region Style Configuration

    /// <summary>
    /// Sets the color of the divider.
    /// </summary>
    /// <param name="color">The color scheme</param>
    /// <returns>This builder for method chaining</returns>
    public DividerBuilder Color(OrigamiColor color)
    {
        _color = color;
        return this;
    }

    /// <summary>
    /// Sets the thickness of the divider in pixels.
    /// </summary>
    /// <param name="thickness">Thickness (1, 2, or 4 pixels)</param>
    /// <returns>This builder for method chaining</returns>
    public DividerBuilder Thickness(int thickness)
    {
        _thickness = Math.Clamp(thickness, 1, 4);
        return this;
    }

    /// <summary>
    /// Makes the divider vertical instead of horizontal.
    /// </summary>
    /// <param name="vertical">True for vertical, false for horizontal</param>
    /// <returns>This builder for method chaining</returns>
    public DividerBuilder Vertical(bool vertical = true)
    {
        _isVertical = vertical;
        return this;
    }

    #endregion

    #region Build Method

    /// <summary>
    /// Builds and returns the configured divider as an ElementBuilder.
    /// </summary>
    /// <returns>An ElementBuilder representing the divider</returns>
    public ElementBuilder Build()
    {
        var theme = Origami.Theme;
        var dividerColor = theme.Divider.Base;

        // Override with theme color if not using default
        if (_color != OrigamiColor.Primary)
        {
            dividerColor = theme.GetColor(_color).Base;
        }

        var divider = _paper.Box(_stringId, _intId, _lineId)
            .BackgroundColor(dividerColor)
            .IsNotInteractable()
            .IsNotFocusable();

        if (_isVertical)
        {
            // Vertical divider - stretch height, fixed width
            divider.Width(_thickness)
                   .Height(UnitValue.StretchOne);
        }
        else
        {
            // Horizontal divider - stretch width, fixed height
            divider.Width(UnitValue.StretchOne)
                   .Height(_thickness);
        }

        return divider;
    }

    #endregion
}
