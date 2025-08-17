using System.Drawing;

namespace Prowl.PaperUI
{
    /// <summary>
    /// Describes a gradient brush used for filling shapes.
    /// Values are relative to the element's rectangle unless otherwise noted.
    /// </summary>
    public struct Gradient
    {
        public GradientType Type;

        // Common colors
        public Color Color1;
        public Color Color2;

        // Linear gradient parameters (relative start/end points)
        public double X1;
        public double Y1;
        public double X2;
        public double Y2;

        // Radial gradient parameters (relative center, radii are relative to min(width, height))
        public double InnerRadius;
        public double OuterRadius;

        // Box gradient parameters (relative center, size and radii relative to element size)
        public double Width;
        public double Height;
        public float Radius;
        public float Feather;

        public static readonly Gradient None = new Gradient { Type = GradientType.None };

        public static Gradient Linear(double x1, double y1, double x2, double y2, Color color1, Color color2)
        {
            return new Gradient
            {
                Type = GradientType.Linear,
                X1 = x1,
                Y1 = y1,
                X2 = x2,
                Y2 = y2,
                Color1 = color1,
                Color2 = color2
            };
        }

        public static Gradient Radial(double centerX, double centerY, double innerRadius, double outerRadius, Color innerColor, Color outerColor)
        {
            return new Gradient
            {
                Type = GradientType.Radial,
                X1 = centerX,
                Y1 = centerY,
                InnerRadius = innerRadius,
                OuterRadius = outerRadius,
                Color1 = innerColor,
                Color2 = outerColor
            };
        }

        public static Gradient Box(double centerX, double centerY, double width, double height, float radius, float feather, Color innerColor, Color outerColor)
        {
            return new Gradient
            {
                Type = GradientType.Box,
                X1 = centerX,
                Y1 = centerY,
                Width = width,
                Height = height,
                Radius = radius,
                Feather = feather,
                Color1 = innerColor,
                Color2 = outerColor
            };
        }

        /// <summary>
        /// Interpolates between two gradients. If the gradient types differ the
        /// transition snaps to the end gradient when complete.
        /// </summary>
        public static Gradient Lerp(Gradient start, Gradient end, double t)
        {
            if (t >= 1) return end;

            // If the gradient types differ, hold the start until the end of the transition
            if (start.Type != end.Type)
                return t < 1 ? start : end;

            Color LerpColor(Color a, Color b)
            {
                int r = (int)(a.R + (b.R - a.R) * t);
                int g = (int)(a.G + (b.G - a.G) * t);
                int bVal = (int)(a.B + (b.B - a.B) * t);
                int aVal = (int)(a.A + (b.A - a.A) * t);
                return Color.FromArgb(aVal, r, g, bVal);
            }

            return new Gradient
            {
                Type = start.Type,
                Color1 = LerpColor(start.Color1, end.Color1),
                Color2 = LerpColor(start.Color2, end.Color2),
                X1 = start.X1 + (end.X1 - start.X1) * t,
                Y1 = start.Y1 + (end.Y1 - start.Y1) * t,
                X2 = start.X2 + (end.X2 - start.X2) * t,
                Y2 = start.Y2 + (end.Y2 - start.Y2) * t,
                InnerRadius = start.InnerRadius + (end.InnerRadius - start.InnerRadius) * t,
                OuterRadius = start.OuterRadius + (end.OuterRadius - start.OuterRadius) * t,
                Width = start.Width + (end.Width - start.Width) * t,
                Height = start.Height + (end.Height - start.Height) * t,
                Radius = start.Radius + (end.Radius - start.Radius) * (float)t,
                Feather = start.Feather + (end.Feather - start.Feather) * (float)t
            };
        }
    }
}
