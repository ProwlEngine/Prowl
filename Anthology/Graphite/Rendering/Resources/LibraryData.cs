using System;
using System.Reflection;

namespace Prowl.Graphite.Rendering;

/// <summary>Reads the data files an assembly embeds beside its shaders, such as lookup tables and noise.</summary>
public static class LibraryData
{
    /// <summary>The first bytes of an embedded file, by its resource name.</summary>
    public static byte[] Read(Assembly assembly, string name, uint length)
    {
        using System.IO.Stream stream = assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"{assembly.GetName().Name} embeds no {name}.");
        byte[] bytes = new byte[length];
        stream.ReadExactly(bytes);
        return bytes;
    }

    /// <summary>
    /// An RGBA8 texture from an embedded file, in its stored row order, in every layer, mipmapped on the GPU when asked,
    /// its colour sRGB encoded when asked, its pixels changed by edit first when given.
    /// </summary>
    public static Texture LoadTexture(GraphicsDevice device, Assembly assembly, string name, uint width, uint height, bool mipmapped, uint layers = 1, bool srgb = false, Action<byte[]>? edit = null)
    {
        byte[] pixels = Read(assembly, name, width * height * 4);
        edit?.Invoke(pixels);
        uint mips = mipmapped ? (uint)Math.Log2(Math.Max(width, height)) + 1 : 1;
        PixelFormat format = srgb ? PixelFormat.R8_G8_B8_A8_UNorm_SRgb : PixelFormat.R8_G8_B8_A8_UNorm;
        Texture texture = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(width, height, mips, layers, format, TextureUsage.Sampled));
        texture.Name = System.IO.Path.GetFileNameWithoutExtension(name);
        for (uint layer = 0; layer < layers; layer++)
            device.UpdateTexture<byte>(texture, pixels, TextureRegion.Whole(texture, 0, layer));
        if (mipmapped)
            device.Record(cmd => cmd.GenerateMipmaps(texture)).Wait();
        return texture;
    }
}
