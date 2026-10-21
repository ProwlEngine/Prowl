using System.Diagnostics;

namespace Prowl.Graphite;

public abstract partial class GraphicsDevice
{
    private readonly object _progressLock = new();
    private float _gpuHangSeconds;
    private ulong _progressSerial;
    private long _progressTimestamp;
    private long _lastProgressCheck;

    /// <summary>
    /// True = validation layer runs this device's checks. Set once at device creation.
    /// </summary>
    internal bool ValidationEnabled { get; set; }

    private void InitializeFrameOptions_SetValidationEnabled(in GraphicsDeviceOptions options)
    {
        ValidationEnabled = options.GraphiteValidation;
        _gpuHangSeconds = options.GpuHangSeconds;
    }

    /// <summary>True when waits on the GPU should check it still makes progress.</summary>
    internal bool GpuHangCheckEnabled => ValidationEnabled && _gpuHangSeconds > 0;

    /// <summary>Called by a backend between slices of a wait: throws once the GPU finished nothing for the hang time.</summary>
    internal void CheckGpuProgress(ulong completed, ulong waitingFor)
    {
        if (!GpuHangCheckEnabled || completed >= waitingFor)
            return;

        long now = Stopwatch.GetTimestamp();
        lock (_progressLock)
        {
            bool fresh = Stopwatch.GetElapsedTime(_lastProgressCheck, now).TotalSeconds > 1;
            _lastProgressCheck = now;
            if (fresh || completed != _progressSerial)
            {
                _progressSerial = completed;
                _progressTimestamp = now;
                return;
            }
            if (Stopwatch.GetElapsedTime(_progressTimestamp, now).TotalSeconds < _gpuHangSeconds)
                return;
        }

        throw new RenderException(
            $"The GPU finished no work for {_gpuHangSeconds} seconds while waiting for submission {waitingFor} (last finished: {completed}). " +
            "It is most likely hung on a draw or dispatch, or the device was lost. Check the driver's logs. GraphicsDeviceOptions.GpuHangSeconds sets this limit.");
    }

    private void SyncToVerticalBlank_CheckMainSwapchain()
    {
        if (!ValidationEnabled)
            return;

        if (MainSwapchain == null)
        {
            throw new RenderException("This GraphicsDevice was created without a main Swapchain. This property cannot be set.");
        }
    }

    private void Map_CheckResource(DeviceBuffer buffer)
    {
        if (!ValidationEnabled)
            return;

        if ((buffer.Usage & BufferUsage.Dynamic) != BufferUsage.Dynamic
            && (buffer.Usage & BufferUsage.Staging) != BufferUsage.Staging)
        {
            throw new RenderException("Buffers must have the Staging or Dynamic usage flag to be mapped.");
        }
    }

    internal void UpdateTexture_CheckParameters(Texture texture, in TextureRegion region)
    {
        if (!ValidationEnabled)
            return;

        if (FormatHelpers.IsCompressedFormat(texture.Format))
        {
            if (region.X % 4 != 0 || region.Y % 4 != 0 || region.Height % 4 != 0 || region.Width % 4 != 0)
            {
                Util.GetMipDimensions(texture, region.MipLevel, out uint mipWidth, out uint mipHeight, out _);
                if (region.Width != mipWidth && region.Height != mipHeight)
                {
                    throw new RenderException($"Updates to block-compressed textures must use a region that is block-size aligned and sized.");
                }
            }
        }
    }
}
