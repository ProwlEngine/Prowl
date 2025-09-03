using System.Drawing;

using Prowl.PaperUI;
using Prowl.PaperUI.LayoutEngine;

namespace Shared
{
    public static class Components
    {
        public static void DefineStyles()
        {
            Button.DefineStyles();
            Input.DefineStyles();
            TextArea.DefineStyles();
            Switch.DefineStyles();
        }
    }

    public static class Button
    {
        public static void DefineStyles()
        {
            // Primary button variant
            PaperDemo.Gui.CreateStyleFamily("shadcs-button-primary")
                .Base(new StyleTemplate()
                    .Height(40)
                    .Rounded(8)
                    .BackgroundColor(Themes.primaryColor)
                    .Transition(GuiProp.BackgroundColor, 0.2)
                    .Transition(GuiProp.ScaleX, 0.1)
                    .Transition(GuiProp.ScaleY, 0.1))
                .Hovered(new StyleTemplate()
                    .BackgroundColor(Color.FromArgb(150, Themes.primaryColor)))
                .Active(new StyleTemplate()
                    .Scale(0.95)
                    .BackgroundColor(Color.FromArgb(200, Themes.primaryColor)))
                .Register();

            // Secondary button variant
            PaperDemo.Gui.CreateStyleFamily("shadcs-button-secondary")
                .Base(new StyleTemplate()
                    .Height(40)
                    .Rounded(8)
                    .BackgroundColor(Themes.secondaryColor)
                    .Transition(GuiProp.BackgroundColor, 0.2)
                    .Transition(GuiProp.ScaleX, 0.1)
                    .Transition(GuiProp.ScaleY, 0.1))
                .Hovered(new StyleTemplate()
                    .BackgroundColor(Color.FromArgb(150, Themes.secondaryColor)))
                .Active(new StyleTemplate()
                    .Scale(0.95)
                    .BackgroundColor(Color.FromArgb(200, Themes.primaryColor)))
                .Register();

            // Outline button variant
            PaperDemo.Gui.CreateStyleFamily("shadcs-button-outline")
                .Base(new StyleTemplate()
                    .Height(40)
                    .Rounded(8)
                    .BackgroundColor(Themes.backgroundColor)
                    .BorderColor(Themes.secondaryColor)
                    .BorderWidth(1)
                    .Transition(GuiProp.BackgroundColor, 0.2)
                    .Transition(GuiProp.ScaleX, 0.1)
                    .Transition(GuiProp.ScaleY, 0.1))
                .Hovered(new StyleTemplate()
                    .BackgroundColor(Color.FromArgb(150, Themes.backgroundColor)))
                .Active(new StyleTemplate()
                    .Scale(0.95)
                    .BackgroundColor(Color.FromArgb(200, Themes.primaryColor)))
                .Register();

            // Icon button styles
            PaperDemo.Gui.CreateStyleFamily("shadcs-icon-button-primary")
                .Base(new StyleTemplate()
                    .Width(40)
                    .Height(40)
                    .Rounded(8)
                    .BackgroundColor(Themes.primaryColor)
                    // .Transition(GuiProp.BackgroundColor, 0.2)
                    .Transition(GuiProp.Rounded, 0.2))
                .Hovered(new StyleTemplate()
                    .BackgroundColor(Color.FromArgb(100, Themes.primaryColor))
                    .Rounded(20))
                .Register();

            // Icon button styles secondary
            PaperDemo.Gui.CreateStyleFamily("shadcs-icon-button-secondary")
                .Base(new StyleTemplate()
                    .Width(40)
                    .Height(40)
                    .Rounded(8)
                    .BackgroundColor(Themes.secondaryColor)
                    // .Transition(GuiProp.BackgroundColor, 0.2)
                    .Transition(GuiProp.Rounded, 0.2))
                .Hovered(new StyleTemplate()
                    .BackgroundColor(Color.FromArgb(100, Themes.secondaryColor))
                    .Rounded(20))
                .Register();
        }

        public static ElementBuilder Primary(string id, string text = "")
        {
            return PaperDemo.Gui.Box("shadcs-button-" + id)
                .Text(text, Fonts.arial).TextColor(Themes.textColor).Alignment(TextAlignment.MiddleCenter)
                .Style("shadcs-button-primary");
        }

        public static ElementBuilder Secondary(string id, string text = "")
        {
            return PaperDemo.Gui.Box("shadcs-button-" + id)
                .Text(text, Fonts.arial).TextColor(Themes.lightTextColor).Alignment(TextAlignment.MiddleCenter)
                .Style("shadcs-button-secondary");
        }

        public static ElementBuilder Outline(string id, string text = "")
        {
            return PaperDemo.Gui.Box("shadcs-button-" + id)
                .Text(text, Fonts.arial).TextColor(Themes.lightTextColor).Alignment(TextAlignment.MiddleCenter)
                .Style("shadcs-button-outline");
        }

        public static ElementBuilder IconPrimary(string id, string text = "")
        {
            return PaperDemo.Gui.Box("shadcs-button-" + id)
                .Text(text, Fonts.arial).TextColor(Themes.textColor).Alignment(TextAlignment.MiddleCenter)
                .Style("shadcs-icon-button-primary");
        }

        public static ElementBuilder IconSecondary(string id, string text = "")
        {
            return PaperDemo.Gui.Box("shadcs-button-" + id)
                .Text(text, Fonts.arial).TextColor(Themes.lightTextColor).Alignment(TextAlignment.MiddleCenter)
                .Style("shadcs-icon-button-secondary");
        }
    }

    public static class Input
    {
        public static void DefineStyles()
        {
            // Text field styles
            PaperDemo.Gui.CreateStyleFamily("shadcs-text-field-secondary")
                .Base(new StyleTemplate()
                    .Width(300)
                    .Height(40)
                    .Rounded(8)
                    .BackgroundColor(Themes.secondaryColor)
                    .BorderColor(Color.Transparent)
                    .BorderWidth(0)
                    .Transition(GuiProp.BorderColor, 0.2)
                    .Transition(GuiProp.BorderWidth, 0.2))
                .Focused(new StyleTemplate()
                    .BorderColor(Themes.primaryColor)
                    .BorderWidth(1)
                    .BackgroundColor(Themes.secondaryColor))
                .Register();
        }

        public static ElementBuilder Secondary(string id, string value, Action<string> onChange = null, string placeholder = "")
        {
            ElementBuilder parent = PaperDemo.Gui.Box("shadcs-input-" + id).Style("shadcs-text-field-secondary").TabIndex(0);
            using (parent.Enter())
            {
                PaperDemo.Gui.Box("area")
                    .Margin(8, UnitValue.StretchOne)
                    .HookToParent()
                    .IsNotInteractable()
                    .Width(UnitValue.StretchOne)
                    .Height(19)
                    .FontSize(19)
                    .TextField(value, Fonts.arial, onChange, Themes.textColor, placeholder, Color.FromArgb(100, Themes.textColor));
            }
            return parent;

            //return PaperDemo.Gui.Box("shadcs-input-" + id)
            //    .TextField(value, Fonts.arial, onChange, Themes.textColor, placeholder, Color.FromArgb(100, Themes.textColor))
            //    .Style("shadcs-text-field-secondary");
            //    //.SetScroll(Scroll.ScrollX);
        }
    }

    public static class TextArea
    {
        public static void DefineStyles()
        {
            // Text field styles
            PaperDemo.Gui.CreateStyleFamily("shadcs-text-area-secondary")
                .Base(new StyleTemplate()
                    .Width(300)
                    .Height(Prowl.PaperUI.LayoutEngine.UnitValue.Auto)
                    //.MaxHeight(100)
                    .Rounded(8)
                    .BackgroundColor(Themes.secondaryColor)
                    .BorderColor(Color.Transparent)
                    .BorderWidth(0)
                    .Transition(GuiProp.BorderColor, 0.2)
                    .Transition(GuiProp.BorderWidth, 0.2))
                .Focused(new StyleTemplate()
                    .BorderColor(Themes.primaryColor)
                    .BorderWidth(1)
                    .BackgroundColor(Themes.secondaryColor))
                .Register();
        }

        public static ElementBuilder Secondary(string id, string value, Action<string> onChange = null, string placeholder = "")
        {
            ElementBuilder parent = PaperDemo.Gui.Box("shadcs-textarea-" + id).Style("shadcs-text-area-secondary").TabIndex(1);
            using (parent.Enter())
            {
                PaperDemo.Gui.Box("area")
                    .Margin(8)
                    .HookToParent()
                    .IsNotInteractable()
                    .Width(UnitValue.StretchOne)
                    .Height(UnitValue.Auto)
                    .FontSize(19)
                    .TextArea(value, Fonts.arial, onChange, placeholder, Themes.textColor, Color.FromArgb(100, Themes.textColor));
            }
            return parent;
        }
    }

    public static class Slider
    {

        public static ElementBuilder Primary(string id, double sliderValue, Action<double> onChange)
        {
            var parent = PaperDemo.Gui.Box("shadcs-slider-" + id)
                .Height(20)
                .Rounded(10)
                .BackgroundColor(Themes.backgroundColor)
                //.Style(BoxStyle.SolidRounded(Color.FromArgb(30, 0, 0, 0), 10f))
                .Margin(0, 0, 20, 0)
                .OnHeld((e) =>
                {
                    double parentWidth = e.ElementRect.width;
                    double pointerX = e.PointerPosition.x - e.ElementRect.x;

                    // Calculate new slider value based on pointer position
                    sliderValue = Math.Clamp(pointerX / parentWidth, 0f, 1f);
                    onChange.Invoke(sliderValue);
                });

            var cleanup = parent.Enter();

            // Filled part of slider
            using (PaperDemo.Gui.Box("SliderFill")
                .Width(PaperDemo.Gui.Percent(sliderValue * 100))
                .MinWidth(10)
                .RoundedLeft(10)
                .BackgroundColor(Themes.primaryColor)
                //.Style(BoxStyle.SolidRounded(primaryColor, 10f))
                .Enter())
            {
                // Slider handle
                using (PaperDemo.Gui.Box("SliderHandle")
                    .Left(PaperDemo.Gui.Percent(100, -10))
                    .Width(20)
                    .Height(20)
                    .Rounded(10)
                    .BackgroundColor(Themes.textColor)
                    //.Style(BoxStyle.SolidRounded(textColor, 10f))
                    .PositionType(PositionType.SelfDirected)
                    .Enter()) { }
            }

            cleanup.Dispose();

            return parent;
            // // OLD
            // using (PaperDemo.Gui.Box("SliderTrack")
            //                 .Height(20)
            //                 .BackgroundColor(Color.FromArgb(30, 0, 0, 0))
            //                 //.Style(BoxStyle.SolidRounded(Color.FromArgb(30, 0, 0, 0), 10f))
            //                 .Margin(0, 0, 20, 0)
            //                 .OnHeld((e) =>
            //                 {
            //                     double parentWidth = e.ElementRect.width;
            //                     double pointerX = e.PointerPosition.x - e.ElementRect.x;

            //                     // Calculate new slider value based on pointer position
            //                     sliderValue = Math.Clamp(pointerX / parentWidth, 0f, 1f);
            //                 })
            //                 .Enter())
            // {
            //     // Filled part of slider
            //     using (Gui.Box("SliderFill")
            //         .Width(Gui.Percent(sliderValue * 100))
            //         .BackgroundColor(Themes.primaryColor)
            //         //.Style(BoxStyle.SolidRounded(primaryColor, 10f))
            //         .Enter())
            //     {
            //         // Slider handle
            //         using (Gui.Box("SliderHandle")
            //             .Left(Gui.Percent(100, -10))
            //             .Width(20)
            //             .Height(20)
            //             .BackgroundColor(Themes.textColor)
            //             //.Style(BoxStyle.SolidRounded(textColor, 10f))
            //             .PositionType(PositionType.SelfDirected)
            //             .Enter()) { }
            //     }
            // }
        }

        public static ElementBuilder Secondary(string id, double sliderValue, Action<double> onChange)
        {
            var parent = PaperDemo.Gui.Box("shadcs-slider-" + id)
                .Height(20)
                .Rounded(10)
                .BackgroundColor(Themes.backgroundColor)
                //.Style(BoxStyle.SolidRounded(Color.FromArgb(30, 0, 0, 0), 10f))
                .Margin(0, 0, 20, 0)
                .OnHeld((e) =>
                {
                    double parentWidth = e.ElementRect.width;
                    double pointerX = e.PointerPosition.x - e.ElementRect.x;

                    // Calculate new slider value based on pointer position
                    sliderValue = Math.Clamp(pointerX / parentWidth, 0f, 1f);
                    onChange.Invoke(sliderValue);
                });

            var cleanup = parent.Enter();

            // Filled part of slider
            using (PaperDemo.Gui.Box("SliderFill")
                .Width(PaperDemo.Gui.Percent(sliderValue * 100))
                .MinWidth(10)
                .RoundedLeft(10)
                .BackgroundColor(Themes.secondaryColor)
                //.Style(BoxStyle.SolidRounded(primaryColor, 10f))
                .Enter())
            {
                // Slider handle
                using (PaperDemo.Gui.Box("SliderHandle")
                    .Left(PaperDemo.Gui.Percent(100, -10))
                    .Width(20)
                    .Height(20)
                    .Rounded(10)
                    .BackgroundColor(Themes.textColor)
                    //.Style(BoxStyle.SolidRounded(textColor, 10f))
                    .PositionType(PositionType.SelfDirected)
                    .Enter()) { }
            }

            cleanup.Dispose();

            return parent;
        }
    }

    public static class Switch
    {
        public static void DefineStyles()
        {

            // Toggle switch styles
            PaperDemo.Gui.CreateStyleFamily("toggle")
                .Base(new StyleTemplate()
                    .Width(60)
                    .Height(30)
                    .Rounded(20)
                    .Transition(GuiProp.BackgroundColor, 0.25, Easing.CubicInOut))
                .Register();

            PaperDemo.Gui.RegisterStyle("toggle-on", new StyleTemplate()
                .BackgroundColor(Themes.primaryColor));

            PaperDemo.Gui.RegisterStyle("toggle-off", new StyleTemplate()
                .BackgroundColor(Color.FromArgb(100, Themes.secondaryColor)));

            PaperDemo.Gui.RegisterStyle("toggle-dot", new StyleTemplate()
                .Width(24)
                .Height(24)
                .Rounded(20)
                .BackgroundColor(Color.White)
                //.PositionType(PositionType.SelfDirected)
                .Top(PaperDemo.Gui.Pixels(3))
                .Transition(GuiProp.Left, 0.25, Easing.CubicInOut));
        }

        public static ElementBuilder Primary(string id, bool isOn)
        {
            // Toggle switch - much simpler with styles!
            // bool isOn = toggleState[i];
            // int index = i;

            ElementBuilder builder;
            using ((builder = PaperDemo.Gui.Box("shadcs-switch-" + id)
                .Style("toggle")
                .StyleIf(isOn, "toggle-on")
                .StyleIf(!isOn, "toggle-off")).Enter())
            { 
                PaperDemo.Gui.Box($"ToggleDot{id}")
                    .Style("toggle-dot")
                    .Left(PaperDemo.Gui.Pixels(isOn ? 32 : 4));
            }
            return builder;
        }
    }

    public static class PieChart
    {
        public static ElementBuilder Primary(string id, double[] values, double startAngle)
        {
            // "Analysis" mock content
            ElementBuilder builder;
            using ((builder = PaperDemo.Gui.Box("shadcs-piechart-" + id)
                .Margin(20)).Enter())
            {

                // Add a simple pie chart visualization
                PaperDemo.Gui.AddActionElement((vg, rect) =>
                {
                    double centerX = rect.x + rect.width / 2;
                    double centerY = rect.y + rect.height / 2;
                    double radius = Math.Min(rect.width, rect.height) * 0.4f;

                    // double startAngle = 0;
                    // double[] values = { sliderValue, 0.2f, 0.15f, 0.25f, 0.1f };

                    // Normalize Values
                    double total = values.Sum();
                    for (int i = 0; i < values.Length; i++)
                        values[i] /= total;


                    for (int i = 0; i < values.Length; i++)
                    {
                        // Calculate angles
                        double angle = values[i] * Math.PI * 2;
                        double endAngle = startAngle + angle;

                        // Draw pie slice
                        vg.BeginPath();
                        vg.MoveTo(centerX, centerY);
                        vg.Arc(centerX, centerY, radius, startAngle, endAngle);
                        vg.LineTo(centerX, centerY);
                        vg.SetFillColor(Themes.colorPalette[i % Themes.colorPalette.Length]);
                        vg.Fill();

                        // Draw outline
                        vg.BeginPath();
                        vg.MoveTo(centerX, centerY);
                        vg.Arc(centerX, centerY, radius, startAngle, endAngle);
                        vg.LineTo(centerX, centerY);
                        vg.SetStrokeColor(Color.White);
                        vg.SetStrokeWidth(2);
                        vg.Stroke();

                        // Draw percentage labels
                        double labelAngle = startAngle + angle / 2;
                        double labelRadius = radius * 0.7f;
                        double labelX = centerX + Math.Cos(labelAngle) * labelRadius;
                        double labelY = centerY + Math.Sin(labelAngle) * labelRadius;

                        string label = $"{values[i] * 100:F0}%";
                        vg.SetFillColor(Color.White);
                        //vg.TextAlign(Align.Center | Align.Middle);
                        //vg.FontSize(16);
                        //vg.Text(labelX, labelY, label);
                        vg.DrawText(label, labelX, labelY, Color.White, 18, Fonts.arial);

                        // Move to next slice
                        startAngle = endAngle;
                    }

                    // Draw center circle
                    vg.BeginPath();
                    vg.Circle(centerX, centerY, radius * 0.4f);
                    vg.SetFillColor(Color.White);
                    vg.Fill();

                    // Draw center text
                    // Draw center text
                    //vg.FillColor(textColor);
                    //vg.TextAlign(NvgSharp.Align.Center | NvgSharp.Align.Middle);
                    //vg.FontSize(20);
                    //vg.Text(centerX, centerY, $"Analytics\n{(sliderValue * 100):F0}%");
                    //vg.Text(fontSmall, $"Analytics\n{(sliderValue * 100):F0}%", centerX, centerY);
                });
            }
            return builder;
        }

        // // OLD
        // // "Analysis" mock content
        // using (Gui.Box("AnalyticsVisual")
        //     .Margin(20)
        //     .Enter())
        // {
        //     // Add a simple pie chart visualization
        //     Gui.AddActionElement((vg, rect) => {
        //         double centerX = rect.x + rect.width / 2;
        //         double centerY = rect.y + rect.height / 2;
        //         double radius = Math.Min(rect.width, rect.height) * 0.4f;

        //         double startAngle = 0;
        //         double[] values = { sliderValue, 0.2f, 0.15f, 0.25f, 0.1f };

        //         // Normalize Values
        //         double total = values.Sum();
        //         for (int i = 0; i < values.Length; i++)
        //             values[i] /= total;


        //         for (int i = 0; i < values.Length; i++)
        //         {
        //             // Calculate angles
        //             double angle = values[i] * Math.PI * 2;
        //             double endAngle = startAngle + angle;

        //             // Draw pie slice
        //             vg.BeginPath();
        //             vg.MoveTo(centerX, centerY);
        //             vg.Arc(centerX, centerY, radius, startAngle, endAngle);
        //             vg.LineTo(centerX, centerY);
        //             vg.SetFillColor(Themes.colorPalette[i % Themes.colorPalette.Length]);
        //             vg.Fill();

        //             // Draw outline
        //             vg.BeginPath();
        //             vg.MoveTo(centerX, centerY);
        //             vg.Arc(centerX, centerY, radius, startAngle, endAngle);
        //             vg.LineTo(centerX, centerY);
        //             vg.SetStrokeColor(Color.White);
        //             vg.SetStrokeWidth(2);
        //             vg.Stroke();

        //             // Draw percentage labels
        //             double labelAngle = startAngle + angle / 2;
        //             double labelRadius = radius * 0.7f;
        //             double labelX = centerX + Math.Cos(labelAngle) * labelRadius;
        //             double labelY = centerY + Math.Sin(labelAngle) * labelRadius;

        //             string label = $"{values[i] * 100:F0}%";
        //             vg.SetFillColor(Color.White);
        //             //vg.TextAlign(Align.Center | Align.Middle);
        //             //vg.FontSize(16);
        //             //vg.Text(labelX, labelY, label);
        //             vg.DrawText(Fonts.fontSmall, label, labelX, labelY, Color.White);

        //             // Move to next slice
        //             startAngle = endAngle;
        //         }

        //         // Draw center circle
        //         vg.BeginPath();
        //         vg.Circle(centerX, centerY, radius * 0.4f);
        //         vg.SetFillColor(Color.White);
        //         vg.Fill();

        //         // Draw center text
        //         // Draw center text
        //         //vg.FillColor(textColor);
        //         //vg.TextAlign(NvgSharp.Align.Center | NvgSharp.Align.Middle);
        //         //vg.FontSize(20);
        //         //vg.Text(centerX, centerY, $"Analytics\n{(sliderValue * 100):F0}%");
        //         //vg.Text(fontSmall, $"Analytics\n{(sliderValue * 100):F0}%", centerX, centerY);
        //     });
        // }
    }
}
