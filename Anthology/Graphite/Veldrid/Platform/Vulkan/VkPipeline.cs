using Silk.NET.Vulkan;

using Prowl.Vector;

using static Prowl.Veldrid.Vk.VulkanUtil;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

using VkPipelineHandle = Silk.NET.Vulkan.Pipeline;

namespace Prowl.Veldrid.Vk;

internal unsafe class VkPipeline : Pipeline
{
    private readonly VkGraphicsDevice _gd;
    private readonly VkPipelineHandle _devicePipeline;
    private readonly PipelineLayout _pipelineLayout;
    private readonly RenderPass _renderPass;
    private bool _destroyed;
    private string _name;

    public VkPipelineHandle DevicePipeline => _devicePipeline;

    public PipelineLayout PipelineLayout => _pipelineLayout;

    /// <summary>
    /// Number of descriptor set slots in this pipeline, equal to max(layout.Set) + 1.
    /// Slots with no provided <see cref="ResourceLayout"/> are filled with an empty descriptor set layout.
    /// </summary>
    public uint ResourceSetCount { get; }
    public int DynamicOffsetsCount { get; private set; }
    public bool ScissorTestEnabled { get; }

    public override bool IsComputePipeline { get; }

    public ResourceRefCount RefCount { get; }

    public override bool IsDisposed => _destroyed;

    private readonly VkShaderProgram _referencedShaderProgram;
    private readonly VkComputeProgram _referencedComputeProgram;

    public VkShaderProgram ShaderProgramRef => _referencedShaderProgram;
    public VkComputeProgram ComputeProgramRef => _referencedComputeProgram;

    public VkPipeline(VkGraphicsDevice gd, ResourceFactory factory, ref GraphicsPipelineDescription description)
        : base(factory, ref description)
    {
        _gd = gd;
        IsComputePipeline = false;
        RefCount = new ResourceRefCount(DisposeCore);
        _referencedShaderProgram = Util.AssertSubtype<ShaderProgram, VkShaderProgram>(description.Program);

        GraphicsPipelineCreateInfo pipelineCI = new GraphicsPipelineCreateInfo { SType = StructureType.GraphicsPipelineCreateInfo };

        // Blend State
        PipelineColorBlendStateCreateInfo blendStateCI = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo };
        BlendStateDescription programBlendState = _referencedShaderProgram.BlendState;
        int attachmentsCount = programBlendState.AttachmentStates.Length;
        PipelineColorBlendAttachmentState* attachmentsPtr
            = stackalloc PipelineColorBlendAttachmentState[attachmentsCount];
        for (int i = 0; i < attachmentsCount; i++)
        {
            BlendAttachmentDescription vdDesc = programBlendState.AttachmentStates[i];
            PipelineColorBlendAttachmentState attachmentState = new PipelineColorBlendAttachmentState();
            attachmentState.SrcColorBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.SourceColorFactor);
            attachmentState.DstColorBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.DestinationColorFactor);
            attachmentState.ColorBlendOp = VkFormats.VdToVkBlendOp(vdDesc.ColorFunction);
            attachmentState.SrcAlphaBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.SourceAlphaFactor);
            attachmentState.DstAlphaBlendFactor = VkFormats.VdToVkBlendFactor(vdDesc.DestinationAlphaFactor);
            attachmentState.AlphaBlendOp = VkFormats.VdToVkBlendOp(vdDesc.AlphaFunction);
            attachmentState.ColorWriteMask = VkFormats.VdToVkColorWriteMask(vdDesc.ColorWriteMask.GetOrDefault());
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
        RasterizerStateDescription rsDesc = _referencedShaderProgram.RasterizerState;
        PipelineRasterizationStateCreateInfo rsCI = new PipelineRasterizationStateCreateInfo { SType = StructureType.PipelineRasterizationStateCreateInfo };
        rsCI.CullMode = VkFormats.VdToVkCullMode(rsDesc.CullMode);
        rsCI.PolygonMode = PolygonMode.Fill;
        rsCI.DepthClampEnable = !rsDesc.DepthClipEnabled;
        rsCI.FrontFace = rsDesc.FrontFace == FrontFace.Clockwise ? Silk.NET.Vulkan.FrontFace.Clockwise : Silk.NET.Vulkan.FrontFace.CounterClockwise;
        rsCI.LineWidth = 1f;

        pipelineCI.PRasterizationState = &rsCI;

        ScissorTestEnabled = rsDesc.ScissorTestEnabled;

        // Dynamic State
        PipelineDynamicStateCreateInfo dynamicStateCI = new PipelineDynamicStateCreateInfo { SType = StructureType.PipelineDynamicStateCreateInfo };
        DynamicState* dynamicStates = stackalloc DynamicState[2];
        dynamicStates[0] = DynamicState.Viewport;
        dynamicStates[1] = DynamicState.Scissor;
        dynamicStateCI.DynamicStateCount = 2;
        dynamicStateCI.PDynamicStates = dynamicStates;

        pipelineCI.PDynamicState = &dynamicStateCI;

        // Depth Stencil State
        DepthStencilStateDescription vdDssDesc = _referencedShaderProgram.DepthStencilState;
        PipelineDepthStencilStateCreateInfo dssCI = new PipelineDepthStencilStateCreateInfo { SType = StructureType.PipelineDepthStencilStateCreateInfo };
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
        PipelineMultisampleStateCreateInfo multisampleCI = new PipelineMultisampleStateCreateInfo { SType = StructureType.PipelineMultisampleStateCreateInfo };
        SampleCountFlags vkSampleCount = VkFormats.VdToVkSampleCount(description.Outputs.SampleCount);
        multisampleCI.RasterizationSamples = vkSampleCount;
        multisampleCI.AlphaToCoverageEnable = programBlendState.AlphaToCoverageEnabled;

        pipelineCI.PMultisampleState = &multisampleCI;

        // Input Assembly
        PipelineInputAssemblyStateCreateInfo inputAssemblyCI = new PipelineInputAssemblyStateCreateInfo { SType = StructureType.PipelineInputAssemblyStateCreateInfo };
        inputAssemblyCI.Topology = VkFormats.VdToVkPrimitiveTopology(description.PrimitiveTopology);

        pipelineCI.PInputAssemblyState = &inputAssemblyCI;

        // Vertex Input State
        PipelineVertexInputStateCreateInfo vertexInputCI = new PipelineVertexInputStateCreateInfo { SType = StructureType.PipelineVertexInputStateCreateInfo };

        VertexLayoutDescription[] inputDescriptions = _referencedShaderProgram.VertexLayoutsArray;
        uint bindingCount = (uint)inputDescriptions.Length;
        uint attributeCount = 0;
        for (int i = 0; i < inputDescriptions.Length; i++)
        {
            attributeCount += (uint)inputDescriptions[i].Elements.Length;
        }
        VertexInputBindingDescription* bindingDescs = stackalloc VertexInputBindingDescription[(int)bindingCount];
        VertexInputAttributeDescription* attributeDescs = stackalloc VertexInputAttributeDescription[(int)attributeCount];

        // VkBinding is the vertex buffer slot (matches CmdBindVertexBuffers' firstBinding,
        // which receives the layout index from SetVertexBuffer). VkLocation is the shader
        // attribute index, taken from VertexLayoutDescription.Location + element offset.
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
                currentOffset += FormatSizeHelpers.GetSizeInBytes(inputElement.Format);
            }
        }

        vertexInputCI.VertexBindingDescriptionCount = bindingCount;
        vertexInputCI.PVertexBindingDescriptions = bindingDescs;
        vertexInputCI.VertexAttributeDescriptionCount = attributeCount;
        vertexInputCI.PVertexAttributeDescriptions = attributeDescs;

        pipelineCI.PVertexInputState = &vertexInputCI;

        // Shader Stage

        StackList<PipelineShaderStageCreateInfo> stages = new StackList<PipelineShaderStageCreateInfo>();
        foreach (KeyValuePair<ShaderStages, ShaderModule> kvp in _referencedShaderProgram.Modules)
        {
            PipelineShaderStageCreateInfo stageCI = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo };
            stageCI.Module = kvp.Value;
            stageCI.Stage = VkFormats.VdToVkShaderStages(kvp.Key);
            stageCI.PName = new FixedUtf8String(_referencedShaderProgram.GetEntryPoint(kvp.Key));
            stages.Add(stageCI);
        }

        pipelineCI.StageCount = stages.Count;
        pipelineCI.PStages = (PipelineShaderStageCreateInfo*)stages.Data;

        // ViewportState
        PipelineViewportStateCreateInfo viewportStateCI = new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo };
        viewportStateCI.ViewportCount = 1;
        viewportStateCI.ScissorCount = 1;

        pipelineCI.PViewportState = &viewportStateCI;

        // Pipeline Layout: reuse program's pre-built pipeline layout.
        _pipelineLayout = _referencedShaderProgram.PipelineLayout;
        uint setCount = _referencedShaderProgram.ResourceSetCount;
        pipelineCI.Layout = _pipelineLayout;

        // Create fake RenderPass for compatibility.

        RenderPassCreateInfo renderPassCI = new RenderPassCreateInfo { SType = StructureType.RenderPassCreateInfo };
        OutputDescription outputDesc = description.Outputs;
        StackList<AttachmentDescription, Size512Bytes> attachments = new StackList<AttachmentDescription, Size512Bytes>();

        // TODO: A huge portion of this next part is duplicated in VkFramebuffer.cs.

        StackList<AttachmentDescription> colorAttachmentDescs = new StackList<AttachmentDescription>();
        StackList<AttachmentReference> colorAttachmentRefs = new StackList<AttachmentReference>();
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
            attachments.Add(colorAttachmentDescs[i]);

            colorAttachmentRefs[i].Attachment = i;
            colorAttachmentRefs[i].Layout = ImageLayout.ColorAttachmentOptimal;
        }

        AttachmentDescription depthAttachmentDesc = new AttachmentDescription();
        AttachmentReference depthAttachmentRef = new AttachmentReference();
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

        SubpassDescription subpass = new SubpassDescription();
        subpass.PipelineBindPoint = PipelineBindPoint.Graphics;
        subpass.ColorAttachmentCount = (uint)outputDesc.ColorAttachments.Length;
        subpass.PColorAttachments = (AttachmentReference*)colorAttachmentRefs.Data;
        for (int i = 0; i < colorAttachmentDescs.Count; i++)
        {
            attachments.Add(colorAttachmentDescs[i]);
        }

        if (outputDesc.DepthAttachment != null)
        {
            subpass.PDepthStencilAttachment = &depthAttachmentRef;
            attachments.Add(depthAttachmentDesc);
        }

        SubpassDependency subpassDependency = new SubpassDependency();
        subpassDependency.SrcSubpass = Silk.NET.Vulkan.Vk.SubpassExternal;
        subpassDependency.SrcStageMask = PipelineStageFlags.ColorAttachmentOutputBit;
        subpassDependency.DstStageMask = PipelineStageFlags.ColorAttachmentOutputBit;
        subpassDependency.DstAccessMask = AccessFlags.ColorAttachmentReadBit | AccessFlags.ColorAttachmentWriteBit;

        renderPassCI.AttachmentCount = attachments.Count;
        renderPassCI.PAttachments = (AttachmentDescription*)attachments.Data;
        renderPassCI.SubpassCount = 1;
        renderPassCI.PSubpasses = &subpass;
        renderPassCI.DependencyCount = 1;
        renderPassCI.PDependencies = &subpassDependency;

        Result creationResult = _gd.Vk.CreateRenderPass(_gd.Device, in renderPassCI, null, out _renderPass);
        CheckResult(creationResult);

        pipelineCI.RenderPass = _renderPass;

        Result result = _gd.Vk.CreateGraphicsPipelines(_gd.Device, default, 1, in pipelineCI, null, out _devicePipeline);
        CheckResult(result);

        ResourceSetCount = setCount;
        DynamicOffsetsCount = _referencedShaderProgram.DynamicOffsetsCount;
    }

    public VkPipeline(VkGraphicsDevice gd, ResourceFactory factory, ref ComputePipelineDescription description)
        : base(factory, ref description)
    {
        _gd = gd;
        IsComputePipeline = true;
        RefCount = new ResourceRefCount(DisposeCore);
        _referencedComputeProgram = Util.AssertSubtype<ComputeProgram, VkComputeProgram>(description.Program);
        _referencedComputeProgram.RefCount.Increment();

        _devicePipeline = _referencedComputeProgram.DevicePipeline;
        _pipelineLayout = _referencedComputeProgram.PipelineLayout;
        ResourceSetCount = _referencedComputeProgram.ResourceSetCount;
        DynamicOffsetsCount = _referencedComputeProgram.DynamicOffsetsCount;
    }


    public override string Name
    {
        get => _name;
        set
        {
            _name = value;
            _gd.SetResourceName(this, value);
        }
    }

    public override void Dispose()
    {
        RefCount.Decrement();
    }

    private void DisposeCore()
    {
        if (!_destroyed)
        {
            _destroyed = true;
            DisposeAdapterResourceLayouts();
            if (!IsComputePipeline)
            {
                // Graphics pipeline is owned by this VkPipeline; pipeline layout is owned by the program.
                _gd.Vk.DestroyPipeline(_gd.Device, _devicePipeline, null);
                _gd.Vk.DestroyRenderPass(_gd.Device, _renderPass, null);
            }
            else
            {
                // Compute pipeline is owned by VkComputeProgram (we incremented refcount); release.
                _referencedComputeProgram?.RefCount.Decrement();
            }
        }
    }

}
