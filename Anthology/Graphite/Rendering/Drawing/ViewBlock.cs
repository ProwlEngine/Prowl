using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>One layer of a view as shaders read it through Graphite_View(), 496 bytes, matching Graphite_ViewData in Core.glsl.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ViewBlock
{
    public Float4x4 View;
    public Float4x4 Projection;
    public Float4x4 ViewProjection;
    public Float4x4 InverseViewProjection;
    public Float4x4 PreviousViewProjection;
    public Float4 CameraHigh;
    public Float4 CameraLow;
    public Float4 PreviousCameraHigh;
    public Float4 PreviousCameraLow;
    public Float4 Screen;
    public Float4 Jitter;
    public Float4 Time;
    public Float4 Depth;

    /// <summary>The camera minus each static tree's origin, so shaders move camera relative positions into tree space.</summary>
    public Float4 StaticLightOffset;
    public Float4 StaticDecalOffset;

    /// <summary>Roots of the static light, view light, static decal and view decal trees, all ones for an empty tree.</summary>
    public uint StaticLightRoot, ViewLightRoot, StaticDecalRoot, ViewDecalRoot;

    /// <summary>
    /// A view block from a camera relative view matrix (rotation only) and a projection, with the camera's world position.
    /// Without a previous frame, the previous matrices and camera are this frame's.
    /// </summary>
    public static ViewBlock Create(in Float4x4 view, in Float4x4 projection, Double3 camera, uint width, uint height, float near, float farDistance)
    {
        Float4x4 viewProjection = projection * view;
        (Float3 high, Float3 low) = Precise.Split(camera);
        return new ViewBlock
        {
            View = view,
            Projection = projection,
            ViewProjection = viewProjection,
            InverseViewProjection = viewProjection.Invert(),
            PreviousViewProjection = viewProjection,
            CameraHigh = new Float4(high.X, high.Y, high.Z, 0),
            CameraLow = new Float4(low.X, low.Y, low.Z, 0),
            PreviousCameraHigh = new Float4(high.X, high.Y, high.Z, 0),
            PreviousCameraLow = new Float4(low.X, low.Y, low.Z, 0),
            Screen = new Float4(width, height, 1f / width, 1f / height),
            Depth = new Float4(near, farDistance, 0, 0),
            StaticLightRoot = uint.MaxValue,
            ViewLightRoot = uint.MaxValue,
            StaticDecalRoot = uint.MaxValue,
            ViewDecalRoot = uint.MaxValue,
        };
    }
}
