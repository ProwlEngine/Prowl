using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace Prowl.Veldrid.D3D11;

internal unsafe partial class D3D11Sampler : Sampler
{
    private readonly D3D11GraphicsDevice _gd;
    private ComPtr<ID3D11SamplerState> _deviceSampler;
    private string _name;
    private bool _disposed;

    public ref ComPtr<ID3D11SamplerState> DeviceSampler => ref _deviceSampler;

    public D3D11Sampler(D3D11GraphicsDevice gd, ref SamplerDescription description)
    {
        _gd = gd;
        ID3D11Device* device = gd.Device;
        ComparisonFunc comparison = description.ComparisonKind == null
            ? ComparisonFunc.Never
            : D3D11Formats.VdToD3D11ComparisonFunc(description.ComparisonKind.Value);

        SamplerDesc samplerStateDesc = new()
        {
            AddressU = D3D11Formats.VdToD3D11AddressMode(description.AddressModeU),
            AddressV = D3D11Formats.VdToD3D11AddressMode(description.AddressModeV),
            AddressW = D3D11Formats.VdToD3D11AddressMode(description.AddressModeW),
            Filter = D3D11Formats.ToD3D11Filter(description.Filter, description.ComparisonKind.HasValue),
            MinLOD = description.MinimumLod,
            MaxLOD = description.MaximumLod,
            MaxAnisotropy = (uint)description.MaximumAnisotropy,
            ComparisonFunc = comparison,
            MipLODBias = description.LodBias,
        };

        SetBorderColor(ref samplerStateDesc, description.BorderColor);

        ID3D11SamplerState* pSampler;
        SilkMarshal.ThrowHResult(device->CreateSamplerState(in samplerStateDesc, &pSampler));
        _deviceSampler = default;
        _deviceSampler.Handle = pSampler;

        _gd.RecordAllocation(AllocBin.Sampler, 0);
    }

    private static void SetBorderColor(ref SamplerDesc desc, SamplerBorderColor borderColor)
    {
        float r, g, b, a;
        switch (borderColor)
        {
            case SamplerBorderColor.TransparentBlack:
                r = g = b = a = 0f;
                break;
            case SamplerBorderColor.OpaqueBlack:
                r = g = b = 0f;
                a = 1f;
                break;
            case SamplerBorderColor.OpaqueWhite:
                r = g = b = a = 1f;
                break;
            default:
                throw Illegal.Value<SamplerBorderColor>();
        }

        desc.BorderColor[0] = r;
        desc.BorderColor[1] = g;
        desc.BorderColor[2] = b;
        desc.BorderColor[3] = a;
    }

    public override string Name
    {
        get => _name;
        set
        {
            _name = value;
            D3D11Util.SetDebugName((ID3D11DeviceChild*)_deviceSampler.Handle, value);
        }
    }

    public override bool IsDisposed => _disposed;

    public override void Dispose()
    {
        if (!_disposed)
        {
            _deviceSampler.Dispose();
            _disposed = true;
            _gd.RecordFree(AllocBin.Sampler, 0);
        }
    }
}
