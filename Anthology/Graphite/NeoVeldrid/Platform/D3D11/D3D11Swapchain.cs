using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Silk.NET.Core.Native;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace NeoVeldrid.D3D11
{
    internal unsafe class D3D11Swapchain : Swapchain
    {
        private readonly D3D11GraphicsDevice _gd;
        private readonly PixelFormat? _depthFormat;
        private ComPtr<IDXGISwapChain> _dxgiSwapChain;
        private bool _vsync;
        private int _syncInterval;
        private D3D11Framebuffer _framebuffer;
        private D3D11Texture _depthTexture;
        private D3D11Texture _backBufferVdTexture;
        private float _pixelScale = 1f;
        private bool _disposed;
        private string _name;

        private readonly object _referencedCLsLock = new object();
        private HashSet<D3D11CommandList> _referencedCLs = new HashSet<D3D11CommandList>();

        public override Framebuffer Framebuffer => _framebuffer;

        public override string Name
        {
            get => _name;
            set => _name = value;
        }

        public override bool SyncToVerticalBlank
        {
            get => _vsync;
            set
            {
                _vsync = value;
                _syncInterval = D3D11Util.GetSyncInterval(value);
            }
        }

        private readonly Format _colorFormat;

        /// <summary>Returns a raw pointer to the underlying DXGI swap chain.</summary>
        public IDXGISwapChain* DxgiSwapChain => _dxgiSwapChain;

        public int SyncInterval => _syncInterval;

        public D3D11Swapchain(D3D11GraphicsDevice gd, ref SwapchainDescription description)
        {
            _gd = gd;
            _depthFormat = description.DepthFormat;
            SyncToVerticalBlank = description.SyncToVerticalBlank;

            _colorFormat = description.ColorSrgb
                ? Format.FormatB8G8R8A8UnormSrgb
                : Format.FormatB8G8R8A8Unorm;

            if (description.Source is Win32SwapchainSource win32Source)
            {
                SwapChainDesc dxgiSCDesc = new SwapChainDesc
                {
                    BufferCount = 2,
                    Windowed = 1,
                    BufferDesc = new ModeDesc
                    {
                        Width = description.Width,
                        Height = description.Height,
                        Format = _colorFormat,
                    },
                    OutputWindow = win32Source.Hwnd,
                    SampleDesc = new SampleDesc(1, 0),
                    SwapEffect = SwapEffect.Discard,
                    // DXGI_USAGE_RENDER_TARGET_OUTPUT = 0x00000020
                    BufferUsage = 0x00000020u,
                };

                IDXGIFactory* pFactory;
                var factoryGuid = IDXGIFactory.Guid;
                SilkMarshal.ThrowHResult(
                    _gd.Adapter->GetParent(&factoryGuid, (void**)&pFactory));

                IDXGISwapChain* pSwapChain;
                SilkMarshal.ThrowHResult(
                    pFactory->CreateSwapChain((IUnknown*)_gd.Device, &dxgiSCDesc, &pSwapChain));

                // DXGI_MWA_NO_ALT_ENTER = 0x2
                pFactory->MakeWindowAssociation(win32Source.Hwnd, 0x2u);
                pFactory->Release();

                _dxgiSwapChain = default;
                _dxgiSwapChain.Handle = pSwapChain;
            }
            else
            {
                throw new NeoVeldridException($"Unsupported swapchain source type: {description.Source?.GetType().Name}");
            }

            Resize(description.Width, description.Height);
        }

        public override void Resize(uint width, uint height)
        {
            lock (_referencedCLsLock)
            {
                foreach (D3D11CommandList cl in _referencedCLs)
                {
                    cl.Reset();
                }

                _referencedCLs.Clear();
            }

            bool resizeBuffers = false;

            if (_framebuffer != null)
            {
                resizeBuffers = true;
                if (_depthTexture != null)
                {
                    _depthTexture.Dispose();
                }

                _framebuffer.Dispose();
                _backBufferVdTexture?.Dispose();
            }

            uint actualWidth = (uint)(width * _pixelScale);
            uint actualHeight = (uint)(height * _pixelScale);
            if (resizeBuffers)
            {
                SilkMarshal.ThrowHResult(
                    ((IDXGISwapChain*)_dxgiSwapChain)->ResizeBuffers(2, actualWidth, actualHeight, _colorFormat, 0u));
            }

            // Get the backbuffer from the swapchain
            ID3D11Texture2D* pBackBuffer;
            var tex2dGuid = ID3D11Texture2D.Guid;
            SilkMarshal.ThrowHResult(
                ((IDXGISwapChain*)_dxgiSwapChain)->GetBuffer(0, &tex2dGuid, (void**)&pBackBuffer));

            if (_depthFormat != null)
            {
                TextureDescription depthDesc = new TextureDescription(
                    actualWidth, actualHeight, 1, 1, 1,
                    _depthFormat.Value,
                    TextureUsage.DepthStencil,
                    TextureType.Texture2D);
                _depthTexture = new D3D11Texture(_gd.Device, ref depthDesc);
            }

            _backBufferVdTexture = new D3D11Texture(
                pBackBuffer,
                TextureType.Texture2D,
                D3D11Formats.ToVdFormat(_colorFormat));

            // Release our GetBuffer reference; D3D11Texture constructor calls AddRef to take ownership.
            pBackBuffer->Release();

            FramebufferDescription desc = new FramebufferDescription(_depthTexture, _backBufferVdTexture);
            _framebuffer = new D3D11Framebuffer(_gd.Device, ref desc)
            {
                Swapchain = this
            };
        }

        public void AddCommandListReference(D3D11CommandList cl)
        {
            lock (_referencedCLsLock)
            {
                _referencedCLs.Add(cl);
            }
        }

        public void RemoveCommandListReference(D3D11CommandList cl)
        {
            lock (_referencedCLsLock)
            {
                _referencedCLs.Remove(cl);
            }
        }

        public override bool IsDisposed => _disposed;

        public override void Dispose()
        {
            if (!_disposed)
            {
                _depthTexture?.Dispose();
                _framebuffer.Dispose();
                _backBufferVdTexture?.Dispose();
                _dxgiSwapChain.Dispose();

                _disposed = true;
            }
        }
    }
}
