namespace Prowl.Graphite;

public abstract partial class ResourceFactory
{
    private void CreateTexture_CheckDescription(ref TextureDescription description)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (description.Width == 0 || description.Height == 0 || description.Depth == 0)
        {
            throw new RenderException("Width, Height, and Depth must be non-zero.");
        }
        if ((description.Format == PixelFormat.D24_UNorm_S8_UInt || description.Format == PixelFormat.D32_Float_S8_UInt)
            && (description.Usage & TextureUsage.DepthStencil) == 0)
        {
            throw new RenderException("The givel PixelFormat can only be used in a Texture with DepthStencil usage.");
        }
        if ((description.Type == TextureType.Texture1D || description.Type == TextureType.Texture3D)
            && description.SampleCount != TextureSampleCount.Count1)
        {
            throw new RenderException(
                $"1D and 3D Textures must use {nameof(TextureSampleCount)}.{nameof(TextureSampleCount.Count1)}.");
        }
        if (description.Type == TextureType.Texture1D && !Features.Texture1D)
        {
            throw new RenderException($"1D Textures are not supported by this device.");
        }
        if ((description.Usage & TextureUsage.Staging) != 0 && description.Usage != TextureUsage.Staging)
        {
            throw new RenderException($"{nameof(TextureUsage)}.{nameof(TextureUsage.Staging)} cannot be combined with any other flags.");
        }
        if ((description.Usage & TextureUsage.DepthStencil) != 0 && (description.Usage & TextureUsage.GenerateMipmaps) != 0)
        {
            throw new RenderException(
                $"{nameof(TextureUsage)}.{nameof(TextureUsage.DepthStencil)} and {nameof(TextureUsage)}.{nameof(TextureUsage.GenerateMipmaps)} cannot be combined.");
        }
    }

    private void CreateTextureView_CheckDescription(ref TextureViewDescription description)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (description.MipLevels == 0 || description.ArrayLayers == 0
            || (description.BaseMipLevel + description.MipLevels) > description.Target.MipLevels
            || (description.BaseArrayLayer + description.ArrayLayers) > description.Target.ArrayLayers)
        {
            throw new RenderException(
                "TextureView mip level and array layer range must be contained in the target Texture.");
        }
        if ((description.Target.Usage & TextureUsage.Sampled) == 0
            && (description.Target.Usage & TextureUsage.Storage) == 0)
        {
            throw new RenderException(
                "To create a TextureView, the target texture must have either Sampled or Storage usage flags.");
        }
        if (!Features.SubsetTextureView &&
            (description.BaseMipLevel != 0 || description.MipLevels != description.Target.MipLevels
            || description.BaseArrayLayer != 0 || description.ArrayLayers != description.Target.ArrayLayers))
        {
            throw new RenderException("GraphicsDevice does not support subset TextureViews.");
        }
        if (description.Format != null && description.Format != description.Target.Format)
        {
            if (!FormatHelpers.IsFormatViewCompatible(description.Format.Value, description.Target.Format))
            {
                throw new RenderException(
                    $"Cannot create a TextureView with format {description.Format.Value} targeting a Texture with format " +
                    $"{description.Target.Format}. A TextureView's format must have the same size and number of " +
                    $"components as the underlying Texture's format, or the same format.");
            }
        }
    }

    private void CreateBuffer_CheckDescription(ref BufferDescription description)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        BufferUsage usage = description.Usage;
        if ((usage & BufferUsage.StructuredBufferReadOnly) == BufferUsage.StructuredBufferReadOnly
            || (usage & BufferUsage.StructuredBufferReadWrite) == BufferUsage.StructuredBufferReadWrite)
        {
            if (!Features.StructuredBuffer)
            {
                throw new RenderException("GraphicsDevice does not support structured buffers.");
            }

            if (description.StructureByteStride == 0)
            {
                throw new RenderException("Structured Buffer objects must have a non-zero StructureByteStride.");
            }

            if ((usage & BufferUsage.UniformBuffer) != 0)
            {
                throw new RenderException(
                    $"Structured Buffer objects cannot specify {nameof(BufferUsage)}.{nameof(BufferUsage.UniformBuffer)}.");
            }

            if (description.UseTypedHlslBinding
                    && (usage & (BufferUsage.VertexBuffer | BufferUsage.IndexBuffer | BufferUsage.IndirectBuffer)) != 0)
            {
                throw new RenderException(
                    $"A structured buffer with {nameof(BufferDescription.UseTypedHlslBinding)} set cannot also specify {nameof(BufferUsage.VertexBuffer)}, {nameof(BufferUsage.IndexBuffer)}, or {nameof(BufferUsage.IndirectBuffer)}. Leave {nameof(BufferDescription.UseTypedHlslBinding)} false (the default) to fill a vertex, index, or indirect buffer from a compute shader.");
            }
        }
        else if (description.StructureByteStride != 0)
        {
            throw new RenderException("Non-structured Buffers must have a StructureByteStride of zero.");
        }
        if ((usage & BufferUsage.Staging) != 0 && usage != BufferUsage.Staging)
        {
            throw new RenderException("Buffers with Staging Usage must not specify any other Usage flags.");
        }
        if ((usage & BufferUsage.Dynamic) != 0 && (usage & (BufferUsage.StructuredBufferReadWrite | BufferUsage.IndirectBuffer)) != 0)
        {
            throw new RenderException($"{nameof(BufferUsage)}.{nameof(BufferUsage.Dynamic)} cannot be combined with {nameof(BufferUsage.StructuredBufferReadWrite)} or {nameof(BufferUsage.IndirectBuffer)}.");
        }
        if ((usage & BufferUsage.UniformBuffer) != 0 && (description.SizeInBytes % 16) != 0)
        {
            throw new RenderException($"Uniform buffer size must be a multiple of 16 bytes.");
        }
    }

    private void CreateSampler_CheckDescription(ref SamplerDescription description)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (!Features.SamplerLodBias && description.LodBias != 0)
        {
            throw new RenderException(
                "GraphicsDevice does not support Sampler LOD bias. SamplerDescription.LodBias must be 0.");
        }
        if (!Features.SamplerAnisotropy && description.Filter == SamplerFilter.Anisotropic)
        {
            throw new RenderException(
                "SamplerFilter.Anisotropic cannot be used unless GraphicsDeviceFeatures.SamplerAnisotropy is supported.");
        }
    }

    private void CreateGraphicsProgram_CheckDescription(ref ShaderDescription description)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        ShaderStageDescription[] stages = description.Stages;
        if (stages == null || stages.Length == 0)
        {
            throw new RenderException($"{nameof(ShaderDescription)}.{nameof(ShaderDescription.Stages)} must contain at least one stage.");
        }

        bool hasVertex = false;
        for (int i = 0; i < stages.Length; i++)
        {
            ShaderStages stage = stages[i].Stage;
            if (stage == ShaderStages.Vertex) hasVertex = true;
            for (int j = i + 1; j < stages.Length; j++)
            {
                if (stages[j].Stage == stage)
                {
                    throw new RenderException(
                        $"{nameof(ShaderDescription)}.{nameof(ShaderDescription.Stages)} contains duplicate stage {stage}.");
                }
            }
            if (!Features.ComputeShader && stage == ShaderStages.Compute)
            {
                throw new RenderException("GraphicsDevice does not support Compute Shaders.");
            }
            if (!Features.GeometryShader && stage == ShaderStages.Geometry)
            {
                throw new RenderException("GraphicsDevice does not support Geometry Shaders.");
            }
            if (!Features.TessellationShaders
                && (stage == ShaderStages.TessellationControl || stage == ShaderStages.TessellationEvaluation))
            {
                throw new RenderException("GraphicsDevice does not support Tessellation Shaders.");
            }
        }

        if (!hasVertex)
        {
            throw new RenderException(
                $"{nameof(ShaderDescription)} must include a vertex stage.");
        }

        RasterizerStateDescription rasterizerState = description.RasterizerState;
        BlendStateDescription blendState = description.BlendState;
        if (!rasterizerState.DepthClipEnabled && !Features.DepthClipDisable)
        {
            throw new RenderException(
                "RasterizerState.DepthClipEnabled must be true if GraphicsDeviceFeatures.DepthClipDisable is not supported.");
        }
        if (!Features.IndependentBlend && blendState.AttachmentStates != null && blendState.AttachmentStates.Length > 0)
        {
            BlendAttachmentDescription attachmentState = blendState.AttachmentStates[0];
            for (int i = 1; i < blendState.AttachmentStates.Length; i++)
            {
                if (!attachmentState.Equals(blendState.AttachmentStates[i]))
                {
                    throw new RenderException(
                        $"If GraphicsDeviceFeatures.IndependentBlend is false, then all members of BlendState.AttachmentStates must be equal.");
                }
            }
        }

        ValidateProgramResourceLayouts(description.ResourceLayouts, nameof(GraphicsProgram));

        if (description.VertexLayouts != null)
        {
            foreach (VertexLayoutDescription layoutDesc in description.VertexLayouts)
            {
                bool hasExplicitLayout = false;
                uint minOffset = 0;
                foreach (VertexElementDescription elementDesc in layoutDesc.Elements)
                {
                    if (hasExplicitLayout && elementDesc.Offset == 0)
                    {
                        throw new RenderException(
                            $"If any vertex element has an explicit offset, then all elements must have an explicit offset.");
                    }

                    if (elementDesc.Offset != 0 && elementDesc.Offset < minOffset)
                    {
                        throw new RenderException(
                            $"Vertex element \"{elementDesc.Name}\" has an explicit offset which overlaps with the previous element.");
                    }

                    minOffset = elementDesc.Offset + elementDesc.Format.GetSizeInBytes();
                    hasExplicitLayout |= elementDesc.Offset != 0;
                }

                if (minOffset > layoutDesc.Stride)
                {
                    throw new RenderException(
                        $"The vertex layout's stride ({layoutDesc.Stride}) is less than the full size of the vertex ({minOffset})");
                }
            }
        }
    }

    private void CreateComputeProgram_CheckDescription(ref ComputeDescription description)
    {
        if (!GraphicsDevice.ValidationEnabled)
            return;

        if (!Features.ComputeShader)
        {
            throw new RenderException("GraphicsDevice does not support Compute Shaders.");
        }
        if (description.Stage.Stage != ShaderStages.Compute)
        {
            throw new RenderException(
                $"{nameof(ComputeDescription)}.{nameof(ComputeDescription.Stage)} must have Stage == ShaderStages.Compute.");
        }

        ValidateProgramResourceLayouts(description.ResourceLayouts, nameof(ComputeProgram));
    }

    private static void ValidateProgramResourceLayouts(ResourceLayoutDescription[] layouts, string programType)
    {
        if (layouts == null) return;

        for (int i = 0; i < layouts.Length; i++)
        {
            for (int j = i + 1; j < layouts.Length; j++)
            {
                if (layouts[i].Set == layouts[j].Set)
                {
                    throw new RenderException(
                        $"Two ResourceLayouts on the {programType} share Set index {layouts[i].Set}.");
                }
            }

            ValidateResourceLayoutElements(layouts[i]);
        }
    }

    private static void ValidateResourceLayoutElements(ResourceLayoutDescription layout)
    {
        ResourceLayoutElementDescription[] elements = layout.Elements;
        if (elements == null) return;

        for (int e = 0; e < elements.Length; e++)
        {
            ref ResourceLayoutElementDescription element = ref elements[e];
            UniformBlockField[]? fields = element.UniformFields;
            bool hasFields = fields != null && fields.Length > 0;

            if (hasFields && element.Kind != ResourceKind.UniformBuffer)
            {
                throw new RenderException(
                    $"ResourceLayoutElementDescription '{PropertyID.ToString(element.Name)}' has UniformFields but its Kind is {element.Kind}; UniformFields are only valid on UniformBuffer elements.");
            }

            if (!hasFields || fields == null) continue;

            for (int a = 0; a < fields.Length; a++)
            {
                ref UniformBlockField fa = ref fields[a];
                uint expected = NaturalSize(fa.Type);
                if (fa.Size != expected)
                {
                    throw new RenderException(
                        $"UniformBlockField '{PropertyID.ToString(fa.Name)}' on '{PropertyID.ToString(element.Name)}' has Size={fa.Size} but Type {fa.Type} requires Size={expected}.");
                }

                for (int b = a + 1; b < fields.Length; b++)
                {
                    ref UniformBlockField fb = ref fields[b];
                    if (fa.Name == fb.Name)
                    {
                        throw new RenderException(
                            $"UniformBlockField name '{PropertyID.ToString(fa.Name)}' is duplicated on '{PropertyID.ToString(element.Name)}'.");
                    }

                    bool nonOverlap = (fa.Offset + fa.Size <= fb.Offset) || (fb.Offset + fb.Size <= fa.Offset);
                    if (!nonOverlap)
                    {
                        throw new RenderException(
                            $"UniformBlockFields '{PropertyID.ToString(fa.Name)}' and '{PropertyID.ToString(fb.Name)}' on '{PropertyID.ToString(element.Name)}' overlap.");
                    }
                }
            }
        }
    }

    private static uint NaturalSize(UniformScalarType type) => type switch
    {
        UniformScalarType.Float1 => 4,
        UniformScalarType.Int1 => 4,
        UniformScalarType.Float2 => 8,
        UniformScalarType.Int2 => 8,
        UniformScalarType.Double1 => 8,
        UniformScalarType.Float3 => 12,
        UniformScalarType.Int3 => 12,
        UniformScalarType.Float4 => 16,
        UniformScalarType.Int4 => 16,
        UniformScalarType.Double2 => 16,
        UniformScalarType.Double3 => 24,
        UniformScalarType.Double4 => 32,
        UniformScalarType.Float4x4 => 64,
        UniformScalarType.Double4x4 => 128,
        _ => 0,
    };
}
