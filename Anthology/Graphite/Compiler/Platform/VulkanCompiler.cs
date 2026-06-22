using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;

using Prowl.Slang;


namespace Prowl.Graphite.Compiler;


/// <summary>
/// The SPIR-V compiler module for all Vulkan backends.
/// </summary>
public class VulkanCompiler : CompilerModule
{
    private TargetDescription _target;

    /// <inheritdoc/>
    public TargetDescription Target => _target;

    /// <inheritdoc/>
    public GraphicsBackend Backend => GraphicsBackend.Vulkan;


    /// <summary>
    /// Creates a new instance of <see cref="VulkanCompiler"/> 
    /// </summary>
    /// <param name="profileString"></param>
    public VulkanCompiler(string profileString = "spirv_1_5")
    {
        _target = new()
        {
            Profile = GlobalSession.FindProfile(profileString),
            Format = CompileTarget.Spirv
        };
    }


    /// <inheritdoc/>
    public ShaderDescription CompileForTarget(ComponentType linkedComponent, int layoutIndex, DiagnosticHandler handler)
    {
        ShaderDescription description = SlangReflector.BuildDescription(linkedComponent, layoutIndex, handler, entryPointNameOverride: "main");
        description.ResourceLayouts = Reflect(linkedComponent, layoutIndex, description.Stages);
        return description;
    }


    static ResourceLayoutDescription[] Reflect(ComponentType linkedComponent, int layoutIndex, ShaderStageDescription[] stages)
    {
        ShaderStages programStages = ShaderStages.None;
        foreach (ShaderStageDescription stage in stages)
            programStages |= stage.Stage;

        ShaderReflection layout = linkedComponent.GetLayout(layoutIndex, out _);

        Dictionary<uint, List<ResourceLayoutElementDescription>> bySet = [];

        foreach (VariableLayoutReflection parameter in layout.Parameters)
            Collect(parameter, baseSpace: 0, programStages, bySet);

        List<ResourceLayoutDescription> layouts = [];
        foreach ((uint set, List<ResourceLayoutElementDescription> elements) in bySet)
            layouts.Add(new ResourceLayoutDescription(set, [.. elements]));

        return [.. layouts];
    }


    static void Collect(
        VariableLayoutReflection parameter, uint baseSpace, ShaderStages stages,
        Dictionary<uint, List<ResourceLayoutElementDescription>> bySet)
    {
        TypeLayoutReflection typeLayout = parameter.TypeLayout;

        if (typeLayout.Kind == TypeKind.ParameterBlock)
        {
            CollectParameterBlock(parameter, baseSpace, stages, bySet);
            return;
        }

        if (!SlangReflector.TryGetResourceKind(typeLayout, out ResourceKind kind))
            return;

        uint set = baseSpace + parameter.GetBindingSpace(ParameterCategory.DescriptorTableSlot);
        int binding = (int)parameter.GetOffset(ParameterCategory.DescriptorTableSlot);

        UniformBlockField[] fields = kind == ResourceKind.UniformBuffer
            ? SlangReflector.ReflectUniformFields(typeLayout.ElementTypeLayout)
            : [];

        ResourceLayoutElementOptions options = SlangReflector.IsCombinedTextureSampler(typeLayout)
            ? ResourceLayoutElementOptions.CombinedImageSampler
            : ResourceLayoutElementOptions.None;

        Add(bySet, set, parameter.Name, kind, stages, binding, options, fields);
    }


    static void CollectParameterBlock(
        VariableLayoutReflection block, uint baseSpace, ShaderStages stages,
        Dictionary<uint, List<ResourceLayoutElementDescription>> bySet)
    {
        TypeLayoutReflection typeLayout = block.TypeLayout;
        uint set = baseSpace + RegisterSpaceOf(block);

        // Ordinary uniform data within the block collapses into one implicit uniform buffer, placed
        // at the binding the container reserves for it (binding 0 when the block holds uniform data).
        TypeLayoutReflection elementLayout = typeLayout.ElementTypeLayout;
        if (elementLayout.GetSize() > 0)
        {
            int uboBinding = (int)typeLayout.ContainerVarLayout.GetOffset(ParameterCategory.DescriptorTableSlot);
            Add(bySet, set, block.Name, ResourceKind.UniformBuffer, stages, uboBinding,
                ResourceLayoutElementOptions.None, SlangReflector.ReflectUniformFields(elementLayout));
        }

        // Resources declared inside the block bind into the block's own space; their slot offsets are
        // already absolute within that space (past the implicit uniform buffer, if any).
        foreach (VariableLayoutReflection field in elementLayout.Fields)
            if (field.TypeLayout.Kind != TypeKind.Scalar
                && field.TypeLayout.Kind != TypeKind.Vector
                && field.TypeLayout.Kind != TypeKind.Matrix)
                Collect(field, set, stages, bySet);
    }


    // The descriptor-set index a parameter block occupies, as assigned by Slang.
    static uint RegisterSpaceOf(VariableLayoutReflection block) =>
        block.GetOffset(ParameterCategory.SubElementRegisterSpace);


    static void Add(
        Dictionary<uint, List<ResourceLayoutElementDescription>> bySet,
        uint set, PropertyID name, ResourceKind kind, ShaderStages stages, int binding,
        ResourceLayoutElementOptions options, UniformBlockField[] fields)
    {
        if (!bySet.TryGetValue(set, out List<ResourceLayoutElementDescription>? elements))
            bySet[set] = elements = [];

        elements.Add(new ResourceLayoutElementDescription(
            name,
            kind,
            stages,
            binding,
            options,
            PropertyID.ToString(name) ?? string.Empty,
            fields));
    }
}
