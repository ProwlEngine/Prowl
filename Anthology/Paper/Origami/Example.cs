using Prowl.PaperUI.Themes.Origami.Button;
using Prowl.Scribe;

namespace Prowl.PaperUI.Themes.Origami;

/// <summary>
/// Example usage of the Origami Component Library.
/// This class demonstrates how to initialize Origami and use its components.
/// </summary>
public static class OrigamiExample
{
    /// <summary>
    /// Example of how to set up and use Origami components with the simplified API.
    /// Call this method after initializing your Paper UI instance.
    /// </summary>
    /// <param name="paper">Your Paper UI instance</param>
    /// <param name="font">A font file to use for text rendering</param>
    public static void ShowExample(Paper paper, FontFile font)
    {
        // 1. Initialize Origami with your Paper instance
        Origami.Initialize(paper);
        
        // 2. Create a UI layout with Origami components
        using (paper.Column("origami-example").Enter())
        {
            // Title
            paper.Box("example-title")
                .Text("Origami Button Examples", font)
                .FontSize(24)
                .Height(60)
                .TextColor(Origami.Theme.Foreground.Base)
                .Alignment(Prowl.PaperUI.TextAlignment.MiddleCenter);

            // Spacing
            paper.Box("spacer1").Height(10);

            // Button variants row
            using (paper.Row("button-variants").Height(paper.Auto).Enter())
            {
                // Solid button
                Origami.Button("SolidBtn")
                    .Text("Solid", font)
                    .Variant(ButtonVariant.Solid)
                    .Build().Margin(0, 10, 0, 0);

                // Faded button  
                Origami.Button("FadedBtn")
                    .Text("Faded", font)
                    .Variant(ButtonVariant.Faded)
                    .Build().Margin(0, 10, 0, 0);

                // Bordered button
                Origami.Button("BorderedBtn")
                    .Text("Bordered", font)
                    .Variant(ButtonVariant.Bordered)
                    .Build().Margin(0, 10, 0, 0);

                // Light button
                Origami.Button("LightBtn")
                    .Text("Light", font)
                    .Variant(ButtonVariant.Light)
                    .Build().Margin(0, 10, 0, 0);

                // Ghost button
                Origami.Button("GhostBtn")
                    .Text("Ghost", font)
                    .Variant(ButtonVariant.Ghost)
                    .Build().Margin(0, 10, 0, 0);

                // Shadow button
                Origami.Button("ShadowBtn")
                    .Text("Shadow", font)
                    .Variant(ButtonVariant.Shadow)
                    .Build().Margin(0, 10, 0, 0);
            }

            // Spacing
            paper.Box("spacer2").Height(10);

            // Button sizes row
            using (paper.Row("button-sizes").Height(paper.Auto).Enter())
            {
                // Small button
                Origami.Button("small-btn")
                    .Text("Small", font)
                    .Size(OrigamiSize.Small)
                    .Build().Margin(0, 10, paper.Stretch(), paper.Stretch());

                // Medium button
                Origami.Button("medium-btn")
                    .Text("Medium", font)
                    .Size(OrigamiSize.Medium)
                    .Build().Margin(0, 10, paper.Stretch(), paper.Stretch());

                // Large button
                Origami.Button("large-btn")
                    .Text("Large", font)
                    .Size(OrigamiSize.Large)
                    .Build().Margin(0, 10, paper.Stretch(), paper.Stretch());
            }

            // Spacing
            paper.Box("spacer3").Height(10);

            // Button colors row
            using (paper.Row("button-colors").Height(paper.Auto).Enter())
            {
                // Different color variants
                Origami.Button("primary-btn")
                    .Text("Primary", font)
                    .Color(OrigamiColor.Primary)
                    .Build().Margin(0, 10, 0, 0);

                Origami.Button("secondary-btn")
                    .Text("Secondary", font)
                    .Color(OrigamiColor.Secondary)
                    .Build().Margin(0, 10, 0, 0);

                Origami.Button("success-btn")
                    .Text("Success", font)
                    .Color(OrigamiColor.Success)
                    .Build().Margin(0, 10, 0, 0);

                Origami.Button("warning-btn")
                    .Text("Warning", font)
                    .Color(OrigamiColor.Warning)
                    .Build().Margin(0, 10, 0, 0);

                Origami.Button("danger-btn")
                    .Text("Danger", font)
                    .Color(OrigamiColor.Danger)
                    .Build().Margin(0, 10, 0, 0);
            }

            // Spacing
            paper.Box("spacer4").Height(10);

            // Border radius row
            using (paper.Row("button-radius").Height(paper.Auto).Enter())
            {
                // Different radius variants
                Origami.Button("none-radius-btn")
                    .Text("None", font)
                    .Radius(OrigamiRadius.None)
                    .Build().Margin(0, 10, 0, 0);

                Origami.Button("small-radius-btn")
                    .Text("Small", font)
                    .Radius(OrigamiRadius.Small)
                    .Build().Margin(0, 10, 0, 0);

                Origami.Button("medium-radius-btn")
                    .Text("Medium", font)
                    .Radius(OrigamiRadius.Medium)
                    .Build().Margin(0, 10, 0, 0);

                Origami.Button("large-radius-btn")
                    .Text("Large", font)
                    .Radius(OrigamiRadius.Large)
                    .Build().Margin(0, 10, 0, 0);

                Origami.Button("full-radius-btn")
                    .Text("Full", font)
                    .Radius(OrigamiRadius.Full).Build();
            }

            // Spacing
            paper.Box("spacer5").Height(10);

            // Icon buttons row
            using (paper.Row("icon-buttons").Height(paper.Auto).Enter())
            {
                // Button with icon and text
                Origami.Button("icon-text-btn")
                    .Text("Save", font)
                    .Icon("⚙️", font)
                    .Variant(ButtonVariant.Solid)
                    .Build().Margin(0, 10, 0, 0);

                // Icon-only button
                Origami.Button("icon-only-btn")
                    .Icon("⚙️", font)
                    .Variant(ButtonVariant.Ghost)
                    .IsIconOnly(true)
                    .Build().Margin(0, 10, 0, 0);

                // Icon-only with different radius
                Origami.Button("round-icon-btn")
                    .Icon("➕", font)
                    .Variant(ButtonVariant.Light)
                    .IsIconOnly(true)
                    .Radius(OrigamiRadius.Full)
                    .Build().Margin(0, 10, 0, 0);
            }

            // Spacing
            paper.Box("spacer6").Height(10);

            // Spinner buttons row
            using (paper.Row("spinner-buttons").Height(paper.Auto).Enter())
            {
                // Button with spinner replacing content
                Origami.Button("spinner-replace-btn")
                    .Text("Done Loading!", font)
                    .IsLoading(true)
                    .SpinnerPlacement(SpinnerPosition.Replace)
                    .Build().Margin(0, 10, 0, 0);

                // Button with spinner before text
                Origami.Button("spinner-before-btn")
                    .Text("Done Loading!", font)
                    .IsLoading(true)
                    .SpinnerPlacement(SpinnerPosition.Before)
                    .Build().Margin(0, 10, 0, 0);

                // Button with spinner after text
                Origami.Button("spinner-after-btn")
                    .Text("Done Loading!", font)
                    .IsLoading(true)
                    .SpinnerPlacement(SpinnerPosition.After)
                    .Build().Margin(0, 10, 0, 0);
            }

            // Spacing
            paper.Box("spacer7").Height(10);

            // Full-width button
            Origami.Button("fullwidth-btn")
                .Text("Full Width Button", font)
                .Variant(ButtonVariant.Solid)
                .FullWidth()
                .OnClick(() => Console.WriteLine("Full width button clicked!"))
                .Build();

            // Disabled button
            Origami.Button("disabled-btn")
                .Text("Disabled Button", font)
                .IsDisabled(true)
                .Build()
                .Margin(0, 0, 10, 0);
        }
    }

}
