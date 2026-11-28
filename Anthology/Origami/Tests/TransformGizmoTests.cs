using Prowl.OrigamiUI.Gizmo;
using Prowl.Vector;

using Xunit;

namespace Prowl.OrigamiUI.Tests;

public class TransformGizmoTests
{
    private static readonly Float2 Size = new(1600, 900);

    // A camera a few metres from an object far from the origin, where a float holds less of a position
    private static readonly Float3 CamPos = new(-0.53f, 2.665f, 33.03f);
    private static readonly Float3 Start = new(1.38691f, 0.66510f, 30.75647f);

    [Fact]
    public void TranslateAxis_HeldStill_DoesNotDriftWhenTheObjectLandsSlightlyOff()
    {
        var drag = new Drag(Start, Quaternion.FromEuler(new Float3(0f, 273.7f * MathF.PI / 180f, 0f)));
        drag.Grab(pixelsAlongAxis: 60f);
        Float3 grabbed = drag.Position;

        // The host applies each delta through its own transforms, so the object lands a rounding error away from
        // where the gizmo sent it. The drag must not feed on that: its axis is the one that was grabbed.
        var rounding = new Random(1);
        for (int frame = 0; frame < 1000; frame++)
        {
            drag.Hold();
            drag.Position += new Float3(Noise(rounding), Noise(rounding), Noise(rounding));
        }

        // A thousand frames of 2e-6 noise wander well under a millimetre. Fed back, it ran to centimetres.
        Assert.True(Float3.Length(drag.Position - grabbed) < 1e-3f, $"Drifted {Float3.Length(drag.Position - grabbed)} with the mouse still");
    }

    [Fact]
    public void TranslateAxis_Dragged_MovesAlongTheGrabbedAxisOnly()
    {
        var drag = new Drag(Start, Quaternion.Identity);
        drag.Grab(pixelsAlongAxis: 60f);
        Float3 grabbed = drag.Position;

        for (int frame = 0; frame < 60; frame++)
            drag.MoveMouse(drag.AxisOnScreen * 2f);

        Float3 moved = drag.Position - grabbed;
        float along = Float3.Dot(moved, drag.Axis);
        Assert.True(along > 0.1f, $"Moved {along} along the axis");
        Assert.True(Float3.Length(moved - drag.Axis * along) < 1e-3f, "Left the axis");
    }

    private static float Noise(Random random) => (float)(random.NextDouble() - 0.5) * 4e-6f;

    /// <summary>A host driving the gizmo's Z arrow the way an editor does: set the transform, update, add the delta.</summary>
    private sealed class Drag
    {
        public Float3 Position;
        public readonly Float3 Axis;
        public Float2 AxisOnScreen { get; private set; }

        private readonly Quaternion _rotation;
        private readonly TransformGizmo _gizmo = new(TransformGizmoMode.Translate) { GizmoSize = 100f, Orientation = TransformGizmo.GizmoOrientation.Local };
        private readonly Float3 _forward, _right, _up;
        private readonly Float4x4 _view, _projection, _viewProjection;
        private Float2 _mouse;

        public Drag(Float3 position, Quaternion rotation)
        {
            Position = position;
            _rotation = rotation;
            Axis = rotation * Float3.UnitZ;

            _forward = Float3.Normalize(position - CamPos);
            _right = Float3.Normalize(Float3.Cross(Float3.UnitY, _forward));
            _up = Float3.Cross(_forward, _right);
            _view = Float4x4.CreateLookTo(CamPos, _forward, _up);
            _projection = Float4x4.CreatePerspectiveFov(60f * MathF.PI / 180f, Size.X / Size.Y, 0.1f, 1000f);
            _viewProjection = _projection * _view;
        }

        public void Grab(float pixelsAlongAxis)
        {
            Float2 centre = ToScreen(Position);
            AxisOnScreen = Float2.Normalize(ToScreen(Position + Axis * 0.1f) - centre);
            _mouse = centre + AxisOnScreen * pixelsAlongAxis;

            _gizmo.IsMouseDown = true;
            Step();
            _gizmo.IsMouseDown = false;
            Assert.True(_gizmo.IsOver, "The arrow was not picked");
        }

        public void Hold() => Step();

        public void MoveMouse(Float2 pixels)
        {
            _mouse += pixels;
            Step();
        }

        private void Step()
        {
            _gizmo.UpdateCamera(new Rect(0, 0, Size.X, Size.Y), _view, _projection, _up, _forward, _right, CamPos);
            _gizmo.SetTransform(Position, _rotation, Float3.One);
            if (_gizmo.Update(RayAt(_mouse), _mouse, false)?.TranslationDelta is { } delta) Position += delta;
        }

        private Float2 ToScreen(Float3 world)
        {
            Float4 clip = Float4x4.TransformPoint(new Float4(world, 1f), _viewProjection);
            return new Float2((clip.X / clip.W + 1f) * 0.5f * Size.X, (1f - clip.Y / clip.W) * 0.5f * Size.Y);
        }

        private Ray RayAt(Float2 pixel)
        {
            var ndc = new Float2(pixel.X / Size.X * 2f - 1f, 1f - pixel.Y / Size.Y * 2f);
            Float4x4 inverse = _viewProjection.Invert();
            Float4 near = Float4x4.TransformPoint(new Float4(ndc.X, ndc.Y, 0f, 1f), inverse);
            Float4 far = Float4x4.TransformPoint(new Float4(ndc.X, ndc.Y, 1f, 1f), inverse);
            near /= near.W;
            far /= far.W;
            return new Ray(near.XYZ, Float3.Normalize(far.XYZ - near.XYZ));
        }
    }
}
