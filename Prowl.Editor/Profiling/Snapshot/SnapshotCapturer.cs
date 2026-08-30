using System;
using System.Collections.Generic;

using Prowl.Graphite;

namespace Prowl.Editor.Profiling;

/// <summary>
/// Captures GPU resources referenced this frame into a Snapshot. Per-pass texture copies happen in
/// HandleCapture; readbacks and everything else are deferred to Finalize.
/// </summary>
public sealed class SnapshotCapturer
{
    private sealed record PendingTextureCapture(uint Id, string FramebufferName, uint Version, List<(string Name, Texture Src, Texture Staging)> Attachments);
    private sealed record PendingBufferCapture(uint Id, string Name, DeviceBuffer Src, uint Offset, uint SizeInBytes, uint Version, DeviceBuffer Staging);

    private GraphicsDevice? _device;
    private bool _armed;

    private readonly Dictionary<uint, RenderTexture> _textures = new();
    private readonly Dictionary<uint, DeviceBuffer> _buffers = new();

    private readonly List<PendingTextureCapture> _pendingCaptures = new();
    private readonly HashSet<uint> _capturedResourceIds = new();

    private readonly Dictionary<(DeviceBuffer Buffer, uint Offset, uint Version), BufferBindingInfo> _currentPassDrawBuffers = new();
    private readonly HashSet<(DeviceBuffer Buffer, uint Offset, uint Version)> _capturedBufferVersions = new();
    private readonly List<PendingBufferCapture> _pendingBufferCaptures = new();

    public void Attach(GraphicsDevice device) => _device = device;
    public void Detach() => _device = null;

    public void OnFrameBegin()
    {
        _armed = false;
        _textures.Clear();
        _buffers.Clear();
        _currentPassDrawBuffers.Clear();
        _capturedBufferVersions.Clear();

        foreach (PendingTextureCapture pending in _pendingCaptures)
            foreach ((string _, Texture _, Texture staging) in pending.Attachments)
                staging.Dispose();
        _pendingCaptures.Clear();
        _capturedResourceIds.Clear();

        foreach (PendingBufferCapture pending in _pendingBufferCaptures)
            pending.Staging.Dispose();
        _pendingBufferCaptures.Clear();
    }

    /// <summary>Registered as EditorProfiler.BeginPass. No-op: HandleCapture now runs every pass and
    /// drains _currentPassDrawBuffers itself.</summary>
    public void OnPassBegin()
    {
    }

    public void OnPassRead(in PassInfo p, RenderResourceID id, RenderTexture? texture, DeviceBuffer? buffer)
    {
        uint resourceId = (uint)id.GetHashCode();
        if (texture != null)
            _textures[resourceId] = texture;
        else if (buffer != null)
            _buffers[resourceId] = buffer;
    }

    /// <summary>Registered as EditorProfiler.RecordDrawBuffers. Dedupes bindings by (Buffer, Offset,
    /// ContentVersion) so each revision a draw saw gets its own capture.</summary>
    public void OnDrawBuffers(in DrawBufferInfo info)
    {
        foreach (BufferBindingInfo vb in info.VertexBuffers)
            _currentPassDrawBuffers[(vb.Buffer, vb.Offset, vb.ContentVersion)] = vb;

        if (info.IndexBuffer is { } ib)
            _currentPassDrawBuffers[(ib.Buffer, ib.Offset, ib.ContentVersion)] = ib;

        foreach (BufferBindingInfo b in info.BoundBuffers)
            _currentPassDrawBuffers[(b.Buffer, b.Offset, b.ContentVersion)] = b;
    }

    /// <summary>Registered as EditorProfiler.CaptureHandler. Arms the capture and copies this pass's
    /// outputs to staging. Readback happens in Finalize, so this always returns null.</summary>
    public Snapshot? HandleCapture(in PassInfo pass, IReadOnlyList<Framebuffer> passOutputs, TransferCommandBuffer transfer)
    {
        _armed = true;

        if (_device is null)
            return null;

        foreach (Framebuffer fb in passOutputs)
        {
            var attachments = new List<(string Name, Texture Src, Texture Staging)>();

            foreach (FramebufferAttachment color in fb.ColorTargets)
                if (CopyToStaging(_device, transfer, color.Target) is { } colorStaging)
                    attachments.Add((color.Target.Name, color.Target, colorStaging));
            if (fb.DepthTarget is { } depth && CopyToStaging(_device, transfer, depth.Target) is { } depthStaging)
                attachments.Add((depth.Target.Name, depth.Target, depthStaging));

            if (attachments.Count == 0)
                continue;

            uint id = ResolveId(fb);
            _pendingCaptures.Add(new PendingTextureCapture(id, fb.Name, (uint)pass.Index, attachments));
            _capturedResourceIds.Add(id);
        }

        StageDrawBuffers(transfer);
        StageRemainingResources(_device, transfer);

        return null;
    }

    // Catches resources seen via OnPassRead that weren't this pass's framebuffer outputs (compute-bound
    // buffers, textures read but never written). Staged on the same per-pass transfer as everything else
    // so the copy is ordered after the GPU work that produced the data, instead of Finalize's old
    // stand-alone submit which could run before that work was even flushed.
    private void StageRemainingResources(GraphicsDevice device, TransferCommandBuffer transfer)
    {
        foreach (KeyValuePair<uint, RenderTexture> entry in _textures)
        {
            if (_capturedResourceIds.Contains(entry.Key))
                continue;

            RenderTexture rt = entry.Value;
            var attachments = new List<(string Name, Texture Src, Texture Staging)>();

            foreach (Texture color in rt.ColorTextures)
                if (CopyToStaging(device, transfer, color) is { } colorStaging)
                    attachments.Add((color.Name, color, colorStaging));
            if (rt.DepthTexture != null && CopyToStaging(device, transfer, rt.DepthTexture) is { } depthStaging)
                attachments.Add((rt.DepthTexture.Name, rt.DepthTexture, depthStaging));

            if (attachments.Count == 0)
                continue;

            _pendingCaptures.Add(new PendingTextureCapture(entry.Key, rt.Framebuffer.Name, 0, attachments));
            _capturedResourceIds.Add(entry.Key);
        }

        foreach (KeyValuePair<uint, DeviceBuffer> entry in _buffers)
        {
            if (_capturedResourceIds.Contains(entry.Key))
                continue;

            DeviceBuffer src = entry.Value;
            DeviceBuffer staging = device.ResourceFactory.CreateBuffer(new BufferDescription(src.SizeInBytes, BufferUsage.Staging));
            transfer.CopyBuffer(src, 0, staging, 0, src.SizeInBytes);
            _pendingBufferCaptures.Add(new PendingBufferCapture(entry.Key, src.Name, src, 0, src.SizeInBytes, src.ContentVersion, staging));
            _capturedResourceIds.Add(entry.Key);
        }
    }

    // Stage each distinct (Buffer, Offset, ContentVersion) revision exactly once, then clear the set.
    private void StageDrawBuffers(TransferCommandBuffer transfer)
    {
        if (_device is null || _currentPassDrawBuffers.Count == 0)
            return;

        GraphicsDevice device = _device;
        foreach (KeyValuePair<(DeviceBuffer Buffer, uint Offset, uint Version), BufferBindingInfo> entry in _currentPassDrawBuffers)
        {
            if (_capturedBufferVersions.Contains(entry.Key))
                continue;

            DeviceBuffer src = entry.Key.Buffer;
            uint offset = entry.Key.Offset;
            uint version = entry.Key.Version;
            BufferBindingInfo binding = entry.Value;

            DeviceBuffer staging = device.ResourceFactory.CreateBuffer(new BufferDescription(binding.SizeInBytes, BufferUsage.Staging));
            transfer.CopyBuffer(src, offset, staging, 0, binding.SizeInBytes);
            uint bufId = (uint)src.GetHashCode() ^ offset;
            _pendingBufferCaptures.Add(new PendingBufferCapture(bufId, binding.Name, src, offset, binding.SizeInBytes, version, staging));
            _capturedBufferVersions.Add(entry.Key);
        }
        _currentPassDrawBuffers.Clear();
    }

    // RecordPassRead runs before Capture, so _textures already holds this pass's outputs.
    private uint ResolveId(Framebuffer fb)
    {
        foreach (KeyValuePair<uint, RenderTexture> entry in _textures)
        {
            if (ReferenceEquals(entry.Value.Framebuffer, fb))
                return entry.Key;
        }
        return (uint)fb.GetHashCode();
    }

    /// <summary>Registered as EditorProfiler.CaptureFinalizeHandler. Called once per armed frame with a
    /// cloned ProfiledFrame.</summary>
    public Snapshot? Finalize(ProfiledFrame frame)
    {
        if (!_armed || _device is null)
            return null;

        GraphicsDevice device = _device;
        _armed = false;

        var resources = new Dictionary<uint, (string Name, SnapshotResourceKind Kind, List<SnapshotResourceVersion> Versions)>();

        void AddVersion(uint id, string name, SnapshotResourceKind kind, SnapshotResourceVersion version)
        {
            if (!resources.TryGetValue(id, out (string Name, SnapshotResourceKind Kind, List<SnapshotResourceVersion> Versions) entry))
            {
                entry = (name, kind, new List<SnapshotResourceVersion>());
                resources[id] = entry;
            }
            entry.Versions.Add(version);
        }

        // Everything reachable this frame was already staged per-pass in HandleCapture, on that pass's own
        // transfer buffer, so the GPU work producing the data is guaranteed flushed ahead of the copy.
        // Finalize only needs to wait for all of it to land, then read the staging buffers/textures back.
        if (_pendingCaptures.Count > 0 || _pendingBufferCaptures.Count > 0)
        {
            device.WaitForIdle();

            foreach (PendingBufferCapture pending in _pendingBufferCaptures)
            {
                byte[] data = ReadBufferBytes(device, pending.Staging, pending.SizeInBytes);
                var meta = new SnapshotBufferMeta(ClassifyKind(pending.Src.Usage), pending.SizeInBytes, 0, Array.Empty<BufferField>());
                AddVersion(pending.Id, pending.Name, SnapshotResourceKind.Buffer, new SnapshotResourceVersion(pending.Version, Array.Empty<SnapshotSubTexture>(), data, meta));
                pending.Staging.Dispose();
            }
            _pendingBufferCaptures.Clear();

            foreach (PendingTextureCapture pending in _pendingCaptures)
            {
                var subtextures = new List<SnapshotSubTexture>(pending.Attachments.Count);
                foreach ((string name, Texture src, Texture staging) in pending.Attachments)
                {
                    byte[] pixels = ReadTextureMip0(device, staging);
                    subtextures.Add(new SnapshotSubTexture(name, src.Format, src.Width, src.Height, src.Depth, src.MipLevels, pixels));
                    staging.Dispose();
                }
                AddVersion(pending.Id, pending.FramebufferName, SnapshotResourceKind.Texture, new SnapshotResourceVersion(pending.Version, subtextures, Array.Empty<byte>(), null));
            }
            _pendingCaptures.Clear();
        }

        _capturedResourceIds.Clear();

        var result = new List<SnapshotResource>(resources.Count);
        foreach (KeyValuePair<uint, (string Name, SnapshotResourceKind Kind, List<SnapshotResourceVersion> Versions)> entry in resources)
        {
            entry.Value.Versions.Sort((a, b) => a.Version.CompareTo(b.Version));
            result.Add(new SnapshotResource(entry.Key, entry.Value.Name, entry.Value.Kind, entry.Value.Versions));
        }

        return new Snapshot(null, frame.FrameIndex, frame, result);
    }

    // Depth-stencil formats can't be combined with TextureUsage.Staging, so they can't be captured.
    private static bool IsStageable(PixelFormat format)
        => format != PixelFormat.D24_UNorm_S8_UInt && format != PixelFormat.D32_Float_S8_UInt;

    private static Texture? CopyToStaging(GraphicsDevice device, TransferCommandBuffer xfer, Texture src)
    {
        if (!IsStageable(src.Format))
            return null;

        TextureDescription desc = TextureDescription.Texture2D(
            src.Width, src.Height, src.MipLevels, src.ArrayLayers, src.Format, TextureUsage.Staging);
        Texture staging = device.ResourceFactory.CreateTexture(desc);
        xfer.CopyTexture(src, staging, 0, 0);
        return staging;
    }

    // Reads back mip 0 / layer 0 only; MipLevels is still reported as metadata.
    private static unsafe byte[] ReadTextureMip0(GraphicsDevice device, Texture staging)
    {
        uint bytesPerPixel = staging.Format.GetSizeInBytes();
        uint rowBytes = staging.Width * bytesPerPixel;
        var result = new byte[rowBytes * staging.Height * staging.Depth];

        MappedResource map = device.Map(staging, MapMode.Read, 0);
        try
        {
            byte* src = (byte*)map.Data;
            fixed (byte* dst = result)
            {
                for (uint z = 0; z < staging.Depth; z++)
                {
                    for (uint y = 0; y < staging.Height; y++)
                    {
                        byte* srcRow = src + (z * map.DepthPitch) + (y * map.RowPitch);
                        byte* dstRow = dst + (((z * staging.Height) + y) * rowBytes);
                        Buffer.MemoryCopy(srcRow, dstRow, rowBytes, rowBytes);
                    }
                }
            }
        }
        finally
        {
            device.Unmap(staging, 0);
        }

        return result;
    }

    private static unsafe byte[] ReadBufferBytes(GraphicsDevice device, DeviceBuffer staging, uint sizeInBytes)
    {
        var result = new byte[sizeInBytes];

        MappedResource map = device.Map(staging, MapMode.Read);
        try
        {
            fixed (byte* dst = result)
                Buffer.MemoryCopy((void*)map.Data, dst, sizeInBytes, sizeInBytes);
        }
        finally
        {
            device.Unmap(staging);
        }

        return result;
    }

    private static BufferUsage ClassifyKind(BufferUsage usage)
    {
        if ((usage & BufferUsage.IndexBuffer) != 0)
            return BufferUsage.IndexBuffer;
        if ((usage & BufferUsage.VertexBuffer) != 0)
            return BufferUsage.VertexBuffer;
        if ((usage & BufferUsage.UniformBuffer) != 0)
            return BufferUsage.UniformBuffer;
        if ((usage & (BufferUsage.StructuredBufferReadOnly | BufferUsage.StructuredBufferReadWrite)) != 0)
            return usage & (BufferUsage.StructuredBufferReadOnly | BufferUsage.StructuredBufferReadWrite);
        if ((usage & BufferUsage.IndirectBuffer) != 0)
            return BufferUsage.IndirectBuffer;
        return usage;
    }
}
