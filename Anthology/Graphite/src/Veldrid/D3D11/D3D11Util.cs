using System;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;

namespace Veldrid.D3D11
{
    internal static unsafe class D3D11Util
    {
        /// <summary>Sets the debug name on a D3D11 device child via SetPrivateData.</summary>
        internal static void SetDebugName(ID3D11DeviceChild* deviceChild, string name)
        {
            if (deviceChild == null) return;

            // WKPDID_D3DDebugObjectName = {429b8c22-9188-4b0c-8742-acb0bf85c200}
            Guid debugNameGuid = new Guid(0x429b8c22, 0x9188, 0x4b0c, 0x87, 0x42, 0xac, 0xb0, 0xbf, 0x85, 0xc2, 0x00);

            if (string.IsNullOrEmpty(name))
            {
                deviceChild->SetPrivateData(&debugNameGuid, 0, null);
            }
            else
            {
                nint namePtr = Marshal.StringToHGlobalAnsi(name);
                deviceChild->SetPrivateData(&debugNameGuid, (uint)name.Length, (void*)namePtr);
                Marshal.FreeHGlobal(namePtr);
            }
        }

        public static int ComputeSubresource(uint mipLevel, uint mipLevelCount, uint arrayLayer)
        {
            return (int)((arrayLayer * mipLevelCount) + mipLevel);
        }

        internal static ShaderResourceViewDesc GetSrvDesc(
            D3D11Texture tex,
            uint baseMipLevel,
            uint levelCount,
            uint baseArrayLayer,
            uint layerCount,
            PixelFormat format)
        {
            ShaderResourceViewDesc srvDesc = new ShaderResourceViewDesc();
            srvDesc.Format = D3D11Formats.GetViewFormat(
                D3D11Formats.ToDxgiFormat(format, (tex.Usage & TextureUsage.DepthStencil) != 0));

            if ((tex.Usage & TextureUsage.Cubemap) == TextureUsage.Cubemap)
            {
                if (tex.ArrayLayers == 1)
                {
                    srvDesc.ViewDimension = D3DSrvDimension.D3DSrvDimensionTexturecube;
                    srvDesc.TextureCube.MostDetailedMip = baseMipLevel;
                    srvDesc.TextureCube.MipLevels = levelCount;
                }
                else
                {
                    srvDesc.ViewDimension = D3DSrvDimension.D3DSrvDimensionTexturecubearray;
                    srvDesc.TextureCubeArray.MostDetailedMip = baseMipLevel;
                    srvDesc.TextureCubeArray.MipLevels = levelCount;
                    srvDesc.TextureCubeArray.First2DArrayFace = baseArrayLayer;
                    srvDesc.TextureCubeArray.NumCubes = tex.ArrayLayers;
                }
            }
            else if (tex.Depth == 1)
            {
                if (tex.ArrayLayers == 1)
                {
                    if (tex.Type == TextureType.Texture1D)
                    {
                        srvDesc.ViewDimension = D3DSrvDimension.D3DSrvDimensionTexture1D;
                        srvDesc.Texture1D.MostDetailedMip = baseMipLevel;
                        srvDesc.Texture1D.MipLevels = levelCount;
                    }
                    else
                    {
                        if (tex.SampleCount == TextureSampleCount.Count1)
                            srvDesc.ViewDimension = D3DSrvDimension.D3DSrvDimensionTexture2D;
                        else
                            srvDesc.ViewDimension = D3DSrvDimension.D3DSrvDimensionTexture2Dms;
                        srvDesc.Texture2D.MostDetailedMip = baseMipLevel;
                        srvDesc.Texture2D.MipLevels = levelCount;
                    }
                }
                else
                {
                    if (tex.Type == TextureType.Texture1D)
                    {
                        srvDesc.ViewDimension = D3DSrvDimension.D3DSrvDimensionTexture1Darray;
                        srvDesc.Texture1DArray.MostDetailedMip = baseMipLevel;
                        srvDesc.Texture1DArray.MipLevels = levelCount;
                        srvDesc.Texture1DArray.FirstArraySlice = baseArrayLayer;
                        srvDesc.Texture1DArray.ArraySize = layerCount;
                    }
                    else
                    {
                        srvDesc.ViewDimension = D3DSrvDimension.D3DSrvDimensionTexture2Darray;
                        srvDesc.Texture2DArray.MostDetailedMip = baseMipLevel;
                        srvDesc.Texture2DArray.MipLevels = levelCount;
                        srvDesc.Texture2DArray.FirstArraySlice = baseArrayLayer;
                        srvDesc.Texture2DArray.ArraySize = layerCount;
                    }
                }
            }
            else
            {
                srvDesc.ViewDimension = D3DSrvDimension.D3DSrvDimensionTexture3D;
                srvDesc.Texture3D.MostDetailedMip = baseMipLevel;
                srvDesc.Texture3D.MipLevels = levelCount;
            }

            return srvDesc;
        }

        internal static int GetSyncInterval(bool syncToVBlank)
        {
            return syncToVBlank ? 1 : 0;
        }
    }
}
