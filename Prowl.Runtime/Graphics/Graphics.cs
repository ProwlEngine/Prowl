// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Graphite;

namespace Prowl.Runtime;

/// <summary>
/// Slim facade hosting the single ambient Prowl.Graphite <see cref="Device"/> plus a few
/// helpers the resource wrappers (Texture2D, Mesh, RenderTexture, ...) read internally.
/// The GL backend and render thread it used to own were removed in the Graphite port:
/// Graphite manages its own threading, and the frame loop lives in <see cref="Window"/>.
/// </summary>
public static class Graphics
{
    public static int MaxTextureSize { get; internal set; }
    public static int MaxCubeMapTextureSize { get; internal set; }
    public static int MaxArrayTextureLayers { get; internal set; }
    public static int MaxFramebufferColorAttachments { get; internal set; }

    /// <summary>
    /// The single ambient Prowl.Graphite device. Created during window load and read
    /// internally by the resource wrappers to allocate GPU resources.
    /// </summary>
    public static GraphicsDevice Device { get; internal set; }

    /// <summary>
    /// The frame currently open for recording, set by <see cref="Window"/>'s main loop between
    /// <c>BeginFrame</c> and <c>EndFrame</c>. Renderers record command buffers and submit them here.
    /// Null outside the render phase.
    /// </summary>
    public static Frame CurrentFrame { get; internal set; }

    // Mipmap generation must be recorded onto a command buffer inside an open frame,
    // but textures request it at any time (e.g. during async asset load). Requests are
    // queued here and drained by the frame loop via SubmitPendingMipmaps.
    private static readonly List<Texture> s_pendingMipmaps = new();
    private static CommandBuffer s_mipmapCommands;

    /// <summary>
    /// Reads device limits Prowl validates against. Call once after <see cref="Device"/> is set.
    /// </summary>
    public static void QueryDeviceLimits()
    {
        if (Device.GetPixelFormatSupport(PixelFormat.R8_G8_B8_A8_UNorm,
            TextureType.Texture2D, TextureUsage.Sampled, out PixelFormatProperties props2D))
        {
            MaxTextureSize = (int)props2D.MaxWidth;
            MaxArrayTextureLayers = (int)props2D.MaxArrayLayers;
        }

        if (Device.GetPixelFormatSupport(PixelFormat.R8_G8_B8_A8_UNorm,
            TextureType.Texture2D,
            TextureUsage.Sampled | TextureUsage.Cubemap, out PixelFormatProperties propsCube))
        {
            MaxCubeMapTextureSize = (int)propsCube.MaxWidth;
        }

        // Graphite exposes no color-attachment-count query; 8 is the value every backend guarantees.
        MaxFramebufferColorAttachments = 8;
    }

    /// <summary>
    /// Opens a fresh Graphite command buffer for recording. Bridges the old Prowl
    /// <c>Graphics.GetCommandBuffer</c> API the render pipeline still uses.
    /// </summary>
    public static CommandBuffer GetCommandBuffer(string name = "")
    {
        CommandBuffer cb = Device.ResourceFactory.CreateCommandBuffer();
        cb.Begin();
        return cb;
    }

    /// <summary>
    /// Closes a command buffer opened with <see cref="GetCommandBuffer"/>. Replaying recorded work
    /// onto the open <see cref="CurrentFrame"/> is part of the Stage-2 render-loop port (the old
    /// render-thread executor was removed), so the buffer is only ended here and may be disposed
    /// by the caller.
    /// </summary>
    public static void Submit(CommandBuffer cmd)
    {
        cmd.End();
        // CurrentFrame?.SubmitCommands(cmd); // Stage-2: re-enable once the frame loop owns these buffers.
    }

    /// <summary>No-op retained for the old deferred-dispose API; Graphite owns resource lifetime.</summary>
    public static void FlushDeferredDisposes() { }

    /// <summary>
    /// Sampler wrap/filter now live on <see cref="Resources.Texture.Sampler"/> rather than the GPU
    /// texture, so these old GL-style setters that take a raw Graphite texture are no-ops. Call
    /// <see cref="Resources.Texture.SetWrapModes"/> / <see cref="Resources.Texture.SetTextureFilters"/>
    /// on the Prowl texture wrapper instead.
    /// </summary>
    public static void SetWrapS(Graphite.Texture texture, TextureWrap wrap) { }

    /// <inheritdoc cref="SetWrapS"/>
    public static void SetWrapT(Graphite.Texture texture, TextureWrap wrap) { }

    /// <inheritdoc cref="SetWrapS"/>
    public static void SetTextureFilters(Graphite.Texture texture, TextureMin min, TextureMag mag) { }

    /// <summary>Queue a Graphite texture for mipmap generation on the next open frame.</summary>
    public static void RequestMipmapGeneration(Graphite.Texture texture)
    {
        if (texture == null) return;
        lock (s_pendingMipmaps)
            s_pendingMipmaps.Add(texture);
    }

    /// <summary>Record queued mipmap generations into a command buffer and submit them onto
    /// <paramref name="frame"/>. Called by the frame loop while a frame is open.</summary>
    public static void SubmitPendingMipmaps(Graphite.Frame frame)
    {
        lock (s_pendingMipmaps)
        {
            if (s_pendingMipmaps.Count == 0)
                return;

            s_mipmapCommands ??= Device.ResourceFactory.CreateCommandBuffer();
            s_mipmapCommands.Begin();
            for (int i = 0; i < s_pendingMipmaps.Count; i++)
                s_mipmapCommands.GenerateMipmaps(s_pendingMipmaps[i]);
            s_mipmapCommands.End();
            s_pendingMipmaps.Clear();
        }

        frame.SubmitCommands(s_mipmapCommands);
    }
}
