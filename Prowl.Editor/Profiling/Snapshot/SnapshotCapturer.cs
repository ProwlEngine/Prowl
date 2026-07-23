using System;
using System.Collections.Generic;

using Prowl.Graphite;

namespace Prowl.Editor.Profiling;

/// <summary>
/// Services capture requests: tracks textures/buffers referenced via pass reads this frame (cheap,
/// every frame), and when a capture is armed, reads them back to CPU and assembles a Snapshot once
/// the frame's GPU work has been submitted (see EditorProfiler.CaptureFinalizeHandler).
///
/// IProfiler.Capture runs mid-frame, once per pass that has texture outputs, and must not block
/// waiting on the GPU - Graphite non-blocking-submits the given TransferCommandBuffer right after
/// Capture returns. HandleCapture uses that per-pass buffer to copy the pass's own outputs to
/// staging immediately, tagged with that pass's index as the resource's version (matching the
/// ResourceRef.Resource stamped by PassGraphCollector), so a render target reused and overwritten by a
/// later pass still yields a distinct captured version for each pass that wrote it. The CPU-side
/// map/readback of those staging copies is deferred to Finalize, once the frame's GPU work has been
/// submitted, using GraphicsDevice.WaitForIdle/SubmitAndWait.
///
/// Buffers referenced via RecordPassRead (render-graph-declared pass inputs/outputs) have no
/// equivalent per-pass capture hook (Capture is only invoked for passes with texture outputs), so they
/// are still copied to staging once, in Finalize, tagged with whatever ContentVersion they have at that
/// point - reflecting only their end-of-frame state, not every version PassGraphCollector may have
/// stamped into the tree if the buffer was written more than once this frame. This is a known gap:
/// draw-time buffers (below) don't have it because HandleCapture stages them synchronously per-pass.
///
/// Draw-call buffers (vertex/index buffers plus bound PropertySet buffers, reported via
/// RecordDrawBuffers - not visible to the render graph at all) are tracked per pass in progress and
/// staged the same way as this pass's output textures: at HandleCapture, using that pass's own
/// TransferCommandBuffer, so they reflect the state as of this pass's draws.
///
/// A whole DeviceBuffer isn't always one logical resource - a transient/streaming buffer serves many
/// independent sub-allocations across a frame, one per draw - so draw-call buffers are tracked and
/// deduped by (DeviceBuffer, Offset), not by DeviceBuffer alone, using the same
/// (uint)DeviceBuffer.GetHashCode() ^ Offset identity DrawHierarchyCollector stamps onto
/// ReferenceBuffer.Resource. Within that, DeviceBuffer.ContentVersion (bumped on every CPU write /
/// GPU-side copy into a buffer) lets a buffer that's rebound unchanged across many passes get staged
/// exactly once per distinct version instead of once per pass.
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

    private readonly Dictionary<(DeviceBuffer Buffer, uint Offset), BufferBindingInfo> _currentPassDrawBuffers = new();
    private readonly Dictionary<(DeviceBuffer Buffer, uint Offset), uint> _lastCapturedBufferVersion = new();
    private readonly List<PendingBufferCapture> _pendingBufferCaptures = new();

    public void Attach(GraphicsDevice device) => _device = device;
    public void Detach() => _device = null;

    public void OnFrameBegin()
    {
        _armed = false;
        _textures.Clear();
        _buffers.Clear();
        _currentPassDrawBuffers.Clear();
        _lastCapturedBufferVersion.Clear();

        foreach (PendingTextureCapture pending in _pendingCaptures)
            foreach ((string _, Texture _, Texture staging) in pending.Attachments)
                staging.Dispose();
        _pendingCaptures.Clear();
        _capturedResourceIds.Clear();

        foreach (PendingBufferCapture pending in _pendingBufferCaptures)
            pending.Staging.Dispose();
        _pendingBufferCaptures.Clear();
    }

    /// <summary>Registered as EditorProfiler.BeginPass. Drops any draw-call buffers tracked for the
    /// previous pass, so they never bleed into a later pass's capture (e.g. if the previous pass's
    /// Capture never fired because it had no texture outputs).</summary>
    public void OnPassBegin()
    {
        _currentPassDrawBuffers.Clear();
    }

    public void OnPassRead(in PassInfo p, RenderResourceID id, RenderTexture? texture, DeviceBuffer? buffer)
    {
        uint resourceId = (uint)id.GetHashCode();
        if (texture != null)
            _textures[resourceId] = texture;
        else if (buffer != null)
            _buffers[resourceId] = buffer;
    }

    /// <summary>Registered as EditorProfiler.RecordDrawBuffers, via IProfiler.RequestCapture -
    /// Graphite only reports this when a capture is armed. Bindings are deduped by (Buffer, Offset)
    /// within the current pass; the last one reported for a given (Buffer, Offset) wins, which also
    /// carries that binding's latest ContentVersion as of this draw.</summary>
    public void OnDrawBuffers(in DrawBufferInfo info)
    {
        foreach (BufferBindingInfo vb in info.VertexBuffers)
            _currentPassDrawBuffers[(vb.Buffer, vb.Offset)] = vb;

        if (info.IndexBuffer is { } ib)
            _currentPassDrawBuffers[(ib.Buffer, ib.Offset)] = ib;

        foreach (BufferBindingInfo b in info.BoundBuffers)
            _currentPassDrawBuffers[(b.Buffer, b.Offset)] = b;
    }

    /// <summary>Registered as EditorProfiler.CaptureHandler. Marks a capture as armed for this
    /// frame and records a copy of this pass's outputs to staging textures using the given
    /// per-pass TransferCommandBuffer, tagged with this pass's index as the resource's version. The
    /// CPU-side readback of those staging textures happens later in Finalize, so this always returns
    /// null.</summary>
    public Snapshot? HandleCapture(in PassInfo pass, IReadOnlyList<Framebuffer> passOutputs, TransferCommandBuffer transfer)
    {
        _armed = true;

        if (_device is null)
            return null;

        foreach (Framebuffer fb in passOutputs)
        {
            var attachments = new List<(string Name, Texture Src, Texture Staging)>();

            foreach (FramebufferAttachment color in fb.ColorTargets)
                attachments.Add((color.Target.Name, color.Target, CopyToStaging(_device, transfer, color.Target)));
            if (fb.DepthTarget is { } depth)
                attachments.Add((depth.Target.Name, depth.Target, CopyToStaging(_device, transfer, depth.Target)));

            if (attachments.Count == 0)
                continue;

            uint id = ResolveId(fb);
            _pendingCaptures.Add(new PendingTextureCapture(id, fb.Name, (uint)pass.Index, attachments));
            _capturedResourceIds.Add(id);
        }

        foreach (KeyValuePair<(DeviceBuffer Buffer, uint Offset), BufferBindingInfo> entry in _currentPassDrawBuffers)
        {
            (DeviceBuffer src, uint offset) = entry.Key;
            BufferBindingInfo binding = entry.Value;

            // Same (Buffer, Offset) already staged this frame at this exact content version - the
            // bytes can't have changed, so skip the redundant copy.
            if (_lastCapturedBufferVersion.TryGetValue(entry.Key, out uint capturedVersion)
                && capturedVersion == binding.ContentVersion)
            {
                continue;
            }

            DeviceBuffer staging = _device.ResourceFactory.CreateBuffer(new BufferDescription(binding.SizeInBytes, BufferUsage.Staging));
            transfer.CopyBuffer(src, offset, staging, 0, binding.SizeInBytes);
            uint bufId = (uint)src.GetHashCode() ^ offset;
            _pendingBufferCaptures.Add(new PendingBufferCapture(bufId, binding.Name, src, offset, binding.SizeInBytes, binding.ContentVersion, staging));
            _lastCapturedBufferVersion[entry.Key] = binding.ContentVersion;
        }
        _currentPassDrawBuffers.Clear();

        return null;
    }

    // RecordPassRead is invoked for this pass's outputs immediately before Capture, so _textures
    // already holds an entry for each of passOutputs by the time HandleCapture runs.
    private uint ResolveId(Framebuffer fb)
    {
        foreach (KeyValuePair<uint, RenderTexture> entry in _textures)
        {
            if (ReferenceEquals(entry.Value.Framebuffer, fb))
                return entry.Key;
        }
        return (uint)fb.GetHashCode();
    }

    /// <summary>Registered as EditorProfiler.CaptureFinalizeHandler. Called once per frame where a
    /// capture was armed, with a cloned, fully independent (HasCaptureDepth == true) ProfiledFrame.</summary>
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

        // Textures referenced this frame that were never captured via a pass's own Capture
        // invocation (e.g. only read, not written, while armed) still need a copy - do that now,
        // reflecting their end-of-frame state (version 0, matching PassGraphCollector's fallback for a
        // texture nothing wrote this frame) since no earlier hook was available for them.
        var uncapturedTextures = new List<KeyValuePair<uint, RenderTexture>>();
        foreach (KeyValuePair<uint, RenderTexture> entry in _textures)
            if (!_capturedResourceIds.Contains(entry.Key))
                uncapturedTextures.Add(entry);

        if (_pendingCaptures.Count > 0 || _pendingBufferCaptures.Count > 0 || uncapturedTextures.Count > 0 || _buffers.Count > 0)
        {
            device.WaitForIdle();

            var stagingTextures = new List<(uint Id, string TextureName, uint Version, List<(string Name, Texture Src, Texture Staging)> Attachments)>();
            foreach (PendingTextureCapture pending in _pendingCaptures)
                stagingTextures.Add((pending.Id, pending.FramebufferName, pending.Version, pending.Attachments));
            _pendingCaptures.Clear();

            // Draw-call buffers already have their staging copy from HandleCapture; just read them
            // back here, same as the per-pass texture captures above.
            foreach (PendingBufferCapture pending in _pendingBufferCaptures)
            {
                byte[] data = ReadBufferBytes(device, pending.Staging, pending.SizeInBytes);
                var meta = new SnapshotBufferMeta(ClassifyKind(pending.Src.Usage), pending.SizeInBytes, 0, Array.Empty<BufferField>());
                AddVersion(pending.Id, pending.Name, SnapshotResourceKind.Buffer, new SnapshotResourceVersion(pending.Version, Array.Empty<SnapshotSubTexture>(), data, meta));
                pending.Staging.Dispose();
            }
            _pendingBufferCaptures.Clear();

            if (uncapturedTextures.Count > 0 || _buffers.Count > 0)
            {
                TransferCommandBuffer xfer = device.ResourceFactory.CreateTransferCommandBuffer();
                xfer.Begin();

                foreach (KeyValuePair<uint, RenderTexture> entry in uncapturedTextures)
                {
                    RenderTexture rt = entry.Value;
                    var attachments = new List<(string Name, Texture Src, Texture Staging)>();

                    foreach (Texture color in rt.ColorTextures)
                        attachments.Add((color.Name, color, CopyToStaging(device, xfer, color)));
                    if (rt.DepthTexture != null)
                        attachments.Add((rt.DepthTexture.Name, rt.DepthTexture, CopyToStaging(device, xfer, rt.DepthTexture)));

                    stagingTextures.Add((entry.Key, rt.Framebuffer.Name, 0, attachments));
                }

                var stagingBuffers = new List<(uint Id, string Name, DeviceBuffer Src, uint Version, DeviceBuffer Staging)>();
                foreach (KeyValuePair<uint, DeviceBuffer> entry in _buffers)
                {
                    DeviceBuffer src = entry.Value;
                    DeviceBuffer staging = device.ResourceFactory.CreateBuffer(new BufferDescription(src.SizeInBytes, BufferUsage.Staging));
                    xfer.CopyBuffer(src, 0, staging, 0, src.SizeInBytes);
                    stagingBuffers.Add((entry.Key, src.Name, src, src.ContentVersion, staging));
                }

                xfer.End();
                device.SubmitAndWait(xfer);
                xfer.Dispose();

                foreach ((uint id, string name, DeviceBuffer src, uint version, DeviceBuffer staging) in stagingBuffers)
                {
                    byte[] data = ReadBufferBytes(device, staging, src.SizeInBytes);
                    var meta = new SnapshotBufferMeta(ClassifyKind(src.Usage), src.SizeInBytes, 0, Array.Empty<BufferField>());
                    AddVersion(id, name, SnapshotResourceKind.Buffer, new SnapshotResourceVersion(version, Array.Empty<SnapshotSubTexture>(), data, meta));
                    staging.Dispose();
                }
            }

            foreach ((uint id, string textureName, uint version, List<(string Name, Texture Src, Texture Staging)> attachments) in stagingTextures)
            {
                var subtextures = new List<SnapshotSubTexture>(attachments.Count);
                foreach ((string name, Texture src, Texture staging) in attachments)
                {
                    byte[] pixels = ReadTextureMip0(device, staging);
                    subtextures.Add(new SnapshotSubTexture(name, src.Format, src.Width, src.Height, src.Depth, src.MipLevels, pixels));
                    staging.Dispose();
                }
                AddVersion(id, textureName, SnapshotResourceKind.Texture, new SnapshotResourceVersion(version, subtextures, Array.Empty<byte>(), null));
            }
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

    private static Texture CopyToStaging(GraphicsDevice device, TransferCommandBuffer xfer, Texture src)
    {
        TextureDescription desc = TextureDescription.Texture2D(
            src.Width, src.Height, src.MipLevels, src.ArrayLayers, src.Format, TextureUsage.Staging);
        Texture staging = device.ResourceFactory.CreateTexture(desc);
        xfer.CopyTexture(src, staging, 0, 0);
        return staging;
    }

    // Reads back mip 0 / array layer 0 / depth slice range only. Multi-mip/array pixel data is not
    // captured; MipLevels is still reported as metadata on SnapshotSubTexture.
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
