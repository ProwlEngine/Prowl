using System;
using System.Collections.Generic;
using Silk.NET.Vulkan;
using VkSamplerAddressMode = Silk.NET.Vulkan.SamplerAddressMode;
using VkBlendFactor = Silk.NET.Vulkan.BlendFactor;
using VkPrimitiveTopology = Silk.NET.Vulkan.PrimitiveTopology;

namespace Veldrid.Vk
{
    internal static partial class VkFormats
    {
        internal static VkSamplerAddressMode VdToVkSamplerAddressMode(SamplerAddressMode mode)
        {
            switch (mode)
            {
                case SamplerAddressMode.Wrap:
                    return VkSamplerAddressMode.Repeat;
                case SamplerAddressMode.Mirror:
                    return VkSamplerAddressMode.MirroredRepeat;
                case SamplerAddressMode.Clamp:
                    return VkSamplerAddressMode.ClampToEdge;
                case SamplerAddressMode.Border:
                    return VkSamplerAddressMode.ClampToBorder;
                default:
                    throw Illegal.Value<SamplerAddressMode>();
            }
        }

        internal static void GetFilterParams(
            SamplerFilter filter,
            out Filter minFilter,
            out Filter magFilter,
            out SamplerMipmapMode mipmapMode)
        {
            switch (filter)
            {
                case SamplerFilter.Anisotropic:
                    minFilter = Filter.Linear;
                    magFilter = Filter.Linear;
                    mipmapMode = SamplerMipmapMode.Linear;
                    break;
                case SamplerFilter.MinPoint_MagPoint_MipPoint:
                    minFilter = Filter.Nearest;
                    magFilter = Filter.Nearest;
                    mipmapMode = SamplerMipmapMode.Nearest;
                    break;
                case SamplerFilter.MinPoint_MagPoint_MipLinear:
                    minFilter = Filter.Nearest;
                    magFilter = Filter.Nearest;
                    mipmapMode = SamplerMipmapMode.Linear;
                    break;
                case SamplerFilter.MinPoint_MagLinear_MipPoint:
                    minFilter = Filter.Nearest;
                    magFilter = Filter.Linear;
                    mipmapMode = SamplerMipmapMode.Nearest;
                    break;
                case SamplerFilter.MinPoint_MagLinear_MipLinear:
                    minFilter = Filter.Nearest;
                    magFilter = Filter.Linear;
                    mipmapMode = SamplerMipmapMode.Linear;
                    break;
                case SamplerFilter.MinLinear_MagPoint_MipPoint:
                    minFilter = Filter.Linear;
                    magFilter = Filter.Nearest;
                    mipmapMode = SamplerMipmapMode.Nearest;
                    break;
                case SamplerFilter.MinLinear_MagPoint_MipLinear:
                    minFilter = Filter.Linear;
                    magFilter = Filter.Nearest;
                    mipmapMode = SamplerMipmapMode.Linear;
                    break;
                case SamplerFilter.MinLinear_MagLinear_MipPoint:
                    minFilter = Filter.Linear;
                    magFilter = Filter.Linear;
                    mipmapMode = SamplerMipmapMode.Nearest;
                    break;
                case SamplerFilter.MinLinear_MagLinear_MipLinear:
                    minFilter = Filter.Linear;
                    magFilter = Filter.Linear;
                    mipmapMode = SamplerMipmapMode.Linear;
                    break;
                default:
                    throw Illegal.Value<SamplerFilter>();
            }
        }

        internal static ImageUsageFlags VdToVkTextureUsage(TextureUsage vdUsage)
        {
            ImageUsageFlags vkUsage = (ImageUsageFlags)0;

            vkUsage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit;
            bool isDepthStencil = (vdUsage & TextureUsage.DepthStencil) == TextureUsage.DepthStencil;
            if ((vdUsage & TextureUsage.Sampled) == TextureUsage.Sampled)
            {
                vkUsage |= ImageUsageFlags.SampledBit;
            }
            if (isDepthStencil)
            {
                vkUsage |= ImageUsageFlags.DepthStencilAttachmentBit;
            }
            if ((vdUsage & TextureUsage.RenderTarget) == TextureUsage.RenderTarget)
            {
                vkUsage |= ImageUsageFlags.ColorAttachmentBit;
            }
            if ((vdUsage & TextureUsage.Storage) == TextureUsage.Storage)
            {
                vkUsage |= ImageUsageFlags.StorageBit;
            }

            return vkUsage;
        }

        internal static ImageType VdToVkTextureType(TextureType type)
        {
            switch (type)
            {
                case TextureType.Texture1D:
                    return ImageType.Type1D;
                case TextureType.Texture2D:
                    return ImageType.Type2D;
                case TextureType.Texture3D:
                    return ImageType.Type3D;
                default:
                    throw Illegal.Value<TextureType>();
            }
        }

        internal static DescriptorType VdToVkDescriptorType(ResourceKind kind, ResourceLayoutElementOptions options)
        {
            bool dynamicBinding = (options & ResourceLayoutElementOptions.DynamicBinding) != 0;
            switch (kind)
            {
                case ResourceKind.UniformBuffer:
                    return dynamicBinding ? DescriptorType.UniformBufferDynamic : DescriptorType.UniformBuffer;
                case ResourceKind.StructuredBufferReadWrite:
                case ResourceKind.StructuredBufferReadOnly:
                    return dynamicBinding ? DescriptorType.StorageBufferDynamic : DescriptorType.StorageBuffer;
                case ResourceKind.TextureReadOnly:
                    return DescriptorType.SampledImage;
                case ResourceKind.TextureReadWrite:
                    return DescriptorType.StorageImage;
                case ResourceKind.Sampler:
                    return DescriptorType.Sampler;
                default:
                    throw Illegal.Value<ResourceKind>();
            }
        }

        internal static SampleCountFlags VdToVkSampleCount(TextureSampleCount sampleCount)
        {
            switch (sampleCount)
            {
                case TextureSampleCount.Count1:
                    return SampleCountFlags.Count1Bit;
                case TextureSampleCount.Count2:
                    return SampleCountFlags.Count2Bit;
                case TextureSampleCount.Count4:
                    return SampleCountFlags.Count4Bit;
                case TextureSampleCount.Count8:
                    return SampleCountFlags.Count8Bit;
                case TextureSampleCount.Count16:
                    return SampleCountFlags.Count16Bit;
                case TextureSampleCount.Count32:
                    return SampleCountFlags.Count32Bit;
                default:
                    throw Illegal.Value<TextureSampleCount>();
            }
        }

        internal static StencilOp VdToVkStencilOp(StencilOperation op)
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
                    return StencilOp.IncrementAndClamp;
                case StencilOperation.DecrementAndClamp:
                    return StencilOp.DecrementAndClamp;
                case StencilOperation.Invert:
                    return StencilOp.Invert;
                case StencilOperation.IncrementAndWrap:
                    return StencilOp.IncrementAndWrap;
                case StencilOperation.DecrementAndWrap:
                    return StencilOp.DecrementAndWrap;
                default:
                    throw Illegal.Value<StencilOperation>();
            }
        }

        internal static PolygonMode VdToVkPolygonMode(PolygonFillMode fillMode)
        {
            switch (fillMode)
            {
                case PolygonFillMode.Solid:
                    return PolygonMode.Fill;
                case PolygonFillMode.Wireframe:
                    return PolygonMode.Line;
                default:
                    throw Illegal.Value<PolygonFillMode>();
            }
        }

        internal static CullModeFlags VdToVkCullMode(FaceCullMode cullMode)
        {
            switch (cullMode)
            {
                case FaceCullMode.Back:
                    return CullModeFlags.BackBit;
                case FaceCullMode.Front:
                    return CullModeFlags.FrontBit;
                case FaceCullMode.None:
                    return CullModeFlags.None;
                default:
                    throw Illegal.Value<FaceCullMode>();
            }
        }

        internal static BlendOp VdToVkBlendOp(BlendFunction func)
        {
            switch (func)
            {
                case BlendFunction.Add:
                    return BlendOp.Add;
                case BlendFunction.Subtract:
                    return BlendOp.Subtract;
                case BlendFunction.ReverseSubtract:
                    return BlendOp.ReverseSubtract;
                case BlendFunction.Minimum:
                    return BlendOp.Min;
                case BlendFunction.Maximum:
                    return BlendOp.Max;
                default:
                    throw Illegal.Value<BlendFunction>();
            }
        }

        internal static ColorComponentFlags VdToVkColorWriteMask(ColorWriteMask mask)
        {
            ColorComponentFlags flags = (ColorComponentFlags)0;

            if ((mask & ColorWriteMask.Red) == ColorWriteMask.Red)
                flags |= ColorComponentFlags.RBit;
            if ((mask & ColorWriteMask.Green) == ColorWriteMask.Green)
                flags |= ColorComponentFlags.GBit;
            if ((mask & ColorWriteMask.Blue) == ColorWriteMask.Blue)
                flags |= ColorComponentFlags.BBit;
            if ((mask & ColorWriteMask.Alpha) == ColorWriteMask.Alpha)
                flags |= ColorComponentFlags.ABit;

            return flags;
        }

        internal static VkPrimitiveTopology VdToVkPrimitiveTopology(PrimitiveTopology topology)
        {
            switch (topology)
            {
                case PrimitiveTopology.TriangleList:
                    return VkPrimitiveTopology.TriangleList;
                case PrimitiveTopology.TriangleStrip:
                    return VkPrimitiveTopology.TriangleStrip;
                case PrimitiveTopology.LineList:
                    return VkPrimitiveTopology.LineList;
                case PrimitiveTopology.LineStrip:
                    return VkPrimitiveTopology.LineStrip;
                case PrimitiveTopology.PointList:
                    return VkPrimitiveTopology.PointList;
                default:
                    throw Illegal.Value<PrimitiveTopology>();
            }
        }

        internal static uint GetSpecializationConstantSize(ShaderConstantType type)
        {
            switch (type)
            {
                case ShaderConstantType.Bool:
                    return 4;
                case ShaderConstantType.UInt16:
                    return 2;
                case ShaderConstantType.Int16:
                    return 2;
                case ShaderConstantType.UInt32:
                    return 4;
                case ShaderConstantType.Int32:
                    return 4;
                case ShaderConstantType.UInt64:
                    return 8;
                case ShaderConstantType.Int64:
                    return 8;
                case ShaderConstantType.Float:
                    return 4;
                case ShaderConstantType.Double:
                    return 8;
                default:
                    throw Illegal.Value<ShaderConstantType>();
            }
        }

        internal static VkBlendFactor VdToVkBlendFactor(BlendFactor factor)
        {
            switch (factor)
            {
                case BlendFactor.Zero:
                    return VkBlendFactor.Zero;
                case BlendFactor.One:
                    return VkBlendFactor.One;
                case BlendFactor.SourceAlpha:
                    return VkBlendFactor.SrcAlpha;
                case BlendFactor.InverseSourceAlpha:
                    return VkBlendFactor.OneMinusSrcAlpha;
                case BlendFactor.DestinationAlpha:
                    return VkBlendFactor.DstAlpha;
                case BlendFactor.InverseDestinationAlpha:
                    return VkBlendFactor.OneMinusDstAlpha;
                case BlendFactor.SourceColor:
                    return VkBlendFactor.SrcColor;
                case BlendFactor.InverseSourceColor:
                    return VkBlendFactor.OneMinusSrcColor;
                case BlendFactor.DestinationColor:
                    return VkBlendFactor.DstColor;
                case BlendFactor.InverseDestinationColor:
                    return VkBlendFactor.OneMinusDstColor;
                case BlendFactor.BlendFactor:
                    return VkBlendFactor.ConstantColor;
                case BlendFactor.InverseBlendFactor:
                    return VkBlendFactor.OneMinusConstantColor;
                default:
                    throw Illegal.Value<BlendFactor>();
            }
        }

        internal static Format VdToVkVertexElementFormat(VertexElementFormat format)
        {
            switch (format)
            {
                case VertexElementFormat.Float1:
                    return Format.R32Sfloat;
                case VertexElementFormat.Float2:
                    return Format.R32G32Sfloat;
                case VertexElementFormat.Float3:
                    return Format.R32G32B32Sfloat;
                case VertexElementFormat.Float4:
                    return Format.R32G32B32A32Sfloat;
                case VertexElementFormat.Byte2_Norm:
                    return Format.R8G8Unorm;
                case VertexElementFormat.Byte2:
                    return Format.R8G8Uint;
                case VertexElementFormat.Byte4_Norm:
                    return Format.R8G8B8A8Unorm;
                case VertexElementFormat.Byte4:
                    return Format.R8G8B8A8Uint;
                case VertexElementFormat.SByte2_Norm:
                    return Format.R8G8SNorm;
                case VertexElementFormat.SByte2:
                    return Format.R8G8Sint;
                case VertexElementFormat.SByte4_Norm:
                    return Format.R8G8B8A8SNorm;
                case VertexElementFormat.SByte4:
                    return Format.R8G8B8A8Sint;
                case VertexElementFormat.UShort2_Norm:
                    return Format.R16G16Unorm;
                case VertexElementFormat.UShort2:
                    return Format.R16G16Uint;
                case VertexElementFormat.UShort4_Norm:
                    return Format.R16G16B16A16Unorm;
                case VertexElementFormat.UShort4:
                    return Format.R16G16B16A16Uint;
                case VertexElementFormat.Short2_Norm:
                    return Format.R16G16SNorm;
                case VertexElementFormat.Short2:
                    return Format.R16G16Sint;
                case VertexElementFormat.Short4_Norm:
                    return Format.R16G16B16A16SNorm;
                case VertexElementFormat.Short4:
                    return Format.R16G16B16A16Sint;
                case VertexElementFormat.UInt1:
                    return Format.R32Uint;
                case VertexElementFormat.UInt2:
                    return Format.R32G32Uint;
                case VertexElementFormat.UInt3:
                    return Format.R32G32B32Uint;
                case VertexElementFormat.UInt4:
                    return Format.R32G32B32A32Uint;
                case VertexElementFormat.Int1:
                    return Format.R32Sint;
                case VertexElementFormat.Int2:
                    return Format.R32G32Sint;
                case VertexElementFormat.Int3:
                    return Format.R32G32B32Sint;
                case VertexElementFormat.Int4:
                    return Format.R32G32B32A32Sint;
                case VertexElementFormat.Half1:
                    return Format.R16Sfloat;
                case VertexElementFormat.Half2:
                    return Format.R16G16Sfloat;
                case VertexElementFormat.Half4:
                    return Format.R16G16B16A16Sfloat;
                default:
                    throw Illegal.Value<VertexElementFormat>();
            }
        }

        internal static ShaderStageFlags VdToVkShaderStages(ShaderStages stage)
        {
            ShaderStageFlags ret = (ShaderStageFlags)0;

            if ((stage & ShaderStages.Vertex) == ShaderStages.Vertex)
                ret |= ShaderStageFlags.VertexBit;

            if ((stage & ShaderStages.Geometry) == ShaderStages.Geometry)
                ret |= ShaderStageFlags.GeometryBit;

            if ((stage & ShaderStages.TessellationControl) == ShaderStages.TessellationControl)
                ret |= ShaderStageFlags.TessellationControlBit;

            if ((stage & ShaderStages.TessellationEvaluation) == ShaderStages.TessellationEvaluation)
                ret |= ShaderStageFlags.TessellationEvaluationBit;

            if ((stage & ShaderStages.Fragment) == ShaderStages.Fragment)
                ret |= ShaderStageFlags.FragmentBit;

            if ((stage & ShaderStages.Compute) == ShaderStages.Compute)
                ret |= ShaderStageFlags.ComputeBit;

            return ret;
        }

        internal static BorderColor VdToVkSamplerBorderColor(SamplerBorderColor borderColor)
        {
            switch (borderColor)
            {
                case SamplerBorderColor.TransparentBlack:
                    return BorderColor.FloatTransparentBlack;
                case SamplerBorderColor.OpaqueBlack:
                    return BorderColor.FloatOpaqueBlack;
                case SamplerBorderColor.OpaqueWhite:
                    return BorderColor.FloatOpaqueWhite;
                default:
                    throw Illegal.Value<SamplerBorderColor>();
            }
        }

        internal static IndexType VdToVkIndexFormat(IndexFormat format)
        {
            switch (format)
            {
                case IndexFormat.UInt16:
                    return IndexType.Uint16;
                case IndexFormat.UInt32:
                    return IndexType.Uint32;
                default:
                    throw Illegal.Value<IndexFormat>();
            }
        }

        internal static CompareOp VdToVkCompareOp(ComparisonKind comparisonKind)
        {
            switch (comparisonKind)
            {
                case ComparisonKind.Never:
                    return CompareOp.Never;
                case ComparisonKind.Less:
                    return CompareOp.Less;
                case ComparisonKind.Equal:
                    return CompareOp.Equal;
                case ComparisonKind.LessEqual:
                    return CompareOp.LessOrEqual;
                case ComparisonKind.Greater:
                    return CompareOp.Greater;
                case ComparisonKind.NotEqual:
                    return CompareOp.NotEqual;
                case ComparisonKind.GreaterEqual:
                    return CompareOp.GreaterOrEqual;
                case ComparisonKind.Always:
                    return CompareOp.Always;
                default:
                    throw Illegal.Value<ComparisonKind>();
            }
        }

        internal static PixelFormat VkToVdPixelFormat(Format vkFormat)
        {
            switch (vkFormat)
            {
                case Format.R8Unorm:
                    return PixelFormat.R8_UNorm;
                case Format.R8SNorm:
                    return PixelFormat.R8_SNorm;
                case Format.R8Uint:
                    return PixelFormat.R8_UInt;
                case Format.R8Sint:
                    return PixelFormat.R8_SInt;

                case Format.R16Unorm:
                    return PixelFormat.R16_UNorm;
                case Format.R16SNorm:
                    return PixelFormat.R16_SNorm;
                case Format.R16Uint:
                    return PixelFormat.R16_UInt;
                case Format.R16Sint:
                    return PixelFormat.R16_SInt;
                case Format.R16Sfloat:
                    return PixelFormat.R16_Float;

                case Format.R32Uint:
                    return PixelFormat.R32_UInt;
                case Format.R32Sint:
                    return PixelFormat.R32_SInt;
                case Format.R32Sfloat:
                case Format.D32Sfloat:
                    return PixelFormat.R32_Float;

                case Format.R8G8Unorm:
                    return PixelFormat.R8_G8_UNorm;
                case Format.R8G8SNorm:
                    return PixelFormat.R8_G8_SNorm;
                case Format.R8G8Uint:
                    return PixelFormat.R8_G8_UInt;
                case Format.R8G8Sint:
                    return PixelFormat.R8_G8_SInt;

                case Format.R16G16Unorm:
                    return PixelFormat.R16_G16_UNorm;
                case Format.R16G16SNorm:
                    return PixelFormat.R16_G16_SNorm;
                case Format.R16G16Uint:
                    return PixelFormat.R16_G16_UInt;
                case Format.R16G16Sint:
                    return PixelFormat.R16_G16_SInt;
                case Format.R16G16Sfloat:
                    return PixelFormat.R16_G16_Float;

                case Format.R32G32Uint:
                    return PixelFormat.R32_G32_UInt;
                case Format.R32G32Sint:
                    return PixelFormat.R32_G32_SInt;
                case Format.R32G32Sfloat:
                    return PixelFormat.R32_G32_Float;

                case Format.R8G8B8A8Unorm:
                    return PixelFormat.R8_G8_B8_A8_UNorm;
                case Format.R8G8B8A8Srgb:
                    return PixelFormat.R8_G8_B8_A8_UNorm_SRgb;
                case Format.B8G8R8A8Unorm:
                    return PixelFormat.B8_G8_R8_A8_UNorm;
                case Format.B8G8R8A8Srgb:
                    return PixelFormat.B8_G8_R8_A8_UNorm_SRgb;
                case Format.R8G8B8A8SNorm:
                    return PixelFormat.R8_G8_B8_A8_SNorm;
                case Format.R8G8B8A8Uint:
                    return PixelFormat.R8_G8_B8_A8_UInt;
                case Format.R8G8B8A8Sint:
                    return PixelFormat.R8_G8_B8_A8_SInt;

                case Format.R16G16B16A16Unorm:
                    return PixelFormat.R16_G16_B16_A16_UNorm;
                case Format.R16G16B16A16SNorm:
                    return PixelFormat.R16_G16_B16_A16_SNorm;
                case Format.R16G16B16A16Uint:
                    return PixelFormat.R16_G16_B16_A16_UInt;
                case Format.R16G16B16A16Sint:
                    return PixelFormat.R16_G16_B16_A16_SInt;
                case Format.R16G16B16A16Sfloat:
                    return PixelFormat.R16_G16_B16_A16_Float;

                case Format.R32G32B32A32Uint:
                    return PixelFormat.R32_G32_B32_A32_UInt;
                case Format.R32G32B32A32Sint:
                    return PixelFormat.R32_G32_B32_A32_SInt;
                case Format.R32G32B32A32Sfloat:
                    return PixelFormat.R32_G32_B32_A32_Float;

                case Format.BC1RgbUnormBlock:
                    return PixelFormat.BC1_Rgb_UNorm;
                case Format.BC1RgbSrgbBlock:
                    return PixelFormat.BC1_Rgb_UNorm_SRgb;
                case Format.BC1RgbaUnormBlock:
                    return PixelFormat.BC1_Rgba_UNorm;
                case Format.BC1RgbaSrgbBlock:
                    return PixelFormat.BC1_Rgba_UNorm_SRgb;
                case Format.BC2UnormBlock:
                    return PixelFormat.BC2_UNorm;
                case Format.BC2SrgbBlock:
                    return PixelFormat.BC2_UNorm_SRgb;
                case Format.BC3UnormBlock:
                    return PixelFormat.BC3_UNorm;
                case Format.BC3SrgbBlock:
                    return PixelFormat.BC3_UNorm_SRgb;
                case Format.BC4UnormBlock:
                    return PixelFormat.BC4_UNorm;
                case Format.BC4SNormBlock:
                    return PixelFormat.BC4_SNorm;
                case Format.BC5UnormBlock:
                    return PixelFormat.BC5_UNorm;
                case Format.BC5SNormBlock:
                    return PixelFormat.BC5_SNorm;
                case Format.BC7UnormBlock:
                    return PixelFormat.BC7_UNorm;
                case Format.BC7SrgbBlock:
                    return PixelFormat.BC7_UNorm_SRgb;

                case Format.A2B10G10R10UnormPack32:
                    return PixelFormat.R10_G10_B10_A2_UNorm;
                case Format.A2B10G10R10UintPack32:
                    return PixelFormat.R10_G10_B10_A2_UInt;
                case Format.B10G11R11UfloatPack32:
                    return PixelFormat.R11_G11_B10_Float;

                default:
                    throw Illegal.Value<Format>();
            }
        }
    }
}
