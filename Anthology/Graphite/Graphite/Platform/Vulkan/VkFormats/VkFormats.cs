using System;

using Silk.NET.Vulkan;

using VkBlendFactor = Silk.NET.Vulkan.BlendFactor;
using VkPrimitiveTopology = Silk.NET.Vulkan.PrimitiveTopology;
using VkSamplerAddressMode = Silk.NET.Vulkan.SamplerAddressMode;

namespace Prowl.Graphite.Vk;

internal static partial class VkFormats
{
    internal static VkSamplerAddressMode ToVkSamplerAddressMode(SamplerAddressMode mode)
    {
        return mode switch
        {
            SamplerAddressMode.Wrap => VkSamplerAddressMode.Repeat,
            SamplerAddressMode.Mirror => VkSamplerAddressMode.MirroredRepeat,
            SamplerAddressMode.Clamp => VkSamplerAddressMode.ClampToEdge,
            SamplerAddressMode.Border => VkSamplerAddressMode.ClampToBorder,
            _ => throw Illegal.Value<SamplerAddressMode>(),
        };
    }

    internal static Filter ToVkFilter(FilterMode mode)
    {
        return mode switch
        {
            FilterMode.Point => Filter.Nearest,
            FilterMode.Linear => Filter.Linear,
            _ => throw Illegal.Value<FilterMode>(),
        };
    }

    internal static SamplerMipmapMode ToVkMipmapMode(FilterMode mode)
    {
        return mode switch
        {
            FilterMode.Point => SamplerMipmapMode.Nearest,
            FilterMode.Linear => SamplerMipmapMode.Linear,
            _ => throw Illegal.Value<FilterMode>(),
        };
    }

    internal static ImageUsageFlags ToVkTextureUsage(TextureUsage vdUsage)
    {
        ImageUsageFlags vkUsage = ImageUsageFlags.TransferDstBit | ImageUsageFlags.TransferSrcBit;
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

    internal static ImageType ToVkTextureType(TextureType type)
    {
        return type switch
        {
            TextureType.Texture1D => ImageType.Type1D,
            TextureType.Texture2D or TextureType.TextureCube => ImageType.Type2D,
            TextureType.Texture3D => ImageType.Type3D,
            _ => throw Illegal.Value<TextureType>(),
        };
    }

    internal static SampleCountFlags ToVkSampleCount(TextureSampleCount sampleCount)
    {
        return sampleCount switch
        {
            TextureSampleCount.Count1 => SampleCountFlags.Count1Bit,
            TextureSampleCount.Count2 => SampleCountFlags.Count2Bit,
            TextureSampleCount.Count4 => SampleCountFlags.Count4Bit,
            TextureSampleCount.Count8 => SampleCountFlags.Count8Bit,
            TextureSampleCount.Count16 => SampleCountFlags.Count16Bit,
            TextureSampleCount.Count32 => SampleCountFlags.Count32Bit,
            _ => throw Illegal.Value<TextureSampleCount>(),
        };
    }

    internal static StencilOp ToVkStencilOp(StencilOperation op)
    {
        return op switch
        {
            StencilOperation.Keep => StencilOp.Keep,
            StencilOperation.Zero => StencilOp.Zero,
            StencilOperation.Replace => StencilOp.Replace,
            StencilOperation.IncrementAndClamp => StencilOp.IncrementAndClamp,
            StencilOperation.DecrementAndClamp => StencilOp.DecrementAndClamp,
            StencilOperation.Invert => StencilOp.Invert,
            StencilOperation.IncrementAndWrap => StencilOp.IncrementAndWrap,
            StencilOperation.DecrementAndWrap => StencilOp.DecrementAndWrap,
            _ => throw Illegal.Value<StencilOperation>(),
        };
    }

    internal static CullModeFlags ToVkCullMode(FaceCullMode cullMode)
    {
        return cullMode switch
        {
            FaceCullMode.Back => CullModeFlags.BackBit,
            FaceCullMode.Front => CullModeFlags.FrontBit,
            FaceCullMode.None => CullModeFlags.None,
            _ => throw Illegal.Value<FaceCullMode>(),
        };
    }

    internal static BlendOp ToVkBlendOp(BlendFunction func)
    {
        return func switch
        {
            BlendFunction.Add => BlendOp.Add,
            BlendFunction.Subtract => BlendOp.Subtract,
            BlendFunction.ReverseSubtract => BlendOp.ReverseSubtract,
            BlendFunction.Minimum => BlendOp.Min,
            BlendFunction.Maximum => BlendOp.Max,
            _ => throw Illegal.Value<BlendFunction>(),
        };
    }

    internal static ColorComponentFlags ToVkColorWriteMask(ColorWriteMask mask)
    {
        ColorComponentFlags flags = 0;

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

    internal static VkPrimitiveTopology ToVkPrimitiveTopology(PrimitiveTopology topology)
    {
        return topology switch
        {
            PrimitiveTopology.TriangleList => VkPrimitiveTopology.TriangleList,
            PrimitiveTopology.TriangleStrip => VkPrimitiveTopology.TriangleStrip,
            PrimitiveTopology.LineList => VkPrimitiveTopology.LineList,
            PrimitiveTopology.LineStrip => VkPrimitiveTopology.LineStrip,
            PrimitiveTopology.PointList => VkPrimitiveTopology.PointList,
            _ => throw Illegal.Value<PrimitiveTopology>(),
        };
    }

    internal static VkBlendFactor ToVkBlendFactor(BlendFactor factor)
    {
        return factor switch
        {
            BlendFactor.Zero => VkBlendFactor.Zero,
            BlendFactor.One => VkBlendFactor.One,
            BlendFactor.SourceAlpha => VkBlendFactor.SrcAlpha,
            BlendFactor.InverseSourceAlpha => VkBlendFactor.OneMinusSrcAlpha,
            BlendFactor.DestinationAlpha => VkBlendFactor.DstAlpha,
            BlendFactor.InverseDestinationAlpha => VkBlendFactor.OneMinusDstAlpha,
            BlendFactor.SourceColor => VkBlendFactor.SrcColor,
            BlendFactor.InverseSourceColor => VkBlendFactor.OneMinusSrcColor,
            BlendFactor.DestinationColor => VkBlendFactor.DstColor,
            BlendFactor.InverseDestinationColor => VkBlendFactor.OneMinusDstColor,
            BlendFactor.BlendFactor => VkBlendFactor.ConstantColor,
            BlendFactor.InverseBlendFactor => VkBlendFactor.OneMinusConstantColor,
            _ => throw Illegal.Value<BlendFactor>(),
        };
    }

    internal static Format ToVkVertexElementFormat(VertexElementFormat format)
    {
        return format switch
        {
            VertexElementFormat.Float1 => Format.R32Sfloat,
            VertexElementFormat.Float2 => Format.R32G32Sfloat,
            VertexElementFormat.Float3 => Format.R32G32B32Sfloat,
            VertexElementFormat.Float4 => Format.R32G32B32A32Sfloat,
            VertexElementFormat.Byte2_Norm => Format.R8G8Unorm,
            VertexElementFormat.Byte2 => Format.R8G8Uint,
            VertexElementFormat.Byte4_Norm => Format.R8G8B8A8Unorm,
            VertexElementFormat.Byte4 => Format.R8G8B8A8Uint,
            VertexElementFormat.SByte2_Norm => Format.R8G8SNorm,
            VertexElementFormat.SByte2 => Format.R8G8Sint,
            VertexElementFormat.SByte4_Norm => Format.R8G8B8A8SNorm,
            VertexElementFormat.SByte4 => Format.R8G8B8A8Sint,
            VertexElementFormat.UShort2_Norm => Format.R16G16Unorm,
            VertexElementFormat.UShort2 => Format.R16G16Uint,
            VertexElementFormat.UShort4_Norm => Format.R16G16B16A16Unorm,
            VertexElementFormat.UShort4 => Format.R16G16B16A16Uint,
            VertexElementFormat.Short2_Norm => Format.R16G16SNorm,
            VertexElementFormat.Short2 => Format.R16G16Sint,
            VertexElementFormat.Short4_Norm => Format.R16G16B16A16SNorm,
            VertexElementFormat.Short4 => Format.R16G16B16A16Sint,
            VertexElementFormat.UInt1 => Format.R32Uint,
            VertexElementFormat.UInt2 => Format.R32G32Uint,
            VertexElementFormat.UInt3 => Format.R32G32B32Uint,
            VertexElementFormat.UInt4 => Format.R32G32B32A32Uint,
            VertexElementFormat.Int1 => Format.R32Sint,
            VertexElementFormat.Int2 => Format.R32G32Sint,
            VertexElementFormat.Int3 => Format.R32G32B32Sint,
            VertexElementFormat.Int4 => Format.R32G32B32A32Sint,
            VertexElementFormat.Half1 => Format.R16Sfloat,
            VertexElementFormat.Half2 => Format.R16G16Sfloat,
            VertexElementFormat.Half4 => Format.R16G16B16A16Sfloat,
            _ => throw Illegal.Value<VertexElementFormat>(),
        };
    }

    internal static ShaderStageFlags ToVkShaderStages(ShaderStages stage)
    {
        ShaderStageFlags ret = 0;

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

    internal static BorderColor ToVkSamplerBorderColor(SamplerBorderColor borderColor)
    {
        return borderColor switch
        {
            SamplerBorderColor.TransparentBlack => BorderColor.FloatTransparentBlack,
            SamplerBorderColor.OpaqueBlack => BorderColor.FloatOpaqueBlack,
            SamplerBorderColor.OpaqueWhite => BorderColor.FloatOpaqueWhite,
            _ => throw Illegal.Value<SamplerBorderColor>(),
        };
    }

    internal static IndexType ToVkIndexFormat(IndexFormat format)
    {
        return format switch
        {
            IndexFormat.UInt16 => IndexType.Uint16,
            IndexFormat.UInt32 => IndexType.Uint32,
            _ => throw Illegal.Value<IndexFormat>(),
        };
    }

    internal static CompareOp ToVkCompareOp(ComparisonKind comparisonKind)
    {
        return comparisonKind switch
        {
            ComparisonKind.Never => CompareOp.Never,
            ComparisonKind.Less => CompareOp.Less,
            ComparisonKind.Equal => CompareOp.Equal,
            ComparisonKind.LessEqual => CompareOp.LessOrEqual,
            ComparisonKind.Greater => CompareOp.Greater,
            ComparisonKind.NotEqual => CompareOp.NotEqual,
            ComparisonKind.GreaterEqual => CompareOp.GreaterOrEqual,
            ComparisonKind.Always => CompareOp.Always,
            _ => throw Illegal.Value<ComparisonKind>(),
        };
    }

    internal static PixelFormat ToPixelFormat(Format vkFormat)
    {
        return vkFormat switch
        {
            Format.R8Unorm => PixelFormat.R8_UNorm,
            Format.R8SNorm => PixelFormat.R8_SNorm,
            Format.R8Uint => PixelFormat.R8_UInt,
            Format.R8Sint => PixelFormat.R8_SInt,
            Format.R16Unorm => PixelFormat.R16_UNorm,
            Format.R16SNorm => PixelFormat.R16_SNorm,
            Format.R16Uint => PixelFormat.R16_UInt,
            Format.R16Sint => PixelFormat.R16_SInt,
            Format.R16Sfloat => PixelFormat.R16_Float,
            Format.R32Uint => PixelFormat.R32_UInt,
            Format.R32Sint => PixelFormat.R32_SInt,
            Format.R32Sfloat or Format.D32Sfloat => PixelFormat.R32_Float,
            Format.R8G8Unorm => PixelFormat.R8_G8_UNorm,
            Format.R8G8SNorm => PixelFormat.R8_G8_SNorm,
            Format.R8G8Uint => PixelFormat.R8_G8_UInt,
            Format.R8G8Sint => PixelFormat.R8_G8_SInt,
            Format.R16G16Unorm => PixelFormat.R16_G16_UNorm,
            Format.R16G16SNorm => PixelFormat.R16_G16_SNorm,
            Format.R16G16Uint => PixelFormat.R16_G16_UInt,
            Format.R16G16Sint => PixelFormat.R16_G16_SInt,
            Format.R16G16Sfloat => PixelFormat.R16_G16_Float,
            Format.R32G32Uint => PixelFormat.R32_G32_UInt,
            Format.R32G32Sint => PixelFormat.R32_G32_SInt,
            Format.R32G32Sfloat => PixelFormat.R32_G32_Float,
            Format.R8G8B8A8Unorm => PixelFormat.R8_G8_B8_A8_UNorm,
            Format.R8G8B8A8Srgb => PixelFormat.R8_G8_B8_A8_UNorm_SRgb,
            Format.B8G8R8A8Unorm => PixelFormat.B8_G8_R8_A8_UNorm,
            Format.B8G8R8A8Srgb => PixelFormat.B8_G8_R8_A8_UNorm_SRgb,
            Format.R8G8B8A8SNorm => PixelFormat.R8_G8_B8_A8_SNorm,
            Format.R8G8B8A8Uint => PixelFormat.R8_G8_B8_A8_UInt,
            Format.R8G8B8A8Sint => PixelFormat.R8_G8_B8_A8_SInt,
            Format.R16G16B16A16Unorm => PixelFormat.R16_G16_B16_A16_UNorm,
            Format.R16G16B16A16SNorm => PixelFormat.R16_G16_B16_A16_SNorm,
            Format.R16G16B16A16Uint => PixelFormat.R16_G16_B16_A16_UInt,
            Format.R16G16B16A16Sint => PixelFormat.R16_G16_B16_A16_SInt,
            Format.R16G16B16A16Sfloat => PixelFormat.R16_G16_B16_A16_Float,
            Format.R32G32B32A32Uint => PixelFormat.R32_G32_B32_A32_UInt,
            Format.R32G32B32A32Sint => PixelFormat.R32_G32_B32_A32_SInt,
            Format.R32G32B32A32Sfloat => PixelFormat.R32_G32_B32_A32_Float,
            Format.BC1RgbUnormBlock => PixelFormat.BC1_Rgb_UNorm,
            Format.BC1RgbSrgbBlock => PixelFormat.BC1_Rgb_UNorm_SRgb,
            Format.BC1RgbaUnormBlock => PixelFormat.BC1_Rgba_UNorm,
            Format.BC1RgbaSrgbBlock => PixelFormat.BC1_Rgba_UNorm_SRgb,
            Format.BC2UnormBlock => PixelFormat.BC2_UNorm,
            Format.BC2SrgbBlock => PixelFormat.BC2_UNorm_SRgb,
            Format.BC3UnormBlock => PixelFormat.BC3_UNorm,
            Format.BC3SrgbBlock => PixelFormat.BC3_UNorm_SRgb,
            Format.BC4UnormBlock => PixelFormat.BC4_UNorm,
            Format.BC4SNormBlock => PixelFormat.BC4_SNorm,
            Format.BC5UnormBlock => PixelFormat.BC5_UNorm,
            Format.BC5SNormBlock => PixelFormat.BC5_SNorm,
            Format.BC7UnormBlock => PixelFormat.BC7_UNorm,
            Format.BC7SrgbBlock => PixelFormat.BC7_UNorm_SRgb,
            Format.Etc2R8G8B8UnormBlock => PixelFormat.ETC2_R8_G8_B8_UNorm,
            Format.Etc2R8G8B8A1UnormBlock => PixelFormat.ETC2_R8_G8_B8_A1_UNorm,
            Format.Etc2R8G8B8A8UnormBlock => PixelFormat.ETC2_R8_G8_B8_A8_UNorm,
            Format.Astc4x4UnormBlock => PixelFormat.ASTC_4x4_UNorm,
            Format.Astc4x4SrgbBlock => PixelFormat.ASTC_4x4_UNorm_SRgb,
            Format.Astc5x4UnormBlock => PixelFormat.ASTC_5x4_UNorm,
            Format.Astc5x4SrgbBlock => PixelFormat.ASTC_5x4_UNorm_SRgb,
            Format.Astc5x5UnormBlock => PixelFormat.ASTC_5x5_UNorm,
            Format.Astc5x5SrgbBlock => PixelFormat.ASTC_5x5_UNorm_SRgb,
            Format.Astc6x5UnormBlock => PixelFormat.ASTC_6x5_UNorm,
            Format.Astc6x5SrgbBlock => PixelFormat.ASTC_6x5_UNorm_SRgb,
            Format.Astc6x6UnormBlock => PixelFormat.ASTC_6x6_UNorm,
            Format.Astc6x6SrgbBlock => PixelFormat.ASTC_6x6_UNorm_SRgb,
            Format.Astc8x5UnormBlock => PixelFormat.ASTC_8x5_UNorm,
            Format.Astc8x5SrgbBlock => PixelFormat.ASTC_8x5_UNorm_SRgb,
            Format.Astc8x6UnormBlock => PixelFormat.ASTC_8x6_UNorm,
            Format.Astc8x6SrgbBlock => PixelFormat.ASTC_8x6_UNorm_SRgb,
            Format.Astc8x8UnormBlock => PixelFormat.ASTC_8x8_UNorm,
            Format.Astc8x8SrgbBlock => PixelFormat.ASTC_8x8_UNorm_SRgb,
            Format.Astc10x5UnormBlock => PixelFormat.ASTC_10x5_UNorm,
            Format.Astc10x5SrgbBlock => PixelFormat.ASTC_10x5_UNorm_SRgb,
            Format.Astc10x6UnormBlock => PixelFormat.ASTC_10x6_UNorm,
            Format.Astc10x6SrgbBlock => PixelFormat.ASTC_10x6_UNorm_SRgb,
            Format.Astc10x8UnormBlock => PixelFormat.ASTC_10x8_UNorm,
            Format.Astc10x8SrgbBlock => PixelFormat.ASTC_10x8_UNorm_SRgb,
            Format.Astc10x10UnormBlock => PixelFormat.ASTC_10x10_UNorm,
            Format.Astc10x10SrgbBlock => PixelFormat.ASTC_10x10_UNorm_SRgb,
            Format.Astc12x10UnormBlock => PixelFormat.ASTC_12x10_UNorm,
            Format.Astc12x10SrgbBlock => PixelFormat.ASTC_12x10_UNorm_SRgb,
            Format.Astc12x12UnormBlock => PixelFormat.ASTC_12x12_UNorm,
            Format.Astc12x12SrgbBlock => PixelFormat.ASTC_12x12_UNorm_SRgb,
            Format.A2B10G10R10UnormPack32 => PixelFormat.R10_G10_B10_A2_UNorm,
            Format.A2B10G10R10UintPack32 => PixelFormat.R10_G10_B10_A2_UInt,
            Format.B10G11R11UfloatPack32 => PixelFormat.R11_G11_B10_Float,
            _ => throw Illegal.Value<Format>(),
        };
    }
}
