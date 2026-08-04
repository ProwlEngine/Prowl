using System;
using System.Runtime.InteropServices;

namespace Prowl.Graphite;

/// <summary>
/// Records buffer/texture transfer commands (update, copy, mipmap gen), submittable with or without an open frame.
/// <para>
/// No draw/dispatch, no framebuffer state, no property binding - just moves data outside the frame ring-buffer.
/// Good for one-off stuff like readback or streaming uploads without opening a throwaway frame.
/// </para>
/// <para>
/// Get one from ResourceFactory.CreateTransferCommandBuffer. Submit with GraphicsDevice.SubmitAndWait, which
/// blocks until the GPU finishes. Reusable across multiple Begin/End/SubmitAndWait cycles.
/// </para>
/// Not thread-safe, sync externally.
/// </summary>
public abstract partial class TransferCommandBuffer : CommandBufferBase
{
    private static long s_nextId;

    /// <summary>Fresh id per instance, so profiler events across this buffer's lifetime can be correlated. Never tied to a Pass.</summary>
    internal ulong Id { get; } = (ulong)System.Threading.Interlocked.Increment(ref s_nextId);

    internal CommandBufferInfo ProfilerInfo => new(Id, Name, null);

    /// <summary>
    /// Owning device.
    /// </summary>
    public abstract GraphicsDevice Device { get; }

    /// <summary>
    /// Resets to initial state. Call before issuing other commands. Only valid if never called before, or after End
    /// or SubmitAndWait.
    /// </summary>
    public abstract void Begin();

    /// <summary>
    /// Finishes recording, makes the command list executable. Must be called after Begin.
    /// </summary>
    public abstract void End();

    /// <summary>
    /// Updates part of a texture. T must be blittable.
    /// </summary>
    public unsafe void UpdateTexture<T>(
        Texture texture,
        ReadOnlySpan<T> source,
        uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer) where T : unmanaged
    {
        uint sizeInBytes = (uint)(sizeof(T) * source.Length);
        fixed (void* pin = &MemoryMarshal.GetReference(source))
        {
            UpdateTexture(texture, (IntPtr)pin, sizeInBytes, x, y, z, width, height, depth, mipLevel, arrayLayer);
        }
    }

    /// <summary>
    /// Updates part of a texture.
    /// </summary>
    public void UpdateTexture(
        Texture texture,
        IntPtr source,
        uint sizeInBytes,
        uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer)
    {
        UpdateTextureCore(texture, source, sizeInBytes, x, y, z, width, height, depth, mipLevel, arrayLayer);
    }

    private protected abstract void UpdateTextureCore(
        Texture texture,
        IntPtr source,
        uint sizeInBytes,
        uint x, uint y, uint z,
        uint width, uint height, uint depth,
        uint mipLevel, uint arrayLayer);
}
