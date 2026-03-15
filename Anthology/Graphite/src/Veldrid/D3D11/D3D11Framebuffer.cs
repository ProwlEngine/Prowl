using System;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace Veldrid.D3D11
{
    internal unsafe class D3D11Framebuffer : Framebuffer
    {
        private string _name;
        private bool _disposed;
        private ComPtr<ID3D11DepthStencilView> _depthStencilView;

        public ComPtr<ID3D11RenderTargetView>[] RenderTargetViews { get; }
        public ID3D11DepthStencilView* DepthStencilView => _depthStencilView;

        // Only non-null if this is the Framebuffer for a Swapchain.
        internal D3D11Swapchain Swapchain { get; set; }

        public override bool IsDisposed => _disposed;

        public D3D11Framebuffer(ID3D11Device* device, ref FramebufferDescription description)
            : base(description.DepthTarget, description.ColorTargets)
        {
            if (description.DepthTarget != null)
            {
                D3D11Texture d3dDepthTarget = Util.AssertSubtype<Texture, D3D11Texture>(description.DepthTarget.Value.Target);
                DepthStencilViewDesc dsvDesc = new DepthStencilViewDesc
                {
                    Format = D3D11Formats.GetDepthFormat(d3dDepthTarget.Format),
                };
                if (d3dDepthTarget.ArrayLayers == 1)
                {
                    if (d3dDepthTarget.SampleCount == TextureSampleCount.Count1)
                    {
                        dsvDesc.ViewDimension = DsvDimension.Texture2D;
                        dsvDesc.Texture2D.MipSlice = (uint)description.DepthTarget.Value.MipLevel;
                    }
                    else
                    {
                        dsvDesc.ViewDimension = DsvDimension.Texture2Dms;
                    }
                }
                else
                {
                    if (d3dDepthTarget.SampleCount == TextureSampleCount.Count1)
                    {
                        dsvDesc.ViewDimension = DsvDimension.Texture2Darray;
                        dsvDesc.Texture2DArray.FirstArraySlice = (uint)description.DepthTarget.Value.ArrayLayer;
                        dsvDesc.Texture2DArray.ArraySize = 1;
                        dsvDesc.Texture2DArray.MipSlice = (uint)description.DepthTarget.Value.MipLevel;
                    }
                    else
                    {
                        dsvDesc.ViewDimension = DsvDimension.Texture2Dmsarray;
                        dsvDesc.Texture2DMSArray.FirstArraySlice = (uint)description.DepthTarget.Value.ArrayLayer;
                        dsvDesc.Texture2DMSArray.ArraySize = 1;
                    }
                }

                ID3D11DepthStencilView* pDsv;
                SilkMarshal.ThrowHResult(device->CreateDepthStencilView(d3dDepthTarget.DeviceTexture, in dsvDesc, &pDsv));
                _depthStencilView = default;
                _depthStencilView.Handle = pDsv;
            }

            if (description.ColorTargets != null && description.ColorTargets.Length > 0)
            {
                RenderTargetViews = new ComPtr<ID3D11RenderTargetView>[description.ColorTargets.Length];
                for (int i = 0; i < RenderTargetViews.Length; i++)
                {
                    D3D11Texture d3dColorTarget = Util.AssertSubtype<Texture, D3D11Texture>(description.ColorTargets[i].Target);
                    RenderTargetViewDesc rtvDesc = new RenderTargetViewDesc
                    {
                        Format = D3D11Formats.ToDxgiFormat(d3dColorTarget.Format, false),
                    };
                    if (d3dColorTarget.ArrayLayers > 1 || (d3dColorTarget.Usage & TextureUsage.Cubemap) != 0)
                    {
                        if (d3dColorTarget.SampleCount == TextureSampleCount.Count1)
                        {
                            rtvDesc.ViewDimension = RtvDimension.Texture2Darray;
                            rtvDesc.Texture2DArray.ArraySize = 1;
                            rtvDesc.Texture2DArray.FirstArraySlice = (uint)description.ColorTargets[i].ArrayLayer;
                            rtvDesc.Texture2DArray.MipSlice = (uint)description.ColorTargets[i].MipLevel;
                        }
                        else
                        {
                            rtvDesc.ViewDimension = RtvDimension.Texture2Dmsarray;
                            rtvDesc.Texture2DMSArray.ArraySize = 1;
                            rtvDesc.Texture2DMSArray.FirstArraySlice = (uint)description.ColorTargets[i].ArrayLayer;
                        }
                    }
                    else
                    {
                        if (d3dColorTarget.SampleCount == TextureSampleCount.Count1)
                        {
                            rtvDesc.ViewDimension = RtvDimension.Texture2D;
                            rtvDesc.Texture2D.MipSlice = (uint)description.ColorTargets[i].MipLevel;
                        }
                        else
                        {
                            rtvDesc.ViewDimension = RtvDimension.Texture2Dms;
                        }
                    }

                    ID3D11RenderTargetView* pRtv;
                    SilkMarshal.ThrowHResult(device->CreateRenderTargetView(d3dColorTarget.DeviceTexture, in rtvDesc, &pRtv));
                    RenderTargetViews[i] = default;
                    RenderTargetViews[i].Handle = pRtv;
                }
            }
            else
            {
                RenderTargetViews = Array.Empty<ComPtr<ID3D11RenderTargetView>>();
            }
        }

        public override string Name
        {
            get => _name;
            set
            {
                _name = value;
                for (int i = 0; i < RenderTargetViews.Length; i++)
                    D3D11Util.SetDebugName((ID3D11DeviceChild*)RenderTargetViews[i].Handle, value + "_RTV" + i);
                if (_depthStencilView.Handle != null)
                    D3D11Util.SetDebugName((ID3D11DeviceChild*)_depthStencilView.Handle, value + "_DSV");
            }
        }

        public override void Dispose()
        {
            if (!_disposed)
            {
                if (_depthStencilView.Handle != null)
                {
                    _depthStencilView.Dispose();
                }
                foreach (ComPtr<ID3D11RenderTargetView> rtv in RenderTargetViews)
                {
                    rtv.Dispose();
                }

                _disposed = true;
            }
        }
    }
}
