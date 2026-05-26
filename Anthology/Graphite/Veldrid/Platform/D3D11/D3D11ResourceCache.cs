using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

using Silk.NET.Direct3D11;
using Silk.NET.Core.Native;

namespace Prowl.Veldrid.D3D11;

internal unsafe class D3D11ResourceCache : IDisposable
{
    private readonly ID3D11Device* _device;
    private readonly object _lock = new object();

    private struct BlendEntry { public ComPtr<ID3D11BlendState> Handle; public int RefCount; }
    private struct DepthStencilEntry { public ComPtr<ID3D11DepthStencilState> Handle; public int RefCount; }
    private struct RasterizerEntry { public ComPtr<ID3D11RasterizerState> Handle; public int RefCount; }
    private struct InputLayoutEntry { public ComPtr<ID3D11InputLayout> Handle; public int RefCount; }

    private readonly Dictionary<BlendStateDescription, BlendEntry> _blendStates
        = new Dictionary<BlendStateDescription, BlendEntry>();

    private readonly Dictionary<DepthStencilStateDescription, DepthStencilEntry> _depthStencilStates
        = new Dictionary<DepthStencilStateDescription, DepthStencilEntry>();

    private readonly Dictionary<D3D11RasterizerStateCacheKey, RasterizerEntry> _rasterizerStates
        = new Dictionary<D3D11RasterizerStateCacheKey, RasterizerEntry>();

    private readonly Dictionary<InputLayoutCacheKey, InputLayoutEntry> _inputLayouts
        = new Dictionary<InputLayoutCacheKey, InputLayoutEntry>();

    public D3D11ResourceCache(ID3D11Device* device)
    {
        _device = device;
    }

    public void GetPipelineResources(
        ref BlendStateDescription blendDesc,
        ref DepthStencilStateDescription dssDesc,
        ref RasterizerStateDescription rasterDesc,
        bool multisample,
        VertexLayoutDescription[] vertexLayouts,
        byte[] vsBytecode,
        out ID3D11BlendState* blendState,
        out ID3D11DepthStencilState* depthState,
        out ID3D11RasterizerState* rasterState,
        out ID3D11InputLayout* inputLayout)
    {
        lock (_lock)
        {
            blendState = AcquireBlendState(ref blendDesc);
            depthState = AcquireDepthStencilState(ref dssDesc);
            rasterState = AcquireRasterizerState(ref rasterDesc, multisample);
            inputLayout = AcquireInputLayout(vertexLayouts, vsBytecode);
        }
    }

    public void ReleasePipelineResources(
        ref BlendStateDescription blendDesc,
        ref DepthStencilStateDescription dssDesc,
        ref RasterizerStateDescription rasterDesc,
        bool multisampled,
        VertexLayoutDescription[] vertexLayouts)
    {
        lock (_lock)
        {
            ReleaseBlendState(ref blendDesc);
            ReleaseDepthStencilState(ref dssDesc);
            ReleaseRasterizerState(ref rasterDesc, multisampled);
            ReleaseInputLayout(vertexLayouts);
        }
    }

    private ID3D11BlendState* AcquireBlendState(ref BlendStateDescription description)
    {
        Debug.Assert(Monitor.IsEntered(_lock));
        if (!_blendStates.TryGetValue(description, out BlendEntry entry))
        {
            entry.Handle = CreateNewBlendState(ref description);
            entry.RefCount = 1;
            BlendStateDescription key = description;
            key.AttachmentStates = (BlendAttachmentDescription[])key.AttachmentStates.Clone();
            _blendStates.Add(key, entry);
        }
        else
        {
            entry.RefCount += 1;
            _blendStates[description] = entry;
        }
        return entry.Handle;
    }

    private void ReleaseBlendState(ref BlendStateDescription description)
    {
        Debug.Assert(Monitor.IsEntered(_lock));
        if (!_blendStates.TryGetValue(description, out BlendEntry entry))
        {
            throw new RenderException("ReleasePipelineResources: no matching cached BlendState entry.");
        }
        entry.RefCount -= 1;
        if (entry.RefCount <= 0)
        {
            entry.Handle.Dispose();
            _blendStates.Remove(description);
        }
        else
        {
            _blendStates[description] = entry;
        }
    }

    private ComPtr<ID3D11BlendState> CreateNewBlendState(ref BlendStateDescription description)
    {
        BlendAttachmentDescription[] attachmentStates = description.AttachmentStates;
        BlendDesc d3dBlendStateDesc = new BlendDesc();

        for (int i = 0; i < attachmentStates.Length; i++)
        {
            BlendAttachmentDescription state = attachmentStates[i];
            d3dBlendStateDesc.RenderTarget[i].BlendEnable = state.BlendEnabled;
            d3dBlendStateDesc.RenderTarget[i].RenderTargetWriteMask = (byte)D3D11Formats.VdToD3D11ColorWriteEnable(state.ColorWriteMask.GetOrDefault());
            d3dBlendStateDesc.RenderTarget[i].SrcBlend = D3D11Formats.VdToD3D11Blend(state.SourceColorFactor);
            d3dBlendStateDesc.RenderTarget[i].DestBlend = D3D11Formats.VdToD3D11Blend(state.DestinationColorFactor);
            d3dBlendStateDesc.RenderTarget[i].BlendOp = D3D11Formats.VdToD3D11BlendOperation(state.ColorFunction);
            d3dBlendStateDesc.RenderTarget[i].SrcBlendAlpha = D3D11Formats.VdToD3D11Blend(state.SourceAlphaFactor);
            d3dBlendStateDesc.RenderTarget[i].DestBlendAlpha = D3D11Formats.VdToD3D11Blend(state.DestinationAlphaFactor);
            d3dBlendStateDesc.RenderTarget[i].BlendOpAlpha = D3D11Formats.VdToD3D11BlendOperation(state.AlphaFunction);
        }

        d3dBlendStateDesc.AlphaToCoverageEnable = description.AlphaToCoverageEnabled;
        d3dBlendStateDesc.IndependentBlendEnable = true;

        ID3D11BlendState* pBlendState;
        SilkMarshal.ThrowHResult(_device->CreateBlendState(in d3dBlendStateDesc, &pBlendState));
        ComPtr<ID3D11BlendState> result = default;
        result.Handle = pBlendState;
        return result;
    }

    private ID3D11DepthStencilState* AcquireDepthStencilState(ref DepthStencilStateDescription description)
    {
        Debug.Assert(Monitor.IsEntered(_lock));
        if (!_depthStencilStates.TryGetValue(description, out DepthStencilEntry entry))
        {
            entry.Handle = CreateNewDepthStencilState(ref description);
            entry.RefCount = 1;
            _depthStencilStates.Add(description, entry);
        }
        else
        {
            entry.RefCount += 1;
            _depthStencilStates[description] = entry;
        }
        return entry.Handle;
    }

    private void ReleaseDepthStencilState(ref DepthStencilStateDescription description)
    {
        Debug.Assert(Monitor.IsEntered(_lock));
        if (!_depthStencilStates.TryGetValue(description, out DepthStencilEntry entry))
        {
            throw new RenderException("ReleasePipelineResources: no matching cached DepthStencilState entry.");
        }
        entry.RefCount -= 1;
        if (entry.RefCount <= 0)
        {
            entry.Handle.Dispose();
            _depthStencilStates.Remove(description);
        }
        else
        {
            _depthStencilStates[description] = entry;
        }
    }

    private ComPtr<ID3D11DepthStencilState> CreateNewDepthStencilState(ref DepthStencilStateDescription description)
    {
        DepthStencilDesc dssDesc = new DepthStencilDesc
        {
            DepthFunc = D3D11Formats.VdToD3D11ComparisonFunc(description.DepthComparison),
            DepthEnable = description.DepthTestEnabled,
            DepthWriteMask = description.DepthWriteEnabled ? DepthWriteMask.All : DepthWriteMask.Zero,
            StencilEnable = description.StencilTestEnabled,
            FrontFace = ToD3D11StencilOpDesc(description.StencilFront),
            BackFace = ToD3D11StencilOpDesc(description.StencilBack),
            StencilReadMask = description.StencilReadMask,
            StencilWriteMask = description.StencilWriteMask,
        };

        ID3D11DepthStencilState* pDepthStencilState;
        SilkMarshal.ThrowHResult(_device->CreateDepthStencilState(in dssDesc, &pDepthStencilState));
        ComPtr<ID3D11DepthStencilState> result = default;
        result.Handle = pDepthStencilState;
        return result;
    }

    private DepthStencilopDesc ToD3D11StencilOpDesc(StencilBehaviorDescription sbd)
    {
        return new DepthStencilopDesc
        {
            StencilFunc = D3D11Formats.VdToD3D11ComparisonFunc(sbd.Comparison),
            StencilPassOp = D3D11Formats.VdToD3D11StencilOperation(sbd.Pass),
            StencilFailOp = D3D11Formats.VdToD3D11StencilOperation(sbd.Fail),
            StencilDepthFailOp = D3D11Formats.VdToD3D11StencilOperation(sbd.DepthFail),
        };
    }

    private ID3D11RasterizerState* AcquireRasterizerState(ref RasterizerStateDescription description, bool multisample)
    {
        Debug.Assert(Monitor.IsEntered(_lock));
        D3D11RasterizerStateCacheKey key = new D3D11RasterizerStateCacheKey(description, multisample);
        if (!_rasterizerStates.TryGetValue(key, out RasterizerEntry entry))
        {
            entry.Handle = CreateNewRasterizerState(ref key);
            entry.RefCount = 1;
            _rasterizerStates.Add(key, entry);
        }
        else
        {
            entry.RefCount += 1;
            _rasterizerStates[key] = entry;
        }
        return entry.Handle;
    }

    private void ReleaseRasterizerState(ref RasterizerStateDescription description, bool multisample)
    {
        Debug.Assert(Monitor.IsEntered(_lock));
        D3D11RasterizerStateCacheKey key = new D3D11RasterizerStateCacheKey(description, multisample);
        if (!_rasterizerStates.TryGetValue(key, out RasterizerEntry entry))
        {
            throw new RenderException("ReleasePipelineResources: no matching cached RasterizerState entry.");
        }
        entry.RefCount -= 1;
        if (entry.RefCount <= 0)
        {
            entry.Handle.Dispose();
            _rasterizerStates.Remove(key);
        }
        else
        {
            _rasterizerStates[key] = entry;
        }
    }

    private ComPtr<ID3D11RasterizerState> CreateNewRasterizerState(ref D3D11RasterizerStateCacheKey key)
    {
        RasterizerDesc rssDesc = new RasterizerDesc
        {
            CullMode = D3D11Formats.VdToD3D11CullMode(key.VeldridDescription.CullMode),
            FillMode = FillMode.Solid,
            DepthClipEnable = key.VeldridDescription.DepthClipEnabled,
            ScissorEnable = key.VeldridDescription.ScissorTestEnabled,
            FrontCounterClockwise = key.VeldridDescription.FrontFace == FrontFace.CounterClockwise,
            MultisampleEnable = key.Multisampled,
        };

        ID3D11RasterizerState* pRasterizerState;
        SilkMarshal.ThrowHResult(_device->CreateRasterizerState(in rssDesc, &pRasterizerState));
        ComPtr<ID3D11RasterizerState> result = default;
        result.Handle = pRasterizerState;
        return result;
    }

    // Latent: cache key is VertexLayoutDescription[] only. Two programs with identical vertex
    // layouts but incompatible VS input signatures will share an input layout and the second
    // draw will fail D3D11 validation. Deferred per Stage 1 D3D11 doc ("Input-layout cache key").
    private ID3D11InputLayout* AcquireInputLayout(VertexLayoutDescription[] vertexLayouts, byte[] vsBytecode)
    {
        Debug.Assert(Monitor.IsEntered(_lock));

        if (vsBytecode == null || vertexLayouts == null || vertexLayouts.Length == 0) { return null; }

        InputLayoutCacheKey tempKey = InputLayoutCacheKey.CreateTempKey(vertexLayouts);
        if (!_inputLayouts.TryGetValue(tempKey, out InputLayoutEntry entry))
        {
            entry.Handle = CreateNewInputLayout(vertexLayouts, vsBytecode);
            entry.RefCount = 1;
            InputLayoutCacheKey permanentKey = InputLayoutCacheKey.CreatePermanentKey(vertexLayouts);
            _inputLayouts.Add(permanentKey, entry);
        }
        else
        {
            entry.RefCount += 1;
            _inputLayouts[tempKey] = entry;
        }
        return entry.Handle;
    }

    private void ReleaseInputLayout(VertexLayoutDescription[] vertexLayouts)
    {
        Debug.Assert(Monitor.IsEntered(_lock));
        if (vertexLayouts == null || vertexLayouts.Length == 0) { return; }
        InputLayoutCacheKey tempKey = InputLayoutCacheKey.CreateTempKey(vertexLayouts);
        if (!_inputLayouts.TryGetValue(tempKey, out InputLayoutEntry entry))
        {
            throw new RenderException("ReleasePipelineResources: no matching cached InputLayout entry.");
        }
        entry.RefCount -= 1;
        if (entry.RefCount <= 0)
        {
            entry.Handle.Dispose();
            _inputLayouts.Remove(tempKey);
        }
        else
        {
            _inputLayouts[tempKey] = entry;
        }
    }

    private ComPtr<ID3D11InputLayout> CreateNewInputLayout(VertexLayoutDescription[] vertexLayouts, byte[] vsBytecode)
    {
        int totalCount = 0;
        for (int i = 0; i < vertexLayouts.Length; i++)
        {
            totalCount += vertexLayouts[i].Elements.Length;
        }

        int element = 0; // Total element index across slots.
        InputElementDesc[] elements = new InputElementDesc[totalCount];
        nint[] semanticPtrs = new nint[totalCount];
        for (int slot = 0; slot < vertexLayouts.Length; slot++)
        {
            VertexElementDescription[] elementDescs = vertexLayouts[slot].Elements;
            uint stepRate = vertexLayouts[slot].InstanceStepRate;
            int currentOffset = 0;
            for (int i = 0; i < elementDescs.Length; i++)
            {
                VertexElementDescription desc = elementDescs[i];
                string semanticName = VertexAttributeID.ToString(desc.Name)
                    ?? throw new RenderException("Vertex attribute name was not interned.");
                semanticPtrs[element] = SilkMarshal.StringToPtr(semanticName);
                elements[element] = new InputElementDesc
                {
                    SemanticName = (byte*)semanticPtrs[element],
                    SemanticIndex = 0,
                    Format = D3D11Formats.ToDxgiFormat(desc.Format),
                    AlignedByteOffset = desc.Offset != 0 ? desc.Offset : (uint)currentOffset,
                    // InputSlot matches the IASetVertexBuffers slot, which is the layout
                    // index (what IVertexSource.ResolveSlot's layoutSlot receives). D3D11
                    // binds attributes by SemanticName, so VertexLayoutDescription.Location
                    // has no shader effect here.
                    InputSlot = (uint)slot,
                    InputSlotClass = stepRate == 0 ? InputClassification.PerVertexData : InputClassification.PerInstanceData,
                    InstanceDataStepRate = stepRate,
                };

                currentOffset += (int)FormatSizeHelpers.GetSizeInBytes(desc.Format);
                element += 1;
            }
        }

        try
        {
            fixed (InputElementDesc* pElements = elements)
            fixed (byte* pBytecode = vsBytecode)
            {
                ID3D11InputLayout* pInputLayout;
                SilkMarshal.ThrowHResult(_device->CreateInputLayout(pElements, (uint)elements.Length, pBytecode, (nuint)vsBytecode.Length, &pInputLayout));
                ComPtr<ID3D11InputLayout> result = default;
                result.Handle = pInputLayout;
                return result;
            }
        }
        finally
        {
            foreach (nint ptr in semanticPtrs)
            {
                SilkMarshal.Free(ptr);
            }
        }
    }

    public void Dispose()
    {
        foreach (KeyValuePair<BlendStateDescription, BlendEntry> kvp in _blendStates)
        {
            Debug.Assert(kvp.Value.RefCount == 0, $"D3D11ResourceCache: blend entry leaked with RefCount={kvp.Value.RefCount}.");
            kvp.Value.Handle.Dispose();
        }
        foreach (KeyValuePair<DepthStencilStateDescription, DepthStencilEntry> kvp in _depthStencilStates)
        {
            Debug.Assert(kvp.Value.RefCount == 0, $"D3D11ResourceCache: DSS entry leaked with RefCount={kvp.Value.RefCount}.");
            kvp.Value.Handle.Dispose();
        }
        foreach (KeyValuePair<D3D11RasterizerStateCacheKey, RasterizerEntry> kvp in _rasterizerStates)
        {
            Debug.Assert(kvp.Value.RefCount == 0, $"D3D11ResourceCache: raster entry leaked with RefCount={kvp.Value.RefCount}.");
            kvp.Value.Handle.Dispose();
        }
        foreach (KeyValuePair<InputLayoutCacheKey, InputLayoutEntry> kvp in _inputLayouts)
        {
            Debug.Assert(kvp.Value.RefCount == 0, $"D3D11ResourceCache: input layout entry leaked with RefCount={kvp.Value.RefCount}.");
            kvp.Value.Handle.Dispose();
        }
    }

    private struct InputLayoutCacheKey : IEquatable<InputLayoutCacheKey>
    {
        public VertexLayoutDescription[] VertexLayouts;

        public static InputLayoutCacheKey CreateTempKey(VertexLayoutDescription[] original)
            => new InputLayoutCacheKey { VertexLayouts = original };

        public static InputLayoutCacheKey CreatePermanentKey(VertexLayoutDescription[] original)
        {
            VertexLayoutDescription[] vertexLayouts = new VertexLayoutDescription[original.Length];
            for (int i = 0; i < original.Length; i++)
            {
                vertexLayouts[i].Stride = original[i].Stride;
                vertexLayouts[i].InstanceStepRate = original[i].InstanceStepRate;
                vertexLayouts[i].Elements = (VertexElementDescription[])original[i].Elements.Clone();
            }

            return new InputLayoutCacheKey { VertexLayouts = vertexLayouts };
        }

        public bool Equals(InputLayoutCacheKey other)
        {
            return Util.ArrayEqualsEquatable(VertexLayouts, other.VertexLayouts);
        }

        public override int GetHashCode()
        {
            return VertexLayouts.ArrayHash();
        }
    }

    private struct D3D11RasterizerStateCacheKey : IEquatable<D3D11RasterizerStateCacheKey>
    {
        public RasterizerStateDescription VeldridDescription;
        public bool Multisampled;

        public D3D11RasterizerStateCacheKey(RasterizerStateDescription veldridDescription, bool multisampled)
        {
            VeldridDescription = veldridDescription;
            Multisampled = multisampled;
        }

        public bool Equals(D3D11RasterizerStateCacheKey other)
        {
            return VeldridDescription.Equals(other.VeldridDescription)
                && Multisampled.Equals(other.Multisampled);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(VeldridDescription.GetHashCode(), Multisampled.GetHashCode());
        }
    }
}
