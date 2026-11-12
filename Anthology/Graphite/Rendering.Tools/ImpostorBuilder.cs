using System;
using System.Collections.Generic;

using Prowl.Aperture;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering.Tools;

/// <summary>One of a baked impostor's atlases.</summary>
public enum ImpostorChannel : byte
{
    Albedo,
    NormalDepth,
    Material,
}

/// <summary>How <see cref="ImpostorBuilder"/> bakes an impostor.</summary>
public sealed class ImpostorBakeDesc
{
    /// <summary>Frames along each side of the grid, so Frames squared views.</summary>
    public int Frames = 12;

    /// <summary>Pixels along each side of a frame, the atlas being Frames times as large.</summary>
    public int CellSize = 128;

    public ImpostorLayout Layout = ImpostorLayout.Hemi;
}

/// <summary>
/// A baked impostor: its atlases as RGBA8 rows, top row first, and the sphere its frames frame. Albedo holds the base
/// colour, sRGB, and coverage. NormalDepth holds the object space normal folded onto an octahedron in red and green, the
/// depth from the front of the sphere (1) to its back (0) in blue, and occlusion in alpha. Material holds roughness and
/// metalness.
/// </summary>
public sealed class ImpostorData
{
    public required ImpostorLayout Layout { get; init; }
    public required int Frames { get; init; }
    public required int CellSize { get; init; }
    public required Float3 Center { get; init; }
    public required float Radius { get; init; }
    public required byte[] Albedo { get; init; }
    public required byte[] NormalDepth { get; init; }
    public required byte[] Material { get; init; }

    public int AtlasSize => Frames * CellSize;

    /// <summary>Uploads the atlases uncompressed with their mips and makes the impostor drawable. Host thread.</summary>
    public ImpostorAsset CreateAsset(Renderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        Texture albedo = Upload(renderer.Device, ImpostorChannel.Albedo, PixelFormat.R8_G8_B8_A8_UNorm_SRgb);
        Texture normalDepth = Upload(renderer.Device, ImpostorChannel.NormalDepth, PixelFormat.R8_G8_B8_A8_UNorm);
        Texture material = Upload(renderer.Device, ImpostorChannel.Material, PixelFormat.R8_G8_B8_A8_UNorm);
        return new ImpostorAsset(renderer, albedo, normalDepth, material, Layout, Frames, Center, Radius);
    }

    /// <summary>
    /// An atlas and its mips as RGBA8, the full size first, halving while a frame's cell does, so no mip mixes two frames.
    /// Uncovered texels hold zero, so every channel stays weighted by coverage, and the sRGB albedo averages in linear.
    /// </summary>
    public IReadOnlyList<byte[]> Mips(ImpostorChannel channel)
    {
        List<byte[]> mips = [channel switch { ImpostorChannel.Albedo => Albedo, ImpostorChannel.NormalDepth => NormalDepth, _ => Material }];
        int size = AtlasSize;
        for (int cell = CellSize; cell % 2 == 0; cell /= 2, size /= 2)
            mips.Add(Halve(mips[^1], size, channel == ImpostorChannel.Albedo));
        return mips;
    }

    /// <summary>An atlas's mips compressed into a GPU block format such as BC3 or ASTC, for the host to save and load. The albedo wants an sRGB format.</summary>
    public IReadOnlyList<byte[]> Encode(ImpostorChannel channel, TextureFormat format, TextureEncodeOptions? options = null)
    {
        if (!TextureEncoder.CanEncode(format))
            throw new ArgumentException($"Aperture cannot encode {format}.", nameof(format));
        IReadOnlyList<byte[]> mips = Mips(channel);
        byte[][] encoded = new byte[mips.Count][];
        for (int mip = 0; mip < mips.Count; mip++)
        {
            int size = AtlasSize >> mip;
            encoded[mip] = TextureEncoder.Encode(mips[mip], size, size, format, options);
        }
        return encoded;
    }

    private Texture Upload(GraphicsDevice device, ImpostorChannel channel, PixelFormat format)
    {
        IReadOnlyList<byte[]> mips = Mips(channel);
        uint size = (uint)AtlasSize;
        Texture texture = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D(size, size, (uint)mips.Count, 1, format, TextureUsage.Sampled));
        for (int mip = 0; mip < mips.Count; mip++)
            device.UpdateTexture<byte>(texture, mips[mip], new TextureRegion(0, 0, 0, size >> mip, size >> mip, 1, (uint)mip, 0));
        return texture;
    }

    private static byte[] Halve(byte[] pixels, int size, bool srgb)
    {
        int half = size / 2;
        byte[] output = new byte[half * half * 4];
        for (int y = 0; y < half; y++)
        {
            for (int x = 0; x < half; x++)
            {
                for (int c = 0; c < 4; c++)
                {
                    bool encoded = srgb && c < 3;
                    float sum = 0;
                    for (int s = 0; s < 4; s++)
                    {
                        byte value = pixels[((y * 2 + s / 2) * size + x * 2 + s % 2) * 4 + c];
                        sum += encoded ? ToLinear(value) : value / 255f;
                    }
                    float mean = sum / 4;
                    output[(y * half + x) * 4 + c] = (byte)MathF.Round(Math.Clamp(encoded ? ToSrgb(mean) : mean, 0, 1) * 255);
                }
            }
        }
        return output;
    }

    private static float ToLinear(byte value)
    {
        float c = value / 255f;
        return c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
    }

    private static float ToSrgb(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1 / 2.4f) - 0.055f;
}

/// <summary>
/// Bakes impostors through a renderer: the mesh is drawn untransformed with its materials' Impostor pass from every frame
/// of the grid, each into its cell of the atlases, then read back. For tools and import, not for frames.
/// </summary>
public static class ImpostorBuilder
{
    private static readonly PixelFormat[] s_formats = [PixelFormat.R8_G8_B8_A8_UNorm_SRgb, PixelFormat.R8_G8_B8_A8_UNorm, PixelFormat.R8_G8_B8_A8_UNorm];

    // Frames the renderer gets to hand the readbacks back before the bake gives up
    private const int MaxWaitFrames = 64;

    /// <summary>Bakes a mesh drawn with its materials, one per submesh, the last repeating. Submits frames of its own and waits for them. Host thread.</summary>
    public static ImpostorData Bake(Renderer renderer, GpuMesh mesh, ReadOnlySpan<Material> materials, ImpostorBakeDesc? desc = null)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(mesh);
        desc ??= new ImpostorBakeDesc();
        if (desc.Frames < 2 || desc.CellSize < 1)
            throw new ArgumentException("An impostor needs at least 2 frames a side and a cell of a pixel.", nameof(desc));
        if (materials.IsEmpty)
            throw new ArgumentException("An impostor bake needs a material.", nameof(materials));

        AABB bounds = mesh.Bounds;
        Float3 center = (bounds.Min + bounds.Max) * 0.5f;
        Float3 half = (bounds.Max - bounds.Min) * 0.5f;
        float radius = MathF.Max(Float3.Length(half), 1e-4f);
        uint atlasSize = (uint)(desc.Frames * desc.CellSize);

        foreach (Material material in materials)
            renderer.CompileNow(material, PassType.Impostor);

        GraphicsDevice device = renderer.Device;
        RenderTexture atlas = device.ResourceFactory.CreateRenderTexture(new RenderTextureDescription(atlasSize, atlasSize, s_formats, depth: true));
        Texture dummy = device.ResourceFactory.CreateTexture(TextureDescription.Texture2D((uint)desc.CellSize, (uint)desc.CellSize, 1, 1, PixelFormat.R16_G16_B16_A16_Float, TextureUsage.RenderTarget | TextureUsage.Sampled));
        Framebuffer target = device.ResourceFactory.CreateFramebuffer(new FramebufferDescription(null, dummy));
        RenderWorld world = renderer.CreateWorld();
        List<RenderView> views = new();
        try
        {
            world.Add(new RenderableDesc { Mesh = mesh, Transform = Double4x4.Identity, Flags = RenderableFlags.None }, materials);
            for (int y = 0; y < desc.Frames; y++)
            {
                for (int x = 0; x < desc.Frames; x++)
                    views.Add(FrameView(renderer, world, target, atlas, desc, x, y, center, radius, views.Count));
            }

            byte[]?[] read = new byte[]?[3];
            string? error = null;
            for (int c = 0; c < 3; c++)
            {
                int channel = c;
                renderer.Readback(atlas.ColorTextures[c], result => (read[channel], error) = (result.Data, result.Error ?? error));
            }
            renderer.SubmitFrame(new FrameDesc { Views = views.ToArray() });
            for (int wait = 0; wait < MaxWaitFrames && Array.IndexOf(read, null) >= 0 && error == null; wait++)
            {
                renderer.WaitForRenderThread();
                if (Array.IndexOf(read, null) >= 0)
                    renderer.SubmitFrame(new FrameDesc());
            }
            if (error != null)
                throw new InvalidOperationException($"The impostor bake could not read its atlas back: {error}");
            if (Array.IndexOf(read, null) >= 0)
                throw new InvalidOperationException("The impostor bake's atlas never came back from the renderer.");

            return new ImpostorData
            {
                Layout = desc.Layout,
                Frames = desc.Frames,
                CellSize = desc.CellSize,
                Center = center,
                Radius = radius,
                Albedo = read[0]!,
                NormalDepth = read[1]!,
                Material = read[2]!,
            };
        }
        finally
        {
            foreach (RenderView view in views)
                view.Dispose();
            world.Dispose();
            renderer.WaitForRenderThread();
            device.WaitForIdle();
            target.Dispose();
            dummy.Dispose();
            atlas.Dispose();
        }
    }

    // An orthographic view of the bounding sphere from one frame's direction, drawing into its cell
    private static RenderView FrameView(Renderer renderer, RenderWorld world, Framebuffer target, RenderTexture atlas, ImpostorBakeDesc desc, int x, int y, Float3 center, float radius, int order)
    {
        Float3 direction = ImpostorFrames.Direction(desc.Layout, desc.Frames, x, y);
        RenderView view = renderer.CreateView(world, $"Impostor frame {x} {y}");
        view.Kind = ViewKind.RenderTexture;
        view.Target = target;
        view.Sky = false;
        view.Order = order;
        view.Camera = ViewCamera.Orthographic(new Double3(center.X, center.Y, center.Z), -direction, ImpostorFrames.UpReference(direction), radius * 2, -radius, radius, aspect: 1);
        view.Features = new ExtensionSet<RenderFeature>();
        view.Features.Add(new BakeFeature(atlas, (uint)(x * desc.CellSize), (uint)(y * desc.CellSize), (uint)desc.CellSize, clear: order == 0));
        return view;
    }

    // Draws the Impostor pass into one cell of the atlas, the first cell clearing the whole of it
    private sealed class BakeFeature(RenderTexture atlas, uint x, uint y, uint size, bool clear) : RenderFeature(RenderSlot.AfterTransparents, passes: [PassType.Impostor])
    {
        private TextureHandle _atlas;

        public override void Setup(FeatureBuilder builder, in FeatureView view)
        {
            builder.DrawsRenderables();
            _atlas = builder.Import(atlas);
        }

        public override void Render(FeatureContext context)
        {
            RenderTexture texture = context.Get(_atlas);
            context.Command.SetFramebuffer(texture.Framebuffer, clear ? TargetLoadStoreOps.Clear(new Color(0f, 0f, 0f, 0f), 0f) : new TargetLoadStoreOps(AttachmentOps.Loaded, AttachmentOps.Loaded));
            context.Command.SetViewport(new Viewport(x, y, size, size, 0, 1));
            context.Command.SetScissor(x, y, size, size);
            context.DrawPass(PassType.Impostor);
        }
    }
}
