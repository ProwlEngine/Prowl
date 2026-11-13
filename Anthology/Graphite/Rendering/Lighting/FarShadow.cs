using System;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// The sun's shadow far past its cascades, as a height field seen from above: a point is in shadow when it lies below
/// the height its texel holds, the highest the sun's rays are blocked at over that spot. Anything can fill it: the host
/// writes a texture it made, or a frame feature such as <see cref="TerrainSurface"/>'s writes it each frame before the
/// views. Set it as <see cref="RenderWorld.FarShadow"/>. Lit surfaces take the darker of it and the cascades, fading it in
/// from <see cref="FadeStart"/>. See Graphite/FarShadow.glsl.
/// </summary>
public sealed class FarShadowMap : IDisposable
{
    private readonly DeviceBuffer _data;

    public FarShadowMap(Renderer renderer)
    {
        _data = renderer.Device.ResourceFactory.CreateBuffer(new BufferDescription(FarShadowRecord.Size, BufferUsage.StructuredBufferReadOnly));
        _data.Name = "Graphite Far Shadow Data";
        Feature = new UploadFeature(this);
    }

    /// <summary>A single channel float texture of shadow heights in world metres, row by row from the lowest z, or null for no far shadow.</summary>
    public Texture? Heights { get; set; }

    /// <summary>The world x and z of the texture's corner with the lowest x and z, and the metres it spans.</summary>
    public Double2 Min { get; set; }
    public double Size { get; set; } = 1;

    /// <summary>Metres over which a point turns from shadowed to lit around the shadow height, and the metres it is raised by first.</summary>
    public float Softness { get; set; } = 4f;
    public float Bias { get; set; } = 1f;

    /// <summary>The distance from the camera past which the far shadow fades in over a fifth more, 0 to apply it everywhere.</summary>
    public float FadeStart { get; set; }

    public bool Enabled { get; set; } = true;

    internal DeviceBuffer Data => _data;

    internal FrameFeature Feature { get; }

    internal bool Active => Enabled && Heights != null;

    private FarShadowRecord Record() => new()
    {
        Area = new Float4((float)Min.X, (float)Min.Y, (float)Math.Max(Size, 1e-3), Active ? 1f : 0f),
        Params = new Float4(Math.Max(Softness, 0.01f), Bias, Math.Max(FadeStart, 0f), 0f),
    };

    public void Dispose()
    {
        _data.Dispose();
    }

    // Writes the record late in the frame's features, after whatever fills the heights
    private sealed class UploadFeature(FarShadowMap owner) : FrameFeature(FrameSlot.BeforeViews, order: 100)
    {
        private FarShadowRecord _record;

        public override void Extract() => _record = owner.Record();

        public override void Setup(FrameBuilder builder)
        {
            builder.Write(owner._data, BufferAccess.TransferWrite);
            if (owner.Heights != null)
                builder.Read(owner.Heights);
        }

        public override void Render(FrameContext context) => context.Command.UpdateBuffer(owner._data, 0, _record);
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct FarShadowRecord
{
    public const uint Size = 32;

    public Float4 Area;     // min x, min z, size, 1 when on
    public Float4 Params;   // softness, bias, fade start
}

public sealed partial class RenderWorld
{
    private FarShadowMap? _farShadow;

    /// <summary>The sun's shadow past its cascades, or null for none.</summary>
    public FarShadowMap? FarShadow
    {
        get => _farShadow;
        set
        {
            if (value == _farShadow)
                return;
            if (_farShadow != null)
                FrameFeatures.Remove(_farShadow.Feature);
            _farShadow = value;
            if (value != null)
                FrameFeatures.Add(value.Feature);
        }
    }
}
