using System;
using System.Diagnostics;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;

namespace Veldrid.D3D11
{
    internal unsafe class D3D11Texture : Texture
    {
        private readonly ID3D11Device* _device;
        private ComPtr<ID3D11Resource> _deviceTexture;
        private string _name;
        private bool _disposed;

        public override uint Width { get; }
        public override uint Height { get; }
        public override uint Depth { get; }
        public override uint MipLevels { get; }
        public override uint ArrayLayers { get; }
        public override PixelFormat Format { get; }
        public override TextureUsage Usage { get; }
        public override TextureType Type { get; }
        public override TextureSampleCount SampleCount { get; }
        public override bool IsDisposed => _disposed;

        public ID3D11Resource* DeviceTexture => _deviceTexture;
        public Format DxgiFormat { get; }
        public Format TypelessDxgiFormat { get; }

        public D3D11Texture(ID3D11Device* device, ref TextureDescription description)
        {
            _device = device;
            Width = description.Width;
            Height = description.Height;
            Depth = description.Depth;
            MipLevels = description.MipLevels;
            ArrayLayers = description.ArrayLayers;
            Format = description.Format;
            Usage = description.Usage;
            Type = description.Type;
            SampleCount = description.SampleCount;

            DxgiFormat = D3D11Formats.ToDxgiFormat(
                description.Format,
                (description.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil);
            TypelessDxgiFormat = D3D11Formats.GetTypelessFormat(DxgiFormat);

            CpuAccessFlag cpuFlags = CpuAccessFlag.None;
            Silk.NET.Direct3D11.Usage resourceUsage = Silk.NET.Direct3D11.Usage.Default;
            BindFlag bindFlags = BindFlag.None;
            ResourceMiscFlag optionFlags = ResourceMiscFlag.None;

            if ((description.Usage & TextureUsage.RenderTarget) == TextureUsage.RenderTarget)
            {
                bindFlags |= BindFlag.RenderTarget;
            }
            if ((description.Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil)
            {
                bindFlags |= BindFlag.DepthStencil;
            }
            if ((description.Usage & TextureUsage.Sampled) == TextureUsage.Sampled)
            {
                bindFlags |= BindFlag.ShaderResource;
            }
            if ((description.Usage & TextureUsage.Storage) == TextureUsage.Storage)
            {
                bindFlags |= BindFlag.UnorderedAccess;
            }
            if ((description.Usage & TextureUsage.Staging) == TextureUsage.Staging)
            {
                cpuFlags = CpuAccessFlag.Read | CpuAccessFlag.Write;
                resourceUsage = Silk.NET.Direct3D11.Usage.Staging;
            }

            if ((description.Usage & TextureUsage.GenerateMipmaps) != 0)
            {
                bindFlags |= BindFlag.RenderTarget | BindFlag.ShaderResource;
                optionFlags |= ResourceMiscFlag.GenerateMips;
            }

            uint arraySize = description.ArrayLayers;
            if ((description.Usage & TextureUsage.Cubemap) == TextureUsage.Cubemap)
            {
                optionFlags |= ResourceMiscFlag.Texturecube;
                arraySize *= 6;
            }

            int roundedWidth = (int)description.Width;
            int roundedHeight = (int)description.Height;
            if (FormatHelpers.IsCompressedFormat(description.Format))
            {
                roundedWidth = ((roundedWidth + 3) / 4) * 4;
                roundedHeight = ((roundedHeight + 3) / 4) * 4;
            }

            if (Type == TextureType.Texture1D)
            {
                Texture1DDesc desc1D = new Texture1DDesc
                {
                    Width = (uint)roundedWidth,
                    MipLevels = description.MipLevels,
                    ArraySize = arraySize,
                    Format = TypelessDxgiFormat,
                    BindFlags = (uint)bindFlags,
                    CPUAccessFlags = (uint)cpuFlags,
                    Usage = resourceUsage,
                    MiscFlags = (uint)optionFlags,
                };

                ID3D11Texture1D* pTex;
                SilkMarshal.ThrowHResult(device->CreateTexture1D(in desc1D, null, &pTex));
                _deviceTexture = default;
                _deviceTexture.Handle = (ID3D11Resource*)pTex;
            }
            else if (Type == TextureType.Texture2D)
            {
                Texture2DDesc desc2D = new Texture2DDesc
                {
                    Width = (uint)roundedWidth,
                    Height = (uint)roundedHeight,
                    MipLevels = description.MipLevels,
                    ArraySize = arraySize,
                    Format = TypelessDxgiFormat,
                    BindFlags = (uint)bindFlags,
                    CPUAccessFlags = (uint)cpuFlags,
                    Usage = resourceUsage,
                    SampleDesc = new SampleDesc { Count = FormatHelpers.GetSampleCountUInt32(SampleCount), Quality = 0 },
                    MiscFlags = (uint)optionFlags,
                };

                ID3D11Texture2D* pTex;
                SilkMarshal.ThrowHResult(device->CreateTexture2D(in desc2D, null, &pTex));
                _deviceTexture = default;
                _deviceTexture.Handle = (ID3D11Resource*)pTex;
            }
            else
            {
                Debug.Assert(Type == TextureType.Texture3D);
                Texture3DDesc desc3D = new Texture3DDesc
                {
                    Width = (uint)roundedWidth,
                    Height = (uint)roundedHeight,
                    Depth = description.Depth,
                    MipLevels = description.MipLevels,
                    Format = TypelessDxgiFormat,
                    BindFlags = (uint)bindFlags,
                    CPUAccessFlags = (uint)cpuFlags,
                    Usage = resourceUsage,
                    MiscFlags = (uint)optionFlags,
                };

                ID3D11Texture3D* pTex;
                SilkMarshal.ThrowHResult(device->CreateTexture3D(in desc3D, null, &pTex));
                _deviceTexture = default;
                _deviceTexture.Handle = (ID3D11Resource*)pTex;
            }
        }

        public D3D11Texture(ID3D11Texture2D* existingTexture, TextureType type, PixelFormat format)
        {
            Texture2DDesc desc;
            existingTexture->GetDesc(&desc);

            ID3D11Device* pDevice;
            ((ID3D11DeviceChild*)existingTexture)->GetDevice(&pDevice);
            _device = pDevice;
            // GetDevice calls AddRef; release since we only store a borrowed pointer.
            pDevice->Release();

            // AddRef so this D3D11Texture owns its own reference; the caller will Release theirs.
            existingTexture->AddRef();
            _deviceTexture = default;
            _deviceTexture.Handle = (ID3D11Resource*)existingTexture;

            Width = desc.Width;
            Height = desc.Height;
            Depth = 1;
            MipLevels = desc.MipLevels;
            ArrayLayers = desc.ArraySize;
            Format = format;
            SampleCount = FormatHelpers.GetSampleCount(desc.SampleDesc.Count);
            Type = type;
            Usage = D3D11Formats.GetVdUsage(
                (BindFlag)desc.BindFlags,
                (CpuAccessFlag)desc.CPUAccessFlags,
                (ResourceMiscFlag)desc.MiscFlags);

            DxgiFormat = D3D11Formats.ToDxgiFormat(
                format,
                (Usage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil);
            TypelessDxgiFormat = D3D11Formats.GetTypelessFormat(DxgiFormat);
        }

        private protected override TextureView CreateFullTextureView(GraphicsDevice gd)
        {
            TextureViewDescription desc = new TextureViewDescription(this);
            D3D11GraphicsDevice d3d11GD = Util.AssertSubtype<GraphicsDevice, D3D11GraphicsDevice>(gd);
            return new D3D11TextureView(d3d11GD, ref desc);
        }

        public override string Name
        {
            get => _name;
            set
            {
                _name = value;
                D3D11Util.SetDebugName((ID3D11DeviceChild*)_deviceTexture.Handle, value);
            }
        }

        private protected override void DisposeCore()
        {
            if (!_disposed)
            {
                _deviceTexture.Dispose();
                _disposed = true;
            }
        }
    }
}
