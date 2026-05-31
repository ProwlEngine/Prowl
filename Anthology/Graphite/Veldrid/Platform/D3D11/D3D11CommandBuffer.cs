using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

using Prowl.Vector;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Silk.NET.Maths;

namespace Prowl.Veldrid.D3D11;

internal unsafe partial class D3D11CommandBuffer : CommandBuffer
{
    private readonly D3D11GraphicsDevice _gd;
    private ComPtr<ID3D11DeviceContext> _context;
    private ComPtr<ID3D11DeviceContext1> _context1;
    private ComPtr<ID3DUserDefinedAnnotation> _uda;
    private bool _begun;
    private bool _disposed;
    private ComPtr<ID3D11CommandList> _commandList;

    private D3D11Viewport[] _viewports = [];
    private RawRect[] _scissors = [];
    private bool _viewportsChanged;
    private bool _scissorRectsChanged;

    private int[] _vertexStrides = new int[1];

    // Cached pipeline State
    private DeviceBuffer _currentIndexBuffer;
    private Format _currentIndexFormat;
    private uint _currentIndexCount;
    private ID3D11BlendState* _blendState;
    private float[] _blendFactor = new float[4];
    private ID3D11DepthStencilState* _depthStencilState;
    private uint _stencilReference;
    private ID3D11RasterizerState* _rasterizerState;
    private D3DPrimitiveTopology _primitiveTopology;
    private ID3D11InputLayout* _inputLayout;
    private ID3D11VertexShader* _vertexShader;
    private ID3D11GeometryShader* _geometryShader;
    private ID3D11HullShader* _hullShader;
    private ID3D11DomainShader* _domainShader;
    private ID3D11PixelShader* _pixelShader;

    private D3D11GraphicsProgram _currentShaderProgram;
    private D3D11ComputeProgram _currentComputeProgram;
    private string _name;
    private ID3D11Buffer*[] _cbOut = new ID3D11Buffer*[1];
    private int[] _firstConstRef = new int[1];
    private int[] _numConstsRef = new int[1];

    // Cached resources
    private const int MaxCachedUniformBuffers = 15;
    private readonly DeviceBufferRange[] _vertexBoundUniformBuffers = new DeviceBufferRange[MaxCachedUniformBuffers];
    private readonly DeviceBufferRange[] _fragmentBoundUniformBuffers = new DeviceBufferRange[MaxCachedUniformBuffers];
    private const int MaxCachedTextureViews = 16;
    private readonly D3D11TextureView[] _vertexBoundTextureViews = new D3D11TextureView[MaxCachedTextureViews];
    private readonly D3D11TextureView[] _fragmentBoundTextureViews = new D3D11TextureView[MaxCachedTextureViews];
    private const int MaxCachedSamplers = 4;
    private readonly D3D11Sampler[] _vertexBoundSamplers = new D3D11Sampler[MaxCachedSamplers];
    private readonly D3D11Sampler[] _fragmentBoundSamplers = new D3D11Sampler[MaxCachedSamplers];

    private readonly Dictionary<Texture, List<BoundTextureInfo>> _boundSRVs = [];
    private readonly Dictionary<Texture, List<BoundTextureInfo>> _boundUAVs = [];
    private readonly List<List<BoundTextureInfo>> _boundTextureInfoPool = new(20);

    private const int MaxUAVs = 8;
    private readonly List<(DeviceBuffer, int)> _boundComputeUAVBuffers = new(MaxUAVs);
    private readonly List<(DeviceBuffer, int)> _boundOMUAVBuffers = new(MaxUAVs);

    private readonly List<D3D11Buffer> _availableStagingBuffers = [];
    private readonly List<D3D11Buffer> _submittedStagingBuffers = [];

    private readonly List<D3D11Swapchain> _referencedSwapchains = [];

    /// <summary>
    /// Helper to get the raw context pointer for calling D3D11 methods.
    /// </summary>
    private ID3D11DeviceContext* Ctx
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _context;
    }

    public D3D11CommandBuffer(D3D11GraphicsDevice gd, ref CommandBufferDescription description)
        : base(gd.Features, gd.UniformBufferMinOffsetAlignment, gd.StructuredBufferMinOffsetAlignment)
    {
        _gd = gd;

        ID3D11DeviceContext* pDeferredContext;
        SilkMarshal.ThrowHResult(gd.Device->CreateDeferredContext(0, &pDeferredContext));
        _context = default;
        _context.Handle = pDeferredContext;

        ID3D11DeviceContext1* pContext1;
        Guid ctx1Guid = ID3D11DeviceContext1.Guid;

        if (((IUnknown*)pDeferredContext)->QueryInterface(&ctx1Guid, (void**)&pContext1) >= 0)
        {
            _context1 = default;
            _context1.Handle = pContext1;
        }

        ID3DUserDefinedAnnotation* pUda;
        Guid udaGuid = ID3DUserDefinedAnnotation.Guid;

        if (((IUnknown*)pDeferredContext)->QueryInterface(&udaGuid, (void**)&pUda) >= 0)
        {
            _uda = default;
            _uda.Handle = pUda;
        }
    }

    public ID3D11CommandList* DeviceCommandList => _commandList;

    internal ID3D11DeviceContext* DeviceContext => _context;

    private D3D11Framebuffer D3D11Framebuffer => Util.AssertSubtype<Framebuffer, D3D11Framebuffer>(_framebuffer);

    public override bool IsDisposed => _disposed;

    public override void Begin()
    {
        if (_commandList.Handle != null)
        {
            _commandList.Dispose();
            _commandList = default;
        }
        ClearState();
        _begun = true;
        HasEnded = false;
    }

    private void ClearState()
    {
        ClearCachedState();
        Ctx->ClearState();
        ResetManagedState();
    }

    private void ResetManagedState()
    {
        Util.ClearArray(_vertexStrides);

        _framebuffer = null;

        Util.ClearArray(_viewports);
        Util.ClearArray(_scissors);
        _viewportsChanged = false;
        _scissorRectsChanged = false;

        _currentIndexBuffer = null;
        _currentIndexFormat = Format.FormatUnknown;
        _currentIndexCount = 0;
        _currentShaderProgram = null;
        _currentComputeProgram = null;
        _blendState = null;
        _blendFactor[0] = 0; _blendFactor[1] = 0; _blendFactor[2] = 0; _blendFactor[3] = 0;
        _depthStencilState = null;
        _rasterizerState = null;
        _primitiveTopology = D3DPrimitiveTopology.D3DPrimitiveTopologyUndefined;
        _inputLayout = null;
        _vertexShader = null;
        _geometryShader = null;
        _hullShader = null;
        _domainShader = null;
        _pixelShader = null;

        Util.ClearArray(_vertexBoundUniformBuffers);
        Util.ClearArray(_vertexBoundTextureViews);
        Util.ClearArray(_vertexBoundSamplers);

        Util.ClearArray(_fragmentBoundUniformBuffers);
        Util.ClearArray(_fragmentBoundTextureViews);
        Util.ClearArray(_fragmentBoundSamplers);

        foreach (KeyValuePair<Texture, List<BoundTextureInfo>> kvp in _boundSRVs)
        {
            List<BoundTextureInfo> list = kvp.Value;
            list.Clear();
            PoolBoundTextureList(list);
        }
        _boundSRVs.Clear();

        foreach (KeyValuePair<Texture, List<BoundTextureInfo>> kvp in _boundUAVs)
        {
            List<BoundTextureInfo> list = kvp.Value;
            list.Clear();
            PoolBoundTextureList(list);
        }
        _boundUAVs.Clear();
    }

    public override void End()
    {
        if (_commandList.Handle != null)
        {
            throw new RenderException("Invalid use of End().");
        }

        ID3D11CommandList* pCmdList;
        SilkMarshal.ThrowHResult(Ctx->FinishCommandList(0, &pCmdList));
        _commandList = default;
        _commandList.Handle = pCmdList;
        if (_name != null)
            D3D11Util.SetDebugName((ID3D11DeviceChild*)_commandList.Handle, _name);
        ResetManagedState();
        _begun = false;
        HasEnded = true;
    }

    public void Reset()
    {
        if (_commandList.Handle != null)
        {
            _commandList.Dispose();
            _commandList = default;
        }
        else if (_begun)
        {
            Ctx->ClearState();
            ID3D11CommandList* pCmdList;
            Ctx->FinishCommandList(0, &pCmdList);
            pCmdList->Release();
        }

        ResetManagedState();
        _begun = false;
    }

    private protected override void SetVertexSourceCore(IVertexSource source)
    {
        _currentIndexBuffer = null;
    }

    private void ResolveAndBindIndexBuffer()
    {
        bool has = _currentVertexSource.TryGetIndexBuffer(out DeviceBuffer ib, out IndexFormat fmt, out uint count);
        Debug.Assert(has, "Validation must have already trapped a missing index buffer on indexed-draw paths.");
        CheckIndexBufferUsage(ib);

        Format dxgiFmt = D3D11Formats.ToDxgiFormat(fmt);

        if (!ReferenceEquals(_currentIndexBuffer, ib)
            || _currentIndexFormat != dxgiFmt
            || _currentIndexCount != count)
        {
            D3D11Buffer d3d11Buffer = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(ib);
            UnbindUAVBuffer(ib);
            Ctx->IASetIndexBuffer(d3d11Buffer.Buffer, dxgiFmt, count);
            _currentIndexBuffer = ib;
            _currentIndexFormat = dxgiFmt;
            _currentIndexCount = count;
        }
    }

    private protected override void SetShaderCore(GraphicsProgram program)
    {
        D3D11GraphicsProgram sp = Util.AssertSubtype<GraphicsProgram, D3D11GraphicsProgram>(program);
        if (_currentShaderProgram == sp) return;

        bool msaa = _framebuffer != null
            && _framebuffer.OutputDescription.SampleCount != TextureSampleCount.Count1;
        sp.EnsureCacheResolved(msaa);

        _currentShaderProgram = sp;

        ID3D11BlendState* blendState = sp.BlendStateHandle;
        BlendStateDescription bsDesc = sp.BlendState;
        float[] blendFactor = [bsDesc.BlendFactor.R, bsDesc.BlendFactor.G, bsDesc.BlendFactor.B, bsDesc.BlendFactor.A];
        if (_blendState != blendState || !BlendFactorEquals(_blendFactor, blendFactor))
        {
            _blendState = blendState;
            Array.Copy(blendFactor, _blendFactor, 4);
            fixed (float* pBf = _blendFactor)
            {
                Ctx->OMSetBlendState(blendState, pBf, 0xFFFFFFFF);
            }
        }

        ID3D11DepthStencilState* depthStencilState = sp.DepthStencilStateHandle;
        uint stencilReference = sp.DepthStencilState.StencilReference;
        if (_depthStencilState != depthStencilState || _stencilReference != stencilReference)
        {
            _depthStencilState = depthStencilState;
            _stencilReference = stencilReference;
            Ctx->OMSetDepthStencilState(depthStencilState, stencilReference);
        }

        ID3D11RasterizerState* rasterizerState = sp.RasterizerStateHandle;
        if (_rasterizerState != rasterizerState)
        {
            _rasterizerState = rasterizerState;
            Ctx->RSSetState(rasterizerState);
        }

        ID3D11InputLayout* inputLayout = sp.InputLayout;
        if (_inputLayout != inputLayout)
        {
            _inputLayout = inputLayout;
            Ctx->IASetInputLayout(inputLayout);
        }

        if (_vertexShader != sp.VertexShader) { _vertexShader = sp.VertexShader; Ctx->VSSetShader(sp.VertexShader, null, 0); }
        if (_geometryShader != sp.GeometryShader) { _geometryShader = sp.GeometryShader; Ctx->GSSetShader(sp.GeometryShader, null, 0); }
        if (_hullShader != sp.HullShader) { _hullShader = sp.HullShader; Ctx->HSSetShader(sp.HullShader, null, 0); }
        if (_domainShader != sp.DomainShader) { _domainShader = sp.DomainShader; Ctx->DSSetShader(sp.DomainShader, null, 0); }
        if (_pixelShader != sp.PixelShader) { _pixelShader = sp.PixelShader; Ctx->PSSetShader(sp.PixelShader, null, 0); }

        int[] strides = sp.VertexStridesInts;
        if (!Util.ArrayEqualsEquatable(_vertexStrides, strides))
        {
            if (strides != null)
            {
                Util.EnsureArrayMinimumSize(ref _vertexStrides, (uint)strides.Length);
                strides.CopyTo(_vertexStrides, 0);
            }
        }
        Util.EnsureArrayMinimumSize(ref _vertexStrides, 1);
    }

    private protected override void SetComputeShaderCore(ComputeProgram program)
    {
        D3D11ComputeProgram cp = Util.AssertSubtype<ComputeProgram, D3D11ComputeProgram>(program);
        if (_currentComputeProgram == cp) return;
        _currentComputeProgram = cp;
        Ctx->CSSetShader(cp.ComputeShader, null, 0);
    }

    private static bool BlendFactorEquals(float[] a, float[] b)
    {
        return a[0] == b[0] && a[1] == b[1] && a[2] == b[2] && a[3] == b[3];
    }

    /// <inheritdoc/>
    private protected override void SetPropertiesCore(PropertySet ps) { }

    /// <inheritdoc/>
    private protected override void ClearPropertiesCore() { }

    private void UnbindSRVTexture(Texture target)
    {
        if (_boundSRVs.TryGetValue(target, out List<BoundTextureInfo> btis))
        {
            foreach (BoundTextureInfo bti in btis)
                BindTextureView(null, bti.Slot, bti.Stages);

            bool result = _boundSRVs.Remove(target);
            Debug.Assert(result);

            btis.Clear();
            PoolBoundTextureList(btis);
        }
    }

    private void PoolBoundTextureList(List<BoundTextureInfo> btis)
    {
        _boundTextureInfoPool.Add(btis);
    }

    private void UnbindUAVTexture(Texture target)
    {
        if (_boundUAVs.TryGetValue(target, out List<BoundTextureInfo> btis))
        {
            foreach (BoundTextureInfo bti in btis)
                BindUnorderedAccessView(null, null, null, bti.Slot, bti.Stages);

            bool result = _boundUAVs.Remove(target);
            Debug.Assert(result);

            btis.Clear();
            PoolBoundTextureList(btis);
        }
    }

    private protected override void DrawCore(uint vertexCount, uint instanceCount, uint vertexStart, uint instanceStart)
    {
        PreDrawCommand();

        if (instanceCount == 1 && instanceStart == 0)
        {
            Ctx->Draw(vertexCount, vertexStart);
        }
        else
        {
            Ctx->DrawInstanced(vertexCount, instanceCount, vertexStart, instanceStart);
        }
    }

    private protected override void DrawIndexedCore(uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
    {
        PreDrawCommand();
        ResolveAndBindIndexBuffer();

        if (instanceCount == 1 && instanceStart == 0)
        {
            Ctx->DrawIndexed(_currentIndexCount, indexStart, vertexOffset);
        }
        else
        {
            Ctx->DrawIndexedInstanced(_currentIndexCount, instanceCount, indexStart, vertexOffset, instanceStart);
        }
    }

    private protected override void DrawIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        PreDrawCommand();

        D3D11Buffer d3d11Buffer = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(indirectBuffer);
        uint currentOffset = offset;
        for (uint i = 0; i < drawCount; i++)
        {
            Ctx->DrawInstancedIndirect(d3d11Buffer.Buffer, currentOffset);
            currentOffset += stride;
        }
    }

    private protected override void DrawIndexedIndirectCore(DeviceBuffer indirectBuffer, uint offset, uint drawCount, uint stride)
    {
        PreDrawCommand();
        ResolveAndBindIndexBuffer();

        D3D11Buffer d3d11Buffer = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(indirectBuffer);
        uint currentOffset = offset;
        for (uint i = 0; i < drawCount; i++)
        {
            Ctx->DrawIndexedInstancedIndirect(d3d11Buffer.Buffer, currentOffset);
            currentOffset += stride;
        }
    }

    private void PreDrawCommand()
    {
        FlushViewports();
        FlushScissorRects();

        D3DPrimitiveTopology topology = D3D11Formats.VdToD3D11PrimitiveTopology(_currentVertexSource.Topology);
        if (_primitiveTopology != topology)
        {
            _primitiveTopology = topology;
            Ctx->IASetPrimitiveTopology(topology);
        }

        FlushVertexBindings();
        BindProperties(isCompute: false);
    }

    public override void Dispatch(uint groupCountX, uint groupCountY, uint groupCountZ)
    {
        PreDispatchCommand();

        Ctx->Dispatch(groupCountX, groupCountY, groupCountZ);
    }

    private protected override void DispatchIndirectCore(DeviceBuffer indirectBuffer, uint offset)
    {
        PreDispatchCommand();
        D3D11Buffer d3d11Buffer = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(indirectBuffer);
        Ctx->DispatchIndirect(d3d11Buffer.Buffer, offset);
    }

    private void PreDispatchCommand()
    {
        BindProperties(isCompute: true);
    }

    private void BindProperties(bool isCompute)
    {
        ResourceLayoutDescription[] layouts = isCompute
            ? _currentComputeProgram.ResourceLayoutsArray
            : _currentShaderProgram.ResourceLayoutsArray;

        for (int setIdx = 0; setIdx < layouts.Length; setIdx++)
        {
            ResourceLayoutDescription layout = layouts[setIdx];
            foreach (ResourceLayoutElementDescription elem in layout.Elements)
                BindPropertySlot(in elem, in layout, isCompute, (uint)setIdx);
        }
    }

    private void BindPropertySlot(
        in ResourceLayoutElementDescription elem,
        in ResourceLayoutDescription layout,
        bool isCompute, uint setIdx)
    {
        int bindingIndex = elem.BindingIndex;
        ShaderStages stages = isCompute ? ShaderStages.Compute : elem.Stages;

        switch (elem.Kind)
        {
            case ResourceKind.UniformBuffer:
                {
                    DeviceBufferRange range = ResolveUboD3D11(in elem, isCompute, setIdx);
                    BindUniformBuffer(range, bindingIndex, stages);
                    break;
                }
            case ResourceKind.StructuredBufferReadOnly:
                {
                    DeviceBufferRange range = ResolveStorageBufferD3D11(in elem, isCompute, setIdx);
                    if (range.Buffer != null)
                        BindStorageBufferView(range, bindingIndex, stages);
                    else
                        BindNullStorageBuffer(bindingIndex, stages, false);
                    break;
                }
            case ResourceKind.StructuredBufferReadWrite:
                {
                    DeviceBufferRange range = ResolveStorageBufferD3D11(in elem, isCompute, setIdx);
                    if (range.Buffer != null)
                    {
                        ID3D11UnorderedAccessView* uav = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(range.Buffer)
                            .GetUnorderedAccessView(range.Offset, range.SizeInBytes);

                        BindUnorderedAccessView(null, range.Buffer, uav, bindingIndex, stages);
                    }
                    else
                        BindNullStorageBuffer(bindingIndex, stages, true);
                    break;
                }
            case ResourceKind.TextureReadOnly:
                {
                    D3D11TextureView texView = ResolveTextureViewD3D11(in elem, isCompute, setIdx, false);
                    UnbindUAVTexture(texView.Target);
                    BindTextureView(texView, bindingIndex, stages);
                    break;
                }
            case ResourceKind.TextureReadWrite:
                {
                    D3D11TextureView rwTexView = ResolveTextureViewD3D11(in elem, isCompute, setIdx, true);
                    UnbindSRVTexture(rwTexView.Target);
                    BindUnorderedAccessView(rwTexView.Target, null, rwTexView.UnorderedAccessView, bindingIndex, stages);
                    break;
                }
            case ResourceKind.Sampler:
                {
                    D3D11Sampler sampler = ResolveD3D11Sampler(in elem, in layout);
                    BindSampler(sampler, bindingIndex, stages);
                    break;
                }
        }
    }

    private DeviceBufferRange ResolveUboD3D11(
        in ResourceLayoutElementDescription elem, bool isCompute, uint setIdx)
    {
        if (elem.UniformFields != null && elem.UniformFields.Length > 0)
        {
            ShaderProgram key = isCompute ? _currentComputeProgram : _currentShaderProgram;

            DeviceBufferRange r = GetOrBuildImplicitUboD3D11(key, setIdx, elem.BindingIndex, elem.UniformFields);

            return new DeviceBufferRange(
                Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(r.Buffer), r.Offset, r.SizeInBytes);
        }

        if (_mergedTable.Entries.TryGetValue(elem.Name, out PropertyEntry? uboEntry) && uboEntry.Kind == PropertyEntryKind.Buffer)
        {
            return uboEntry.Buffer!.Value;
        }

        _gd.OnMissingProperty?.Invoke(
            !isCompute ? _currentShaderProgram : null,
            isCompute ? _currentComputeProgram : null,
            elem.Name, ResourceKind.UniformBuffer, setIdx, elem.BindingIndex);

        return new DeviceBufferRange(Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(_gd.NullUniform), 0, 16);
    }

    private DeviceBufferRange GetOrBuildImplicitUboD3D11(
        ShaderProgram programKey, uint setIdx, int bindingIndex,
        UniformBlockField[] fields)
    {
        uint uniformVersion = _mergedTable.UniformVersion;

        UboCacheKey key = new UboCacheKey(programKey, setIdx, bindingIndex, uniformVersion);
        if (_frameUboCache.TryGetValue(key, out DeviceBufferRange cached)) return cached;

        uint totalSize = 0;
        foreach (UniformBlockField field in fields)
            totalSize = Math.Max(totalSize, field.Offset + field.Size);
        if (totalSize == 0) totalSize = 16;

        DeviceBufferRange range = _gd.CurrentFrame.AllocateTransient(totalSize);

        byte[] uploadBuf = ArrayPool<byte>.Shared.Rent((int)totalSize);
        try
        {
            Array.Clear(uploadBuf, 0, (int)totalSize);
            foreach (UniformBlockField field in fields)
            {
                if (_mergedTable.Entries.TryGetValue(field.Name, out PropertyEntry uEntry)
                    && uEntry.Kind == PropertyEntryKind.Uniform)
                {
                    MemoryMarshal.CreateReadOnlySpan(
                        ref Unsafe.As<PropertyEntry.UniformPayload, byte>(ref uEntry.Uniform),
                        (int)field.Size)
                        .CopyTo(uploadBuf.AsSpan((int)field.Offset, (int)field.Size));
                }
            }
            fixed (byte* ptr = uploadBuf)
                ((D3D11Frame)_gd.CurrentFrame).WriteTransient(range.Offset, ptr, totalSize);
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(uploadBuf);
        }

        _frameUboCache[key] = range;

        return range;
    }

    private DeviceBufferRange ResolveStorageBufferD3D11(
        in ResourceLayoutElementDescription elem, bool isCompute, uint setIdx)
    {
        if (_mergedTable.Entries.TryGetValue(elem.Name, out PropertyEntry? entry)
            && entry.Kind == PropertyEntryKind.Buffer)
        {
            return entry.Buffer!.Value;
        }

        _gd.OnMissingProperty?.Invoke(
            !isCompute ? _currentShaderProgram : null,
            isCompute ? _currentComputeProgram : null,
            elem.Name, elem.Kind, setIdx, elem.BindingIndex);

        return default;
    }

    private void BindNullStorageBuffer(int slot, ShaderStages stages, bool isUav)
    {
        if (isUav)
        {
            ID3D11UnorderedAccessView* nullUav = null;
            uint initialCount = unchecked((uint)-1);
            if ((stages & ShaderStages.Compute) == ShaderStages.Compute)
                Ctx->CSSetUnorderedAccessViews((uint)slot, 1, &nullUav, &initialCount);
        }
        else
        {
            ID3D11ShaderResourceView* nullSrv = null;
            if ((stages & ShaderStages.Vertex) == ShaderStages.Vertex)
                Ctx->VSSetShaderResources((uint)slot, 1, &nullSrv);
            if ((stages & ShaderStages.Fragment) == ShaderStages.Fragment)
                Ctx->PSSetShaderResources((uint)slot, 1, &nullSrv);
            if ((stages & ShaderStages.Compute) == ShaderStages.Compute)
                Ctx->CSSetShaderResources((uint)slot, 1, &nullSrv);
        }
    }

    private D3D11TextureView ResolveTextureViewD3D11(
        in ResourceLayoutElementDescription elem, bool isCompute, uint setIdx, bool isReadWrite)
    {
        if (_mergedTable.Entries.TryGetValue(elem.Name, out PropertyEntry texEntry)
            && texEntry.Kind == PropertyEntryKind.Texture)
        {
            if (texEntry.TextureView != null)
                return Util.AssertSubtype<TextureView, D3D11TextureView>(texEntry.TextureView);
            if (texEntry.Texture != null)
                return Util.AssertSubtype<TextureView, D3D11TextureView>(texEntry.Texture.GetFullTextureView(_gd));
        }

        _gd.OnMissingProperty?.Invoke(
            !isCompute ? _currentShaderProgram : null,
            isCompute ? _currentComputeProgram : null,
            elem.Name, elem.Kind, setIdx, elem.BindingIndex);
        Texture fallbackTex = isReadWrite ? _gd.NullTextureRW2D : _gd.NullTexture2D;
        return Util.AssertSubtype<TextureView, D3D11TextureView>(fallbackTex.GetFullTextureView(_gd));
    }

    private D3D11Sampler ResolveD3D11Sampler(
        in ResourceLayoutElementDescription elem, in ResourceLayoutDescription layout)
    {
        if (_mergedTable.Entries.TryGetValue(elem.Name, out PropertyEntry samplerEntry)
            && samplerEntry.Kind == PropertyEntryKind.Sampler
            && samplerEntry.Sampler != null)
        {
            return Util.AssertSubtype<Sampler, D3D11Sampler>(samplerEntry.Sampler);
        }

        foreach (ResourceLayoutElementDescription other in layout.Elements)
        {
            if (other.Name == elem.Name
                && (other.Kind == ResourceKind.TextureReadOnly || other.Kind == ResourceKind.TextureReadWrite))
            {
                if (_mergedTable.Entries.TryGetValue(elem.Name, out PropertyEntry texEntry)
                    && texEntry.Kind == PropertyEntryKind.Texture
                    && texEntry.Sampler != null)
                {
                    return Util.AssertSubtype<Sampler, D3D11Sampler>(texEntry.Sampler);
                }
                break;
            }
        }

        return Util.AssertSubtype<Sampler, D3D11Sampler>(_gd.LinearSampler);
    }

    protected override void ResolveTextureCore(Texture source, Texture destination)
    {
        D3D11Texture d3d11Source = Util.AssertSubtype<Texture, D3D11Texture>(source);
        D3D11Texture d3d11Destination = Util.AssertSubtype<Texture, D3D11Texture>(destination);
        Ctx->ResolveSubresource(
            d3d11Destination.DeviceTexture,
            0,
            d3d11Source.DeviceTexture,
            0,
            d3d11Destination.DxgiFormat);
    }

    private void FlushViewports()
    {
        if (_viewportsChanged)
        {
            _viewportsChanged = false;
            fixed (D3D11Viewport* pViewports = _viewports)
            {
                Ctx->RSSetViewports((uint)_viewports.Length, (Silk.NET.Direct3D11.Viewport*)pViewports);
            }
        }
    }

    private void FlushScissorRects()
    {
        if (_scissorRectsChanged)
        {
            _scissorRectsChanged = false;
            if (_scissors.Length > 0)
            {
                // Because this array is resized using Util.EnsureMinimumArraySize, this might set more scissor rectangles
                // than are actually needed, but this is okay -- extras are essentially ignored and should be harmless.
                fixed (RawRect* pRects = _scissors)
                {
                    Ctx->RSSetScissorRects((uint)_scissors.Length, (Box2D<int>*)pRects);
                }
            }
        }
    }

    private void FlushVertexBindings()
    {
        System.Collections.Generic.IReadOnlyList<VertexLayoutDescription> layouts = _currentShaderProgram.VertexLayouts;
        int count = layouts.Count;
        if (count == 0) return;

        ID3D11Buffer** ppBuffers = stackalloc ID3D11Buffer*[count];
        uint* pStrides = stackalloc uint[count];
        uint* pOffsets = stackalloc uint[count];

        for (int slot = 0; slot < count; slot++)
        {
            VertexLayoutDescription layout = layouts[slot];

            _currentVertexSource.ResolveSlot((uint)slot, in layout, out VertexBinding binding);
            CheckVertexBindingUsage(in binding, (uint)slot);

            D3D11Buffer d3d11Buffer = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(binding.Buffer);
            UnbindUAVBuffer(binding.Buffer);

            ppBuffers[slot] = d3d11Buffer.Buffer;
            pStrides[slot] = (uint)_vertexStrides[slot];
            pOffsets[slot] = binding.Offset;
        }

        Ctx->IASetVertexBuffers(0u, (uint)count, ppBuffers, pStrides, pOffsets);
    }

    public override void SetScissorRect(uint index, uint x, uint y, uint width, uint height)
    {
        _scissorRectsChanged = true;
        Util.EnsureArrayMinimumSize(ref _scissors, index + 1);
        _scissors[index] = new RawRect((int)x, (int)y, (int)(x + width), (int)(y + height));
    }

    public override void SetViewport(uint index, ref Viewport viewport)
    {
        _viewportsChanged = true;
        Util.EnsureArrayMinimumSize(ref _viewports, index + 1);
        _viewports[index] = new D3D11Viewport
        {
            TopLeftX = viewport.X,
            TopLeftY = viewport.Y,
            Width = viewport.Width,
            Height = viewport.Height,
            MinDepth = viewport.MinDepth,
            MaxDepth = viewport.MaxDepth,
        };
    }

    private void BindTextureView(D3D11TextureView texView, int slot, ShaderStages stages)
    {
        ID3D11ShaderResourceView* srv = texView != null ? texView.ShaderResourceView : null;
        if (srv != null)
        {
            if (!_boundSRVs.TryGetValue(texView.Target, out List<BoundTextureInfo> list))
            {
                list = GetNewOrCachedBoundTextureInfoList();
                _boundSRVs.Add(texView.Target, list);
            }
            list.Add(new BoundTextureInfo { Slot = slot, Stages = stages });
        }

        if ((stages & ShaderStages.Vertex) == ShaderStages.Vertex)
        {
            bool bind = false;
            if (slot < MaxCachedUniformBuffers)
            {
                if (_vertexBoundTextureViews[slot] != texView)
                {
                    _vertexBoundTextureViews[slot] = texView;
                    bind = true;
                }
            }
            else
            {
                bind = true;
            }
            if (bind)
            {
                Ctx->VSSetShaderResources((uint)slot, 1, &srv);
            }
        }
        if ((stages & ShaderStages.Geometry) == ShaderStages.Geometry)
        {
            Ctx->GSSetShaderResources((uint)slot, 1, &srv);
        }
        if ((stages & ShaderStages.TessellationControl) == ShaderStages.TessellationControl)
        {
            Ctx->HSSetShaderResources((uint)slot, 1, &srv);
        }
        if ((stages & ShaderStages.TessellationEvaluation) == ShaderStages.TessellationEvaluation)
        {
            Ctx->DSSetShaderResources((uint)slot, 1, &srv);
        }
        if ((stages & ShaderStages.Fragment) == ShaderStages.Fragment)
        {
            bool bind = false;
            if (slot < MaxCachedUniformBuffers)
            {
                if (_fragmentBoundTextureViews[slot] != texView)
                {
                    _fragmentBoundTextureViews[slot] = texView;
                    bind = true;
                }
            }
            else
            {
                bind = true;
            }
            if (bind)
            {
                Ctx->PSSetShaderResources((uint)slot, 1, &srv);
            }
        }
        if ((stages & ShaderStages.Compute) == ShaderStages.Compute)
        {
            Ctx->CSSetShaderResources((uint)slot, 1, &srv);
        }
    }

    private List<BoundTextureInfo> GetNewOrCachedBoundTextureInfoList()
    {
        if (_boundTextureInfoPool.Count > 0)
        {
            int index = _boundTextureInfoPool.Count - 1;
            List<BoundTextureInfo> ret = _boundTextureInfoPool[index];
            _boundTextureInfoPool.RemoveAt(index);
            return ret;
        }

        return [];
    }

    private void BindStorageBufferView(DeviceBufferRange range, int slot, ShaderStages stages)
    {
        bool compute = (stages & ShaderStages.Compute) != 0;
        UnbindUAVBuffer(range.Buffer);

        ID3D11ShaderResourceView* srv = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(range.Buffer)
            .GetShaderResourceView(range.Offset, range.SizeInBytes);

        if ((stages & ShaderStages.Vertex) == ShaderStages.Vertex)
        {
            Ctx->VSSetShaderResources((uint)slot, 1, &srv);
        }
        if ((stages & ShaderStages.Geometry) == ShaderStages.Geometry)
        {
            Ctx->GSSetShaderResources((uint)slot, 1, &srv);
        }
        if ((stages & ShaderStages.TessellationControl) == ShaderStages.TessellationControl)
        {
            Ctx->HSSetShaderResources((uint)slot, 1, &srv);
        }
        if ((stages & ShaderStages.TessellationEvaluation) == ShaderStages.TessellationEvaluation)
        {
            Ctx->DSSetShaderResources((uint)slot, 1, &srv);
        }
        if ((stages & ShaderStages.Fragment) == ShaderStages.Fragment)
        {
            Ctx->PSSetShaderResources((uint)slot, 1, &srv);
        }
        if (compute)
        {
            Ctx->CSSetShaderResources((uint)slot, 1, &srv);
        }
    }


    private delegate void SetBufferFunc(uint startSlot, uint numBuffers, ID3D11Buffer** buffers);
    private delegate void SetBuffer1Func(uint startSlot, uint numBuffers, ID3D11Buffer** buffers, uint* firstConstant, uint* numConstants);


    private void BindUniformBuffer(DeviceBufferRange range, int slot, ShaderStages stages)
    {
        void SetBuffer(SetBufferFunc set, SetBuffer1Func set1)
        {
            ID3D11Buffer* cb = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(range.Buffer).Buffer;

            if (range.IsFullRange)
            {
                set((uint)slot, 1, &cb);
            }
            else
            {
                PackRangeParams(range);
                if (!_gd.SupportsCommandBuffers)
                {
                    ID3D11Buffer* nullBuf = null;
                    set((uint)slot, 1, &nullBuf);
                }
                ID3D11Buffer* cbOut = _cbOut[0];
                fixed (int* pFirstConst = _firstConstRef)
                fixed (int* pNumConsts = _numConstsRef)
                {
                    set1((uint)slot, 1, &cbOut, (uint*)pFirstConst, (uint*)pNumConsts);
                }
            }
        }

        ID3D11DeviceContext1* ctx1 = (ID3D11DeviceContext1*)_context1;

        if ((stages & ShaderStages.Vertex) == ShaderStages.Vertex)
        {
            bool bind = false;
            if (slot < MaxCachedUniformBuffers)
            {
                if (!_vertexBoundUniformBuffers[slot].Equals(range))
                {
                    _vertexBoundUniformBuffers[slot] = range;
                    bind = true;
                }
            }
            else
            {
                bind = true;
            }
            if (bind)
                SetBuffer(Ctx->VSSetConstantBuffers, ctx1->VSSetConstantBuffers1);
        }

        if ((stages & ShaderStages.Geometry) == ShaderStages.Geometry)
            SetBuffer(Ctx->GSSetConstantBuffers, ctx1->GSSetConstantBuffers1);

        if ((stages & ShaderStages.TessellationControl) == ShaderStages.TessellationControl)
            SetBuffer(Ctx->HSSetConstantBuffers, ctx1->HSSetConstantBuffers1);

        if ((stages & ShaderStages.TessellationEvaluation) == ShaderStages.TessellationEvaluation)
            SetBuffer(Ctx->DSSetConstantBuffers, ctx1->DSSetConstantBuffers1);

        if ((stages & ShaderStages.Fragment) == ShaderStages.Fragment)
        {
            bool bind = false;
            if (slot < MaxCachedUniformBuffers)
            {
                if (!_fragmentBoundUniformBuffers[slot].Equals(range))
                {
                    _fragmentBoundUniformBuffers[slot] = range;
                    bind = true;
                }
            }
            else
            {
                bind = true;
            }

            if (bind)
                SetBuffer(Ctx->PSSetConstantBuffers, ctx1->PSSetConstantBuffers1);
        }
        if ((stages & ShaderStages.Compute) == ShaderStages.Compute)
        {
            SetBuffer(Ctx->CSSetConstantBuffers, ctx1->CSSetConstantBuffers1);
        }
    }

    private void PackRangeParams(DeviceBufferRange range)
    {
        _cbOut[0] = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(range.Buffer).Buffer;
        _firstConstRef[0] = (int)range.Offset / 16;
        uint roundedSize = range.SizeInBytes < 256 ? 256u : range.SizeInBytes;
        _numConstsRef[0] = (int)roundedSize / 16;
    }

    private void BindUnorderedAccessView(
        Texture texture,
        DeviceBuffer buffer,
        ID3D11UnorderedAccessView* uav,
        int slot,
        ShaderStages stages)
    {
        bool compute = stages == ShaderStages.Compute;
        Debug.Assert(compute || ((stages & ShaderStages.Compute) == 0));
        Debug.Assert(texture == null || buffer == null);

        if (texture != null && uav != null)
        {
            if (!_boundUAVs.TryGetValue(texture, out List<BoundTextureInfo> list))
            {
                list = GetNewOrCachedBoundTextureInfoList();
                _boundUAVs.Add(texture, list);
            }
            list.Add(new BoundTextureInfo { Slot = slot, Stages = stages });
        }

        int baseSlot = 0;
        if (!compute && _fragmentBoundSamplers != null)
        {
            baseSlot = _framebuffer.ColorTargets.Count;
        }
        int actualSlot = baseSlot + slot;

        if (buffer != null)
        {
            TrackBoundUAVBuffer(buffer, actualSlot, compute);
        }

        if (compute)
        {
            uint initialCount = unchecked((uint)-1);
            Ctx->CSSetUnorderedAccessViews((uint)actualSlot, 1, &uav, &initialCount);
        }
        else
        {
            // For OM UAVs, use OMSetRenderTargetsAndUnorderedAccessViews with KeepRenderTargetsAndDepthStencil
            uint initialCount = unchecked((uint)-1);
            Ctx->OMSetRenderTargetsAndUnorderedAccessViews(
                0xFFFFFFFF, // D3D11_KEEP_RENDER_TARGETS_AND_DEPTH_STENCIL
                null,
                null,
                (uint)actualSlot,
                1,
                &uav,
                &initialCount);
        }
    }

    private void TrackBoundUAVBuffer(DeviceBuffer buffer, int slot, bool compute)
    {
        List<(DeviceBuffer, int)> list = compute ? _boundComputeUAVBuffers : _boundOMUAVBuffers;
        list.Add((buffer, slot));
    }

    private void UnbindUAVBuffer(DeviceBuffer buffer)
    {
        UnbindUAVBufferIndividual(buffer, false);
        UnbindUAVBufferIndividual(buffer, true);
    }

    private void UnbindUAVBufferIndividual(DeviceBuffer buffer, bool compute)
    {
        List<(DeviceBuffer, int)> list = compute ? _boundComputeUAVBuffers : _boundOMUAVBuffers;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Item1 == buffer)
            {
                int slot = list[i].Item2;
                if (compute)
                {
                    ID3D11UnorderedAccessView* nullUav = null;
                    uint initialCount = unchecked((uint)-1);
                    Ctx->CSSetUnorderedAccessViews((uint)slot, 1, &nullUav, &initialCount);
                }
                else
                {
                    ID3D11UnorderedAccessView* nullUav = null;
                    uint initialCount = unchecked((uint)-1);
                    Ctx->OMSetRenderTargetsAndUnorderedAccessViews(
                        0xFFFFFFFF,
                        null,
                        null,
                        (uint)slot,
                        1,
                        &nullUav,
                        &initialCount);
                }

                list.RemoveAt(i);
                i -= 1;
            }
        }
    }

    private void BindSampler(D3D11Sampler sampler, int slot, ShaderStages stages)
    {
        ID3D11SamplerState* samplerPtr = sampler.DeviceSampler;

        if ((stages & ShaderStages.Vertex) == ShaderStages.Vertex)
        {
            bool bind = false;
            if (slot < MaxCachedSamplers)
            {
                if (_vertexBoundSamplers[slot] != sampler)
                {
                    _vertexBoundSamplers[slot] = sampler;
                    bind = true;
                }
            }
            else
            {
                bind = true;
            }
            if (bind)
            {
                Ctx->VSSetSamplers((uint)slot, 1, &samplerPtr);
            }
        }
        if ((stages & ShaderStages.Geometry) == ShaderStages.Geometry)
        {
            Ctx->GSSetSamplers((uint)slot, 1, &samplerPtr);
        }
        if ((stages & ShaderStages.TessellationControl) == ShaderStages.TessellationControl)
        {
            Ctx->HSSetSamplers((uint)slot, 1, &samplerPtr);
        }
        if ((stages & ShaderStages.TessellationEvaluation) == ShaderStages.TessellationEvaluation)
        {
            Ctx->DSSetSamplers((uint)slot, 1, &samplerPtr);
        }
        if ((stages & ShaderStages.Fragment) == ShaderStages.Fragment)
        {
            bool bind = false;
            if (slot < MaxCachedSamplers)
            {
                if (_fragmentBoundSamplers[slot] != sampler)
                {
                    _fragmentBoundSamplers[slot] = sampler;
                    bind = true;
                }
            }
            else
            {
                bind = true;
            }
            if (bind)
            {
                Ctx->PSSetSamplers((uint)slot, 1, &samplerPtr);
            }
        }
        if ((stages & ShaderStages.Compute) == ShaderStages.Compute)
        {
            Ctx->CSSetSamplers((uint)slot, 1, &samplerPtr);
        }
    }

    private protected override void SetFramebufferCore(Framebuffer fb)
    {
        D3D11Framebuffer d3dFB = Util.AssertSubtype<Framebuffer, D3D11Framebuffer>(fb);
        if (d3dFB.Swapchain != null)
        {
            d3dFB.Swapchain.AddCommandBufferReference(this);
            _referencedSwapchains.Add(d3dFB.Swapchain);
        }

        for (int i = 0; i < fb.ColorTargets.Count; i++)
        {
            UnbindSRVTexture(fb.ColorTargets[i].Target);
        }

        int rtvCount = d3dFB.RenderTargetViews.Length;
        ID3D11RenderTargetView** ppRTVs = stackalloc ID3D11RenderTargetView*[rtvCount];
        for (int i = 0; i < rtvCount; i++)
        {
            ppRTVs[i] = d3dFB.RenderTargetViews[i];
        }

        Ctx->OMSetRenderTargets((uint)rtvCount, ppRTVs, d3dFB.DepthStencilView);
    }

    private protected override void ClearColorTargetCore(uint index, Color clearColor)
    {
        float* color = stackalloc float[4];
        color[0] = clearColor.R;
        color[1] = clearColor.G;
        color[2] = clearColor.B;
        color[3] = clearColor.A;
        Ctx->ClearRenderTargetView(D3D11Framebuffer.RenderTargetViews[index], color);
    }

    private protected override void ClearDepthStencilCore(float depth, byte stencil)
    {
        Ctx->ClearDepthStencilView(
            D3D11Framebuffer.DepthStencilView,
            (uint)(ClearFlag.Depth | ClearFlag.Stencil),
            depth,
            stencil);
    }

    private protected override void UpdateBufferCore(DeviceBuffer buffer, uint bufferOffsetInBytes, IntPtr source, uint sizeInBytes)
    {
        D3D11Buffer d3dBuffer = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(buffer);
        if (sizeInBytes == 0)
        {
            return;
        }

        bool isDynamic = (buffer.Usage & BufferUsage.Dynamic) == BufferUsage.Dynamic;
        bool isStaging = (buffer.Usage & BufferUsage.Staging) == BufferUsage.Staging;
        bool isUniformBuffer = (buffer.Usage & BufferUsage.UniformBuffer) == BufferUsage.UniformBuffer;
        bool useMap = isDynamic;
        bool updateFullBuffer = bufferOffsetInBytes == 0 && sizeInBytes == buffer.SizeInBytes;
        bool useUpdateSubresource = !isDynamic && !isStaging && (!isUniformBuffer || updateFullBuffer);

        if (useUpdateSubresource)
        {
            Box subregion = new()
            {
                Left = bufferOffsetInBytes,
                Top = 0,
                Front = 0,
                Right = sizeInBytes + bufferOffsetInBytes,
                Bottom = 1,
                Back = 1,
            };
            Box* pSubregion = &subregion;
            if (isUniformBuffer)
            {
                pSubregion = null;
            }

            if (bufferOffsetInBytes == 0)
            {
                Ctx->UpdateSubresource((ID3D11Resource*)d3dBuffer.Buffer, 0, pSubregion, source.ToPointer(), 0, 0);
            }
            else
            {
                UpdateSubresource_Workaround((ID3D11Resource*)d3dBuffer.Buffer, 0, subregion, source);
            }
        }
        else if (useMap && updateFullBuffer) // Can only update full buffer with WriteDiscard.
        {
            MappedSubresource msb;
            SilkMarshal.ThrowHResult(
                Ctx->Map(
                    (ID3D11Resource*)d3dBuffer.Buffer,
                    0,
                    D3D11Formats.VdToD3D11MapMode(isDynamic, MapMode.Write),
                    0,
                    &msb));
            if (sizeInBytes < 1024)
            {
                Unsafe.CopyBlock(msb.PData, source.ToPointer(), sizeInBytes);
            }
            else
            {
                Buffer.MemoryCopy(source.ToPointer(), msb.PData, buffer.SizeInBytes, sizeInBytes);
            }
            Ctx->Unmap((ID3D11Resource*)d3dBuffer.Buffer, 0);
        }
        else
        {
            D3D11Buffer staging = GetFreeStagingBuffer(sizeInBytes);
            _gd.UpdateBuffer(staging, 0, source, sizeInBytes);
            CopyBuffer(staging, 0, buffer, bufferOffsetInBytes, sizeInBytes);
            _submittedStagingBuffers.Add(staging);
        }
    }

    private void UpdateSubresource_Workaround(
        ID3D11Resource* resource,
        int subresource,
        Box region,
        IntPtr data)
    {
        bool needWorkaround = !_gd.SupportsCommandBuffers;
        void* pAdjustedSrcData = data.ToPointer();
        if (needWorkaround)
        {
            Debug.Assert(region.Top == 0 && region.Front == 0);
            pAdjustedSrcData = (byte*)data - region.Left;
        }

        Ctx->UpdateSubresource(resource, (uint)subresource, &region, pAdjustedSrcData, 0, 0);
    }


    private D3D11Buffer GetFreeStagingBuffer(uint sizeInBytes)
    {
        foreach (D3D11Buffer buffer in _availableStagingBuffers)
        {
            if (buffer.SizeInBytes >= sizeInBytes)
            {
                _availableStagingBuffers.Remove(buffer);
                return buffer;
            }
        }

        DeviceBuffer staging = _gd.ResourceFactory.CreateBuffer(
            new BufferDescription(sizeInBytes, BufferUsage.Staging));

        return Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(staging);
    }

    private protected override void CopyBufferCore(DeviceBuffer source, uint sourceOffset, DeviceBuffer destination, uint destinationOffset, uint sizeInBytes)
    {
        D3D11Buffer srcD3D11Buffer = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(source);
        D3D11Buffer dstD3D11Buffer = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(destination);

        Box region = new()
        {
            Left = sourceOffset,
            Top = 0,
            Front = 0,
            Right = sourceOffset + sizeInBytes,
            Bottom = 1,
            Back = 1,
        };

        Ctx->CopySubresourceRegion(
            (ID3D11Resource*)dstD3D11Buffer.Buffer, 0, destinationOffset, 0, 0,
            (ID3D11Resource*)srcD3D11Buffer.Buffer, 0, &region);

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
        D3D11Texture srcD3D11Texture = Util.AssertSubtype<Texture, D3D11Texture>(source);
        D3D11Texture dstD3D11Texture = Util.AssertSubtype<Texture, D3D11Texture>(destination);

        uint blockSize = FormatHelpers.IsCompressedFormat(source.Format) ? 4u : 1u;
        uint clampedWidth = Math.Max(blockSize, width);
        uint clampedHeight = Math.Max(blockSize, height);

        bool useRegion = srcX != 0 || srcY != 0 || srcZ != 0
            || clampedWidth != source.Width || clampedHeight != source.Height || depth != source.Depth;

        Box region = new()
        {
            Left = srcX,
            Top = srcY,
            Front = srcZ,
            Right = srcX + clampedWidth,
            Bottom = srcY + clampedHeight,
            Back = srcZ + depth,
        };

        for (uint i = 0; i < layerCount; i++)
        {
            int srcSubresource = D3D11Util.ComputeSubresource(srcMipLevel, source.MipLevels, srcBaseArrayLayer + i);
            int dstSubresource = D3D11Util.ComputeSubresource(dstMipLevel, destination.MipLevels, dstBaseArrayLayer + i);

            Ctx->CopySubresourceRegion(
                dstD3D11Texture.DeviceTexture,
                (uint)dstSubresource,
                dstX,
                dstY,
                dstZ,
                srcD3D11Texture.DeviceTexture,
                (uint)srcSubresource,
                useRegion ? &region : null);
        }
    }

    private protected override void GenerateMipmapsCore(Texture texture)
    {
        TextureView fullTexView = texture.GetFullTextureView(_gd);
        D3D11TextureView d3d11View = Util.AssertSubtype<TextureView, D3D11TextureView>(fullTexView);
        ID3D11ShaderResourceView* srv = d3d11View.ShaderResourceView;
        Ctx->GenerateMips(srv);
    }

    public override string Name
    {
        get => _name;
        set
        {
            _name = value;
            if (_context.Handle != null)
                D3D11Util.SetDebugName((ID3D11DeviceChild*)_context.Handle, value);
        }
    }

    internal void OnCompleted()
    {
        _commandList.Dispose();
        _commandList = default;

        foreach (D3D11Swapchain sc in _referencedSwapchains)
        {
            sc.RemoveCommandBufferReference(this);
        }
        _referencedSwapchains.Clear();

        foreach (D3D11Buffer buffer in _submittedStagingBuffers)
        {
            _availableStagingBuffers.Add(buffer);
        }

        _submittedStagingBuffers.Clear();
    }

    private protected override void PushDebugGroupCore(string name)
    {
        if (_uda.Handle != null)
        {
            _uda.BeginEvent(name);
        }
    }

    private protected override void PopDebugGroupCore()
    {
        if (_uda.Handle != null)
        {
            _uda.EndEvent();
        }
    }

    private protected override void InsertDebugMarkerCore(string name)
    {
        if (_uda.Handle != null)
        {
            _uda.SetMarker(name);
        }
    }

    public override void Dispose()
    {
        if (!_disposed)
        {
            if (_uda.Handle != null) _uda.Dispose();
            if (_commandList.Handle != null) _commandList.Dispose();
            if (_context1.Handle != null) _context1.Dispose();
            _context.Dispose();

            foreach (D3D11Buffer buffer in _availableStagingBuffers)
            {
                buffer.Dispose();
            }
            _availableStagingBuffers.Clear();

            _disposed = true;
        }
    }

    private struct BoundTextureInfo
    {
        public int Slot;
        public ShaderStages Stages;
    }

    /// <summary>
    /// A viewport struct matching D3D11_VIEWPORT layout (6 floats).
    /// Used instead of Prowl.Veldrid.Viewport to match Silk.NET's Viewport struct layout exactly.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11Viewport
    {
        public float TopLeftX;
        public float TopLeftY;
        public float Width;
        public float Height;
        public float MinDepth;
        public float MaxDepth;
    }

    /// <summary>
    /// A RECT struct (left, top, right, bottom) matching the Win32 RECT layout.
    /// Used for scissor rects passed to RSSetScissorRects.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct RawRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public RawRect(int left, int top, int right, int bottom)
        {
            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
        }
    }
}
