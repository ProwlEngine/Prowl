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
        using (paper.Column("origami-example").Height(paper.Auto).Enter())
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

            // Divider Section
            paper.Box("spacer8").Height(20);

            // Divider title
            paper.Box("divider-title")
                .Text("Divider Examples", font)
                .FontSize(24)
                .Height(60)
                .TextColor(Origami.Theme.Foreground.Base)
                .Alignment(Prowl.PaperUI.TextAlignment.MiddleCenter);

            paper.Box("spacer9").Height(10);

            // Horizontal dividers
            paper.Box("horizontal-dividers-label")
                .Text("Horizontal Dividers", font)
                .FontSize(16)
                .Height(30)
                .TextColor(Origami.Theme.Foreground.Base)
                .Alignment(Prowl.PaperUI.TextAlignment.MiddleLeft);

            // Basic horizontal divider
            Origami.Divider("basic-divider").Build().Margin(0, 0, 5, 5);

            // Thick horizontal divider
            Origami.Divider("thick-divider")
                .Thickness(4)
                .Color(OrigamiColor.Primary)
                .Build().Margin(0, 0, 5, 5);

            // Colored horizontal divider
            Origami.Divider("colored-divider")
                .Thickness(2)
                .Color(OrigamiColor.Success)
                .Build().Margin(0, 0, 5, 5);

            paper.Box("spacer10").Height(10);

            // Vertical dividers
            paper.Box("vertical-dividers-label")
                .Text("Vertical Dividers", font)
                .FontSize(16)
                .Height(30)
                .TextColor(Origami.Theme.Foreground.Base)
                .Alignment(Prowl.PaperUI.TextAlignment.MiddleLeft);

            using (paper.Row("vertical-dividers-row").Height(100).Enter())
            {
                paper.Box("text1").Text("Content", font).Width(paper.Auto).TextColor(Origami.Theme.Foreground.Base).Alignment(Prowl.PaperUI.TextAlignment.MiddleCenter).Margin(10, 0, 0, 0);

                // Basic vertical divider
                Origami.Divider("vertical-divider1")
                    .Vertical()
                    .Build().Margin(10, 10, 0, 0);

                paper.Box("text2").Text("More", font).Width(paper.Auto).TextColor(Origami.Theme.Foreground.Base).Alignment(Prowl.PaperUI.TextAlignment.MiddleCenter).Margin(10, 0, 0, 0);

                // Thick colored vertical divider
                Origami.Divider("vertical-divider2")
                    .Vertical()
                    .Thickness(4)
                    .Color(OrigamiColor.Warning)
                    .Build().Margin(10, 10, 0, 0);

                paper.Box("text3").Text("Content", font).Width(paper.Auto).TextColor(Origami.Theme.Foreground.Base).Alignment(Prowl.PaperUI.TextAlignment.MiddleCenter).Margin(10, 0, 0, 0);
            }

            // Switch Section
            paper.Box("spacer11").Height(20);

            // Switch title
            paper.Box("switch-title")
                .Text("Switch Examples", font)
                .FontSize(24)
                .Height(60)
                .TextColor(Origami.Theme.Foreground.Base)
                .Alignment(Prowl.PaperUI.TextAlignment.MiddleCenter);

            paper.Box("spacer12").Height(10);

            using (paper.Row("basic-switches").Height(paper.Auto).Enter())
            {
                // Basic switch - off
                Origami.Switch("basic-switch-off")
                    .Label("Basic Switch (Off)", font)
                    .IsOn(false)
                    .OnChange(value => Console.WriteLine($"Basic switch changed to: {value}"))
                    .Build().Margin(0, 0, 10, 0);

                // Basic switch - on
                Origami.Switch("basic-switch-on")
                    .Label("Basic Switch (On)", font)
                    .IsOn(true)
                    .OnChange(value => Console.WriteLine($"Basic switch changed to: {value}"))
                    .Build().Margin(0, 0, 10, 0);
            }

            paper.Box("spacer13").Height(10);

            using (paper.Row("switch-sizes").Height(paper.Auto).Enter())
            {
                // Small switch
                Origami.Switch("small-switch")
                    .Label("Small", font)
                    .Size(OrigamiSize.Small)
                    .IsOn(true)
                    .Build().Margin(0, paper.Stretch());

                // Medium switch
                Origami.Switch("medium-switch")
                    .Label("Medium", font)
                    .Size(OrigamiSize.Medium)
                    .IsOn(true)
                    .Build().Margin(0, paper.Stretch());

                // Large switch
                Origami.Switch("large-switch")
                    .Label("Large", font)
                    .Size(OrigamiSize.Large)
                    .IsOn(true)
                    .Build().Margin(0, paper.Stretch());
            }

            paper.Box("spacer14").Height(10);

            using (paper.Row("switch-colors").Height(paper.Auto).Enter())
            {
                // Primary switch
                Origami.Switch("primary-switch")
                    .Label("Primary", font)
                    .Color(OrigamiColor.Primary)
                    .IsOn(true)
                    .Build().Margin(0, 0, 8, 0);

                // Secondary switch
                Origami.Switch("secondary-switch")
                    .Label("Secondary", font)
                    .Color(OrigamiColor.Secondary)
                    .IsOn(true)
                    .Build().Margin(0, 0, 8, 0);

                // Success switch
                Origami.Switch("success-switch")
                    .Label("Success", font)
                    .Color(OrigamiColor.Success)
                    .IsOn(true)
                    .Build().Margin(0, 0, 8, 0);

                // Warning switch
                Origami.Switch("warning-switch")
                    .Label("Warning", font)
                    .Color(OrigamiColor.Warning)
                    .IsOn(true)
                    .Build().Margin(0, 0, 8, 0);

                // Danger switch
                Origami.Switch("danger-switch")
                    .Label("Danger", font)
                    .Color(OrigamiColor.Danger)
                    .IsOn(true)
                    .Build().Margin(0, 0, 8, 0);
            }

            paper.Box("spacer15").Height(10);

            using (paper.Row("switch-icons").Height(paper.Auto).Enter())
            {
                // Switch with track icons
                Origami.Switch("track-icons-switch")
                    .Label("Track Icons", font)
                    .Icons("✓", "✗", font)
                    .IsOn(true)
                    .Build().Margin(0, 0, 10, 0);

                // Switch with thumb icon
                Origami.Switch("thumb-icon-switch")
                    .Label("Thumb Icon", font)
                    .ThumbIcon("⚙", font)
                    .Color(OrigamiColor.Success)
                    .IsOn(false)
                    .Build().Margin(0, 0, 10, 0);

                // Switch with both icons
                Origami.Switch("both-icons-switch")
                    .Label("Both Icons", font)
                    .Icons("✓", "✗", font)
                    .ThumbIcon("●", font)
                    .Color(OrigamiColor.Warning)
                    .IsOn(true)
                    .Build().Margin(0, 0, 10, 0);
            }

            paper.Box("spacer16").Height(10);

            using (paper.Row("switch-states").Height(paper.Auto).Enter())
            {
                // Disabled switch
                Origami.Switch("disabled-switch")
                    .Label("Disabled Switch", font)
                    .IsOn(true)
                    .IsDisabled(true)
                    .Build().Margin(0, 0, 10, 0);

                // Read-only switch
                Origami.Switch("readonly-switch")
                    .Label("Read-Only Switch", font)
                    .IsOn(true)
                    .IsReadOnly(true)
                    .Build().Margin(0, 0, 10, 0);
            }
        }
    }

}
