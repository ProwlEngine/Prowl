using System.Runtime.InteropServices;

using System.Numerics;
using System.Drawing;
using Texture2D = System.Object;
using Prowl.PaperUI;

namespace Prowl.PaperUI.Graphics
{
    [StructLayout(LayoutKind.Sequential)]
	public struct Brush
	{
		public Transform Transform;
		public Vector2 Extent;
		public float Radius;
		public float Feather;
		public Color InnerColor;
		public Color OuterColor;
		public Texture2D? Image;
		
		public Brush(Color color)
        {
            Transform = Transform.Identity;
            Extent = new Vector2();
            Radius = 0.0f;
			Feather = 1.0f;
			InnerColor = color;
			OuterColor = color;
			Image = null;
		}

		public void Zero()
		{
			Transform.Zero();
			Extent = Vector2.Zero;
			Radius = 0;
			Feather = 0;
			InnerColor = Color.Transparent;
			OuterColor = Color.Transparent;
			Image = null;
		}

		public static Brush Interpolate(Brush start, Brush end, float t)
		{
            // Clamp t to [0,1] range to ensure valid interpolation
            t = MathF.Max(0, MathF.Min(1, t));

			// Create a new transform for the result
			var result = new Brush();

            result.Transform = Transform.Interpolate(start.Transform, end.Transform, t);
            result.Extent = Vector2.Lerp(start.Extent, end.Extent, t);
			result.Radius = start.Radius + (end.Radius - start.Radius) * t;
            result.Feather = start.Feather + (end.Feather - start.Feather) * t;
            result.InnerColor = Color.FromArgb(
                (int)(start.InnerColor.A + (end.InnerColor.A - start.InnerColor.A) * t),
                (int)(start.InnerColor.R + (end.InnerColor.R - start.InnerColor.R) * t),
                (int)(start.InnerColor.G + (end.InnerColor.G - start.InnerColor.G) * t),
                (int)(start.InnerColor.B + (end.InnerColor.B - start.InnerColor.B) * t));
            result.OuterColor = Color.FromArgb(
                (int)(start.OuterColor.A + (end.OuterColor.A - start.OuterColor.A) * t),
                (int)(start.OuterColor.R + (end.OuterColor.R - start.OuterColor.R) * t),
                (int)(start.OuterColor.G + (end.OuterColor.G - start.OuterColor.G) * t),
                (int)(start.OuterColor.B + (end.OuterColor.B - start.OuterColor.B) * t));

            // Note: Image interpolate makes no sense, we no do dat, just if t > 0.5f we use end image
            result.Image = t > 0.5f ? end.Image : start.Image;

            return result;
        }
    }
}