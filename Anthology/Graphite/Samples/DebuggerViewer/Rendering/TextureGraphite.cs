using Prowl.Graphite;
using Prowl.Vector;

namespace DebuggerViewer.Rendering;

internal sealed class TextureGraphite : IDisposable
{
    private readonly Texture _texture;
    private readonly Sampler _sampler;
    private readonly bool _owned;

    private TextureGraphite(Texture texture, Sampler sampler, bool owned)
    {
        _texture = texture;
        _sampler = sampler;
        _owned = owned;
    }

    public Texture Texture => _texture;

    public uint Width => _texture.Width;

    public uint Height => _texture.Height;

    public void SetTexture(PropertySet properties, PropertyID id) => properties.SetTexture(id, _texture, _sampler);

    public static TextureGraphite Create(GraphicsDevice device, uint width, uint height)
    {
        Texture texture = device.ResourceFactory.CreateTexture(
            TextureDescription.Texture2D(width, height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        Sampler sampler = device.ResourceFactory.CreateSampler(new SamplerDescription
        {
            AddressModeU = SamplerAddressMode.Wrap,
            AddressModeV = SamplerAddressMode.Wrap,
            AddressModeW = SamplerAddressMode.Wrap,
            MinFilter = FilterMode.Linear,
            MagFilter = FilterMode.Linear,
            MipFilter = FilterMode.Linear,
        });

        return new TextureGraphite(texture, sampler, true);
    }

    public static TextureGraphite Wrap(Texture texture, Sampler sampler) => new(texture, sampler, false);

    public unsafe void SetData(GraphicsDevice device, IntRect rect, byte[] data)
    {
        fixed (byte* pointer = data)
            device.UpdateTexture(_texture, (nint)pointer, (uint)data.Length, new TextureRegion((uint)rect.Min.X, (uint)rect.Min.Y, 0, (uint)rect.Size.X, (uint)rect.Size.Y, 1));
    }

    public void Dispose()
    {
        if (!_owned)
            return;
        _texture.Dispose();
        _sampler.Dispose();
    }
}
