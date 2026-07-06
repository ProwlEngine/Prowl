// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
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
    // Defaulted to conservative real-world minimums so CPU-side validation (e.g. texture size
    // checks) passes before/without a GL context. Initialize() overwrites them with real device
    // limits when a graphics device is present.
    public static int MaxTextureSize { get; internal set; } = 16384;
    public static int MaxCubeMapTextureSize { get; internal set; } = 16384;
    public static int MaxArrayTextureLayers { get; internal set; } = 2048;
    public static int MaxFramebufferColorAttachments { get; internal set; } = 8;

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

    // GPU resources retired this frame may still be referenced by frames the GPU hasn't finished.
    // Disposing them immediately is a use-after-free that wedges the device on the next SwapBuffers
    // (notably reimporting a texture that the UI or a pending mipmap pass is still reading). Each
    // entry is paired with the newest in-flight frame id at retirement time; it is freed only once
    // that frame has completed, drained every frame by FlushDeferredDisposes.
    private static readonly object s_deferredDisposeLock = new();
    private static readonly List<(IDisposable resource, ulong frameId)> s_deferredDisposes = new();

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

    // Pooled command buffers. Submit returns a CB to this pool; GetCommandBuffer rents from it.
    // CBs are reused across frames; Begin() at rent time clears the previous recording.
    private static readonly List<CommandBuffer> s_cbPool = new();

    /// <summary>
    /// Opens a fresh Graphite command buffer for recording. Rents from an internal pool;
    /// the caller must call <see cref="Submit"/> when done to close and return the buffer.
    /// </summary>
    public static CommandBuffer GetCommandBuffer(string name = "")
    {
        CommandBuffer cb;
        if (s_cbPool.Count > 0)
        {
            cb = s_cbPool[^1];
            s_cbPool.RemoveAt(s_cbPool.Count - 1);
        }
        else
        {
            cb = Device.ResourceFactory.CreateCommandBuffer();
            cb.Name = "Pooled CommandBuffer";
        }
        cb.Begin();
        if (!string.IsNullOrEmpty(name))
            cb.Name = name;
        return cb;
    }

    /// <summary>
    /// Ends a command buffer opened with <see cref="GetCommandBuffer"/>, submits it to the open
    /// frame for execution, and returns it to the internal pool for reuse.
    /// </summary>
    public static void Submit(CommandBuffer cmd)
    {
        cmd.End();
        CurrentFrame?.SubmitCommands(cmd);
        s_cbPool.Add(cmd);
    }

    /// <summary>
    /// Queue a GPU resource for disposal once the GPU has finished every frame that could still
    /// reference it. Use this instead of <c>resource.Dispose()</c> for any handle that may be bound
    /// by an in-flight command buffer (textures, samplers, buffers retired during asset reimport).
    /// Falls back to immediate disposal when no device/frame exists yet.
    /// </summary>
    public static void DisposeDeferred(IDisposable resource)
    {
        if (resource == null) return;

        GraphicsDevice device = Device;
        if (device == null)
        {
            resource.Dispose();
            return;
        }

        // Newest started frame. The resource can be referenced by frames up to and including this
        // one; once it completes, nothing the GPU still has queued can touch the handle.
        ulong frameId = device.LastCompletedFrameId + device.FramesInFlight;

        lock (s_deferredDisposeLock)
            s_deferredDisposes.Add((resource, frameId));
    }

    /// <summary>Dispose any deferred resources whose retiring frame the GPU has finished. Called once
    /// per frame by the frame loop.</summary>
    public static void FlushDeferredDisposes()
    {
        GraphicsDevice device = Device;
        if (device == null) return;

        lock (s_deferredDisposeLock)
        {
            for (int i = s_deferredDisposes.Count - 1; i >= 0; i--)
            {
                (IDisposable resource, ulong frameId) = s_deferredDisposes[i];

                // frameId 0 means nothing was ever in flight at retirement; free immediately.
                if (frameId == 0 || device.IsFrameComplete(frameId))
                {
                    resource.Dispose();
                    s_deferredDisposes.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>Queue a Graphite texture for mipmap generation on the next open frame.</summary>
    public static void RequestMipmapGeneration(Graphite.Texture texture)
    {
        if (texture == null) return;
        lock (s_pendingMipmaps)
            s_pendingMipmaps.Add(texture);
    }

    /// <summary>Drop any pending mipmap request for a texture being retired, so the next
    /// <see cref="SubmitPendingMipmaps"/> doesn't record a generate against a dead handle.</summary>
    public static void CancelMipmapGeneration(Graphite.Texture texture)
    {
        if (texture == null) return;
        lock (s_pendingMipmaps)
            s_pendingMipmaps.Remove(texture);
    }

    /// <summary>Record queued mipmap generations into a command buffer and submit them onto
    /// <paramref name="frame"/>. Called by the frame loop while a frame is open.</summary>
    public static void SubmitPendingMipmaps(Graphite.Frame frame)
    {
        int recorded = 0;
        lock (s_pendingMipmaps)
        {
            if (s_pendingMipmaps.Count == 0)
                return;

            if (s_mipmapCommands == null)
            {
                s_mipmapCommands = Device.ResourceFactory.CreateCommandBuffer();
                s_mipmapCommands.Name = "Mipmap Generation";
            }
            s_mipmapCommands.Begin();
            for (int i = 0; i < s_pendingMipmaps.Count; i++)
            {
                Texture texture = s_pendingMipmaps[i];
                // A texture retired before its mipmap pass ran leaves a stale entry; generating
                // against the dead handle is what stalled the device on reimport.
                if (texture == null || texture.IsDisposed)
                    continue;
                s_mipmapCommands.GenerateMipmaps(texture);
                recorded++;
            }
            s_mipmapCommands.End();
            s_pendingMipmaps.Clear();
        }

        if (recorded > 0)
            frame.SubmitCommands(s_mipmapCommands);
    }
}
