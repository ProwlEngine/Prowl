using System.Drawing;
using System.Runtime.CompilerServices;

using Prowl.PaperUI;
using Prowl.PaperUI.Themes.Origami.Button;

namespace Prowl.PaperUI.Themes.Origami;

/// <summary>
/// Main entry point for the Origami UI Component Library.
/// Provides a fluent builder API for creating customizable UI components with consistent theming.
/// </summary>
public static class Origami
{
    private static Paper? _paper;
    private static OrigamiTheme _currentTheme = new OrigamiTheme();

    /// <summary>
    /// Initializes Origami with the Paper UI instance.
    /// Must be called before using any Origami components.
    /// </summary>
    /// <param name="paper">The Paper UI instance to use for rendering components</param>
    public static void Initialize(Paper paper)
    {
        _paper = paper ?? throw new ArgumentNullException(nameof(paper));
    }

    /// <summary>
    /// Gets the current Paper UI instance.
    /// </summary>
    internal static Paper Paper => _paper ?? throw new InvalidOperationException("Origami not initialized. Call Origami.Initialize() first.");

    /// <summary>
    /// Sets the global theme for all Origami components.
    /// </summary>
    /// <param name="theme">The theme to apply</param>
    public static void SetTheme(OrigamiTheme theme)
    {
        _currentTheme = theme ?? throw new ArgumentNullException(nameof(theme));
    }

    /// <summary>
    /// Gets the current theme.
    /// </summary>
    public static OrigamiTheme Theme => _currentTheme;

    /// <summary>
    /// Creates a new Button builder.
    /// </summary>
    /// <param name="id">Unique identifier for the button</param>
    /// <param name="intID">Line number based identifier (auto-provided as Source Line Number)</param>
    /// <returns>A ButtonBuilder for configuring the button</returns>
    public static ButtonBuilder Button(string id, [CallerLineNumber] int intID = 0)
    {
        return new ButtonBuilder(Paper, id, intID);
    }

}
