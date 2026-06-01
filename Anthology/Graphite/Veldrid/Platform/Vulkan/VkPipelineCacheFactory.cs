using System.Collections.Generic;

using Prowl.Vector;

using Silk.NET.Vulkan;

namespace Prowl.Veldrid.Vk;

/// <summary>
/// Builds a <see cref="VkPipelineCacheEntry"/> from a <see cref="VkGraphicsProgram"/> and a
/// <see cref="VkPipelineCacheKey"/>. Builds a Vulkan graphics pipeline for the given program /
/// framebuffer / topology key. Called lazily from the program's per-program cache at draw time.
/// </summary>
internal static unsafe class VkPipelineCacheFactory
{
    public static VkPipelineCacheEntry Build(VkGraphicsDevice gd, VkGraphicsProgram program, in VkPipelineCacheKey key)
    {
        OutputDescription outputDesc = key.Outputs;

        GraphicsPipelineCreateInfo pipelineCI = new() { SType = StructureType.GraphicsPipelineCreateInfo };

        // Blend State
        PipelineColorBlendStateCreateInfo blendStateCI = new() { SType = StructureType.PipelineColorBlendStateCreateInfo };
        BlendStateDescription programBlendState = program.BlendState;
        int attachmentsCount = programBlendState.AttachmentStates.Length;
        PipelineColorBlendAttachmentState* attachmentsPtr
            = stackalloc PipelineColorBlendAttachmentState[attachmentsCount];
        for (int i = 0; i < attachmentsCount; i++)
        {
            BlendAttachmentDescription vdDesc = programBlendState.AttachmentStates[i];
            PipelineColorBlendAttachmentState attachmentState = new();
            attachmentState.SrcColorBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.SourceColorFactor);
            attachmentState.DstColorBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.DestinationColorFactor);
            attachmentState.ColorBlendOp = VkFormats.VdToVkBlendOp(vdDesc.ColorFunction);
            attachmentState.SrcAlphaBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.SourceAlphaFactor);
            attachmentState.DstAlphaBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.DestinationAlphaFactor);
            attachmentState.AlphaBlendOp = VkFormats.VdToVkBlendOp(vdDesc.AlphaFunction);
            attachmentState.ColorWriteMask = VkFormats.VdToVkColorWriteMask(vdDesc.ColorWriteMask ?? ColorWriteMask.All);
            attachmentState.BlendEnable = vdDesc.BlendEnabled;
            attachmentsPtr[i] = attachmentState;
        }

        blendStateCI.AttachmentCount = (uint)attachmentsCount;
        blendStateCI.PAttachments = attachmentsPtr;
        Color blendFactor = programBlendState.BlendFactor;
        blendStateCI.BlendConstants[0] = blendFactor.R;
        blendStateCI.BlendConstants[1] = blendFactor.G;
        blendStateCI.BlendConstants[2] = blendFactor.B;
        blendStateCI.BlendConstants[3] = blendFactor.A;

        pipelineCI.PColorBlendState = &blendStateCI;

        // Rasterizer State
        RasterizerStateDescription rsDesc = program.RasterizerState;
        PipelineRasterizationStateCreateInfo rsCI = new() { SType = StructureType.PipelineRasterizationStateCreateInfo };
        rsCI.CullMode = VkFormats.VdToVkCullMode(rsDesc.CullMode);
        rsCI.PolygonMode = PolygonMode.Fill;
        rsCI.DepthClampEnable = !rsDesc.DepthClipEnabled;
        rsCI.FrontFace = rsDesc.FrontFace == FrontFace.Clockwise ? Silk.NET.Vulkan.FrontFace.Clockwise : Silk.NET.Vulkan.FrontFace.CounterClockwise;
        rsCI.LineWidth = 1f;

        pipelineCI.PRasterizationState = &rsCI;

        // Dynamic State
        PipelineDynamicStateCreateInfo dynamicStateCI = new() { SType = StructureType.PipelineDynamicStateCreateInfo };
        DynamicState* dynamicStates = stackalloc DynamicState[2];
        dynamicStates[0] = DynamicState.Viewport;
        dynamicStates[1] = DynamicState.Scissor;
        dynamicStateCI.DynamicStateCount = 2;
        dynamicStateCI.PDynamicStates = dynamicStates;

        pipelineCI.PDynamicState = &dynamicStateCI;

        // Depth Stencil State
        DepthStencilStateDescription vdDssDesc = program.DepthStencilState;
        PipelineDepthStencilStateCreateInfo dssCI = new() { SType = StructureType.PipelineDepthStencilStateCreateInfo };
        dssCI.DepthWriteEnable = vdDssDesc.DepthWriteEnabled;
        dssCI.DepthTestEnable = vdDssDesc.DepthTestEnabled;
        dssCI.DepthCompareOp = VkFormats.VdToVkCompareOp(vdDssDesc.DepthComparison);
        dssCI.StencilTestEnable = vdDssDesc.StencilTestEnabled;

        dssCI.Front.FailOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilFront.Fail);
        dssCI.Front.PassOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilFront.Pass);
        dssCI.Front.DepthFailOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilFront.DepthFail);
        dssCI.Front.CompareOp = VkFormats.VdToVkCompareOp(vdDssDesc.StencilFront.Comparison);
        dssCI.Front.CompareMask = vdDssDesc.StencilReadMask;
        dssCI.Front.WriteMask = vdDssDesc.StencilWriteMask;
        dssCI.Front.Reference = vdDssDesc.StencilReference;

        dssCI.Back.FailOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilBack.Fail);
        dssCI.Back.PassOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilBack.Pass);
        dssCI.Back.DepthFailOp = VkFormats.VdToVkStencilOp(vdDssDesc.StencilBack.DepthFail);
        dssCI.Back.CompareOp = VkFormats.VdToVkCompareOp(vdDssDesc.StencilBack.Comparison);
        dssCI.Back.CompareMask = vdDssDesc.StencilReadMask;
        dssCI.Back.WriteMask = vdDssDesc.StencilWriteMask;
        dssCI.Back.Reference = vdDssDesc.StencilReference;

        pipelineCI.PDepthStencilState = &dssCI;

        // Multisample
        PipelineMultisampleStateCreateInfo multisampleCI = new() { SType = StructureType.PipelineMultisampleStateCreateInfo };
        SampleCountFlags vkSampleCount = VkFormats.VdToVkSampleCount(outputDesc.SampleCount);
        multisampleCI.RasterizationSamples = vkSampleCount;
        multisampleCI.AlphaToCoverageEnable = programBlendState.AlphaToCoverageEnabled;

        pipelineCI.PMultisampleState = &multisampleCI;

        // Input Assembly
        PipelineInputAssemblyStateCreateInfo inputAssemblyCI = new() { SType = StructureType.PipelineInputAssemblyStateCreateInfo };
        inputAssemblyCI.Topology = VkFormats.VdToVkPrimitiveTopology(key.Topology);

        pipelineCI.PInputAssemblyState = &inputAssemblyCI;

        // Vertex Input State
        PipelineVertexInputStateCreateInfo vertexInputCI = new() { SType = StructureType.PipelineVertexInputStateCreateInfo };

        VertexLayoutDescription[] inputDescriptions = program.VertexLayoutsArray;
        uint bindingCount = (uint)inputDescriptions.Length;
        uint attributeCount = 0;
        for (int i = 0; i < inputDescriptions.Length; i++)
        {
            attributeCount += (uint)inputDescriptions[i].Elements.Length;
        }
        VertexInputBindingDescription* bindingDescs = stackalloc VertexInputBindingDescription[(int)bindingCount];
        VertexInputAttributeDescription* attributeDescs = stackalloc VertexInputAttributeDescription[(int)attributeCount];

        int targetIndex = 0;
        for (int binding = 0; binding < inputDescriptions.Length; binding++)
        {
            VertexLayoutDescription inputDesc = inputDescriptions[binding];
            bindingDescs[binding] = new VertexInputBindingDescription()
            {
                Binding = (uint)binding,
                InputRate = (inputDesc.InstanceStepRate != 0) ? VertexInputRate.Instance : VertexInputRate.Vertex,
                Stride = inputDesc.Stride
            };

            uint currentOffset = 0;
            for (int location = 0; location < inputDesc.Elements.Length; location++)
            {
                VertexElementDescription inputElement = inputDesc.Elements[location];

                attributeDescs[targetIndex] = new VertexInputAttributeDescription()
                {
                    Format = VkFormats.VdToVkVertexElementFormat(inputElement.Format),
                    Binding = (uint)binding,
                    Location = inputDesc.Location + (uint)location,
                    Offset = inputElement.Offset != 0 ? inputElement.Offset : currentOffset
                };

                targetIndex += 1;
                currentOffset += inputElement.Format.GetSizeInBytes();
            }
        }

        vertexInputCI.VertexBindingDescriptionCount = bindingCount;
        vertexInputCI.PVertexBindingDescriptions = bindingDescs;
        vertexInputCI.VertexAttributeDescriptionCount = attributeCount;
        vertexInputCI.PVertexAttributeDescriptions = attributeDescs;

        pipelineCI.PVertexInputState = &vertexInputCI;

        // Shader Stage
        PipelineShaderStageCreateInfo* stages = stackalloc PipelineShaderStageCreateInfo[program.Modules.Count];
        uint stageCount = 0;
        foreach (KeyValuePair<ShaderStages, ShaderModule> kvp in program.Modules)
        {
            PipelineShaderStageCreateInfo stageCI = new() { SType = StructureType.PipelineShaderStageCreateInfo };
            stageCI.Module = kvp.Value;
            stageCI.Stage = VkFormats.VdToVkShaderStages(kvp.Key);
            stageCI.PName = new FixedUtf8String(program.GetEntryPoint(kvp.Key)); // TODO: DONT ALLOCATE HERE
            stages[stageCount++] = stageCI;
        }

        pipelineCI.StageCount = stageCount;
        pipelineCI.PStages = stages;

        // ViewportState
        PipelineViewportStateCreateInfo viewportStateCI = new() { SType = StructureType.PipelineViewportStateCreateInfo };
        viewportStateCI.ViewportCount = 1;
        viewportStateCI.ScissorCount = 1;

        pipelineCI.PViewportState = &viewportStateCI;

        // Pipeline Layout: reuse program's pre-built pipeline layout.
        PipelineLayout pipelineLayout = program.PipelineLayout;
        pipelineCI.Layout = pipelineLayout;

        // Compatibility RenderPass
        RenderPassCreateInfo renderPassCI = new() { SType = StructureType.RenderPassCreateInfo };
        AttachmentDescription* attachments = stackalloc AttachmentDescription[outputDesc.ColorAttachments.Length + 1];
        uint attachmentCount = 0;

        AttachmentDescription* colorAttachmentDescs = stackalloc AttachmentDescription[outputDesc.ColorAttachments.Length];
        AttachmentReference* colorAttachmentRefs = stackalloc AttachmentReference[outputDesc.ColorAttachments.Length];
        for (uint i = 0; i < outputDesc.ColorAttachments.Length; i++)
        {
            colorAttachmentDescs[i].Format = VkFormats.VdToVkPixelFormat(outputDesc.ColorAttachments[i].Format);
            colorAttachmentDescs[i].Samples = vkSampleCount;
            colorAttachmentDescs[i].LoadOp = AttachmentLoadOp.DontCare;
            colorAttachmentDescs[i].StoreOp = AttachmentStoreOp.Store;
            colorAttachmentDescs[i].StencilLoadOp = AttachmentLoadOp.DontCare;
            colorAttachmentDescs[i].StencilStoreOp = AttachmentStoreOp.DontCare;
            colorAttachmentDescs[i].InitialLayout = ImageLayout.Undefined;
            colorAttachmentDescs[i].FinalLayout = ImageLayout.ShaderReadOnlyOptimal;
            attachments[attachmentCount++] = colorAttachmentDescs[i];

            colorAttachmentRefs[i].Attachment = i;
            colorAttachmentRefs[i].Layout = ImageLayout.ColorAttachmentOptimal;
        }

        AttachmentDescription depthAttachmentDesc = new();
        AttachmentReference depthAttachmentRef = new();
        if (outputDesc.DepthAttachment != null)
        {
            PixelFormat depthFormat = outputDesc.DepthAttachment.Value.Format;
            bool hasStencil = FormatHelpers.IsStencilFormat(depthFormat);
            depthAttachmentDesc.Format = VkFormats.VdToVkPixelFormat(outputDesc.DepthAttachment.Value.Format, toDepthFormat: true);
            depthAttachmentDesc.Samples = vkSampleCount;
            depthAttachmentDesc.LoadOp = AttachmentLoadOp.DontCare;
            depthAttachmentDesc.StoreOp = AttachmentStoreOp.Store;
            depthAttachmentDesc.StencilLoadOp = AttachmentLoadOp.DontCare;
            depthAttachmentDesc.StencilStoreOp = hasStencil ? AttachmentStoreOp.Store : AttachmentStoreOp.DontCare;
            depthAttachmentDesc.InitialLayout = ImageLayout.Undefined;
            depthAttachmentDesc.FinalLayout = ImageLayout.DepthStencilAttachmentOptimal;

            depthAttachmentRef.Attachment = (uint)outputDesc.ColorAttachments.Length;
            depthAttachmentRef.Layout = ImageLayout.DepthStencilAttachmentOptimal;
        }

        SubpassDescription subpass = new();
        subpass.PipelineBindPoint = PipelineBindPoint.Graphics;
        subpass.ColorAttachmentCount = (uint)outputDesc.ColorAttachments.Length;
        subpass.PColorAttachments = colorAttachmentRefs;

        if (outputDesc.DepthAttachment != null)
        {
            subpass.PDepthStencilAttachment = &depthAttachmentRef;
            attachments[attachmentCount++] = depthAttachmentDesc;
        }

        SubpassDependency subpassDependency = new();
        subpassDependency.SrcSubpass = Silk.NET.Vulkan.Vk.SubpassExternal;
        subpassDependency.SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit;
        subpassDependency.DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit;
        subpassDependency.DstAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit;

        renderPassCI.AttachmentCount = attachmentCount;
        renderPassCI.PAttachments = attachments;
        renderPassCI.SubpassCount = 1;
        renderPassCI.PSubpasses = &subpass;
        renderPassCI.DependencyCount = 1;
        renderPassCI.PDependencies = &subpassDependency;

        gd.Vk.CreateRenderPass(gd.Device, in renderPassCI, null, out RenderPass renderPass).CheckResult();

        pipelineCI.RenderPass = renderPass;

        gd.Vk.CreateGraphicsPipelines(gd.Device, gd.DriverPipelineCache, 1, in pipelineCI, null, out Silk.NET.Vulkan.Pipeline pipeline).CheckResult();

        return new VkPipelineCacheEntry(
            pipeline,
            renderPass,
            pipelineLayout,
            program.ResourceSetCount,
            program.TotalDynamicUboCount);
    }
}
