using System.Drawing;

using Prowl.Quill;
using Prowl.Vector;

namespace Prowl.PaperUI.Themes.Origami;


/// <summary>
/// Placement options for spinner within buttons.
/// </summary>
public enum SpinnerPosition
{
    /// <summary>Replace the content with spinner.</summary>
    Replace,
    /// <summary>Show spinner before the content.</summary>
    Before,
    /// <summary>Show spinner after the content.</summary>
    After
}

/// <summary>
/// Utility class for creating animated spinners that can be reused across components.
/// Provides customizable loading indicators with smooth animations.
/// </summary>
public static class SpinnerUtil
{
    /// <summary>
    /// Configuration options for spinner appearance and behavior.
    /// </summary>
    public struct SpinnerConfig
    {
        /// <summary>Size of the spinner in pixels.</summary>
        public double Size { get; set; }
        
        /// <summary>Color of the spinner.</summary>
        public Color Color { get; set; }
        
        /// <summary>Width of the spinner stroke.</summary>
        public double StrokeWidth { get; set; }
        
        /// <summary>Animation speed multiplier (1.0 = normal speed).</summary>
        public double Speed { get; set; }

        /// <summary>Creates a default spinner configuration.</summary>
        public static SpinnerConfig Default => new()
        {
            Size = 16,
            Color = Color.FromArgb(255, 100, 116, 139), // slate-500
            StrokeWidth = 2,
            Speed = 1.0
        };

        /// <summary>Creates a small spinner configuration.</summary>
        public static SpinnerConfig Small => new()
        {
            Size = 12,
            Color = Color.FromArgb(255, 100, 116, 139),
            StrokeWidth = 1.5,
            Speed = 1.0
        };

        /// <summary>Creates a large spinner configuration.</summary>
        public static SpinnerConfig Large => new()
        {
            Size = 24,
            Color = Color.FromArgb(255, 100, 116, 139),
            StrokeWidth = 3,
            Speed = 1.0
        };
    }

    /// <summary>
    /// Adds a spinner animation to an element using AddActionElement.
    /// The spinner will be centered within the element's bounds.
    /// </summary>
    /// <param name="paper">Paper UI instance</param>
    /// <param name="config">Spinner configuration</param>
    /// <returns>Action that can be used with AddActionElement</returns>
    public static Action<Canvas, Rect> CreateSpinner(Paper paper, SpinnerConfig config)
    {
        return (canvas, rect) => {
            var centerX = rect.x + rect.width / 2;
            var centerY = rect.y + rect.height / 2;
            var radius = config.Size / 2;
            
            // Calculate rotation based on time
            var time = paper.Time;
            var rotation = (time * config.Speed * 2) % (Math.PI * 2); // Full rotation every second at speed 1.0
            var rotDegrees = MathD.ToDeg(rotation);

            canvas.SaveState();
            
            // Move to center and rotate
            canvas.TransformBy(Transform2D.CreateTranslation(centerX, centerY));
            canvas.TransformBy(Transform2D.CreateRotate(rotDegrees));

            // Draw the spinner arc
            canvas.BeginPath();
            canvas.Arc(0, 0, radius, 0, Math.PI * 1.5); // 3/4 circle
            canvas.SetStrokeColor(config.Color);
            canvas.SetStrokeWidth(config.StrokeWidth);
            canvas.Stroke();

            canvas.RestoreState();
        };
    }

    /// <summary>
    /// Creates a spinner with theme-appropriate colors.
    /// </summary>
    /// <param name="paper">Paper UI instance</param>
    /// <param name="theme">Origami theme for color consistency</param>
    /// <param name="size">Size variant</param>
    /// <returns>Action that can be used with AddActionElement</returns>
    public static Action<Canvas, Rect> CreateThemedSpinner(Paper paper, OrigamiTheme theme, OrigamiSize size = OrigamiSize.Medium)
    {
        var config = size switch
        {
            OrigamiSize.Small => SpinnerConfig.Small,
            OrigamiSize.Large => SpinnerConfig.Large,
            _ => SpinnerConfig.Default
        };

        // Use theme's muted foreground color
        config.Color = theme.Divider.Base;

        return CreateSpinner(paper, config);
    }

    /// <summary>
    /// Creates a spinner with a specific color.
    /// </summary>
    /// <param name="paper">Paper UI instance</param>
    /// <param name="color">Color for the spinner</param>
    /// <param name="size">Size variant</param>
    /// <returns>Action that can be used with AddActionElement</returns>
    public static Action<Canvas, Rect> CreateColoredSpinner(Paper paper, Color color, OrigamiSize size = OrigamiSize.Medium)
    {
        var config = size switch
        {
            OrigamiSize.Small => SpinnerConfig.Small,
            OrigamiSize.Large => SpinnerConfig.Large,
            _ => SpinnerConfig.Default
        };

        config.Color = color;
        return CreateSpinner(paper, config);
    }

    /// <summary>
    /// Creates a dots-style spinner (three animated dots).
    /// </summary>
    /// <param name="paper">Paper UI instance</param>
    /// <param name="config">Spinner configuration</param>
    /// <returns>Action that can be used with AddActionElement</returns>
    public static Action<Canvas, Rect> CreateDotsSpinner(Paper paper, SpinnerConfig config)
    {
        return (canvas, rect) => {
            var centerX = rect.x + rect.width / 2;
            var centerY = rect.y + rect.height / 2;
            var time = paper.Time * config.Speed;
            
            // Three dots with staggered animation
            var dotSize = config.Size / 6;
            var spacing = config.Size / 3;

            canvas.SaveState();

            for (int i = 0; i < 3; i++)
            {
                var x = centerX + (i - 1) * spacing;
                var y = centerY;
                
                // Animate opacity based on time and dot index
                var animationOffset = i * 0.3; // Stagger the animation
                var opacity = (Math.Sin(time * 3 + animationOffset) + 1) / 2; // 0 to 1
                opacity = Math.Max(0.3, opacity); // Minimum visibility
                
                var dotColor = Color.FromArgb((int)(255 * opacity), config.Color);

                canvas.BeginPath();
                canvas.Circle(x, y, dotSize);
                canvas.SetFillColor(dotColor);
                canvas.Fill();
            }

            canvas.RestoreState();
        };
    }

    /// <summary>
    /// Creates a pulse-style spinner (growing and shrinking circle).
    /// </summary>
    /// <param name="paper">Paper UI instance</param>
    /// <param name="config">Spinner configuration</param>
    /// <returns>Action that can be used with AddActionElement</returns>
    public static Action<Canvas, Rect> CreatePulseSpinner(Paper paper, SpinnerConfig config)
    {
        return (canvas, rect) => {
            var centerX = rect.x + rect.width / 2;
            var centerY = rect.y + rect.height / 2;
            var time = paper.Time * config.Speed;
            
            // Pulsing radius
            var baseRadius = config.Size / 3;
            var pulseRadius = baseRadius + (Math.Sin(time * 4) + 1) / 2 * baseRadius * 0.5;
            
            // Pulsing opacity
            var opacity = (Math.Sin(time * 4) + 1) / 2 * 0.7 + 0.3; // 0.3 to 1.0
            var pulseColor = Color.FromArgb((int)(255 * opacity), config.Color);

            canvas.SaveState();
            
            canvas.BeginPath();
            canvas.Circle(centerX, centerY, pulseRadius);
            canvas.SetFillColor(pulseColor);
            canvas.Fill();

            canvas.RestoreState();
        };
    }

    /// <summary>
    /// Gets the recommended spinner size for a given OrigamiSize.
    /// </summary>
    /// <param name="size">The size variant</param>
    /// <returns>Spinner size in pixels</returns>
    public static double GetSpinnerSize(OrigamiSize size)
    {
        return size switch
        {
            OrigamiSize.Small => 12,
            OrigamiSize.Large => 24,
            _ => 16
        };
    }

    /// <summary>
    /// Helper method to create a spinner element that can be used in layouts.
    /// </summary>
    /// <param name="paper">Paper UI instance</param>
    /// <param name="id">Unique identifier for the spinner element</param>
    /// <param name="config">Spinner configuration</param>
    /// <returns>ElementBuilder with spinner animation</returns>
    public static ElementBuilder CreateSpinnerElement(Paper paper, string id, SpinnerConfig config)
    {
        return paper.Box($"spinner-{id}")
            .Width(config.Size)
            .Height(config.Size)
            .OnPostLayout((handle, rect) => {
                paper.AddActionElement(ref handle, CreateSpinner(paper, config));
            });
    }

    /// <summary>
    /// Helper method to create a themed spinner element.
    /// </summary>
    /// <param name="paper">Paper UI instance</param>
    /// <param name="id">Unique identifier for the spinner element</param>
    /// <param name="theme">Origami theme</param>
    /// <param name="size">Size variant</param>
    /// <returns>ElementBuilder with themed spinner animation</returns>
    public static ElementBuilder CreateThemedSpinnerElement(Paper paper, string id, OrigamiTheme theme, OrigamiSize size = OrigamiSize.Medium)
    {
        var spinnerSize = GetSpinnerSize(size);
        return paper.Box($"spinner-{id}")
            .Width(spinnerSize)
            .Height(spinnerSize)
            .OnPostLayout((handle, rect) => {
                paper.AddActionElement(ref handle, CreateThemedSpinner(paper, theme, size));
            });
    }
}
