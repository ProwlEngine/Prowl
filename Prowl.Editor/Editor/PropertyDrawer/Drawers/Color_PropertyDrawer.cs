// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Editor.Preferences;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.GUI.Layout;
using Prowl.Runtime.Rendering;

namespace Prowl.Editor.PropertyDrawers;

[Drawer(typeof(Color))]
public class Color_PropertyDrawer : PropertyDrawer
{
    private static AssetRef<Texture2D> _checkerboardTexture;
    public static Texture2D Checkerboard
    {
        get
        {
            if (!_checkerboardTexture.IsAvailable)
            {
                using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Prowl.Editor.EmbeddedResources.Checkerboard.png"))
                    _checkerboardTexture = Texture2DLoader.FromStream(stream);

                _checkerboardTexture.Res.Sampler.SetFilter(FilterType.Point, FilterType.Point);
            }

            return _checkerboardTexture.Res;
        }
    }


    const int HueWheelSegments = 128;

    private static List<Color32>? s_colorHues;
    private static List<Color32> ColorHues => s_colorHues ??= Enumerable.Range(0, HueWheelSegments)
        .Select(x => (Color32)Color.FromHSV((float)x / HueWheelSegments * 360f, 1f, 1f))
        .ToList();


    public override bool OnValueGUI(Gui gui, string ID, Type targetType, ref object? value)
    {
        bool changed = false;

        using (gui.Node($"{ID}_#ColorView").Expand().Enter())
        {
            string popupName = $"{ID}_#ColorPopup";

            if (gui.IsNodeHovered() && gui.IsPointerClick(MouseButton.Left))
                gui.OpenPopup(popupName, gui.CurrentNode.LayoutData.Rect.BottomLeft);

            if (gui.BeginPopup(popupName, out LayoutNode? node))
            {
                changed |= DrawColorPopup(gui, node, true, ref value);
            }

            Color col = (Color)value!;
            Color pure = new Color(col.r, col.g, col.b, 1);
            Color transparent = new Color(1, 1, 1, col.a);

            Rect rect = gui.CurrentNode.LayoutData.Rect;

            gui.Draw2D.DrawRectFilled(rect, pure, (float)EditorStylePrefs.Instance.ButtonRoundness);

            Rect footer = rect;
            footer.y += footer.height - 3;
            footer.height = 3;

            gui.Draw2D.DrawRectFilled(footer, transparent, (float)EditorStylePrefs.Instance.ButtonRoundness, CornerRounding.Bottom);
        }

        return changed;
    }


    protected bool DrawColorPopup(Gui gui, LayoutNode node, bool hasAlpha, ref object? colorValue)
    {
        Color color = (Color)colorValue!;

        float alpha = color.a;
        Color.ToHSV(color, out float hue, out float saturation, out float value);

        using (node.Scale(256, 382).Layout(LayoutType.Column).Padding(10).Enter())
        {
            Rect rootRect = gui.CurrentNode.LayoutData.Rect;
            double minHeight = Math.Min(rootRect.width, rootRect.height);

            using (gui.Node("HueWheel").Width(minHeight - 20).Height(minHeight - 20).Padding(55).Enter())
            {
                if (!gui.IsPointerDown(MouseButton.Left))
                    gui.SetNodeStorage(node, "Selected", 0);

                int selected = gui.GetNodeStorage(node, "Selected", 0);

                Rect cRect = gui.CurrentNode.LayoutData.Rect;
                double size = Math.Min(cRect.width, cRect.height) / 2;

                const float wheelWidth = 24;

                gui.Draw2D.DrawCircle(cRect.Center, (float)size - (wheelWidth / 2), ColorHues, HueWheelSegments, wheelWidth);

                Vector2 dir = new Vector2(MathD.Cos(hue * MathD.Deg2Rad), MathD.Sin(hue * MathD.Deg2Rad));

                gui.Draw2D.DrawLine(cRect.Center + dir * size, cRect.Center + dir * (size - wheelWidth + 1), Color.white, 3);

                Vector2 relativePtr = gui.PointerPos - cRect.Center;
                double len = relativePtr.magnitude;
                Vector2 ptrDir = relativePtr / len;

                if (selected == 0 && gui.IsPointerHovering() && gui.IsPointerClick(MouseButton.Left) && len <= size && len >= size - wheelWidth)
                    selected = 1;

                if (selected == 1)
                    hue = (float)(Math.Atan2(-ptrDir.y, -ptrDir.x) * MathD.Rad2Deg) + 180;

                using (gui.Node("SaturationValueRect").Expand().Enter())
                {
                    Rect svRect = gui.CurrentNode.LayoutData.Rect;

                    DrawSatValRect(gui, hue, svRect);

                    gui.Draw2D.DrawCircle(new Vector2(saturation * svRect.width, value * -1 * svRect.height) + svRect.BottomLeft, 6, Color.white, thickness: 2);

                    if (selected == 0 && gui.IsPointerHovering() && gui.IsPointerClick(MouseButton.Left))
                        selected = 2;

                    if (selected == 2)
                    {
                        relativePtr = gui.PointerPos - svRect.BottomLeft;

                        saturation = (float)MathD.Clamp01(relativePtr.x / svRect.width);
                        value = (float)MathD.Clamp01((relativePtr.y / svRect.height) * -1);
                    }
                }

                gui.SetNodeStorage(node, "Selected", selected);
            }

            using (gui.Node("ColorDisplay").Width(80).Top(10).Height(40).Enter())
            {
                Color pure = new Color(color.r, color.g, color.b, 1);

                Rect left = gui.CurrentNode.LayoutData.Rect;
                left.width *= 0.5f;

                Rect right = left;
                right.x += left.width;

                gui.Draw2D.DrawRectFilled(left, pure, (float)EditorStylePrefs.Instance.ButtonRoundness, CornerRounding.Left);

                gui.Draw2D.DrawImage(Checkerboard, right);
                gui.Draw2D.DrawRectFilled(right, color, (float)EditorStylePrefs.Instance.ButtonRoundness, CornerRounding.None);
            }

            if (hasAlpha)
                EditorGUI.InputFloat("ColorAlpha", ref alpha, 0, 10, 120, EditorGUI.InputFieldStyle);
        }

        Color newColor = Color.FromHSV(hue, saturation, value, alpha);

        if (color != newColor)
        {
            colorValue = newColor;
            return true;
        }

        return false;
    }


    // Hardware interpolation doesn't do HSV values properly, so manually interpolate multiple rects to help it a bit.
    private void DrawSatValRect(Gui gui, float hue, Rect rect)
    {
        int resolution = 4;

        float xOffset = (float)rect.width / resolution;
        float yOffset = (float)rect.height / resolution;

        Vector2 size = new(xOffset, yOffset);

        for (int x = 0; x < resolution; x++)
        {
            for (int y = 0; y < resolution; y++)
            {
                float satA = (float)x / resolution;
                float satB = ((float)x + 1) / resolution;

                float valA = 1 - ((float)y / resolution);
                float valB = 1 - (((float)y + 1) / resolution);

                Vector2 offset = new Vector2(xOffset * x, yOffset * y);
                gui.Draw2D.DrawRectFilledMultiColor(rect.Position + offset, size,
                    Color.FromHSV(hue, satA, valA),
                    Color.FromHSV(hue, satB, valA),
                    Color.FromHSV(hue, satA, valB),
                    Color.FromHSV(hue, satB, valB));
            }
        }
    }
}
