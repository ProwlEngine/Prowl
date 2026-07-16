// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Graphite;

using GraphiteTexture = Prowl.Graphite.Texture;

namespace Prowl.Runtime.Resources;

/// <summary>
/// This is the base class for all texture types and manages some of their internal workings.
/// <para>
/// Backed by Prowl.Graphite. Graphite keeps sampler state (filter/address modes) on a
/// separate <see cref="Graphite.Sampler"/> rather than on the texture, so changing a
/// filter or wrap mode rebuilds <see cref="Sampler"/>. The GPU texture itself is immutable
/// in size, so resizing recreates <see cref="Handle"/> (done by the derived types once the
/// dimensions are known).
/// </para>
/// </summary>
public abstract class Texture : EngineObject
{
    private protected const SamplerFilter DefaultFilter = SamplerFilter.MinPoint_MagPoint_MipPoint;
    private protected const SamplerFilter DefaultMipmapFilter = SamplerFilter.MinPoint_MagPoint_MipLinear;

    /// <summary>The backing Graphite GPU texture. Null until a derived type allocates storage.</summary>
    public GraphiteTexture Handle { get; private protected set; }

    /// <summary>The Graphite sampler describing how this texture is filtered and wrapped.</summary>
    public Sampler Sampler { get; private set; }

    /// <summary>The type of this <see cref="Texture"/>, such as 2D, 3D, CubeMap.</summary>
    public readonly TextureType Type;

    public SamplerFilter Filter { get; private set; }
    public SamplerAddressMode AddressModeU { get; private set; }
    public SamplerAddressMode AddressModeV { get; private set; }
    public SamplerAddressMode AddressModeW { get; private set; }

    /// <summary>The pixel format for this <see cref="Texture"/>'s image.</summary>
    public readonly PixelFormat ImageFormat;

    /// <summary>Gets whether this <see cref="Texture"/> is mipmapped.</summary>
    public bool IsMipmapped { get; private protected set; }

    /// <summary>False if this <see cref="Texture"/> can be mipmapped (depends on texture type).</summary>
    private readonly bool isNotMipmappable;

    /// <summary>Gets whether this <see cref="Texture"/> can be mipmapped (depends on texture type).</summary>
    public bool IsMipmappable => !isNotMipmappable;

    public bool IsCubemap { get; private protected set; }


    internal Texture(TextureType type, PixelFormat imageFormat, bool isCubemap = false) : base("New Texture")
    {
        if (!Enum.IsDefined(type))
            throw new FormatException("Invalid texture target");

        Type = type;
        ImageFormat = imageFormat;
        IsMipmapped = false;
        IsCubemap = isCubemap;
        isNotMipmappable = !IsTextureTypeMipmappable(imageFormat, TextureSampleCount.Count1);

        Filter = DefaultFilter;
        AddressModeU = SamplerAddressMode.Wrap;
        AddressModeV = SamplerAddressMode.Wrap;
        AddressModeW = SamplerAddressMode.Wrap;
        RebuildSampler();
    }

    /// <summary>Recreates <see cref="Sampler"/> from the current filter and address-mode state.</summary>
    private protected void RebuildSampler()
    {
        // Defer the old sampler's disposal: a filter/wrap change can swap it while an in-flight
        // frame still binds it.
        Graphics.DisposeDeferred(Sampler);
        Sampler = Graphics.Device.ResourceFactory.CreateSampler(new SamplerDescription
        {
            AddressModeU = AddressModeU,
            AddressModeV = AddressModeV,
            AddressModeW = AddressModeW,
            Filter = Filter,
            MinimumLod = 0,
            MaximumLod = uint.MaxValue,
        });
        Sampler.Name = $"{Name} Sampler";
    }

    /// <summary>Sets this <see cref="Texture"/>'s sampling filter.</summary>
    public void SetTextureFilters(SamplerFilter filter)
    {
        Filter = filter;
        RebuildSampler();
    }

    /// <summary>
    /// No-op compatibility shim: shadow-map depth-compare sampling is a sampler feature that has not
    /// been ported to the Graphite sampler path yet.
    /// </summary>
    public void SetDepthCompareMode(bool enabled) { }

    /// <summary>
    /// Sets the texture coordinate wrapping modes for when a texture is sampled outside the [0, 1] range.
    /// </summary>
    public void SetWrapModes(SamplerAddressMode u, SamplerAddressMode v, SamplerAddressMode w = SamplerAddressMode.Wrap)
    {
        AddressModeU = u;
        AddressModeV = v;
        AddressModeW = w;
        RebuildSampler();
    }

    /// <summary>
    /// Generates mipmaps for this <see cref="Texture"/>. The actual mip generation is recorded
    /// onto a command buffer that the frame loop flushes, since Graphite requires it to run inside
    /// an open frame.
    /// </summary>
    /// <exception cref="InvalidOperationException"/>
    public void GenerateMipmaps()
    {
        if (isNotMipmappable)
            throw new InvalidOperationException(string.Concat("This texture type is not mipmappable! Type: ", Type.ToString()));

        Graphics.RequestMipmapGeneration(Handle);
        IsMipmapped = true;
        SetTextureFilters(DefaultMipmapFilter);
    }

    public override void OnDispose()
    {
        // Defer the GPU handle's disposal: it may still be bound by an in-flight frame (e.g. the UI
        // drawing this texture while it is reimported). Freeing it now is a use-after-free that
        // stalls the device on SwapBuffers. Drop any queued mipmap pass against it first.
        Graphics.CancelMipmapGeneration(Handle);
        Graphics.DisposeDeferred(Handle);
        Graphics.DisposeDeferred(Sampler);
    }

    /// <summary>
    /// Gets whether the specified <see cref="TextureType"/> type is mipmappable.
    /// </summary>
    public static bool IsTextureTypeMipmappable(PixelFormat format, TextureSampleCount sampleCount)
    {
        return format != PixelFormat.D24_UNorm_S8_UInt && format != PixelFormat.D32_Float_S8_UInt
            && sampleCount == TextureSampleCount.Count1;
    }

    /// <summary>Number of mip levels in the full chain for a texture of the given dimensions.</summary>
    private protected static uint ComputeMipLevels(uint width, uint height)
    {
        uint levels = 1;
        uint size = Math.Max(width, height);
        while (size > 1) { size /= 2; levels++; }
        return levels;
    }

    /// <summary>
    /// Reads one subresource of <see cref="Handle"/> back into CPU memory. Graphite only allows
    /// mapping a Texture created with <see cref="TextureUsage.Staging"/>, and a Texture can only be
    /// mapped once the copy into it has actually finished on the GPU.
    /// <para>
    /// When called outside a frame (the common case - asset saves, editor tooling) this copies the
    /// requested region into a throwaway staging texture on its own dedicated frame, blocks until the
    /// GPU has finished it, then maps and copies the result out synchronously; <paramref name="consume"/>
    /// and <paramref name="onComplete"/> both run before this method returns, and it returns true.
    /// </para>
    /// <para>
    /// When called while a frame is already open (e.g. mid-render, such as a one-shot capture into a
    /// render target followed by a read-back) Graphite forbids opening a second frame, and the copy
    /// cannot be waited on/mapped until the frame it lands in actually completes - which cannot happen
    /// before this method would return, since nothing calls <c>EndFrame</c> until the caller's own stack
    /// unwinds back into the frame loop. So instead the copy is recorded onto that frame's own command
    /// stream (piggybacking rather than opening a redundant frame) and the map + <paramref name="consume"/>
    /// + <paramref name="onComplete"/> are deferred until <see cref="Graphics.FlushPendingReadbacks"/>
    /// finds the frame complete (drained once per tick, whenever no frame is open); this method then
    /// returns false immediately without having read anything yet.
    /// </para>
    /// </summary>
    /// <returns>True if the read-back completed synchronously before returning; false if it was queued
    /// to complete on a later tick.</returns>
    private protected bool ReadBackSubresource(TextureDescription stagingDescription, uint width, uint height,
        uint depth, uint srcMipLevel, uint srcArrayLayer, Action<MappedResource> consume, Action onComplete = null)
    {
        GraphicsDevice device = Graphics.Device;
        GraphiteTexture staging = device.ResourceFactory.CreateTexture(stagingDescription);
        staging.Name = $"{Name} Readback Staging";

        if (Graphics.CurrentFrame == null)
        {
            CommandBuffer cmd = device.ResourceFactory.CreateCommandBuffer();
            cmd.Name = "Texture Readback";
            cmd.Begin();
            cmd.CopyTexture(Handle, 0, 0, 0, srcMipLevel, srcArrayLayer,
                staging, 0, 0, 0, 0, 0, width, height, depth, 1);
            cmd.End();

            Frame frame = device.BeginFrame();
            frame.SubmitCommands(cmd);
            device.EndFrame(frame);
            device.WaitForFrame(frame);
            cmd.Dispose();

            MappedResource mapped = device.Map(staging, MapMode.Read);
            try { consume(mapped); }
            finally { device.Unmap(staging); }
            staging.Dispose();
            onComplete?.Invoke();
            return true;
        }

        CommandBuffer piggybackCmd = Graphics.GetCommandBuffer("Texture Readback");
        piggybackCmd.CopyTexture(Handle, 0, 0, 0, srcMipLevel, srcArrayLayer,
            staging, 0, 0, 0, 0, 0, width, height, depth, 1);
        ulong frameId = Graphics.CurrentFrame.FrameId;
        Graphics.Submit(piggybackCmd);

        Graphics.QueueReadback(staging, frameId, consume, onComplete);
        return false;
    }

    /// <summary>Copies one mapped region into <paramref name="destination"/>, honoring <see cref="MappedResource.RowPitch"/>/<see cref="MappedResource.DepthPitch"/> when they don't tightly pack.</summary>
    private protected unsafe void CopyMappedRegion(MappedResource mapped, nint destination, uint destinationSizeInBytes, uint width, uint height, uint depth)
    {
        byte* src = (byte*)mapped.Data;
        byte* dst = (byte*)destination;
        uint rowBytes = width * ImageFormat.GetSizeInBytes();

        if (mapped.RowPitch == rowBytes)
        {
            Buffer.MemoryCopy(src, dst, destinationSizeInBytes, (long)rowBytes * height * depth);
        }
        else
        {
            for (uint z = 0; z < depth; z++)
            {
                byte* srcSlice = src + z * mapped.DepthPitch;
                byte* dstSlice = dst + z * rowBytes * height;
                for (uint y = 0; y < height; y++)
                    Buffer.MemoryCopy(srcSlice + y * mapped.RowPitch, dstSlice + y * rowBytes, rowBytes, rowBytes);
            }
        }
    }
}
