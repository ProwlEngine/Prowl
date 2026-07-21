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

        // Newest started execution. The resource can be referenced by executions up to and including
        // this one; once it completes, nothing the GPU still has queued can touch the handle.
        ulong executionId = device.LastCompletedExecutionId + device.MaxExecutingTasks;

        lock (s_deferredDisposeLock)
            s_deferredDisposes.Add((resource, executionId));
    }

    /// <summary>Dispose any deferred resources whose retiring execution the GPU has finished. Called
    /// once per frame by the frame loop.</summary>
    public static void FlushDeferredDisposes()
    {
        GraphicsDevice device = Device;
        if (device == null) return;

        lock (s_deferredDisposeLock)
        {
            for (int i = s_deferredDisposes.Count - 1; i >= 0; i--)
            {
                (IDisposable resource, ulong executionId) = s_deferredDisposes[i];

                // executionId 0 means nothing was ever in flight at retirement; free immediately.
                // LastCompletedExecutionId advances on its own each BeginExecution (every DispatchGraph
                // call reclaims finished ring slots), so no explicit wait/poll is needed here.
                if (executionId == 0 || device.LastCompletedExecutionId >= executionId)
                {
                    resource.Dispose();
                    s_deferredDisposes.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>
    /// Generates mipmaps for a texture immediately via a <see cref="TransferCommandBuffer"/>, blocking
    /// until done. Replaces the old per-frame deferred queue: Graphite's Frame/BeginFrame/EndFrame
    /// model that queue depended on is gone, but TransferCommandBuffer.GenerateMipmaps runs outside
    /// any render pass/frame, so there is no need to defer this to a frame boundary anymore.
    /// </summary>
    public static void RequestMipmapGeneration(Graphite.Texture texture)
    {
        if (texture == null || texture.IsDisposed) return;

        TransferCommandBuffer cmd = Device.ResourceFactory.CreateTransferCommandBuffer();
        cmd.Name = "Mipmap Generation";
        cmd.Begin();
        cmd.GenerateMipmaps(texture);
        cmd.End();
        Device.SubmitAndWait(cmd);
        cmd.Dispose();
    }
}
