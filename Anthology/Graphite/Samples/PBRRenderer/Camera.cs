using System;

using Prowl.Graphite.Rendering;
using Prowl.Vector;


namespace Prowl.Graphite.Samples.PBRRenderer;


internal sealed class FlyCamera
{
    public const float Near = 0.1f;

    public Double3 Position;
    public float Yaw;
    public float Pitch;
    public float FieldOfView = 60f * MathF.PI / 180f;

    public Float3 Forward => new(MathF.Cos(Pitch) * MathF.Sin(Yaw), MathF.Sin(Pitch), MathF.Cos(Pitch) * MathF.Cos(Yaw));

    public Float3 Right => Float3.Normalize(Float3.Cross(Float3.UnitY, Forward));

    public void Look(float deltaX, float deltaY)
    {
        Yaw += deltaX * 0.0025f;
        Pitch = Math.Clamp(Pitch - deltaY * 0.0025f, -1.55f, 1.55f);
    }

    public void Move(float right, float up, float forward, float distance)
    {
        Float3 f = Forward;
        Float3 r = Right;
        Float3 delta = (r * right + Float3.UnitY * up + f * forward) * distance;
        Position += new Double3(delta.X, delta.Y, delta.Z);
    }

    public ViewLayer Layer(float aspect)
    {
        Float3 f = Forward;
        Float3 r = Right;
        Float3 u = Float3.Cross(f, r);
        float focal = 1f / MathF.Tan(FieldOfView * 0.5f);

        return new ViewLayer
        {
            View = new Float4x4(new Float4(r.X, u.X, f.X, 0), new Float4(r.Y, u.Y, f.Y, 0), new Float4(r.Z, u.Z, f.Z, 0), new Float4(0, 0, 0, 1)),
            Projection = new Float4x4(new Float4(focal / aspect, 0, 0, 0), new Float4(0, -focal, 0, 0), new Float4(0, 0, 0, 1), new Float4(0, 0, Near, 0)),
            Position = Position,
        };
    }
}
