using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Buffers;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Silk.NET.Core.Native;
using Silk.NET.Maths;

namespace Veldrid.D3D11
{
    internal unsafe class D3D11CommandList : CommandList
    {
        private readonly D3D11GraphicsDevice _gd;
        private ComPtr<ID3D11DeviceContext> _context;
        private ComPtr<ID3D11DeviceContext1> _context1;
        private ComPtr<ID3DUserDefinedAnnotation> _uda;
        private bool _begun;
        private bool _disposed;
        private ComPtr<ID3D11CommandList> _commandList;

        private D3D11Viewport[] _viewports = new D3D11Viewport[0];
        private RawRect[] _scissors = new RawRect[0];
        private bool _viewportsChanged;
        private bool _scissorRectsChanged;

        private uint _numVertexBindings = 0;
        private nint[] _vertexBindings = new nint[1];
        private int[] _vertexStrides = new int[1];
        private int[] _vertexOffsets = new int[1];

        // Cached pipeline State
        private DeviceBuffer _ib;
        private uint _ibOffset;
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

        private new D3D11Pipeline _graphicsPipeline;
        private BoundResourceSetInfo[] _graphicsResourceSets = new BoundResourceSetInfo[1];
        // Resource sets are invalidated when a new resource set is bound with an incompatible SRV or UAV.
        private bool[] _invalidatedGraphicsResourceSets = new bool[1];

        private new D3D11Pipeline _computePipeline;
        private BoundResourceSetInfo[] _computeResourceSets = new BoundResourceSetInfo[1];
        // Resource sets are invalidated when a new resource set is bound with an incompatible SRV or UAV.
        private bool[] _invalidatedComputeResourceSets = new bool[1];
        private string _name;
        private bool _vertexBindingsChanged;
        private ID3D11Buffer*[] _cbOut = new ID3D11Buffer*[1];
        private int[] _firstConstRef = new int[1];
        private int[] _numConstsRef = new int[1];

        // Cached resources
        private const int MaxCachedUniformBuffers = 15;
        private readonly D3D11BufferRange[] _vertexBoundUniformBuffers = new D3D11BufferRange[MaxCachedUniformBuffers];
        private readonly D3D11BufferRange[] _fragmentBoundUniformBuffers = new D3D11BufferRange[MaxCachedUniformBuffers];
        private const int MaxCachedTextureViews = 16;
        private readonly D3D11TextureView[] _vertexBoundTextureViews = new D3D11TextureView[MaxCachedTextureViews];
        private readonly D3D11TextureView[] _fragmentBoundTextureViews = new D3D11TextureView[MaxCachedTextureViews];
        private const int MaxCachedSamplers = 4;
        private readonly D3D11Sampler[] _vertexBoundSamplers = new D3D11Sampler[MaxCachedSamplers];
        private readonly D3D11Sampler[] _fragmentBoundSamplers = new D3D11Sampler[MaxCachedSamplers];

        private readonly Dictionary<Texture, List<BoundTextureInfo>> _boundSRVs = new Dictionary<Texture, List<BoundTextureInfo>>();
        private readonly Dictionary<Texture, List<BoundTextureInfo>> _boundUAVs = new Dictionary<Texture, List<BoundTextureInfo>>();
        private readonly List<List<BoundTextureInfo>> _boundTextureInfoPool = new List<List<BoundTextureInfo>>(20);

        private const int MaxUAVs = 8;
        private readonly List<(DeviceBuffer, int)> _boundComputeUAVBuffers = new List<(DeviceBuffer, int)>(MaxUAVs);
        private readonly List<(DeviceBuffer, int)> _boundOMUAVBuffers = new List<(DeviceBuffer, int)>(MaxUAVs);

        private readonly List<D3D11Buffer> _availableStagingBuffers = new List<D3D11Buffer>();
        private readonly List<D3D11Buffer> _submittedStagingBuffers = new List<D3D11Buffer>();

        private readonly List<D3D11Swapchain> _referencedSwapchains = new List<D3D11Swapchain>();

        /// <summary>
        /// Helper to get the raw context pointer for calling D3D11 methods.
        /// </summary>
        private ID3D11DeviceContext* Ctx
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _context;
        }

        public D3D11CommandList(D3D11GraphicsDevice gd, ref CommandListDescription description)
            : base(ref description, gd.Features, gd.UniformBufferMinOffsetAlignment, gd.StructuredBufferMinOffsetAlignment)
        {
            _gd = gd;

            ID3D11DeviceContext* pDeferredContext;
            SilkMarshal.ThrowHResult(gd.Device->CreateDeferredContext(0, &pDeferredContext));
            _context = default;
            _context.Handle = pDeferredContext;

            ID3D11DeviceContext1* pContext1;
            var ctx1Guid = ID3D11DeviceContext1.Guid;
            if (((IUnknown*)pDeferredContext)->QueryInterface(&ctx1Guid, (void**)&pContext1) >= 0)
            {
                _context1 = default;
                _context1.Handle = pContext1;
            }

            ID3DUserDefinedAnnotation* pUda;
            var udaGuid = ID3DUserDefinedAnnotation.Guid;
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
        }

        private void ClearState()
        {
            ClearCachedState();
            Ctx->ClearState();
            ResetManagedState();
        }

        private void ResetManagedState()
        {
            _numVertexBindings = 0;
            Util.ClearArray(_vertexBindings);
            Util.ClearArray(_vertexStrides);
            Util.ClearArray(_vertexOffsets);

            _framebuffer = null;

            Util.ClearArray(_viewports);
            Util.ClearArray(_scissors);
            _viewportsChanged = false;
            _scissorRectsChanged = false;

            _ib = null;
            _graphicsPipeline = null;
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

            ClearSets(_graphicsResourceSets);

            Util.ClearArray(_vertexBoundUniformBuffers);
            Util.ClearArray(_vertexBoundTextureViews);
            Util.ClearArray(_vertexBoundSamplers);

            Util.ClearArray(_fragmentBoundUniformBuffers);
            Util.ClearArray(_fragmentBoundTextureViews);
            Util.ClearArray(_fragmentBoundSamplers);

            _computePipeline = null;
            ClearSets(_computeResourceSets);

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

        private void ClearSets(BoundResourceSetInfo[] boundSets)
        {
            foreach (BoundResourceSetInfo boundSetInfo in boundSets)
            {
                boundSetInfo.Offsets.Dispose();
            }
            Util.ClearArray(boundSets);
        }

        public override void End()
        {
            if (_commandList.Handle != null)
            {
                throw new VeldridException("Invalid use of End().");
            }

            ID3D11CommandList* pCmdList;
            SilkMarshal.ThrowHResult(Ctx->FinishCommandList(0, &pCmdList));
            _commandList = default;
            _commandList.Handle = pCmdList;
            if (_name != null)
                D3D11Util.SetDebugName((ID3D11DeviceChild*)_commandList.Handle, _name);
            ResetManagedState();
            _begun = false;
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

        private protected override void SetIndexBufferCore(DeviceBuffer buffer, IndexFormat format, uint offset)
        {
            if (_ib != buffer || _ibOffset != offset)
            {
                _ib = buffer;
                _ibOffset = offset;
                D3D11Buffer d3d11Buffer = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(buffer);
                UnbindUAVBuffer(buffer);
                Ctx->IASetIndexBuffer(d3d11Buffer.Buffer, D3D11Formats.ToDxgiFormat(format), (uint)offset);
            }
        }

        private protected override void SetPipelineCore(Pipeline pipeline)
        {
            if (!pipeline.IsComputePipeline && _graphicsPipeline != pipeline)
            {
                D3D11Pipeline d3dPipeline = Util.AssertSubtype<Pipeline, D3D11Pipeline>(pipeline);
                _graphicsPipeline = d3dPipeline;
                ClearSets(_graphicsResourceSets); // Invalidate resource set bindings -- they may be invalid.
                Util.ClearArray(_invalidatedGraphicsResourceSets);

                ID3D11BlendState* blendState = d3dPipeline.BlendState;
                float[] blendFactor = d3dPipeline.BlendFactor;
                if (_blendState != blendState || !BlendFactorEquals(_blendFactor, blendFactor))
                {
                    _blendState = blendState;
                    Array.Copy(blendFactor, _blendFactor, 4);
                    fixed (float* pBf = _blendFactor)
                    {
                        Ctx->OMSetBlendState(blendState, pBf, 0xFFFFFFFF);
                    }
                }

                ID3D11DepthStencilState* depthStencilState = d3dPipeline.DepthStencilState;
                uint stencilReference = d3dPipeline.StencilReference;
                if (_depthStencilState != depthStencilState || _stencilReference != stencilReference)
                {
                    _depthStencilState = depthStencilState;
                    _stencilReference = stencilReference;
                    Ctx->OMSetDepthStencilState(depthStencilState, stencilReference);
                }

                ID3D11RasterizerState* rasterizerState = d3dPipeline.RasterizerState;
                if (_rasterizerState != rasterizerState)
                {
                    _rasterizerState = rasterizerState;
                    Ctx->RSSetState(rasterizerState);
                }

                D3DPrimitiveTopology primitiveTopology = d3dPipeline.PrimitiveTopology;
                if (_primitiveTopology != primitiveTopology)
                {
                    _primitiveTopology = primitiveTopology;
                    Ctx->IASetPrimitiveTopology(primitiveTopology);
                }

                ID3D11InputLayout* inputLayout = d3dPipeline.InputLayout;
                if (_inputLayout != inputLayout)
                {
                    _inputLayout = inputLayout;
                    Ctx->IASetInputLayout(inputLayout);
                }

                ID3D11VertexShader* vertexShader = d3dPipeline.VertexShader;
                if (_vertexShader != vertexShader)
                {
                    _vertexShader = vertexShader;
                    Ctx->VSSetShader(vertexShader, null, 0);
                }

                ID3D11GeometryShader* geometryShader = d3dPipeline.GeometryShader;
                if (_geometryShader != geometryShader)
                {
                    _geometryShader = geometryShader;
                    Ctx->GSSetShader(geometryShader, null, 0);
                }

                ID3D11HullShader* hullShader = d3dPipeline.HullShader;
                if (_hullShader != hullShader)
                {
                    _hullShader = hullShader;
                    Ctx->HSSetShader(hullShader, null, 0);
                }

                ID3D11DomainShader* domainShader = d3dPipeline.DomainShader;
                if (_domainShader != domainShader)
                {
                    _domainShader = domainShader;
                    Ctx->DSSetShader(domainShader, null, 0);
                }

                ID3D11PixelShader* pixelShader = d3dPipeline.PixelShader;
                if (_pixelShader != pixelShader)
                {
                    _pixelShader = pixelShader;
                    Ctx->PSSetShader(pixelShader, null, 0);
                }

                if (!Util.ArrayEqualsEquatable(_vertexStrides, d3dPipeline.VertexStrides))
                {
                    _vertexBindingsChanged = true;

                    if (d3dPipeline.VertexStrides != null)
                    {
                        Util.EnsureArrayMinimumSize(ref _vertexStrides, (uint)d3dPipeline.VertexStrides.Length);
                        d3dPipeline.VertexStrides.CopyTo(_vertexStrides, 0);
                    }
                }

                Util.EnsureArrayMinimumSize(ref _vertexStrides, 1);
                Util.EnsureArrayMinimumSize(ref _vertexBindings, (uint)_vertexStrides.Length);
                Util.EnsureArrayMinimumSize(ref _vertexOffsets, (uint)_vertexStrides.Length);

                Util.EnsureArrayMinimumSize(ref _graphicsResourceSets, (uint)d3dPipeline.ResourceLayouts.Length);
                Util.EnsureArrayMinimumSize(ref _invalidatedGraphicsResourceSets, (uint)d3dPipeline.ResourceLayouts.Length);
            }
            else if (pipeline.IsComputePipeline && _computePipeline != pipeline)
            {
                D3D11Pipeline d3dPipeline = Util.AssertSubtype<Pipeline, D3D11Pipeline>(pipeline);
                _computePipeline = d3dPipeline;
                ClearSets(_computeResourceSets); // Invalidate resource set bindings -- they may be invalid.
                Util.ClearArray(_invalidatedComputeResourceSets);

                ID3D11ComputeShader* computeShader = d3dPipeline.ComputeShader;
                Ctx->CSSetShader(computeShader, null, 0);
                Util.EnsureArrayMinimumSize(ref _computeResourceSets, (uint)d3dPipeline.ResourceLayouts.Length);
                Util.EnsureArrayMinimumSize(ref _invalidatedComputeResourceSets, (uint)d3dPipeline.ResourceLayouts.Length);
            }
        }

        private static bool BlendFactorEquals(float[] a, float[] b)
        {
            return a[0] == b[0] && a[1] == b[1] && a[2] == b[2] && a[3] == b[3];
        }

        private protected override void SetGraphicsResourceSetCore(uint slot, ResourceSet rs, uint dynamicOffsetsCount, ref uint dynamicOffsets)
        {
            if (_graphicsResourceSets[slot].Equals(rs, dynamicOffsetsCount, ref dynamicOffsets))
            {
                return;
            }

            _graphicsResourceSets[slot].Offsets.Dispose();
            _graphicsResourceSets[slot] = new BoundResourceSetInfo(rs, dynamicOffsetsCount, ref dynamicOffsets);
            ActivateResourceSet(slot, _graphicsResourceSets[slot], true);
        }

        private protected override void SetComputeResourceSetCore(uint slot, ResourceSet set, uint dynamicOffsetsCount, ref uint dynamicOffsets)
        {
            if (_computeResourceSets[slot].Equals(set, dynamicOffsetsCount, ref dynamicOffsets))
            {
                return;
            }

            _computeResourceSets[slot].Offsets.Dispose();
            _computeResourceSets[slot] = new BoundResourceSetInfo(set, dynamicOffsetsCount, ref dynamicOffsets);
            ActivateResourceSet(slot, _computeResourceSets[slot], false);
        }

        private void ActivateResourceSet(uint slot, BoundResourceSetInfo brsi, bool graphics)
        {
            D3D11ResourceSet d3d11RS = Util.AssertSubtype<ResourceSet, D3D11ResourceSet>(brsi.Set);

            int cbBase = GetConstantBufferBase(slot, graphics);
            int uaBase = GetUnorderedAccessBase(slot, graphics);
            int textureBase = GetTextureBase(slot, graphics);
            int samplerBase = GetSamplerBase(slot, graphics);

            D3D11ResourceLayout layout = d3d11RS.Layout;
            BindableResource[] resources = d3d11RS.Resources;
            uint dynamicOffsetIndex = 0;
            for (int i = 0; i < resources.Length; i++)
            {
                BindableResource resource = resources[i];
                uint bufferOffset = 0;
                if (layout.IsDynamicBuffer(i))
                {
                    bufferOffset = brsi.Offsets.Get(dynamicOffsetIndex);
                    dynamicOffsetIndex += 1;
                }
                D3D11ResourceLayout.ResourceBindingInfo rbi = layout.GetDeviceSlotIndex(i);
                switch (rbi.Kind)
                {
                    case ResourceKind.UniformBuffer:
                        {
                            D3D11BufferRange range = GetBufferRange(resource, bufferOffset);
                            BindUniformBuffer(range, cbBase + rbi.Slot, rbi.Stages);
                            break;
                        }
                    case ResourceKind.StructuredBufferReadOnly:
                        {
                            D3D11BufferRange range = GetBufferRange(resource, bufferOffset);
                            BindStorageBufferView(range, textureBase + rbi.Slot, rbi.Stages);
                            break;
                        }
                    case ResourceKind.StructuredBufferReadWrite:
                        {
                            D3D11BufferRange range = GetBufferRange(resource, bufferOffset);
                            ID3D11UnorderedAccessView* uav = range.Buffer.GetUnorderedAccessView(range.Offset, range.Size);
                            BindUnorderedAccessView(null, range.Buffer, uav, uaBase + rbi.Slot, rbi.Stages, slot);
                            break;
                        }
                    case ResourceKind.TextureReadOnly:
                        TextureView texView = Util.GetTextureView(_gd, resource);
                        D3D11TextureView d3d11TexView = Util.AssertSubtype<TextureView, D3D11TextureView>(texView);
                        UnbindUAVTexture(d3d11TexView.Target);
                        BindTextureView(d3d11TexView, textureBase + rbi.Slot, rbi.Stages, slot);
                        break;
                    case ResourceKind.TextureReadWrite:
                        TextureView rwTexView = Util.GetTextureView(_gd, resource);
                        D3D11TextureView d3d11RWTexView = Util.AssertSubtype<TextureView, D3D11TextureView>(rwTexView);
                        UnbindSRVTexture(d3d11RWTexView.Target);
                        BindUnorderedAccessView(d3d11RWTexView.Target, null, d3d11RWTexView.UnorderedAccessView, uaBase + rbi.Slot, rbi.Stages, slot);
                        break;
                    case ResourceKind.Sampler:
                        D3D11Sampler sampler = Util.AssertSubtype<BindableResource, D3D11Sampler>(resource);
                        BindSampler(sampler, samplerBase + rbi.Slot, rbi.Stages);
                        break;
                    default: throw Illegal.Value<ResourceKind>();
                }
            }
        }

        private D3D11BufferRange GetBufferRange(BindableResource resource, uint additionalOffset)
        {
            if (resource is D3D11Buffer d3d11Buff)
            {
                return new D3D11BufferRange(d3d11Buff, additionalOffset, d3d11Buff.SizeInBytes);
            }
            else if (resource is DeviceBufferRange range)
            {
                return new D3D11BufferRange(
                    Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(range.Buffer),
                    range.Offset + additionalOffset,
                    range.SizeInBytes);
            }
            else
            {
                throw new VeldridException($"Unexpected resource type used in a buffer type slot: {resource.GetType().Name}");
            }
        }

        private void UnbindSRVTexture(Texture target)
        {
            if (_boundSRVs.TryGetValue(target, out List<BoundTextureInfo> btis))
            {
                foreach (BoundTextureInfo bti in btis)
                {
                    BindTextureView(null, bti.Slot, bti.Stages, 0);

                    if ((bti.Stages & ShaderStages.Compute) == ShaderStages.Compute)
                    {
                        _invalidatedComputeResourceSets[bti.ResourceSet] = true;
                    }
                    else
                    {
                        _invalidatedGraphicsResourceSets[bti.ResourceSet] = true;
                    }
                }

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
                {
                    BindUnorderedAccessView(null, null, null, bti.Slot, bti.Stages, bti.ResourceSet);
                    if ((bti.Stages & ShaderStages.Compute) == ShaderStages.Compute)
                    {
                        _invalidatedComputeResourceSets[bti.ResourceSet] = true;
                    }
                    else
                    {
                        _invalidatedGraphicsResourceSets[bti.ResourceSet] = true;
                    }
                }

                bool result = _boundUAVs.Remove(target);
                Debug.Assert(result);

                btis.Clear();
                PoolBoundTextureList(btis);
            }
        }

        private int GetConstantBufferBase(uint slot, bool graphics)
        {
            D3D11ResourceLayout[] layouts = graphics ? _graphicsPipeline.ResourceLayouts : _computePipeline.ResourceLayouts;
            int ret = 0;
            for (int i = 0; i < slot; i++)
            {
                Debug.Assert(layouts[i] != null);
                ret += layouts[i].UniformBufferCount;
            }

            return ret;
        }

        private int GetUnorderedAccessBase(uint slot, bool graphics)
        {
            D3D11ResourceLayout[] layouts = graphics ? _graphicsPipeline.ResourceLayouts : _computePipeline.ResourceLayouts;
            int ret = 0;
            for (int i = 0; i < slot; i++)
            {
                Debug.Assert(layouts[i] != null);
                ret += layouts[i].StorageBufferCount;
            }

            return ret;
        }

        private int GetTextureBase(uint slot, bool graphics)
        {
            D3D11ResourceLayout[] layouts = graphics ? _graphicsPipeline.ResourceLayouts : _computePipeline.ResourceLayouts;
            int ret = 0;
            for (int i = 0; i < slot; i++)
            {
                Debug.Assert(layouts[i] != null);
                ret += layouts[i].TextureCount;
            }

            return ret;
        }

        private int GetSamplerBase(uint slot, bool graphics)
        {
            D3D11ResourceLayout[] layouts = graphics ? _graphicsPipeline.ResourceLayouts : _computePipeline.ResourceLayouts;
            int ret = 0;
            for (int i = 0; i < slot; i++)
            {
                Debug.Assert(layouts[i] != null);
                ret += layouts[i].SamplerCount;
            }

            return ret;
        }

        private protected override void SetVertexBufferCore(uint index, DeviceBuffer buffer, uint offset)
        {
            D3D11Buffer d3d11Buffer = Util.AssertSubtype<DeviceBuffer, D3D11Buffer>(buffer);
            if ((ID3D11Buffer*)_vertexBindings[index] != d3d11Buffer.Buffer || _vertexOffsets[index] != (int)offset)
            {
                _vertexBindingsChanged = true;
                UnbindUAVBuffer(buffer);
                _vertexBindings[index] = (nint)d3d11Buffer.Buffer;
                _vertexOffsets[index] = (int)offset;
                _numVertexBindings = Math.Max((index + 1), _numVertexBindings);
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

        private protected override void DrawIndexedCore(uint indexCount, uint instanceCount, uint indexStart, int vertexOffset, uint instanceStart)
        {
            PreDrawCommand();

            Debug.Assert(_ib != null);
            if (instanceCount == 1 && instanceStart == 0)
            {
                Ctx->DrawIndexed(indexCount, indexStart, vertexOffset);
            }
            else
            {
                Ctx->DrawIndexedInstanced(indexCount, instanceCount, indexStart, vertexOffset, instanceStart);
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
            FlushVertexBindings();

            int graphicsResourceCount = _graphicsPipeline.ResourceLayouts.Length;
            for (uint i = 0; i < graphicsResourceCount; i++)
            {
                if (_invalidatedGraphicsResourceSets[i])
                {
                    _invalidatedGraphicsResourceSets[i] = false;
                    ActivateResourceSet(i, _graphicsResourceSets[i], true);
                }
            }
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
            int computeResourceCount = _computePipeline.ResourceLayouts.Length;
            for (uint i = 0; i < computeResourceCount; i++)
            {
                if (_invalidatedComputeResourceSets[i])
                {
                    _invalidatedComputeResourceSets[i] = false;
                    ActivateResourceSet(i, _computeResourceSets[i], false);
                }
            }
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
            if (_vertexBindingsChanged)
            {
                int count = (int)_numVertexBindings;
                ID3D11Buffer** ppBuffers = stackalloc ID3D11Buffer*[count];
                for (int i = 0; i < count; i++)
                {
                    ppBuffers[i] = (ID3D11Buffer*)_vertexBindings[i];
                }

                fixed (int* pStrides = _vertexStrides)
                fixed (int* pOffsets = _vertexOffsets)
                {
                    Ctx->IASetVertexBuffers(0, _numVertexBindings, ppBuffers, (uint*)pStrides, (uint*)pOffsets);
                }

                _vertexBindingsChanged = false;
            }
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

        private void BindTextureView(D3D11TextureView texView, int slot, ShaderStages stages, uint resourceSet)
        {
            ID3D11ShaderResourceView* srv = texView != null ? texView.ShaderResourceView : null;
            if (srv != null)
            {
                if (!_boundSRVs.TryGetValue(texView.Target, out List<BoundTextureInfo> list))
                {
                    list = GetNewOrCachedBoundTextureInfoList();
                    _boundSRVs.Add(texView.Target, list);
                }
                list.Add(new BoundTextureInfo { Slot = slot, Stages = stages, ResourceSet = resourceSet });
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

            return new List<BoundTextureInfo>();
        }

        private void BindStorageBufferView(D3D11BufferRange range, int slot, ShaderStages stages)
        {
            bool compute = (stages & ShaderStages.Compute) != 0;
            UnbindUAVBuffer(range.Buffer);

            ID3D11ShaderResourceView* srv = range.Buffer.GetShaderResourceView(range.Offset, range.Size);

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

        private void BindUniformBuffer(D3D11BufferRange range, int slot, ShaderStages stages)
        {
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
                {
                    if (range.IsFullRange)
                    {
                        ID3D11Buffer* cb = range.Buffer.Buffer;
                        Ctx->VSSetConstantBuffers((uint)slot, 1, &cb);
                    }
                    else
                    {
                        PackRangeParams(range);
                        if (!_gd.SupportsCommandLists)
                        {
                            ID3D11Buffer* nullBuf = null;
                            Ctx->VSSetConstantBuffers((uint)slot, 1, &nullBuf);
                        }
                        ID3D11Buffer* cb = _cbOut[0];
                        fixed (int* pFirstConst = _firstConstRef)
                        fixed (int* pNumConsts = _numConstsRef)
                        {
                            ((ID3D11DeviceContext1*)_context1)->VSSetConstantBuffers1((uint)slot, 1, &cb, (uint*)pFirstConst, (uint*)pNumConsts);
                        }
                    }
                }
            }
            if ((stages & ShaderStages.Geometry) == ShaderStages.Geometry)
            {
                if (range.IsFullRange)
                {
                    ID3D11Buffer* cb = range.Buffer.Buffer;
                    Ctx->GSSetConstantBuffers((uint)slot, 1, &cb);
                }
                else
                {
                    PackRangeParams(range);
                    if (!_gd.SupportsCommandLists)
                    {
                        ID3D11Buffer* nullBuf = null;
                        Ctx->GSSetConstantBuffers((uint)slot, 1, &nullBuf);
                    }
                    ID3D11Buffer* cb = _cbOut[0];
                    fixed (int* pFirstConst = _firstConstRef)
                    fixed (int* pNumConsts = _numConstsRef)
                    {
                        ((ID3D11DeviceContext1*)_context1)->GSSetConstantBuffers1((uint)slot, 1, &cb, (uint*)pFirstConst, (uint*)pNumConsts);
                    }
                }
            }
            if ((stages & ShaderStages.TessellationControl) == ShaderStages.TessellationControl)
            {
                if (range.IsFullRange)
                {
                    ID3D11Buffer* cb = range.Buffer.Buffer;
                    Ctx->HSSetConstantBuffers((uint)slot, 1, &cb);
                }
                else
                {
                    PackRangeParams(range);
                    if (!_gd.SupportsCommandLists)
                    {
                        ID3D11Buffer* nullBuf = null;
                        Ctx->HSSetConstantBuffers((uint)slot, 1, &nullBuf);
                    }
                    ID3D11Buffer* cb = _cbOut[0];
                    fixed (int* pFirstConst = _firstConstRef)
                    fixed (int* pNumConsts = _numConstsRef)
                    {
                        ((ID3D11DeviceContext1*)_context1)->HSSetConstantBuffers1((uint)slot, 1, &cb, (uint*)pFirstConst, (uint*)pNumConsts);
                    }
                }
            }
            if ((stages & ShaderStages.TessellationEvaluation) == ShaderStages.TessellationEvaluation)
            {
                if (range.IsFullRange)
                {
                    ID3D11Buffer* cb = range.Buffer.Buffer;
                    Ctx->DSSetConstantBuffers((uint)slot, 1, &cb);
                }
                else
                {
                    PackRangeParams(range);
                    if (!_gd.SupportsCommandLists)
                    {
                        ID3D11Buffer* nullBuf = null;
                        Ctx->DSSetConstantBuffers((uint)slot, 1, &nullBuf);
                    }
                    ID3D11Buffer* cb = _cbOut[0];
                    fixed (int* pFirstConst = _firstConstRef)
                    fixed (int* pNumConsts = _numConstsRef)
                    {
                        ((ID3D11DeviceContext1*)_context1)->DSSetConstantBuffers1((uint)slot, 1, &cb, (uint*)pFirstConst, (uint*)pNumConsts);
                    }
                }
            }
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
                {
                    if (range.IsFullRange)
                    {
                        ID3D11Buffer* cb = range.Buffer.Buffer;
                        Ctx->PSSetConstantBuffers((uint)slot, 1, &cb);
                    }
                    else
                    {
                        PackRangeParams(range);
                        if (!_gd.SupportsCommandLists)
                        {
                            ID3D11Buffer* nullBuf = null;
                            Ctx->PSSetConstantBuffers((uint)slot, 1, &nullBuf);
                        }
                        ID3D11Buffer* cb = _cbOut[0];
                        fixed (int* pFirstConst = _firstConstRef)
                        fixed (int* pNumConsts = _numConstsRef)
                        {
                            ((ID3D11DeviceContext1*)_context1)->PSSetConstantBuffers1((uint)slot, 1, &cb, (uint*)pFirstConst, (uint*)pNumConsts);
                        }
                    }
                }
            }
            if ((stages & ShaderStages.Compute) == ShaderStages.Compute)
            {
                if (range.IsFullRange)
                {
                    ID3D11Buffer* cb = range.Buffer.Buffer;
                    Ctx->CSSetConstantBuffers((uint)slot, 1, &cb);
                }
                else
                {
                    PackRangeParams(range);
                    if (!_gd.SupportsCommandLists)
                    {
                        ID3D11Buffer* nullBuf = null;
                        Ctx->CSSetConstantBuffers((uint)slot, 1, &nullBuf);
                    }
                    ID3D11Buffer* cb = _cbOut[0];
                    fixed (int* pFirstConst = _firstConstRef)
                    fixed (int* pNumConsts = _numConstsRef)
                    {
                        ((ID3D11DeviceContext1*)_context1)->CSSetConstantBuffers1((uint)slot, 1, &cb, (uint*)pFirstConst, (uint*)pNumConsts);
                    }
                }
            }
        }

        private void PackRangeParams(D3D11BufferRange range)
        {
            _cbOut[0] = range.Buffer.Buffer;
            _firstConstRef[0] = (int)range.Offset / 16;
            uint roundedSize = range.Size < 256 ? 256u : range.Size;
            _numConstsRef[0] = (int)roundedSize / 16;
        }

        private void BindUnorderedAccessView(
            Texture texture,
            DeviceBuffer buffer,
            ID3D11UnorderedAccessView* uav,
            int slot,
            ShaderStages stages,
            uint resourceSet)
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
                list.Add(new BoundTextureInfo { Slot = slot, Stages = stages, ResourceSet = resourceSet });
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
            if((stages & ShaderStages.Compute) == ShaderStages.Compute)
            {
                Ctx->CSSetSamplers((uint)slot, 1, &samplerPtr);
            }
        }

        private protected override void SetFramebufferCore(Framebuffer fb)
        {
            D3D11Framebuffer d3dFB = Util.AssertSubtype<Framebuffer, D3D11Framebuffer>(fb);
            if (d3dFB.Swapchain != null)
            {
                d3dFB.Swapchain.AddCommandListReference(this);
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

        private protected override void ClearColorTargetCore(uint index, RgbaFloat clearColor)
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
                Box subregion = new Box
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
            bool needWorkaround = !_gd.SupportsCommandLists;
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

            Box region = new Box
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

            Box region = new Box
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
                sc.RemoveCommandListReference(this);
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

                foreach (BoundResourceSetInfo boundGraphicsSet in _graphicsResourceSets)
                {
                    boundGraphicsSet.Offsets.Dispose();
                }
                foreach (BoundResourceSetInfo boundComputeSet in _computeResourceSets)
                {
                    boundComputeSet.Offsets.Dispose();
                }

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
            public uint ResourceSet;
        }

        private struct D3D11BufferRange : IEquatable<D3D11BufferRange>
        {
            public readonly D3D11Buffer Buffer;
            public readonly uint Offset;
            public readonly uint Size;

            public bool IsFullRange => Offset == 0 && Size == Buffer.SizeInBytes;

            public D3D11BufferRange(D3D11Buffer buffer, uint offset, uint size)
            {
                Buffer = buffer;
                Offset = offset;
                Size = size;
            }

            public bool Equals(D3D11BufferRange other)
            {
                return Buffer == other.Buffer && Offset.Equals(other.Offset) && Size.Equals(other.Size);
            }
        }

        /// <summary>
        /// A viewport struct matching D3D11_VIEWPORT layout (6 floats).
        /// Used instead of Veldrid.Viewport to match Silk.NET's Viewport struct layout exactly.
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
}
