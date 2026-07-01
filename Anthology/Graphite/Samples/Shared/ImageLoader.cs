using System;
using System.IO;

using ImageMagick;

using Prowl.Graphite;
using Prowl.Vector;


namespace Prowl.Graphite.Samples;


public static class ImageLoader
{
    public static (Texture, Sampler) Load(GraphicsDevice device, string name)
    {
        Memory<byte>? file = FileLoader.Load(name);

        if (file == null)
            throw new Exception("File not found: " + file);

        using var image = new MagickImage(file.Value.Span);

        image.Alpha(AlphaOption.Set);
        image.Depth = 8;

        image.Flip();

        using IUnsafePixelCollection<ushort> pixels = image.GetPixelsUnsafe();
        byte[]? color = pixels.ToByteArray(PixelMapping.RGBA) ?? throw new Exception("Failed to load pixel data");

        TextureDescription desc = TextureDescription.Texture2D(image.Width, image.Height, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled);

        Texture texture = device.ResourceFactory.CreateTexture(desc);

        device.UpdateTexture(texture, color, 0, 0, 0, image.Width, image.Height, 1, 0, 0);

        SamplerDescription samplerDesc = SamplerDescription.Linear;

        Sampler sampler = device.ResourceFactory.CreateSampler(samplerDesc);

        return (texture, sampler);
    }
}
