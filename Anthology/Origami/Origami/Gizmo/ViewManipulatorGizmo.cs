// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Scribe;
using Prowl.Vector;

namespace Prowl.OrigamiUI.Gizmo;

/// <summary>
/// Axis gizmo drawn in the corner of the scene view. The three positive axes are labelled arrows,
/// the three negative ones are plain rings, and clicking any of them puts the camera on that side of
/// the scene looking back at it.
/// </summary>
public class ViewManipulatorGizmo
{
    // Fractions of the widget radius. All six handles sit at one distance, the way points on a
    // sphere do, and HandleRing keeps the largest of them off the backdrop's rim.
    private const float HandleRing = 0.70f;
    private const float PositiveRadius = 0.18f;
    private const float NegativeRadius = 0.17f;
    private const float LabelSize = 0.26f;

    // Slightly wider than the discs themselves, so a click that grazes the edge still lands.
    private const float PositiveHitRadius = 0.22f;
    private const float NegativeHitRadius = 0.21f;

    // An axis is unusable once its projection is this short, and starts fading here.
    private const float FadeEnd = 0.10f;
    private const float FadeStart = 0.34f;

    private static readonly Float3[] Axes =
    [
        Float3.UnitX, Float3.UnitY, Float3.UnitZ,
        -Float3.UnitX, -Float3.UnitY, -Float3.UnitZ,
    ];

    private static readonly Color32[] AxisColors =
    [
        Color32.FromArgb(255, 226, 55, 56),
        Color32.FromArgb(255, 94, 234, 141),
        Color32.FromArgb(255, 39, 117, 255),
    ];

    private static readonly string[] AxisLabels = ["X", "Y", "Z"];

    private struct Handle
    {
        public Float3 Axis;
        public Float2 Tip;
        public Float2 Direction;
        public float Depth;
        public float Planar;
        public float Alpha;
        public int AxisIndex;
        public bool Positive;
    }

    private readonly Handle[] _handles = new Handle[Axes.Length];

    private Rect _rect;
    private Float3 _camForward = Float3.UnitZ;
    private Float3 _camUp = Float3.UnitY;
    private FontFile? _font;
    private bool _isHovering;

    /// <summary>True while the cursor is over the widget, so the caller can hold off camera drags.</summary>
    public bool IsOver => _isHovering;

    /// <summary>
    /// Set by the last <see cref="Update"/> when the click landed on the widget but not on a handle.
    /// The caller decides what that means; the scene view swaps the camera's projection.
    /// </summary>
    public bool BackgroundClicked { get; private set; }

    public void SetRect(Rect rect) => _rect = rect;

    public void SetCamera(Float3 camForward, Float3 camUp) { _camForward = camForward; _camUp = camUp; }

    /// <summary>Font for the axis letters. Without one the arrows draw unlabelled.</summary>
    public void SetFont(FontFile? font) => _font = font;

    /// <summary>
    /// Draws the gizmo and reports a click on one of its handles.
    /// <paramref name="newCamForward"/> is the direction the camera should look along.
    /// </summary>
    public bool Update(Prowl.Quill.Canvas canvas, Float2 mousePos, bool mouseClicked, bool blockPicking,
        out Float3 newCamForward)
    {
        newCamForward = _camForward;
        BackgroundClicked = false;

        var center = new Float2((float)(_rect.Min.X + _rect.Size.X / 2), (float)(_rect.Min.Y + _rect.Size.Y / 2));
        float radius = (float)(_rect.Size.X / 2);
        if (radius <= 1f) { _isHovering = false; return false; }

        _isHovering = Float2.Length(mousePos - center) <= radius;

        BuildHandles(center, radius);

        int hovered = blockPicking ? -1 : PickHandle(mousePos, radius);

        // The stroke width and cap set below would otherwise carry into whatever the caller draws
        // next on this canvas.
        canvas.SaveState();
        DrawBackdrop(canvas, center, radius, hovered);
        DrawHandles(canvas, center, radius, hovered);
        canvas.RestoreState();

        if (hovered < 0)
        {
            BackgroundClicked = mouseClicked && !blockPicking && _isHovering;
            return false;
        }

        if (!mouseClicked) return false;

        // The camera goes to that side of the scene and looks back at it, so the handle you clicked
        // is the one now pointing at you.
        newCamForward = -_handles[hovered].Axis;
        return true;
    }

    /// <summary>
    /// Projects the six axes onto the screen and orders them back to front.
    /// </summary>
    private void BuildHandles(Float2 center, float radius)
    {
        Float3 forward = SafeNormalize(_camForward, Float3.UnitZ);
        Float3 right = Float3.Cross(SafeNormalize(_camUp, Float3.UnitY), forward);

        // The up vector collapses against forward when looking straight up or down, leaving no
        // screen basis to project onto. Any perpendicular does, since the roll is arbitrary there.
        if (Float3.Length(right) < 1e-4f)
            right = Float3.Cross(Float3.UnitZ, forward);
        if (Float3.Length(right) < 1e-4f)
            right = Float3.Cross(Float3.UnitX, forward);

        right = SafeNormalize(right, Float3.UnitX);
        Float3 up = Float3.Cross(forward, right);

        for (int i = 0; i < Axes.Length; i++)
        {
            Float3 axis = Axes[i];
            float depth = Float3.Dot(axis, forward);

            // Screen Y grows downward, so the up component is negated.
            var dir = new Float2(Float3.Dot(axis, right), -Float3.Dot(axis, up));
            float planar = MathF.Sqrt(MathF.Max(0f, 1f - depth * depth));

            _handles[i] = new Handle
            {
                Axis = axis,
                Direction = planar > 1e-5f ? dir / planar : new Float2(0f, -1f),
                Tip = center + dir * (radius * HandleRing),
                Depth = depth,
                Planar = planar,
                Alpha = AlphaFor(depth, planar),
                AxisIndex = i % 3,
                Positive = i < 3,
            };
        }

        // Farthest first, so nearer handles draw over them.
        Array.Sort(_handles, static (a, b) => b.Depth.CompareTo(a.Depth));
    }

    /// <summary>
    /// Retires an axis as it turns to line up with the view.
    /// </summary>
    /// <remarks>
    /// Both ends of an axis go, not just the near one: the far handle collapses onto the pivot just
    /// the same, and leaving it parked dead centre reads as a stray dot rather than as an axis.
    /// </remarks>
    private static float AlphaFor(float depth, float planar)
    {
        // Handles on the far side read as behind the pivot rather than in front of it.
        float alpha = depth > 0f ? 0.45f : 1f;
        return planar < FadeStart ? alpha * SmoothStep(FadeEnd, FadeStart, planar) : alpha;
    }

    /// <summary>
    /// The handle under the cursor, or -1.
    /// </summary>
    /// <remarks>
    /// Walks front to back so the handle drawn on top is the one picked. Every handle is measured
    /// against the very point it was drawn at, which is what keeps the clickable area under what you
    /// can see however far the axis is foreshortened.
    /// </remarks>
    private int PickHandle(Float2 mousePos, float radius)
    {
        for (int i = _handles.Length - 1; i >= 0; i--)
        {
            ref Handle handle = ref _handles[i];
            if (handle.Alpha < 0.25f) continue;

            float hitRadius = radius * (handle.Positive ? PositiveHitRadius : NegativeHitRadius);
            if (Float2.Length(mousePos - handle.Tip) <= hitRadius)
                return i;
        }
        return -1;
    }

    /// <summary>
    /// The disc the handles sit on, which lifts on hover.
    /// </summary>
    /// <remarks>
    /// It lifts most when the cursor is on the backdrop itself rather than on a handle, because that
    /// is the state where clicking does something of its own: the backdrop is what swaps the
    /// projection, and nothing else says so.
    /// </remarks>
    private void DrawBackdrop(Prowl.Quill.Canvas canvas, Float2 center, float radius, int hovered)
    {
        byte alpha = !_isHovering ? (byte)120 : hovered >= 0 ? (byte)160 : (byte)200;
        canvas.CircleFilled((float)center.X, (float)center.Y, radius, Color32.FromArgb(alpha, 25, 25, 30), 48);

        if (_isHovering && hovered < 0)
            canvas.CircleFilled((float)center.X, (float)center.Y, radius, Color32.FromArgb(22, 255, 255, 255), 48);
    }

    private void DrawHandles(Prowl.Quill.Canvas canvas, Float2 center, float radius, int hovered)
    {
        for (int i = 0; i < _handles.Length; i++)
        {
            ref Handle handle = ref _handles[i];
            if (handle.Alpha <= 0.01f) continue;

            bool isHovered = i == hovered;
            Color32 color = Tint(AxisColors[handle.AxisIndex], handle.Alpha, isHovered);

            if (handle.Positive)
                DrawPositive(canvas, center, radius, ref handle, color, isHovered);
            else
                DrawNegative(canvas, radius, ref handle, color, isHovered);
        }
    }

    /// <summary>
    /// A positive axis: a stem out to a filled disc with the axis letter inside it.
    /// </summary>
    private void DrawPositive(Prowl.Quill.Canvas canvas, Float2 center, float radius, ref Handle handle,
        Color32 color, bool isHovered)
    {
        float disc = radius * PositiveRadius * (isHovered ? 1.12f : 1f);

        // The stem stops at the disc rather than running under it: a faded handle draws both at the
        // same alpha, and the overlap would read as a darker patch inside the circle.
        float stem = radius * HandleRing * handle.Planar - disc;
        if (stem > 0f)
        {
            Float2 stemEnd = center + handle.Direction * stem;
            canvas.BeginPath();
            canvas.MoveTo((float)center.X, (float)center.Y);
            canvas.LineTo((float)stemEnd.X, (float)stemEnd.Y);
            canvas.SetStrokeColor(color);
            canvas.SetStrokeWidth(MathF.Max(1.5f, radius * 0.045f));
            canvas.SetStrokeCap(Prowl.Quill.EndCapStyle.Butt);
            canvas.Stroke();
        }

        canvas.CircleFilled((float)handle.Tip.X, (float)handle.Tip.Y, disc, color, 28);

        if (_font == null) return;

        canvas.DrawText(AxisLabels[handle.AxisIndex], (float)handle.Tip.X, (float)handle.Tip.Y,
            LabelColor(handle.Alpha), radius * LabelSize, _font, 0f, new Float2(0.5f, 0.5f));
    }

    /// <summary>A negative axis: the same disc, smaller, with no stem and no letter.</summary>
    private static void DrawNegative(Prowl.Quill.Canvas canvas, float radius, ref Handle handle,
        Color32 color, bool isHovered)
    {
        float disc = radius * NegativeRadius * (isHovered ? 1.12f : 1f);
        canvas.CircleFilled((float)handle.Tip.X, (float)handle.Tip.Y, disc, color, 24);
    }

    private static Color32 Tint(Color32 color, float alpha, bool isHovered)
    {
        if (isHovered)
            return Color32.FromArgb(255, Lighten(color.R), Lighten(color.G), Lighten(color.B));
        return Color32.FromArgb((byte)Math.Clamp(alpha * 255f, 0f, 255f), color.R, color.G, color.B);
    }

    private static byte Lighten(byte channel) => (byte)Math.Clamp(channel + (255 - channel) * 0.45f, 0f, 255f);

    private static Color32 LabelColor(float alpha)
        => Color32.FromArgb((byte)Math.Clamp(alpha * 255f, 0f, 255f), 18, 18, 22);

    private static Float3 SafeNormalize(Float3 value, Float3 fallback)
    {
        float length = Float3.Length(value);
        return length < 1e-6f ? fallback : value / length;
    }

    private static float SmoothStep(float edge0, float edge1, float x)
    {
        float t = Math.Clamp((x - edge0) / MathF.Max(1e-6f, edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }
}
