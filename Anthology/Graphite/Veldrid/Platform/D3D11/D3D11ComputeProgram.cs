using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace Prowl.Veldrid.D3D11;

internal unsafe partial class D3D11ComputeProgram : ComputeProgram
{
    private readonly D3D11GraphicsDevice _gd;
    private string _name;
    private bool _disposed;

    public byte[] ComputeBytecode { get; }
    public ID3D11ComputeShader* ComputeShader { get; }
    private ComPtr<ID3D11DeviceChild> _shaderHandle;

    public D3D11ComputeProgram(D3D11GraphicsDevice gd, ref ComputeDescription description)
        : base(ref description)
    {
        _gd = gd;
        ID3D11Device* device = gd.Device;
        ShaderStageDescription stage = description.Stage;
        ComputeBytecode = D3D11GraphicsProgram.GetOrCompileBytecode(ref stage);

        fixed (byte* pBytecode = ComputeBytecode)
        {
            ID3D11ComputeShader* p;
            SilkMarshal.ThrowHResult(device->CreateComputeShader(pBytecode, (nuint)ComputeBytecode.Length, null, &p));
            ComputeShader = p;
            _shaderHandle = default;
            _shaderHandle.Handle = (ID3D11DeviceChild*)p;
        }

        _gd.RecordAllocation(AllocBin.Shader, ComputeBytecode.Length);
    }

    public override string Name { get => _name; set => _name = value; }
    public override bool IsDisposed => _disposed;

    public override void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shaderHandle.Dispose();
        _gd.RecordFree(AllocBin.Shader, ComputeBytecode.Length);
    }
}
