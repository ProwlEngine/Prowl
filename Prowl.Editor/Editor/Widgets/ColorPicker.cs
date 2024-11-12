// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.Editor.Preferences;
using Prowl.Icons;
using Prowl.Runtime;
using Prowl.Runtime.GUI;
using Prowl.Runtime.Rendering;

namespace Prowl.Editor;


public class ColorPickerContext
{
    public string Title;

    public float Hue;
    public float Saturation;
    public float Value;
    public float Alpha;

    public Color Color
    {
        get => Color.FromHSV(Hue, Saturation, Value, Alpha);
        set => Color.ToHSV(value, out Hue, out Saturation, out Value);
    }

    public bool IsNormalized;
    public bool HasAlpha;

    public Action? OnComplete;
}


public class ColorPickerDialog : EditorWindow
{
    private ColorPickerContext? _context;
    public ColorPickerContext? Context
    {
        get => _context;
        set
        {
            if (_context != value && _context != null)
                _context.OnComplete?.Invoke();

            _context = value;
        }
    }

    private AssetRef<Texture2D> _checkerboardTexture;
    public Texture2D Checkerboard
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


    protected override bool Center { get; } = false;
    protected override double Width { get; } = 256;
    protected override double Height { get; } = 382;
    protected override bool BackgroundFade { get; } = false;
    protected override bool IsDockable => false;
    protected override bool LockSize => true;
    protected override bool TitleBar => false;

    private int _selected;

    const int HueWheelSegments = 128;

    private static List<Color32>? s_colorHues;
    private static List<Color32> ColorHues => s_colorHues ??= Enumerable.Range(0, HueWheelSegments)
        .Select(x => (Color32)Color.FromHSV((float)x / HueWheelSegments * 360f, 1f, 1f))
        .ToList();


    public ColorPickerDialog(ColorPickerContext context) : base()
    {
        Context = context;
        Title = context.Title;
    }


    public void CenterOn(Rect fitRect)
    {
        X = fitRect.BottomLeft.x - Width;
        Y = fitRect.BottomLeft.y;

        X = Math.Min(X, gui.ScreenRect.x + (gui.ScreenRect.width - Width));
        Y = Math.Min(Y, gui.ScreenRect.y + (gui.ScreenRect.height - Height));
    }


    protected override void Draw()
    {
        if (Context == null)
        {
            _selected = 0;
            isOpened = false;
            return;
        }

        if (!gui.IsPointerDown(MouseButton.Left))
            _selected = 0;

        using (gui.Node("Root").Expand().Layout(LayoutType.Column).Padding(10).Enter())
        {
            Rect rootRect = gui.CurrentNode.LayoutData.Rect;
            double minHeight = Math.Min(rootRect.width, rootRect.height);

            using (gui.Node("HueWheel").Width(minHeight - 20).Height(minHeight - 20).Padding(55).Enter())
            {
                Rect cRect = gui.CurrentNode.LayoutData.Rect;
                double size = Math.Min(cRect.width, cRect.height) / 2;

                const float wheelWidth = 24;

                gui.Draw2D.DrawCircle(cRect.Center, (float)size - (wheelWidth / 2), ColorHues, HueWheelSegments, wheelWidth);

                Vector2 dir = new Vector2(MathD.Cos(Context.Hue * MathD.Deg2Rad), MathD.Sin(Context.Hue * MathD.Deg2Rad));

                gui.Draw2D.DrawLine(cRect.Center + dir * size, cRect.Center + dir * (size - wheelWidth), Color.white, 3);

                Vector2 relativePtr = gui.PointerPos - cRect.Center;
                double len = relativePtr.magnitude;
                Vector2 ptrDir = relativePtr / len;

                if (_selected == 0 && gui.IsPointerHovering() && gui.IsPointerClick(MouseButton.Left) && len <= size && len >= size - wheelWidth)
                    _selected = 1;

                if (_selected == 1)
                    Context.Hue = (float)(Math.Atan2(-ptrDir.y, -ptrDir.x) * MathD.Rad2Deg) + 180;

                using (gui.Node("SaturationValueRect").Expand().Enter())
                {
                    Rect svRect = gui.CurrentNode.LayoutData.Rect;

                    DrawSatValRect(svRect);

                    gui.Draw2D.DrawCircle(new Vector2(Context.Saturation * svRect.width, Context.Value * -1 * svRect.height) + svRect.BottomLeft, 6, Color.white, thickness: 2);

                    if (_selected == 0 && gui.IsPointerHovering() && gui.IsPointerClick(MouseButton.Left))
                        _selected = 2;

                    if (_selected == 2)
                    {
                        relativePtr = gui.PointerPos - svRect.BottomLeft;

                        Context.Saturation = (float)MathD.Clamp01(relativePtr.x / svRect.width);
                        Context.Value = (float)MathD.Clamp01((relativePtr.y / svRect.height) * -1);
                    }
                }
            }

            using (gui.Node("ColorDisplay").Width(80).Top(10).Height(40).Enter())
            {
                Color col = Context.Color;
                Color pure = new Color(col.r, col.g, col.b, 1);

                Rect left = gui.CurrentNode.LayoutData.Rect;
                left.width *= 0.5f;

                Rect right = left;
                right.x += left.width;

                gui.Draw2D.DrawRectFilled(left, pure, (float)EditorStylePrefs.Instance.ButtonRoundness, CornerRounding.Left);

                gui.Draw2D.DrawImage(Checkerboard, right);
                gui.Draw2D.DrawRectFilled(right, col, (float)EditorStylePrefs.Instance.ButtonRoundness, CornerRounding.None);
            }

            if (Context.HasAlpha)
            {
                double a = Context.Alpha;
                EditorGUI.InputDouble("ColorAlpha", ref a, 0, 10, 120, EditorGUI.InputFieldStyle);
                Context.Alpha = (float)a;
            }
        }

        // Clicked outside Window
        if (gui.IsPointerClick(MouseButton.Left) && !gui.IsPointerHovering())
        {
            _selected = 0;
            isOpened = false;
            Context = null;
        }

        X = Math.Min(X, gui.ScreenRect.x + (gui.ScreenRect.width - Width));
        Y = Math.Min(Y, gui.ScreenRect.y + (gui.ScreenRect.height - Height));
    }


    // Hardware interpolation doesn't do HSV values properly, so manually interpolate multiple rects to fake it.
    private void DrawSatValRect(Rect rect)
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
                    Color.FromHSV(Context.Hue, satA, valA),
                    Color.FromHSV(Context.Hue, satB, valA),
                    Color.FromHSV(Context.Hue, satA, valB),
                    Color.FromHSV(Context.Hue, satB, valB));
            }
        }
    }
}
