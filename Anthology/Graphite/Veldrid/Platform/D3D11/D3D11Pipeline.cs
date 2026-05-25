using System;

using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace Prowl.Veldrid.D3D11;

internal unsafe class D3D11Pipeline : Pipeline
{
    private string _name;
    private bool _disposed;

    private readonly D3D11ShaderProgram _shaderProgram;
    private readonly D3D11ComputeProgram _computeProgram;
    private readonly D3DPrimitiveTopology _primitiveTopology;
    private readonly bool _outputMultisampled;
    private readonly bool _isCompute;

    public D3D11ShaderProgram Program => _shaderProgram;
    public D3D11ComputeProgram ComputeProgramRef => _computeProgram;

    public ID3D11BlendState* BlendState => _shaderProgram.BlendStateHandle;
    public float[] BlendFactor { get; }
    public ID3D11DepthStencilState* DepthStencilState => _shaderProgram.DepthStencilStateHandle;
    public uint StencilReference { get; }
    public ID3D11RasterizerState* RasterizerState => _shaderProgram.RasterizerStateHandle;
    public D3DPrimitiveTopology PrimitiveTopology => _primitiveTopology;
    public ID3D11InputLayout* InputLayout => _shaderProgram.InputLayout;
    public ID3D11VertexShader* VertexShader => _shaderProgram.VertexShader;
    public ID3D11GeometryShader* GeometryShader => _shaderProgram.GeometryShader;
    public ID3D11HullShader* HullShader => _shaderProgram.HullShader;
    public ID3D11DomainShader* DomainShader => _shaderProgram.DomainShader;
    public ID3D11PixelShader* PixelShader => _shaderProgram.PixelShader;
    public ID3D11ComputeShader* ComputeShader => _computeProgram.ComputeShader;
    public new D3D11ResourceLayout[] ResourceLayouts => _isCompute
        ? _computeProgram.D3D11ResourceLayouts
        : _shaderProgram.D3D11ResourceLayouts;
    public int[] VertexStrides => _isCompute
        ? Array.Empty<int>()
        : _shaderProgram.VertexStridesInts;

    public override bool IsComputePipeline => _isCompute;

    public D3D11Pipeline(ResourceFactory factory, D3D11ResourceCache cache, ref GraphicsPipelineDescription description)
        : base(factory, ref description)
    {
        _shaderProgram = Util.AssertSubtype<ShaderProgram, D3D11ShaderProgram>(description.Program);
        _isCompute = false;
        _outputMultisampled = description.Outputs.SampleCount != TextureSampleCount.Count1;
        _shaderProgram.EnsureCacheResolved(_outputMultisampled);

        var bf = _shaderProgram.BlendState.BlendFactor;
        BlendFactor = new float[] { bf.R, bf.G, bf.B, bf.A };
        StencilReference = _shaderProgram.DepthStencilState.StencilReference;
        _primitiveTopology = D3D11Formats.VdToD3D11PrimitiveTopology(description.PrimitiveTopology);
    }

    public D3D11Pipeline(ResourceFactory factory, D3D11ResourceCache cache, ref ComputePipelineDescription description)
        : base(factory, ref description)
    {
        _computeProgram = Util.AssertSubtype<ComputeProgram, D3D11ComputeProgram>(description.Program);
        _isCompute = true;
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
        DisposeAdapterResourceLayouts();
    }
}
