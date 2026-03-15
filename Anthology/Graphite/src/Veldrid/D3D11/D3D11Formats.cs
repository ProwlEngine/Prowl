using System;
using System.Diagnostics;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Silk.NET.Core.Native;

namespace Veldrid.D3D11
{
    internal static class D3D11Formats
    {
        internal static Format ToDxgiFormat(PixelFormat format, bool depthFormat)
        {
            switch (format)
            {
                case PixelFormat.R8_UNorm:
                    return Format.FormatR8Unorm;
                case PixelFormat.R8_SNorm:
                    return Format.FormatR8SNorm;
                case PixelFormat.R8_UInt:
                    return Format.FormatR8Uint;
                case PixelFormat.R8_SInt:
                    return Format.FormatR8Sint;

                case PixelFormat.R16_UNorm:
                    return depthFormat ? Format.FormatR16Typeless : Format.FormatR16Unorm;
                case PixelFormat.R16_SNorm:
                    return Format.FormatR16SNorm;
                case PixelFormat.R16_UInt:
                    return Format.FormatR16Uint;
                case PixelFormat.R16_SInt:
                    return Format.FormatR16Sint;
                case PixelFormat.R16_Float:
                    return Format.FormatR16Float;

                case PixelFormat.R32_UInt:
                    return Format.FormatR32Uint;
                case PixelFormat.R32_SInt:
                    return Format.FormatR32Sint;
                case PixelFormat.R32_Float:
                    return depthFormat ? Format.FormatR32Typeless : Format.FormatR32Float;

                case PixelFormat.R8_G8_UNorm:
                    return Format.FormatR8G8Unorm;
                case PixelFormat.R8_G8_SNorm:
                    return Format.FormatR8G8SNorm;
                case PixelFormat.R8_G8_UInt:
                    return Format.FormatR8G8Uint;
                case PixelFormat.R8_G8_SInt:
                    return Format.FormatR8G8Sint;

                case PixelFormat.R16_G16_UNorm:
                    return Format.FormatR16G16Unorm;
                case PixelFormat.R16_G16_SNorm:
                    return Format.FormatR16G16SNorm;
                case PixelFormat.R16_G16_UInt:
                    return Format.FormatR16G16Uint;
                case PixelFormat.R16_G16_SInt:
                    return Format.FormatR16G16Sint;
                case PixelFormat.R16_G16_Float:
                    return Format.FormatR16G16Float;

                case PixelFormat.R32_G32_UInt:
                    return Format.FormatR32G32Uint;
                case PixelFormat.R32_G32_SInt:
                    return Format.FormatR32G32Sint;
                case PixelFormat.R32_G32_Float:
                    return Format.FormatR32G32Float;

                case PixelFormat.R8_G8_B8_A8_UNorm:
                    return Format.FormatR8G8B8A8Unorm;
                case PixelFormat.R8_G8_B8_A8_UNorm_SRgb:
                    return Format.FormatR8G8B8A8UnormSrgb;
                case PixelFormat.B8_G8_R8_A8_UNorm:
                    return Format.FormatB8G8R8A8Unorm;
                case PixelFormat.B8_G8_R8_A8_UNorm_SRgb:
                    return Format.FormatB8G8R8A8UnormSrgb;
                case PixelFormat.R8_G8_B8_A8_SNorm:
                    return Format.FormatR8G8B8A8SNorm;
                case PixelFormat.R8_G8_B8_A8_UInt:
                    return Format.FormatR8G8B8A8Uint;
                case PixelFormat.R8_G8_B8_A8_SInt:
                    return Format.FormatR8G8B8A8Sint;

                case PixelFormat.R16_G16_B16_A16_UNorm:
                    return Format.FormatR16G16B16A16Unorm;
                case PixelFormat.R16_G16_B16_A16_SNorm:
                    return Format.FormatR16G16B16A16SNorm;
                case PixelFormat.R16_G16_B16_A16_UInt:
                    return Format.FormatR16G16B16A16Uint;
                case PixelFormat.R16_G16_B16_A16_SInt:
                    return Format.FormatR16G16B16A16Sint;
                case PixelFormat.R16_G16_B16_A16_Float:
                    return Format.FormatR16G16B16A16Float;

                case PixelFormat.R32_G32_B32_A32_UInt:
                    return Format.FormatR32G32B32A32Uint;
                case PixelFormat.R32_G32_B32_A32_SInt:
                    return Format.FormatR32G32B32A32Sint;
                case PixelFormat.R32_G32_B32_A32_Float:
                    return Format.FormatR32G32B32A32Float;

                case PixelFormat.BC1_Rgb_UNorm:
                case PixelFormat.BC1_Rgba_UNorm:
                    return Format.FormatBC1Unorm;
                case PixelFormat.BC1_Rgb_UNorm_SRgb:
                case PixelFormat.BC1_Rgba_UNorm_SRgb:
                    return Format.FormatBC1UnormSrgb;
                case PixelFormat.BC2_UNorm:
                    return Format.FormatBC2Unorm;
                case PixelFormat.BC2_UNorm_SRgb:
                    return Format.FormatBC2UnormSrgb;
                case PixelFormat.BC3_UNorm:
                    return Format.FormatBC3Unorm;
                case PixelFormat.BC3_UNorm_SRgb:
                    return Format.FormatBC3UnormSrgb;
                case PixelFormat.BC4_UNorm:
                    return Format.FormatBC4Unorm;
                case PixelFormat.BC4_SNorm:
                    return Format.FormatBC4SNorm;
                case PixelFormat.BC5_UNorm:
                    return Format.FormatBC5Unorm;
                case PixelFormat.BC5_SNorm:
                    return Format.FormatBC5SNorm;
                case PixelFormat.BC7_UNorm:
                    return Format.FormatBC7Unorm;
                case PixelFormat.BC7_UNorm_SRgb:
                    return Format.FormatBC7UnormSrgb;

                case PixelFormat.D24_UNorm_S8_UInt:
                    Debug.Assert(depthFormat);
                    return Format.FormatR24G8Typeless;
                case PixelFormat.D32_Float_S8_UInt:
                    Debug.Assert(depthFormat);
                    return Format.FormatR32G8X24Typeless;

                case PixelFormat.R10_G10_B10_A2_UNorm:
                    return Format.FormatR10G10B10A2Unorm;
                case PixelFormat.R10_G10_B10_A2_UInt:
                    return Format.FormatR10G10B10A2Uint;
                case PixelFormat.R11_G11_B10_Float:
                    return Format.FormatR11G11B10Float;

                case PixelFormat.ETC2_R8_G8_B8_UNorm:
                case PixelFormat.ETC2_R8_G8_B8_A1_UNorm:
                case PixelFormat.ETC2_R8_G8_B8_A8_UNorm:
                    throw new VeldridException("ETC2 formats are not supported on Direct3D 11.");

                default:
                    throw Illegal.Value<PixelFormat>();
            }
        }

        internal static Format GetTypelessFormat(Format format)
        {
            switch (format)
            {
                case Format.FormatR32G32B32A32Typeless:
                case Format.FormatR32G32B32A32Float:
                case Format.FormatR32G32B32A32Uint:
                case Format.FormatR32G32B32A32Sint:
                    return Format.FormatR32G32B32A32Typeless;
                case Format.FormatR32G32B32Typeless:
                case Format.FormatR32G32B32Float:
                case Format.FormatR32G32B32Uint:
                case Format.FormatR32G32B32Sint:
                    return Format.FormatR32G32B32Typeless;
                case Format.FormatR16G16B16A16Typeless:
                case Format.FormatR16G16B16A16Float:
                case Format.FormatR16G16B16A16Unorm:
                case Format.FormatR16G16B16A16Uint:
                case Format.FormatR16G16B16A16SNorm:
                case Format.FormatR16G16B16A16Sint:
                    return Format.FormatR16G16B16A16Typeless;
                case Format.FormatR32G32Typeless:
                case Format.FormatR32G32Float:
                case Format.FormatR32G32Uint:
                case Format.FormatR32G32Sint:
                    return Format.FormatR32G32Typeless;
                case Format.FormatR10G10B10A2Typeless:
                case Format.FormatR10G10B10A2Unorm:
                case Format.FormatR10G10B10A2Uint:
                    return Format.FormatR10G10B10A2Typeless;
                case Format.FormatR8G8B8A8Typeless:
                case Format.FormatR8G8B8A8Unorm:
                case Format.FormatR8G8B8A8UnormSrgb:
                case Format.FormatR8G8B8A8Uint:
                case Format.FormatR8G8B8A8SNorm:
                case Format.FormatR8G8B8A8Sint:
                    return Format.FormatR8G8B8A8Typeless;
                case Format.FormatR16G16Typeless:
                case Format.FormatR16G16Float:
                case Format.FormatR16G16Unorm:
                case Format.FormatR16G16Uint:
                case Format.FormatR16G16SNorm:
                case Format.FormatR16G16Sint:
                    return Format.FormatR16G16Typeless;
                case Format.FormatR32Typeless:
                case Format.FormatD32Float:
                case Format.FormatR32Float:
                case Format.FormatR32Uint:
                case Format.FormatR32Sint:
                    return Format.FormatR32Typeless;
                case Format.FormatR24G8Typeless:
                case Format.FormatD24UnormS8Uint:
                case Format.FormatR24UnormX8Typeless:
                case Format.FormatX24TypelessG8Uint:
                    return Format.FormatR24G8Typeless;
                case Format.FormatR8G8Typeless:
                case Format.FormatR8G8Unorm:
                case Format.FormatR8G8Uint:
                case Format.FormatR8G8SNorm:
                case Format.FormatR8G8Sint:
                    return Format.FormatR8G8Typeless;
                case Format.FormatR16Typeless:
                case Format.FormatR16Float:
                case Format.FormatD16Unorm:
                case Format.FormatR16Unorm:
                case Format.FormatR16Uint:
                case Format.FormatR16SNorm:
                case Format.FormatR16Sint:
                    return Format.FormatR16Typeless;
                case Format.FormatR8Typeless:
                case Format.FormatR8Unorm:
                case Format.FormatR8Uint:
                case Format.FormatR8SNorm:
                case Format.FormatR8Sint:
                case Format.FormatA8Unorm:
                    return Format.FormatR8Typeless;
                case Format.FormatBC1Typeless:
                case Format.FormatBC1Unorm:
                case Format.FormatBC1UnormSrgb:
                    return Format.FormatBC1Typeless;
                case Format.FormatBC2Typeless:
                case Format.FormatBC2Unorm:
                case Format.FormatBC2UnormSrgb:
                    return Format.FormatBC2Typeless;
                case Format.FormatBC3Typeless:
                case Format.FormatBC3Unorm:
                case Format.FormatBC3UnormSrgb:
                    return Format.FormatBC3Typeless;
                case Format.FormatBC4Typeless:
                case Format.FormatBC4Unorm:
                case Format.FormatBC4SNorm:
                    return Format.FormatBC4Typeless;
                case Format.FormatBC5Typeless:
                case Format.FormatBC5Unorm:
                case Format.FormatBC5SNorm:
                    return Format.FormatBC5Typeless;
                case Format.FormatB8G8R8A8Typeless:
                case Format.FormatB8G8R8A8Unorm:
                case Format.FormatB8G8R8A8UnormSrgb:
                    return Format.FormatB8G8R8A8Typeless;
                case Format.FormatBC7Typeless:
                case Format.FormatBC7Unorm:
                case Format.FormatBC7UnormSrgb:
                    return Format.FormatBC7Typeless;
                default:
                    return format;
            }
        }

        internal static BindFlag VdToD3D11BindFlags(BufferUsage usage)
        {
            BindFlag flags = BindFlag.None;
            if ((usage & BufferUsage.VertexBuffer) == BufferUsage.VertexBuffer)
            {
                flags |= BindFlag.VertexBuffer;
            }
            if ((usage & BufferUsage.IndexBuffer) == BufferUsage.IndexBuffer)
            {
                flags |= BindFlag.IndexBuffer;
            }
            if ((usage & BufferUsage.UniformBuffer) == BufferUsage.UniformBuffer)
            {
                flags |= BindFlag.ConstantBuffer;
            }
            if ((usage & BufferUsage.StructuredBufferReadOnly) == BufferUsage.StructuredBufferReadOnly
                || (usage & BufferUsage.StructuredBufferReadWrite) == BufferUsage.StructuredBufferReadWrite)
            {
                flags |= BindFlag.ShaderResource;
            }
            if ((usage & BufferUsage.StructuredBufferReadWrite) == BufferUsage.StructuredBufferReadWrite)
            {
                flags |= BindFlag.UnorderedAccess;
            }

            return flags;
        }

        internal static TextureUsage GetVdUsage(BindFlag bindFlags, CpuAccessFlag cpuFlags, ResourceMiscFlag optionFlags)
        {
            TextureUsage usage = 0;
            if ((bindFlags & BindFlag.RenderTarget) != 0)
            {
                usage |= TextureUsage.RenderTarget;
            }
            if ((bindFlags & BindFlag.DepthStencil) != 0)
            {
                usage |= TextureUsage.DepthStencil;
            }
            if ((bindFlags & BindFlag.ShaderResource) != 0)
            {
                usage |= TextureUsage.Sampled;
            }
            if ((bindFlags & BindFlag.UnorderedAccess) != 0)
            {
                usage |= TextureUsage.Storage;
            }

            if ((optionFlags & ResourceMiscFlag.Texturecube) != 0)
            {
                usage |= TextureUsage.Cubemap;
            }
            if ((optionFlags & ResourceMiscFlag.GenerateMips) != 0)
            {
                usage |= TextureUsage.GenerateMipmaps;
            }

            return usage;
        }

        internal static bool IsUnsupportedFormat(PixelFormat format)
        {
            return format == PixelFormat.ETC2_R8_G8_B8_UNorm
                || format == PixelFormat.ETC2_R8_G8_B8_A1_UNorm
                || format == PixelFormat.ETC2_R8_G8_B8_A8_UNorm;
        }

        internal static Format GetViewFormat(Format format)
        {
            switch (format)
            {
                case Format.FormatR16Typeless:
                    return Format.FormatR16Unorm;
                case Format.FormatR32Typeless:
                    return Format.FormatR32Float;
                case Format.FormatR32G8X24Typeless:
                    return Format.FormatR32FloatX8X24Typeless;
                case Format.FormatR24G8Typeless:
                    return Format.FormatR24UnormX8Typeless;
                default:
                    return format;
            }
        }

        internal static Blend VdToD3D11Blend(BlendFactor factor)
        {
            switch (factor)
            {
                case BlendFactor.Zero:
                    return Blend.Zero;
                case BlendFactor.One:
                    return Blend.One;
                case BlendFactor.SourceAlpha:
                    return Blend.SrcAlpha;
                case BlendFactor.InverseSourceAlpha:
                    return Blend.InvSrcAlpha;
                case BlendFactor.DestinationAlpha:
                    return Blend.DestAlpha;
                case BlendFactor.InverseDestinationAlpha:
                    return Blend.InvDestAlpha;
                case BlendFactor.SourceColor:
                    return Blend.SrcColor;
                case BlendFactor.InverseSourceColor:
                    return Blend.InvSrcColor;
                case BlendFactor.DestinationColor:
                    return Blend.DestColor;
                case BlendFactor.InverseDestinationColor:
                    return Blend.InvDestColor;
                case BlendFactor.BlendFactor:
                    return Blend.BlendFactor;
                case BlendFactor.InverseBlendFactor:
                    return Blend.InvBlendFactor;
                default:
                    throw Illegal.Value<BlendFactor>();
            }
        }

        internal static Format ToDxgiFormat(IndexFormat format)
        {
            switch (format)
            {
                case IndexFormat.UInt16:
                    return Format.FormatR16Uint;
                case IndexFormat.UInt32:
                    return Format.FormatR32Uint;
                default:
                    throw Illegal.Value<IndexFormat>();
            }
        }

        internal static StencilOp VdToD3D11StencilOperation(StencilOperation op)
        {
            switch (op)
            {
                case StencilOperation.Keep:
                    return StencilOp.Keep;
                case StencilOperation.Zero:
                    return StencilOp.Zero;
                case StencilOperation.Replace:
                    return StencilOp.Replace;
                case StencilOperation.IncrementAndClamp:
                    return StencilOp.IncrSat;
                case StencilOperation.DecrementAndClamp:
                    return StencilOp.DecrSat;
                case StencilOperation.Invert:
                    return StencilOp.Invert;
                case StencilOperation.IncrementAndWrap:
                    return StencilOp.Incr;
                case StencilOperation.DecrementAndWrap:
                    return StencilOp.Decr;
                default:
                    throw Illegal.Value<StencilOperation>();
            }
        }

        internal static PixelFormat ToVdFormat(Format format)
        {
            switch (format)
            {
                case Format.FormatR8Unorm:
                    return PixelFormat.R8_UNorm;
                case Format.FormatR8SNorm:
                    return PixelFormat.R8_SNorm;
                case Format.FormatR8Uint:
                    return PixelFormat.R8_UInt;
                case Format.FormatR8Sint:
                    return PixelFormat.R8_SInt;

                case Format.FormatR16Unorm:
                case Format.FormatD16Unorm:
                    return PixelFormat.R16_UNorm;
                case Format.FormatR16SNorm:
                    return PixelFormat.R16_SNorm;
                case Format.FormatR16Uint:
                    return PixelFormat.R16_UInt;
                case Format.FormatR16Sint:
                    return PixelFormat.R16_SInt;
                case Format.FormatR16Float:
                    return PixelFormat.R16_Float;

                case Format.FormatR32Uint:
                    return PixelFormat.R32_UInt;
                case Format.FormatR32Sint:
                    return PixelFormat.R32_SInt;
                case Format.FormatR32Float:
                case Format.FormatD32Float:
                    return PixelFormat.R32_Float;

                case Format.FormatR8G8Unorm:
                    return PixelFormat.R8_G8_UNorm;
                case Format.FormatR8G8SNorm:
                    return PixelFormat.R8_G8_SNorm;
                case Format.FormatR8G8Uint:
                    return PixelFormat.R8_G8_UInt;
                case Format.FormatR8G8Sint:
                    return PixelFormat.R8_G8_SInt;

                case Format.FormatR16G16Unorm:
                    return PixelFormat.R16_G16_UNorm;
                case Format.FormatR16G16SNorm:
                    return PixelFormat.R16_G16_SNorm;
                case Format.FormatR16G16Uint:
                    return PixelFormat.R16_G16_UInt;
                case Format.FormatR16G16Sint:
                    return PixelFormat.R16_G16_SInt;
                case Format.FormatR16G16Float:
                    return PixelFormat.R16_G16_Float;

                case Format.FormatR32G32Uint:
                    return PixelFormat.R32_G32_UInt;
                case Format.FormatR32G32Sint:
                    return PixelFormat.R32_G32_SInt;
                case Format.FormatR32G32Float:
                    return PixelFormat.R32_G32_Float;

                case Format.FormatR8G8B8A8Unorm:
                    return PixelFormat.R8_G8_B8_A8_UNorm;
                case Format.FormatR8G8B8A8UnormSrgb:
                    return PixelFormat.R8_G8_B8_A8_UNorm_SRgb;

                case Format.FormatB8G8R8A8Unorm:
                    return PixelFormat.B8_G8_R8_A8_UNorm;
                case Format.FormatB8G8R8A8UnormSrgb:
                    return PixelFormat.B8_G8_R8_A8_UNorm_SRgb;
                case Format.FormatR8G8B8A8SNorm:
                    return PixelFormat.R8_G8_B8_A8_SNorm;
                case Format.FormatR8G8B8A8Uint:
                    return PixelFormat.R8_G8_B8_A8_UInt;
                case Format.FormatR8G8B8A8Sint:
                    return PixelFormat.R8_G8_B8_A8_SInt;

                case Format.FormatR16G16B16A16Unorm:
                    return PixelFormat.R16_G16_B16_A16_UNorm;
                case Format.FormatR16G16B16A16SNorm:
                    return PixelFormat.R16_G16_B16_A16_SNorm;
                case Format.FormatR16G16B16A16Uint:
                    return PixelFormat.R16_G16_B16_A16_UInt;
                case Format.FormatR16G16B16A16Sint:
                    return PixelFormat.R16_G16_B16_A16_SInt;
                case Format.FormatR16G16B16A16Float:
                    return PixelFormat.R16_G16_B16_A16_Float;

                case Format.FormatR32G32B32A32Uint:
                    return PixelFormat.R32_G32_B32_A32_UInt;
                case Format.FormatR32G32B32A32Sint:
                    return PixelFormat.R32_G32_B32_A32_SInt;
                case Format.FormatR32G32B32A32Float:
                    return PixelFormat.R32_G32_B32_A32_Float;

                case Format.FormatBC1Unorm:
                case Format.FormatBC1Typeless:
                    return PixelFormat.BC1_Rgba_UNorm;
                case Format.FormatBC2Unorm:
                    return PixelFormat.BC2_UNorm;
                case Format.FormatBC3Unorm:
                    return PixelFormat.BC3_UNorm;
                case Format.FormatBC4Unorm:
                    return PixelFormat.BC4_UNorm;
                case Format.FormatBC4SNorm:
                    return PixelFormat.BC4_SNorm;
                case Format.FormatBC5Unorm:
                    return PixelFormat.BC5_UNorm;
                case Format.FormatBC5SNorm:
                    return PixelFormat.BC5_SNorm;
                case Format.FormatBC7Unorm:
                    return PixelFormat.BC7_UNorm;

                case Format.FormatD24UnormS8Uint:
                    return PixelFormat.D24_UNorm_S8_UInt;
                case Format.FormatD32FloatS8X24Uint:
                    return PixelFormat.D32_Float_S8_UInt;

                case Format.FormatR10G10B10A2Uint:
                    return PixelFormat.R10_G10_B10_A2_UInt;
                case Format.FormatR10G10B10A2Unorm:
                    return PixelFormat.R10_G10_B10_A2_UNorm;
                case Format.FormatR11G11B10Float:
                    return PixelFormat.R11_G11_B10_Float;
                default:
                    throw Illegal.Value<PixelFormat>();
            }
        }

        internal static BlendOp VdToD3D11BlendOperation(BlendFunction function)
        {
            switch (function)
            {
                case BlendFunction.Add:
                    return BlendOp.Add;
                case BlendFunction.Subtract:
                    return BlendOp.Subtract;
                case BlendFunction.ReverseSubtract:
                    return BlendOp.RevSubtract;
                case BlendFunction.Minimum:
                    return BlendOp.Min;
                case BlendFunction.Maximum:
                    return BlendOp.Max;
                default:
                    throw Illegal.Value<BlendFunction>();
            }
        }

        internal static ColorWriteEnable VdToD3D11ColorWriteEnable(ColorWriteMask mask)
        {
            ColorWriteEnable enable = ColorWriteEnable.None;

            if ((mask & ColorWriteMask.Red) == ColorWriteMask.Red)
                enable |= ColorWriteEnable.Red;
            if ((mask & ColorWriteMask.Green) == ColorWriteMask.Green)
                enable |= ColorWriteEnable.Green;
            if ((mask & ColorWriteMask.Blue) == ColorWriteMask.Blue)
                enable |= ColorWriteEnable.Blue;
            if ((mask & ColorWriteMask.Alpha) == ColorWriteMask.Alpha)
                enable |= ColorWriteEnable.Alpha;

            return enable;
        }

        internal static Filter ToD3D11Filter(SamplerFilter filter, bool isComparison)
        {
            switch (filter)
            {
                case SamplerFilter.MinPoint_MagPoint_MipPoint:
                    return isComparison ? Filter.ComparisonMinMagMipPoint : Filter.MinMagMipPoint;
                case SamplerFilter.MinPoint_MagPoint_MipLinear:
                    return isComparison ? Filter.ComparisonMinMagPointMipLinear : Filter.MinMagPointMipLinear;
                case SamplerFilter.MinPoint_MagLinear_MipPoint:
                    return isComparison ? Filter.ComparisonMinPointMagLinearMipPoint : Filter.MinPointMagLinearMipPoint;
                case SamplerFilter.MinPoint_MagLinear_MipLinear:
                    return isComparison ? Filter.ComparisonMinPointMagMipLinear : Filter.MinPointMagMipLinear;
                case SamplerFilter.MinLinear_MagPoint_MipPoint:
                    return isComparison ? Filter.ComparisonMinLinearMagMipPoint : Filter.MinLinearMagMipPoint;
                case SamplerFilter.MinLinear_MagPoint_MipLinear:
                    return isComparison ? Filter.ComparisonMinLinearMagPointMipLinear : Filter.MinLinearMagPointMipLinear;
                case SamplerFilter.MinLinear_MagLinear_MipPoint:
                    return isComparison ? Filter.ComparisonMinMagLinearMipPoint : Filter.MinMagLinearMipPoint;
                case SamplerFilter.MinLinear_MagLinear_MipLinear:
                    return isComparison ? Filter.ComparisonMinMagMipLinear : Filter.MinMagMipLinear;
                case SamplerFilter.Anisotropic:
                    return isComparison ? Filter.ComparisonAnisotropic : Filter.Anisotropic;
                default:
                    throw Illegal.Value<SamplerFilter>();
            }
        }

        internal static Map VdToD3D11MapMode(bool isDynamic, MapMode mode)
        {
            switch (mode)
            {
                case MapMode.Read:
                    return Map.Read;
                case MapMode.Write:
                    return isDynamic ? Map.WriteDiscard : Map.Write;
                case MapMode.ReadWrite:
                    return Map.ReadWrite;
                default:
                    throw Illegal.Value<MapMode>();
            }
        }

        internal static D3DPrimitiveTopology VdToD3D11PrimitiveTopology(PrimitiveTopology primitiveTopology)
        {
            switch (primitiveTopology)
            {
                case PrimitiveTopology.TriangleList:
                    return D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist;
                case PrimitiveTopology.TriangleStrip:
                    return D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglestrip;
                case PrimitiveTopology.LineList:
                    return D3DPrimitiveTopology.D3DPrimitiveTopologyLinelist;
                case PrimitiveTopology.LineStrip:
                    return D3DPrimitiveTopology.D3DPrimitiveTopologyLinestrip;
                case PrimitiveTopology.PointList:
                    return D3DPrimitiveTopology.D3DPrimitiveTopologyPointlist;
                default:
                    throw Illegal.Value<PrimitiveTopology>();
            }
        }

        internal static FillMode VdToD3D11FillMode(PolygonFillMode fillMode)
        {
            switch (fillMode)
            {
                case PolygonFillMode.Solid:
                    return FillMode.Solid;
                case PolygonFillMode.Wireframe:
                    return FillMode.Wireframe;
                default:
                    throw Illegal.Value<PolygonFillMode>();
            }
        }

        internal static CullMode VdToD3D11CullMode(FaceCullMode cullingMode)
        {
            switch (cullingMode)
            {
                case FaceCullMode.Back:
                    return CullMode.Back;
                case FaceCullMode.Front:
                    return CullMode.Front;
                case FaceCullMode.None:
                    return CullMode.None;
                default:
                    throw Illegal.Value<FaceCullMode>();
            }
        }

        internal static Format ToDxgiFormat(VertexElementFormat format)
        {
            switch (format)
            {
                case VertexElementFormat.Float1:
                    return Format.FormatR32Float;
                case VertexElementFormat.Float2:
                    return Format.FormatR32G32Float;
                case VertexElementFormat.Float3:
                    return Format.FormatR32G32B32Float;
                case VertexElementFormat.Float4:
                    return Format.FormatR32G32B32A32Float;
                case VertexElementFormat.Byte2_Norm:
                    return Format.FormatR8G8Unorm;
                case VertexElementFormat.Byte2:
                    return Format.FormatR8G8Uint;
                case VertexElementFormat.Byte4_Norm:
                    return Format.FormatR8G8B8A8Unorm;
                case VertexElementFormat.Byte4:
                    return Format.FormatR8G8B8A8Uint;
                case VertexElementFormat.SByte2_Norm:
                    return Format.FormatR8G8SNorm;
                case VertexElementFormat.SByte2:
                    return Format.FormatR8G8Sint;
                case VertexElementFormat.SByte4_Norm:
                    return Format.FormatR8G8B8A8SNorm;
                case VertexElementFormat.SByte4:
                    return Format.FormatR8G8B8A8Sint;
                case VertexElementFormat.UShort2_Norm:
                    return Format.FormatR16G16Unorm;
                case VertexElementFormat.UShort2:
                    return Format.FormatR16G16Uint;
                case VertexElementFormat.UShort4_Norm:
                    return Format.FormatR16G16B16A16Unorm;
                case VertexElementFormat.UShort4:
                    return Format.FormatR16G16B16A16Uint;
                case VertexElementFormat.Short2_Norm:
                    return Format.FormatR16G16SNorm;
                case VertexElementFormat.Short2:
                    return Format.FormatR16G16Sint;
                case VertexElementFormat.Short4_Norm:
                    return Format.FormatR16G16B16A16SNorm;
                case VertexElementFormat.Short4:
                    return Format.FormatR16G16B16A16Sint;
                case VertexElementFormat.UInt1:
                    return Format.FormatR32Uint;
                case VertexElementFormat.UInt2:
                    return Format.FormatR32G32Uint;
                case VertexElementFormat.UInt3:
                    return Format.FormatR32G32B32Uint;
                case VertexElementFormat.UInt4:
                    return Format.FormatR32G32B32A32Uint;
                case VertexElementFormat.Int1:
                    return Format.FormatR32Sint;
                case VertexElementFormat.Int2:
                    return Format.FormatR32G32Sint;
                case VertexElementFormat.Int3:
                    return Format.FormatR32G32B32Sint;
                case VertexElementFormat.Int4:
                    return Format.FormatR32G32B32A32Sint;
                case VertexElementFormat.Half1:
                    return Format.FormatR16Float;
                case VertexElementFormat.Half2:
                    return Format.FormatR16G16Float;
                case VertexElementFormat.Half4:
                    return Format.FormatR16G16B16A16Float;

                default:
                    throw Illegal.Value<VertexElementFormat>();
            }
        }

        internal static ComparisonFunc VdToD3D11ComparisonFunc(ComparisonKind comparisonKind)
        {
            switch (comparisonKind)
            {
                case ComparisonKind.Never:
                    return ComparisonFunc.Never;
                case ComparisonKind.Less:
                    return ComparisonFunc.Less;
                case ComparisonKind.Equal:
                    return ComparisonFunc.Equal;
                case ComparisonKind.LessEqual:
                    return ComparisonFunc.LessEqual;
                case ComparisonKind.Greater:
                    return ComparisonFunc.Greater;
                case ComparisonKind.NotEqual:
                    return ComparisonFunc.NotEqual;
                case ComparisonKind.GreaterEqual:
                    return ComparisonFunc.GreaterEqual;
                case ComparisonKind.Always:
                    return ComparisonFunc.Always;
                default:
                    throw Illegal.Value<ComparisonKind>();
            }
        }

        internal static TextureAddressMode VdToD3D11AddressMode(SamplerAddressMode mode)
        {
            switch (mode)
            {
                case SamplerAddressMode.Wrap:
                    return TextureAddressMode.Wrap;
                case SamplerAddressMode.Mirror:
                    return TextureAddressMode.Mirror;
                case SamplerAddressMode.Clamp:
                    return TextureAddressMode.Clamp;
                case SamplerAddressMode.Border:
                    return TextureAddressMode.Border;
                default:
                    throw Illegal.Value<SamplerAddressMode>();
            }
        }

        internal static Format GetDepthFormat(PixelFormat format)
        {
            switch (format)
            {
                case PixelFormat.R32_Float:
                    return Format.FormatD32Float;
                case PixelFormat.R16_UNorm:
                    return Format.FormatD16Unorm;
                case PixelFormat.D24_UNorm_S8_UInt:
                    return Format.FormatD24UnormS8Uint;
                case PixelFormat.D32_Float_S8_UInt:
                    return Format.FormatD32FloatS8X24Uint;
                default:
                    throw new VeldridException("Invalid depth texture format: " + format);
            }
        }
    }
}
