using StbImageSharp;
using Prowl.Graphite;
using Prowl.Vector;


namespace GraphiteSample;


public class TextureGraphite : IDisposable
{
    private readonly Texture Texture;
    private readonly Sampler Sampler;

    public uint Width => Texture.Width;
    public uint Height => Texture.Height;


    public TextureGraphite(Texture texture, Sampler sampler)
    {
        Texture = texture;
        Sampler = sampler;
    }


    public void SetTexture(PropertySet propertySet, PropertyID id)
    {
        propertySet.SetTexture(id, Texture, Sampler);
    }


    public static TextureGraphite LoadFromFile(GraphicsDevice device, string path)
    {
        // Configure image loading
        StbImage.stbi_set_flip_vertically_on_load(1);

        // Load image data
        using Stream stream = File.OpenRead(Path.Join(AppContext.BaseDirectory, path));
        ImageResult image = ImageResult.FromStream(stream, ColorComponents.RedGreenBlueAlpha);

        TextureGraphite texture = CreateTexture(device, (uint)image.Width, (uint)image.Height);

        texture.SetTextureData(device, new IntRect(0, 0, image.Width, image.Height), image.Data);

        return texture;
    }


    public static TextureGraphite CreateTexture(GraphicsDevice device, uint width, uint height)
    {
        Texture texture = device.ResourceFactory.CreateTexture(
            TextureDescription.Texture2D(width, height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled));

        Sampler sampler = device.ResourceFactory.CreateSampler(new SamplerDescription
        {
            AddressModeU = SamplerAddressMode.Wrap,
            AddressModeV = SamplerAddressMode.Wrap,
            AddressModeW = SamplerAddressMode.Wrap,
            Filter = SamplerFilter.MinLinear_MagLinear_MipLinear
        });

        return new TextureGraphite(texture, sampler);
    }


    public unsafe void SetTextureData(GraphicsDevice device, IntRect rect, byte[] data)
    {
        fixed (byte* dataPtr = data)
            device.UpdateTexture(Texture, (nint)dataPtr, (uint)data.Length, (uint)rect.Min.X, (uint)rect.Min.Y, 0, (uint)rect.Size.X, (uint)rect.Size.Y, 1, 0, 0);
    }


    public void Dispose()
    {
        GC.SuppressFinalize(this);
        Texture?.Dispose();
        Sampler?.Dispose();
    }
}