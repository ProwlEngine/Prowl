using System;
using System.Collections.Generic;

using Prowl.Slang;


namespace Prowl.Graphite.Compiler;



public class DXCompiler : CompilerModule
{
    private TargetDescription _target;
    public TargetDescription Target => _target;

    private GraphicsBackend _backend;
    public GraphicsBackend Backend => _backend;


    public DXCompiler(string profileString = "sm_5_0", GraphicsBackend backend = GraphicsBackend.Direct3D11)
    {
        _backend = backend;
        _target = new()
        {
            Profile = GlobalSession.FindProfile(profileString),
            Format = CompileTarget.Hlsl
        };
    }

    public ShaderDescription CompileForTarget(ComponentType linkedComponent, int layoutIndex, DiagnosticHandler handler)
    {
        ShaderDescription description = SlangReflector.BuildDescription(linkedComponent, layoutIndex, handler, bindsBySemantic: true);
        description.ResourceLayouts = Reflect(linkedComponent, layoutIndex, description.Stages);
        return description;
    }


    public static ResourceLayoutDescription[] Reflect(ComponentType linkedComponent, int layoutIndex, ShaderStageDescription[] stages)
    {
        ShaderStages programStages = ShaderStages.None;
        foreach (ShaderStageDescription stage in stages)
            programStages |= stage.Stage;

        ShaderReflection layout = linkedComponent.GetLayout(layoutIndex, out _);

        Dictionary<uint, List<ResourceLayoutElementDescription>> bySpace = [];

        foreach (VariableLayoutReflection parameter in layout.Parameters)
            Collect(parameter, RegisterBase.Zero, baseSpace: 0, programStages, bySpace);

        List<ResourceLayoutDescription> layouts = [];
        foreach ((uint space, List<ResourceLayoutElementDescription> elements) in bySpace)
            layouts.Add(new ResourceLayoutDescription(space, [.. elements]));

        return [.. layouts];
    }


    // Slang lays HLSL out one of two ways depending on the target shader model. Under sm_5_1+ each
    // ParameterBlock opens its own register space and the resources inside it keep zero-based register
    // indices. Under sm_5_0 (which FXC compiles for D3D11) there are no register spaces, so Slang folds
    // every block into space 0 and carries a per-register-class base offset on the block parameter that
    // its inner resources are relative to. Both cases collapse into the same accumulation: sum the
    // SubElementRegisterSpace offsets down the path for the space, and sum each register class's offset
    // down the path for the slot. Under sm_5_1 only the space term grows; under sm_5_0 only the slot.
    static void Collect(
        VariableLayoutReflection parameter, RegisterBase registerBase, uint baseSpace, ShaderStages stages,
        Dictionary<uint, List<ResourceLayoutElementDescription>> bySpace)
    {
        TypeLayoutReflection typeLayout = parameter.TypeLayout;

        if (typeLayout.Kind == TypeKind.ParameterBlock)
        {
            CollectParameterBlock(parameter, registerBase, baseSpace, stages, bySpace);
            return;
        }

        if (!SlangReflector.TryGetResourceKind(typeLayout, out ResourceKind kind))
            return;

        ParameterCategory category = RegisterClassOf(kind);
        uint space = baseSpace + parameter.GetBindingSpace(category);
        int register = (int)(registerBase.Of(category) + parameter.GetOffset(category));

        UniformBlockField[] fields = kind == ResourceKind.UniformBuffer
            ? SlangReflector.ReflectUniformFields(typeLayout.ElementTypeLayout)
            : [];

        Add(bySpace, space, parameter.Name, kind, stages, register, fields);
    }


    static void CollectParameterBlock(
        VariableLayoutReflection block, RegisterBase registerBase, uint baseSpace, ShaderStages stages,
        Dictionary<uint, List<ResourceLayoutElementDescription>> bySpace)
    {
        TypeLayoutReflection typeLayout = block.TypeLayout;

        uint space = baseSpace + block.GetOffset(ParameterCategory.SubElementRegisterSpace);
        RegisterBase innerBase = registerBase.Add(block);

        // Ordinary uniform data within the block collapses into one implicit constant buffer, placed at
        // the b-register the container reserves for it (relative to the block's own constant-buffer base).
        TypeLayoutReflection elementLayout = typeLayout.ElementTypeLayout;
        if (elementLayout.GetSize() > 0)
        {
            int cbufferRegister = (int)(innerBase.Of(ParameterCategory.ConstantBuffer)
                + typeLayout.ContainerVarLayout.GetOffset(ParameterCategory.ConstantBuffer));
            Add(bySpace, space, block.Name, ResourceKind.UniformBuffer, stages, cbufferRegister,
                SlangReflector.ReflectUniformFields(elementLayout));
        }

        // Resources declared inside the block bind relative to the block's per-class base in its space.
        foreach (VariableLayoutReflection field in elementLayout.Fields)
            if (field.TypeLayout.Kind != TypeKind.Scalar
                && field.TypeLayout.Kind != TypeKind.Vector
                && field.TypeLayout.Kind != TypeKind.Matrix)
                Collect(field, innerBase, space, stages, bySpace);
    }


    // Accumulated register-slot offset per HLSL register class along the path from the root parameter.
    readonly struct RegisterBase
    {
        public static RegisterBase Zero => default;

        readonly uint _cbv, _srv, _sampler, _uav;

        RegisterBase(uint cbv, uint srv, uint sampler, uint uav)
        {
            _cbv = cbv;
            _srv = srv;
            _sampler = sampler;
            _uav = uav;
        }

        public uint Of(ParameterCategory category) =>
            category switch
            {
                ParameterCategory.ConstantBuffer => _cbv,
                ParameterCategory.ShaderResource => _srv,
                ParameterCategory.SamplerState => _sampler,
                ParameterCategory.UnorderedAccess => _uav,
                _ => 0
            };

        // Folds a parameter block's own per-class offsets into the running base for its children.
        public RegisterBase Add(VariableLayoutReflection block) =>
            new(_cbv + block.GetOffset(ParameterCategory.ConstantBuffer),
                _srv + block.GetOffset(ParameterCategory.ShaderResource),
                _sampler + block.GetOffset(ParameterCategory.SamplerState),
                _uav + block.GetOffset(ParameterCategory.UnorderedAccess));
    }


    // The register class (b/t/s/u) a resource of the given kind occupies in HLSL.
    static ParameterCategory RegisterClassOf(ResourceKind kind) =>
        kind switch
        {
            ResourceKind.UniformBuffer => ParameterCategory.ConstantBuffer,
            ResourceKind.Sampler => ParameterCategory.SamplerState,
            ResourceKind.TextureReadOnly or ResourceKind.StructuredBufferReadOnly => ParameterCategory.ShaderResource,
            ResourceKind.TextureReadWrite or ResourceKind.StructuredBufferReadWrite => ParameterCategory.UnorderedAccess,
            _ => throw new NotSupportedException($"No HLSL register class for resource kind {kind}.")
        };


    static void Add(
        Dictionary<uint, List<ResourceLayoutElementDescription>> bySpace,
        uint space, PropertyID name, ResourceKind kind, ShaderStages stages, int register, UniformBlockField[] fields)
    {
        if (!bySpace.TryGetValue(space, out List<ResourceLayoutElementDescription>? elements))
            bySpace[space] = elements = [];

        elements.Add(new ResourceLayoutElementDescription(
            name,
            kind,
            stages,
            register,
            ResourceLayoutElementOptions.None,
            PropertyID.ToString(name) ?? string.Empty,
            fields));
    }
}
