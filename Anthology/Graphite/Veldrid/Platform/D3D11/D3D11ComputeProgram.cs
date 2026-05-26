using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace Prowl.Veldrid.D3D11;

internal unsafe class D3D11ComputeProgram : ComputeProgram
{
    private string _name;
    private bool _disposed;

    public byte[] ComputeBytecode { get; }
    public ID3D11ComputeShader* ComputeShader { get; }
    private ComPtr<ID3D11DeviceChild> _shaderHandle;

    public D3D11ComputeProgram(ID3D11Device* device, ref ComputeDescription description)
        : base(ref description)
    {
        ShaderStageDescription stage = description.Stage;
        ComputeBytecode = D3D11ShaderProgram.GetOrCompileBytecode(ref stage);

        fixed (byte* pBytecode = ComputeBytecode)
        {
            ID3D11ComputeShader* p;
            SilkMarshal.ThrowHResult(device->CreateComputeShader(pBytecode, (nuint)ComputeBytecode.Length, null, &p));
            ComputeShader = p;
            _shaderHandle = default;
            _shaderHandle.Handle = (ID3D11DeviceChild*)p;
        }

    }

    public override string Name { get => _name; set => _name = value; }
    public override bool IsDisposed => _disposed;

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shaderHandle.Dispose();
    }
}
