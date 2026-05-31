using System;
using System.Collections.Generic;
using System.Diagnostics;

using Prowl.Vector;
using Prowl.Veldrid.OpenGL.NoAllocEntryList;

namespace Prowl.Veldrid.OpenGL;

internal partial class OpenGLCommandBuffer : CommandBuffer
{
    private readonly OpenGLGraphicsDevice _gd;
    private OpenGLCommandEntryList _currentCommands;
    private bool _disposed;

    internal OpenGLCommandEntryList CurrentCommands => _currentCommands;
    internal OpenGLGraphicsDevice Device => _gd;

    private readonly object _lock = new();
    private readonly List<OpenGLCommandEntryList> _availableLists = [];
    private readonly List<OpenGLCommandEntryList> _submittedLists = [];

    public override string Name { get; set; }

    public override bool IsDisposed => _disposed;

    public OpenGLCommandBuffer(OpenGLGraphicsDevice gd, ref CommandBufferDescription description)
        : base(gd.Features, gd.UniformBufferMinOffsetAlignment, gd.StructuredBufferMinOffsetAlignment)
    {
        _gd = gd;
    }

    public override void Begin()
    {
        ClearCachedState();
        if (_currentCommands != null)
        {
            _currentCommands.Dispose();
        }

        _currentCommands = GetFreeCommandBuffer();
        _currentCommands.Begin();
        _currentCommands.ClearProperties();
        HasEnded = false;
    }

    private OpenGLCommandEntryList GetFreeCommandBuffer()
    {
        lock (_lock)
        {
            if (_availableLists.Count > 0)
            {
                OpenGLCommandEntryList ret = _availableLists[_availableLists.Count - 1];
                _availableLists.RemoveAt(_availableLists.Count - 1);
                return ret;
            }
            else
            {
                return new OpenGLNoAllocCommandEntryList(this);
            }
        }
    }

    private protected override void ClearColorTargetCore(uint index, Color clearColor)
    {
        _currentCommands.ClearColorTarget(index, clearColor);
    }

    private protected override void ClearDepthStencilCore(float depth, byte stencil)
    {
        _currentCommands.ClearDepthTarget(depth, stencil);
    }

    private protected override void DrawCore(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart)
    {
        _currentCommands.Draw(vertexCount, instanceCount, vertexStart, instanceStart);
    }

    private protected override void DrawIndexedCore(uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
    {
        _currentCommands.DrawIndexed(instanceCount, indexStart, vertexOffset, instanceStart);
    }

    private protected override void DrawIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        _currentCommands.DrawIndirect(indirectBuffer, offset, drawCount, stride);
    }

    private protected override void DrawIndexedIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        _currentCommands.DrawIndexedIndirect(indirectBuffer, offset, drawCount, stride);
    }

    public override void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        _currentCommands.Dispatch(groupCountX, groupCountY, groupCountZ);
    }

    private protected override void DispatchIndirectCore(DeviceBuffer indirectBuffer, uint offset)
    {
        _currentCommands.DispatchIndirect(indirectBuffer, offset);
    }

    protected override void ResolveTextureCore(Texture source, Texture destination)
    {
        _currentCommands.ResolveTexture(source, destination);
    }

    public override void End()
    {
        _currentCommands.End();
        HasEnded = true;
    }

    private protected override void SetFramebufferCore(Framebuffer fb)
    {
        _currentCommands.SetFramebuffer(fb);
    }

    private protected override void SetVertexSourceCore(IVertexSource source)
    {
        _currentCommands.SetVertexSource(source);
    }

    private protected override void SetShaderCore(GraphicsProgram program)
    {
        _currentCommands.SetShader(program);
    }

    private protected override void SetComputeShaderCore(ComputeProgram program)
    {
        _currentCommands.SetComputeShader(program);
    }

    /// <inheritdoc/>
    private protected override void SetPropertiesCore(PropertySet ps)
    {
        _currentCommands.SetProperties(ps);
    }

    /// <inheritdoc/>
    private protected override void ClearPropertiesCore()
    {
        _currentCommands.ClearProperties();
    }

    public override void SetScissorRect(uint index, uint x, uint y, uint width, uint height)
    {
        _currentCommands.SetScissorRect(index, x, y, width, height);
    }

    public override void SetViewport(uint index, ref Viewport viewport)
    {
        _currentCommands.SetViewport(index, ref viewport);
    }

    internal void Reset()
    {
        _currentCommands.Reset();
    }

    private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
    {
        _currentCommands.UpdateBuffer(buffer, bufferOffsetInBytes, source, sizeInBytes);
    }

    private protected override void CopyBufferCore(
        DeviceBuffer source,
        uint sourceOffset,
        DeviceBuffer destination,
        uint destinationOffset,
        uint sizeInBytes)
    {
        _currentCommands.CopyBuffer(source, sourceOffset, destination, destinationOffset, sizeInBytes);
        _gd.RecordBufferOp(BufferOpBin.Copy, sizeInBytes);
    }

    private protected override void CopyTextureCore(
        Texture source,
        uint srcX, uint srcY, uint srcZ,
        uint srcMipLevel,
        uint srcBaseArrayLayer,
        Texture destination,
        uint dstX, uint dstY, uint dstZ,
        uint dstMipLevel,
        uint dstBaseArrayLayer,
        uint width, uint height, uint depth,
        uint layerCount)
    {
        _currentCommands.CopyTexture(
            source,
            srcX, srcY, srcZ,
            srcMipLevel,
            srcBaseArrayLayer,
            destination,
            dstX, dstY, dstZ,
            dstMipLevel,
            dstBaseArrayLayer,
            width, height, depth,
            layerCount);
    }

    private protected override void GenerateMipmapsCore(Texture texture)
    {
        _currentCommands.GenerateMipmaps(texture);
    }

    public void OnSubmitted(OpenGLCommandEntryList entryList)
    {
        _currentCommands = null;
        lock (_lock)
        {
            Debug.Assert(!_submittedLists.Contains(entryList));
            _submittedLists.Add(entryList);

            Debug.Assert(!_availableLists.Contains(entryList));
        }
    }

    public void OnCompleted(OpenGLCommandEntryList entryList)
    {
        lock (_lock)
        {
            entryList.Reset();

            Debug.Assert(!_availableLists.Contains(entryList));
            _availableLists.Add(entryList);

            Debug.Assert(_submittedLists.Contains(entryList));
            _submittedLists.Remove(entryList);
        }
    }

    private protected override void PushDebugGroupCore(string name)
    {
        _currentCommands.PushDebugGroup(name);
    }

    private protected override void PopDebugGroupCore()
    {
        _currentCommands.PopDebugGroup();
    }

    private protected override void InsertDebugMarkerCore(string name)
    {
        _currentCommands.InsertDebugMarker(name);
    }

    [System.Diagnostics.Conditional("VALIDATE_USAGE")]
    internal void Bridge_CheckVertexBindingUsage(in VertexBinding binding, uint slot)
        => CheckVertexBindingUsage(in binding, slot);

    [System.Diagnostics.Conditional("VALIDATE_USAGE")]
    internal void Bridge_CheckIndexBufferUsage(DeviceBuffer buffer)
        => CheckIndexBufferUsage(buffer);

    public override void Dispose()
    {
        _gd.EnqueueDisposal(this);
    }

    public void DestroyResources()
    {
        lock (_lock)
        {
            _currentCommands?.Dispose();
            foreach (OpenGLCommandEntryList list in _availableLists)
            {
                list.Dispose();
            }
            foreach (OpenGLCommandEntryList list in _submittedLists)
            {
                list.Dispose();
            }

            _disposed = true;
        }
    }
}
