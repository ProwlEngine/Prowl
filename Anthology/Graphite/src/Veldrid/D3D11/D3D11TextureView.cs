using System;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace Veldrid.D3D11
{
    internal unsafe class D3D11TextureView : TextureView
    {
        private string _name;
        private bool _disposed;

        private ComPtr<ID3D11ShaderResourceView> _shaderResourceView;
        private ComPtr<ID3D11UnorderedAccessView> _unorderedAccessView;

        public ID3D11ShaderResourceView* ShaderResourceView => _shaderResourceView;
        public ID3D11UnorderedAccessView* UnorderedAccessView => _unorderedAccessView;

        public D3D11TextureView(D3D11GraphicsDevice gd, ref TextureViewDescription description)
            : base(ref description)
        {
            ID3D11Device* device = gd.Device;
            D3D11Texture d3dTex = Util.AssertSubtype<Texture, D3D11Texture>(description.Target);
            ShaderResourceViewDesc srvDesc = D3D11Util.GetSrvDesc(
                d3dTex,
                description.BaseMipLevel,
                description.MipLevels,
                description.BaseArrayLayer,
                description.ArrayLayers,
                Format);
            ID3D11ShaderResourceView* pSrv;
            SilkMarshal.ThrowHResult(device->CreateShaderResourceView(d3dTex.DeviceTexture, in srvDesc, &pSrv));
            _shaderResourceView = default;
            _shaderResourceView.Handle = pSrv;

            if ((d3dTex.Usage & TextureUsage.Storage) == TextureUsage.Storage)
            {
                UnorderedAccessViewDesc uavDesc = new UnorderedAccessViewDesc();
                uavDesc.Format = D3D11Formats.GetViewFormat(d3dTex.DxgiFormat);

                if ((d3dTex.Usage & TextureUsage.Cubemap) == TextureUsage.Cubemap)
                {
                    throw new NotSupportedException();
                }
                else if (d3dTex.Depth == 1)
                {
                    if (d3dTex.ArrayLayers == 1)
                    {
                        if (d3dTex.Type == TextureType.Texture1D)
                        {
                            uavDesc.ViewDimension = UavDimension.Texture1D;
                            uavDesc.Texture1D.MipSlice = description.BaseMipLevel;
                        }
                        else
                        {
                            uavDesc.ViewDimension = UavDimension.Texture2D;
                            uavDesc.Texture2D.MipSlice = description.BaseMipLevel;
                        }
                    }
                    else
                    {
                        if (d3dTex.Type == TextureType.Texture1D)
                        {
                            uavDesc.ViewDimension = UavDimension.Texture1Darray;
                            uavDesc.Texture1DArray.MipSlice = description.BaseMipLevel;
                            uavDesc.Texture1DArray.FirstArraySlice = description.BaseArrayLayer;
                            uavDesc.Texture1DArray.ArraySize = description.ArrayLayers;
                        }
                        else
                        {
                            uavDesc.ViewDimension = UavDimension.Texture2Darray;
                            uavDesc.Texture2DArray.MipSlice = description.BaseMipLevel;
                            uavDesc.Texture2DArray.FirstArraySlice = description.BaseArrayLayer;
                            uavDesc.Texture2DArray.ArraySize = description.ArrayLayers;
                        }
                    }
                }
                else
                {
                    uavDesc.ViewDimension = UavDimension.Texture3D;
                    uavDesc.Texture3D.MipSlice = description.BaseMipLevel;

                    // Map the entire range of the 3D texture.
                    uavDesc.Texture3D.FirstWSlice = 0;
                    uavDesc.Texture3D.WSize = d3dTex.Depth;
                }

                ID3D11UnorderedAccessView* pUav;
                SilkMarshal.ThrowHResult(device->CreateUnorderedAccessView(d3dTex.DeviceTexture, in uavDesc, &pUav));
                _unorderedAccessView = default;
                _unorderedAccessView.Handle = pUav;
            }
        }

        public override string Name
        {
            get => _name;
            set
            {
                _name = value;
                if (_shaderResourceView.Handle != null)
                    D3D11Util.SetDebugName((ID3D11DeviceChild*)_shaderResourceView.Handle, value + "_SRV");
                if (_unorderedAccessView.Handle != null)
                    D3D11Util.SetDebugName((ID3D11DeviceChild*)_unorderedAccessView.Handle, value + "_UAV");
            }
        }

        public override bool IsDisposed => _disposed;

        public override void Dispose()
        {
            if (!_disposed)
            {
                _shaderResourceView.Dispose();
                _unorderedAccessView.Dispose();
                _disposed = true;
            }
        }
    }
}
