using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

using Prowl.Slang;


namespace Prowl.Graphite.Compiler;


/// <summary>
/// The GLSL compiler module for all OpenGL backends.
/// </summary>
public partial class GLCompiler : CompilerModule
{
    private int _profileVersion;
    private TargetDescription _target;

    /// <inheritdoc/>
    public TargetDescription Target => _target;

    private readonly GraphicsBackend _backend;

    /// <inheritdoc/>
    public GraphicsBackend Backend => _backend;


    /// <summary>
    /// Creates a new instance of <see cref="GLCompiler"/> 
    /// </summary>
    public GLCompiler(string profileString = "glsl_450", GraphicsBackend backend = GraphicsBackend.OpenGL)
    {
        _profileVersion = int.Parse(profileString.Split('_')[^1]);

        _backend = backend;
        _target = new()
        {
            Profile = GlobalSession.FindProfile(profileString),
            Format = CompileTarget.Glsl,
        };
    }


    /// <inheritdoc/>
    public ShaderDescription CompileForTarget(ComponentType linkedComponent, int layoutIndex, DiagnosticHandler handler)
    {
        ShaderDescription description = SlangReflector.BuildDescription(linkedComponent, layoutIndex, handler);
        description.ResourceLayouts = Reflect(linkedComponent, layoutIndex, description.Stages);
        MakePortableGlsl(description.Stages);
        return description;
    }


    // Slang emits the Vulkan GLSL vertex/instance builtins (gl_VertexIndex / gl_InstanceIndex) for
    // its GLSL target, which desktop GL does not define (it has gl_VertexID / gl_InstanceID). Rather
    // than depend on GL_KHR_vulkan_glsl - which many desktop drivers do not expose - the emitted
    // GLSL is rewritten to the core GL builtins so it compiles on any OpenGL driver. The names are
    // unique identifiers, so plain token replacement is unambiguous.
    private void MakePortableGlsl(ShaderStageDescription[] stages)
    {
        for (int i = 0; i < stages.Length; i++)
        {
            string source = Encoding.UTF8.GetString(stages[i].ShaderBytes);

            source = source
                .Replace("gl_VertexIndex", "gl_VertexID")
                .Replace("gl_InstanceIndex", "gl_InstanceID");

            // Not robust portability code from vk GLSL->gl 4.1
            // actually very broken
            if (_profileVersion < 450)
            {
                source = source
                    .Replace("#version 450", $"#version {_profileVersion}");
                source = VulkanGLSLRemovalRegex().Replace(source, "");
                System.Console.WriteLine(source);
            }

            stages[i].ShaderBytes = Encoding.UTF8.GetBytes(source);
        }
    }


    static ResourceLayoutDescription[] Reflect(ComponentType linkedComponent, int layoutIndex, ShaderStageDescription[] stages)
    {
        ShaderStages programStages = ShaderStages.None;
        foreach (ShaderStageDescription stage in stages)
            programStages |= stage.Stage;

        ShaderReflection layout = linkedComponent.GetLayout(layoutIndex, out _);

        // Slang assigns a per-base-name disambiguation suffix in declaration order (the first use of a
        // base gets _0, the next _1, ...). The counter is shared across the whole traversal so the
        // reconstructed names match the order Slang emits them.
        Dictionary<string, int> baseNameCounts = [];
        Dictionary<uint, List<ResourceLayoutElementDescription>> bySet = [];

        foreach (VariableLayoutReflection parameter in layout.Parameters)
            Collect(parameter, baseSpace: 0, namePrefix: "", programStages, bySet, baseNameCounts);

        List<ResourceLayoutDescription> layouts = [];
        foreach ((uint set, List<ResourceLayoutElementDescription> elements) in bySet)
            layouts.Add(new ResourceLayoutDescription(set, [.. elements]));

        return [.. layouts];
    }


    // The GL target lays parameter blocks out exactly as Vulkan does: each block opens its own
    // descriptor set and its inner resources bind from the block's descriptor-table slots. OpenGL has
    // no native sets, so the backend uses the (set, slot) pair purely as an addressing key and resolves
    // the actual GL object by its emitted name. Slang prefixes block-member names with the block path
    // (perObject.detail -> perObject_detail), which is reconstructed here.
    static void Collect(
        VariableLayoutReflection parameter, uint baseSpace, string namePrefix, ShaderStages stages,
        Dictionary<uint, List<ResourceLayoutElementDescription>> bySet, Dictionary<string, int> baseNameCounts)
    {
        TypeLayoutReflection typeLayout = parameter.TypeLayout;

        if (typeLayout.Kind == TypeKind.ParameterBlock)
        {
            CollectParameterBlock(parameter, baseSpace, namePrefix, stages, bySet, baseNameCounts);
            return;
        }

        if (!SlangReflector.TryGetResourceKind(typeLayout, out ResourceKind kind))
            return;

        uint set = baseSpace + parameter.GetBindingSpace(ParameterCategory.DescriptorTableSlot);
        int binding = (int)parameter.GetOffset(ParameterCategory.DescriptorTableSlot);

        string glName = Suffixed(BaseName(parameter, kind, namePrefix), baseNameCounts);

        UniformBlockField[] fields = kind == ResourceKind.UniformBuffer
            ? SlangReflector.ReflectUniformFields(typeLayout.ElementTypeLayout)
            : [];

        Add(bySet, set, parameter.Name, kind, stages, binding, glName, fields);
    }


    static void CollectParameterBlock(
        VariableLayoutReflection block, uint baseSpace, string namePrefix, ShaderStages stages,
        Dictionary<uint, List<ResourceLayoutElementDescription>> bySet, Dictionary<string, int> baseNameCounts)
    {
        TypeLayoutReflection typeLayout = block.TypeLayout;
        uint set = baseSpace + block.GetOffset(ParameterCategory.SubElementRegisterSpace);

        TypeLayoutReflection elementLayout = typeLayout.ElementTypeLayout;

        // Ordinary uniform data within the block collapses into one implicit uniform block, emitted as
        // block_<ElementType> (no block-path prefix), at the binding the container reserves for it.
        if (elementLayout.GetSize() > 0)
        {
            int uboBinding = (int)typeLayout.ContainerVarLayout.GetOffset(ParameterCategory.DescriptorTableSlot);
            string uboName = Suffixed($"block_{elementLayout.Name}", baseNameCounts);
            Add(bySet, set, block.Name, ResourceKind.UniformBuffer, stages, uboBinding, uboName,
                SlangReflector.ReflectUniformFields(elementLayout));
        }

        // Resources declared inside the block bind into the block's own set; their emitted names carry
        // the block path as a prefix.
        string childPrefix = $"{namePrefix}{block.Name}_";
        foreach (VariableLayoutReflection field in elementLayout.Fields)
            if (field.TypeLayout.Kind != TypeKind.Scalar
                && field.TypeLayout.Kind != TypeKind.Vector
                && field.TypeLayout.Kind != TypeKind.Matrix)
                Collect(field, set, childPrefix, stages, bySet, baseNameCounts);
    }


    static string Suffixed(string baseName, Dictionary<string, int> baseNameCounts)
    {
        int suffix = baseNameCounts.TryGetValue(baseName, out int n) ? n : 0;
        baseNameCounts[baseName] = suffix + 1;

        return $"{baseName}_{suffix}";
    }


    static string BaseName(VariableLayoutReflection parameter, ResourceKind kind, string namePrefix)
    {
        TypeLayoutReflection typeLayout = parameter.TypeLayout;

        return kind switch
        {
            ResourceKind.UniformBuffer => $"block_{typeLayout.ElementTypeLayout.Name}",
            ResourceKind.StructuredBufferReadOnly => $"StructuredBuffer_{typeLayout.ResourceResultType.Name}_t",
            ResourceKind.StructuredBufferReadWrite => $"RWStructuredBuffer_{typeLayout.ResourceResultType.Name}_t",

            // Textures and samplers keep their source identifier, prefixed by any enclosing block path.
            _ => $"{namePrefix}{parameter.Name}",
        };
    }


    static void Add(
        Dictionary<uint, List<ResourceLayoutElementDescription>> bySet,
        uint set, PropertyID name, ResourceKind kind, ShaderStages stages, int binding, string glName, UniformBlockField[] fields)
    {
        if (!bySet.TryGetValue(set, out List<ResourceLayoutElementDescription>? elements))
            bySet[set] = elements = [];

        elements.Add(new ResourceLayoutElementDescription(
            name,
            kind,
            stages,
            binding,
            ResourceLayoutElementOptions.None,
            glName,
            fields));
    }

    [GeneratedRegex(@"\blayout\s*\(\s*(?=[^)]*\b(?:set|binding)\s*=)[^)]*\)\s*")]
    private static partial Regex VulkanGLSLRemovalRegex();
}
