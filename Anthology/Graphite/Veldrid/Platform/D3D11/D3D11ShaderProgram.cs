using System;
using System.Text;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.Direct3D11;

namespace Prowl.Veldrid.D3D11;

internal unsafe class D3D11ShaderProgram : ShaderProgram
{
    private readonly ID3D11Device* _device;
    private readonly D3D11ResourceCache _cache;

    private string _name;
    private bool _disposed;

    public byte[] VertexBytecode { get; private set; }
    public ID3D11VertexShader* VertexShader { get; private set; }
    public ID3D11GeometryShader* GeometryShader { get; private set; }
    public ID3D11HullShader* HullShader { get; private set; }
    public ID3D11DomainShader* DomainShader { get; private set; }
    public ID3D11PixelShader* PixelShader { get; private set; }

    private ComPtr<ID3D11DeviceChild>[] _stageHandles;

    public int[] VertexStridesInts { get; }

    public ID3D11BlendState* BlendStateHandle { get; private set; }
    public ID3D11DepthStencilState* DepthStencilStateHandle { get; private set; }
    public ID3D11RasterizerState* RasterizerStateHandle { get; private set; }
    public ID3D11InputLayout* InputLayout { get; private set; }
    private bool _cacheResolved;
    private bool _resolvedMultisampled;

    public D3D11ShaderProgram(ID3D11Device* device, D3D11ResourceCache cache, ref ShaderDescription description)
        : base(ref description)
    {
        _device = device;
        _cache = cache;

        ShaderStageDescription[] stages = description.Stages;
        _stageHandles = new ComPtr<ID3D11DeviceChild>[stages.Length];
        for (int i = 0; i < stages.Length; i++)
        {
            byte[] bytecode = GetOrCompileBytecode(ref stages[i]);
            CreateStageShader(stages[i].Stage, bytecode, i);
        }

        VertexStridesInts = new int[VertexLayoutsArray.Length];
        for (int i = 0; i < VertexLayoutsArray.Length; i++)
        {
            VertexStridesInts[i] = (int)VertexLayoutsArray[i].Stride;
        }
    }

    private void CreateStageShader(ShaderStages stage, byte[] bytecode, int idx)
    {
        fixed (byte* pBytecode = bytecode)
        {
            ComPtr<ID3D11DeviceChild> handle = default;
            switch (stage)
            {
                case ShaderStages.Vertex:
                    {
                        ID3D11VertexShader* p;
                        SilkMarshal.ThrowHResult(_device->CreateVertexShader(pBytecode, (nuint)bytecode.Length, null, &p));
                        VertexShader = p; VertexBytecode = bytecode;
                        handle.Handle = (ID3D11DeviceChild*)p;
                        break;
                    }
                case ShaderStages.Geometry:
                    {
                        ID3D11GeometryShader* p;
                        SilkMarshal.ThrowHResult(_device->CreateGeometryShader(pBytecode, (nuint)bytecode.Length, null, &p));
                        GeometryShader = p;
                        handle.Handle = (ID3D11DeviceChild*)p;
                        break;
                    }
                case ShaderStages.TessellationControl:
                    {
                        ID3D11HullShader* p;
                        SilkMarshal.ThrowHResult(_device->CreateHullShader(pBytecode, (nuint)bytecode.Length, null, &p));
                        HullShader = p;
                        handle.Handle = (ID3D11DeviceChild*)p;
                        break;
                    }
                case ShaderStages.TessellationEvaluation:
                    {
                        ID3D11DomainShader* p;
                        SilkMarshal.ThrowHResult(_device->CreateDomainShader(pBytecode, (nuint)bytecode.Length, null, &p));
                        DomainShader = p;
                        handle.Handle = (ID3D11DeviceChild*)p;
                        break;
                    }
                case ShaderStages.Fragment:
                    {
                        ID3D11PixelShader* p;
                        SilkMarshal.ThrowHResult(_device->CreatePixelShader(pBytecode, (nuint)bytecode.Length, null, &p));
                        PixelShader = p;
                        handle.Handle = (ID3D11DeviceChild*)p;
                        break;
                    }
                default:
                    throw new RenderException($"Stage {stage} is not valid for a graphics ShaderProgram.");
            }
            _stageHandles[idx] = handle;
        }
    }

    public void EnsureCacheResolved(bool outputIsMultisampled)
    {
        if (_cacheResolved && _resolvedMultisampled == outputIsMultisampled)
        {
            return;
        }

        if (_cacheResolved)
        {
            // MSAA flag changed: release the previously-resolved handles before re-resolving
            // so refcounts stay balanced.
            BlendStateDescription prevBs = BlendState;
            DepthStencilStateDescription prevDss = DepthStencilState;
            RasterizerStateDescription prevRs = RasterizerState;
            _cache.ReleasePipelineResources(
                ref prevBs,
                ref prevDss,
                ref prevRs,
                _resolvedMultisampled,
                VertexLayoutsArray);
            BlendStateHandle = null;
            DepthStencilStateHandle = null;
            RasterizerStateHandle = null;
            InputLayout = null;
            _cacheResolved = false;
        }

        BlendStateDescription bs = BlendState;
        DepthStencilStateDescription dss = DepthStencilState;
        RasterizerStateDescription rs = RasterizerState;
        _cache.GetPipelineResources(
            ref bs,
            ref dss,
            ref rs,
            outputIsMultisampled,
            VertexLayoutsArray,
            VertexBytecode,
            out ID3D11BlendState* blendState,
            out ID3D11DepthStencilState* depthStencilState,
            out ID3D11RasterizerState* rasterizerState,
            out ID3D11InputLayout* inputLayout);

        BlendStateHandle = blendState;
        DepthStencilStateHandle = depthStencilState;
        RasterizerStateHandle = rasterizerState;
        InputLayout = inputLayout;
        _cacheResolved = true;
        _resolvedMultisampled = outputIsMultisampled;
    }

    internal static byte[] GetOrCompileBytecode(ref ShaderStageDescription description)
    {
        byte[] shaderBytes = description.ShaderBytes;
        if (shaderBytes.Length > 4
            && shaderBytes[0] == 0x44
            && shaderBytes[1] == 0x58
            && shaderBytes[2] == 0x42
            && shaderBytes[3] == 0x43)
        {
            return Util.ShallowClone(shaderBytes);
        }
        return CompileCode(ref description);
    }

    internal static byte[] CompileCode(ref ShaderStageDescription description)
    {
        string profile = description.Stage switch
        {
            ShaderStages.Vertex => "vs_5_0",
            ShaderStages.Geometry => "gs_5_0",
            ShaderStages.TessellationControl => "hs_5_0",
            ShaderStages.TessellationEvaluation => "ds_5_0",
            ShaderStages.Fragment => "ps_5_0",
            ShaderStages.Compute => "cs_5_0",
            _ => throw Illegal.Value<ShaderStages>()
        };

        uint flags = description.Debug ? 0x1u : 0x8000u;
        D3DCompiler compiler = D3DCompiler.GetApi();
        ID3D10Blob* pResult = null;
        ID3D10Blob* pError = null;

        fixed (byte* pSource = description.ShaderBytes)
        {
            nint pEntryPoint = SilkMarshal.StringToPtr(description.EntryPoint);
            nint pProfile = SilkMarshal.StringToPtr(profile);

            int hr = compiler.Compile(
                pSource, (nuint)description.ShaderBytes.Length,
                (byte*)null, null, null,
                (byte*)pEntryPoint, (byte*)pProfile,
                flags, 0,
                &pResult, &pError);

            SilkMarshal.Free(pEntryPoint);
            SilkMarshal.Free(pProfile);

            if (pResult == null)
            {
                string errorMsg = string.Empty;
                if (pError != null)
                {
                    byte* errorPtr = (byte*)pError->GetBufferPointer();
                    nuint errorSize = pError->GetBufferSize();
                    errorMsg = Encoding.ASCII.GetString(errorPtr, (int)errorSize);
                    pError->Release();
                }
                throw new RenderException($"Failed to compile HLSL code: {errorMsg}");
            }

            byte* resultPtr = (byte*)pResult->GetBufferPointer();
            nuint resultSize = pResult->GetBufferSize();
            byte[] bytecode = new byte[resultSize];
            new Span<byte>(resultPtr, (int)resultSize).CopyTo(bytecode);

            if (pError != null) pError->Release();
            pResult->Release();
            return bytecode;
        }
    }

    public override string Name
    {
        get => _name;
        set => _name = value;
    }

    public override bool IsDisposed => _disposed;

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_cacheResolved)
        {
            BlendStateDescription bs = BlendState;
            DepthStencilStateDescription dss = DepthStencilState;
            RasterizerStateDescription rs = RasterizerState;
            _cache.ReleasePipelineResources(
                ref bs,
                ref dss,
                ref rs,
                _resolvedMultisampled,
                VertexLayoutsArray);
            _cacheResolved = false;
        }
        if (_stageHandles != null)
        {
            for (int i = 0; i < _stageHandles.Length; i++)
            {
                _stageHandles[i].Dispose();
            }
        }
    }
}
