using Silk.NET.Vulkan;

using Prowl.Vector;

using static Prowl.Veldrid.Vk.VulkanUtil;

using System;
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
    private DescriptorSetLayout _emptyDescriptorSetLayout;
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

    public VkPipeline(VkGraphicsDevice gd, ref GraphicsPipelineDescription description)
        : base(ref description)
    {
        _gd = gd;
        IsComputePipeline = false;
        RefCount = new ResourceRefCount(DisposeCore);

        GraphicsPipelineCreateInfo pipelineCI = new GraphicsPipelineCreateInfo { SType = StructureType.GraphicsPipelineCreateInfo };

        // Blend State
        PipelineColorBlendStateCreateInfo blendStateCI = new PipelineColorBlendStateCreateInfo { SType = StructureType.PipelineColorBlendStateCreateInfo };
        int attachmentsCount = description.BlendState.AttachmentStates.Length;
        PipelineColorBlendAttachmentState* attachmentsPtr
            = stackalloc PipelineColorBlendAttachmentState[attachmentsCount];
        for (int i = 0; i < attachmentsCount; i++)
        {
            BlendAttachmentDescription vdDesc = description.BlendState.AttachmentStates[i];
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
        Color blendFactor = description.BlendState.BlendFactor;
        blendStateCI.BlendConstants[0] = blendFactor.R;
        blendStateCI.BlendConstants[1] = blendFactor.G;
        blendStateCI.BlendConstants[2] = blendFactor.B;
        blendStateCI.BlendConstants[3] = blendFactor.A;

        pipelineCI.PColorBlendState = &blendStateCI;

        // Rasterizer State
        RasterizerStateDescription rsDesc = description.RasterizerState;
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
        DepthStencilStateDescription vdDssDesc = description.DepthStencilState;
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
        multisampleCI.AlphaToCoverageEnable = description.BlendState.AlphaToCoverageEnabled;

        pipelineCI.PMultisampleState = &multisampleCI;

        // Input Assembly
        PipelineInputAssemblyStateCreateInfo inputAssemblyCI = new PipelineInputAssemblyStateCreateInfo { SType = StructureType.PipelineInputAssemblyStateCreateInfo };
        inputAssemblyCI.Topology = VkFormats.VdToVkPrimitiveTopology(description.PrimitiveTopology);

        pipelineCI.PInputAssemblyState = &inputAssemblyCI;

        // Vertex Input State
        PipelineVertexInputStateCreateInfo vertexInputCI = new PipelineVertexInputStateCreateInfo { SType = StructureType.PipelineVertexInputStateCreateInfo };

        VertexLayoutDescription[] inputDescriptions = description.ShaderSet.VertexLayouts;
        uint bindingCount = (uint)inputDescriptions.Length;
        uint attributeCount = 0;
        for (int i = 0; i < inputDescriptions.Length; i++)
        {
            attributeCount += (uint)inputDescriptions[i].Elements.Length;
        }
        VertexInputBindingDescription* bindingDescs = stackalloc VertexInputBindingDescription[(int)bindingCount];
        VertexInputAttributeDescription* attributeDescs = stackalloc VertexInputAttributeDescription[(int)attributeCount];

        int targetIndex = 0;
        int targetLocation = 0;
        for (int binding = 0; binding < inputDescriptions.Length; binding++)
        {
            VertexLayoutDescription inputDesc = inputDescriptions[binding];
            bindingDescs[binding] = new VertexInputBindingDescription()
            {
                Binding = inputDesc.Location,
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
                    Binding = inputDesc.Location,
                    Location = (uint)(targetLocation + location),
                    Offset = inputElement.Offset != 0 ? inputElement.Offset : currentOffset
                };

                targetIndex += 1;
                currentOffset += FormatSizeHelpers.GetSizeInBytes(inputElement.Format);
            }

            targetLocation += inputDesc.Elements.Length;
        }

        vertexInputCI.VertexBindingDescriptionCount = bindingCount;
        vertexInputCI.PVertexBindingDescriptions = bindingDescs;
        vertexInputCI.VertexAttributeDescriptionCount = attributeCount;
        vertexInputCI.PVertexAttributeDescriptions = attributeDescs;

        pipelineCI.PVertexInputState = &vertexInputCI;

        // Shader Stage

        ShaderProgram[] shaders = description.ShaderSet.Shaders;
        StackList<PipelineShaderStageCreateInfo> stages = new StackList<PipelineShaderStageCreateInfo>();
        foreach (ShaderProgram shader in shaders)
        {
            VkShader vkShader = Util.AssertSubtype<ShaderProgram, VkShader>(shader);
            PipelineShaderStageCreateInfo stageCI = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo };
            stageCI.Module = vkShader.ShaderModule;
            stageCI.Stage = VkFormats.VdToVkShaderStages(shader.Stage);
            // stageCI.PName = CommonStrings.main; // Meh
            stageCI.PName = new FixedUtf8String(shader.EntryPoint); // TODO: DONT ALLOCATE HERE
            stages.Add(stageCI);
        }

        pipelineCI.StageCount = stages.Count;
        pipelineCI.PStages = (PipelineShaderStageCreateInfo*)stages.Data;

        // ViewportState
        PipelineViewportStateCreateInfo viewportStateCI = new PipelineViewportStateCreateInfo { SType = StructureType.PipelineViewportStateCreateInfo };
        viewportStateCI.ViewportCount = 1;
        viewportStateCI.ScissorCount = 1;

        pipelineCI.PViewportState = &viewportStateCI;

        // Pipeline Layout
        _pipelineLayout = CreatePipelineLayout(description.ResourceLayouts, out uint setCount);
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
        DynamicOffsetsCount = SumDynamicOffsets(description.ResourceLayouts);
    }

    public VkPipeline(VkGraphicsDevice gd, ref ComputePipelineDescription description)
        : base(ref description)
    {
        _gd = gd;
        IsComputePipeline = true;
        RefCount = new ResourceRefCount(DisposeCore);

        ComputePipelineCreateInfo pipelineCI = new ComputePipelineCreateInfo { SType = StructureType.ComputePipelineCreateInfo };

        // Pipeline Layout
        _pipelineLayout = CreatePipelineLayout(description.ResourceLayouts, out uint setCount);
        pipelineCI.Layout = _pipelineLayout;

        // Shader Stage

        ShaderProgram shader = description.ComputeShader;
        VkShader vkShader = Util.AssertSubtype<ShaderProgram, VkShader>(shader);
        PipelineShaderStageCreateInfo stageCI = new PipelineShaderStageCreateInfo { SType = StructureType.PipelineShaderStageCreateInfo };
        stageCI.Module = vkShader.ShaderModule;
        stageCI.Stage = VkFormats.VdToVkShaderStages(shader.Stage);
        stageCI.PName = CommonStrings.main; // Meh
        pipelineCI.Stage = stageCI;

        Result result = _gd.Vk.CreateComputePipelines(
            _gd.Device,
            default,
            1,
            in pipelineCI,
            null,
            out _devicePipeline);
        CheckResult(result);

        ResourceSetCount = setCount;
        DynamicOffsetsCount = SumDynamicOffsets(description.ResourceLayouts);
    }

    private static int SumDynamicOffsets(ResourceLayout[] layouts)
    {
        int total = 0;
        for (int i = 0; i < layouts.Length; i++)
        {
            total += Util.AssertSubtype<ResourceLayout, VkResourceLayout>(layouts[i]).DynamicBufferCount;
        }
        return total;
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
            _gd.Vk.DestroyPipelineLayout(_gd.Device, _pipelineLayout, null);
            _gd.Vk.DestroyPipeline(_gd.Device, _devicePipeline, null);
            if (_emptyDescriptorSetLayout.Handle != 0)
            {
                _gd.Vk.DestroyDescriptorSetLayout(_gd.Device, _emptyDescriptorSetLayout, null);
            }
            if (!IsComputePipeline)
            {
                _gd.Vk.DestroyRenderPass(_gd.Device, _renderPass, null);
            }
        }
    }

    private PipelineLayout CreatePipelineLayout(ResourceLayout[] resourceLayouts, out uint setCount)
    {
        // Determine the highest Vulkan set index requested across the supplied layouts.
        // pSetLayouts must be contiguous from set 0, so any gaps are filled with an empty
        // descriptor set layout (lazily created on demand and destroyed in DisposeCore).
        uint maxSet = 0;
        bool any = false;
        for (int i = 0; i < resourceLayouts.Length; i++)
        {
            uint set = resourceLayouts[i].Description.Set;
            if (!any || set > maxSet)
            {
                maxSet = set;
            }
            any = true;
        }

        setCount = any ? maxSet + 1 : 0;

        DescriptorSetLayout* dsls = stackalloc DescriptorSetLayout[(int)setCount];
        for (int i = 0; i < setCount; i++)
        {
            dsls[i] = default;
        }

        for (int i = 0; i < resourceLayouts.Length; i++)
        {
            uint set = resourceLayouts[i].Description.Set;
            DescriptorSetLayout dsl = Util.AssertSubtype<ResourceLayout, VkResourceLayout>(resourceLayouts[i]).DescriptorSetLayout;
            if (dsls[set].Handle != 0)
            {
                throw new RenderException($"Multiple ResourceLayouts share Set index {set}.");
            }
            dsls[set] = dsl;
        }

        for (int i = 0; i < setCount; i++)
        {
            if (dsls[i].Handle == 0)
            {
                dsls[i] = GetOrCreateEmptyDescriptorSetLayout();
            }
        }

        PipelineLayoutCreateInfo pipelineLayoutCI = new PipelineLayoutCreateInfo
        {
            SType = StructureType.PipelineLayoutCreateInfo,
            SetLayoutCount = setCount,
            PSetLayouts = dsls
        };

        Result result = _gd.Vk.CreatePipelineLayout(_gd.Device, in pipelineLayoutCI, null, out PipelineLayout layout);
        CheckResult(result);
        return layout;
    }

    private DescriptorSetLayout GetOrCreateEmptyDescriptorSetLayout()
    {
        if (_emptyDescriptorSetLayout.Handle != 0)
        {
            return _emptyDescriptorSetLayout;
        }

        DescriptorSetLayoutCreateInfo dslCI = new DescriptorSetLayoutCreateInfo
        {
            SType = StructureType.DescriptorSetLayoutCreateInfo,
            BindingCount = 0,
            PBindings = null,
        };
        Result result = _gd.Vk.CreateDescriptorSetLayout(_gd.Device, in dslCI, null, out _emptyDescriptorSetLayout);
        CheckResult(result);
        return _emptyDescriptorSetLayout;
    }
}
