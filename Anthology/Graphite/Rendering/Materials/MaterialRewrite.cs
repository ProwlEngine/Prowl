using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Prowl.Graphite.Rendering;

/// <summary>A shader cannot be used by the renderer, for example a loose uniform that is neither a material property nor a global.</summary>
public sealed class ShaderLoadException(string message) : Exception(message);

/// <summary>Where material passes find the renderer's data. Every material program shares these.</summary>
internal static class GraphiteBindings
{
    public const uint FrameSet = 0;
    public const uint ViewSet = 1;
    public const uint PassSet = 2;

    /// <summary>Resources a material pass declares that are not the renderer's move here.</summary>
    public const uint UserSet = 3;

    public const int Texture2DTable = 0;
    public const int Texture2DArrayTable = 1;
    public const int Texture3DTable = 2;
    public const int TextureCubeTable = 3;
    public const int TextureCubeArrayTable = 4;


    public const int SamplerTable = 0;
    public const int Globals = 1;
    public const int Views = 2;
    public const int SpatialRecords = 3;
    public const int SpatialNodes = 4;
    public const int SpatialRegions = 5;
    public const int SpatialRoots = 6;
    public const int Chunks = 13;
    public const int Transforms = 14;
    public const int PreviousTransforms = 15;
    public const int Objects = 16;
    public const int MaterialSlots = 17;
    public const int WorldLighting = 22;
    public const int ObjectProbes = 23;
    public const int SkyAmbient = 26;
    public const int Directionals = 27;
    public const int ShadowSampler = 28;
    public const int ShadowFaces = 29;
    public const int Cascades = 30;
    public const int ScreenVisibility = 31;
    public const int ScreenIrradiance = 32;
    public const int ScreenSampler = 33;
    public const int SceneColor = 34;
    public const int SceneColorSampler = 35;

    public const int Materials = 0;
    public const int Instances = 1;
    public const int DrawRecords = 2;
    public const int Vertices = 3;

    /// <summary>The flat varying carrying the material slot from the vertex to the fragment stage.</summary>
    public const uint MaterialSlotLocation = 31;

    /// <summary>The flat varying carrying the object index from the vertex to the fragment stage.</summary>
    public const uint ObjectIndexLocation = 30;

    public const string MaterialsName = "Graphite_Materials";
    public const string InstancesName = "Graphite_Instances";
    public const string DrawRecordsName = "Graphite_DrawRecords";
    public const string VerticesName = "Graphite_Vertices";

    private const ShaderStages Graphics = ShaderStages.Vertex | ShaderStages.Fragment;

    /// <summary>
    /// The texture table every material program declares as set 0: the renderer's bindless textures, one table shared by
    /// every world and view, written only at slots no frame in flight reads.
    /// </summary>
    public static ResourceLayoutDescription FrameTable(uint textures2D, uint otherTextures) => new(FrameSet,
        new ResourceLayoutElementDescription("Graphite_Textures2D", ResourceKind.TextureReadOnly, Graphics, Texture2DTable) { ArrayCount = textures2D },
        new ResourceLayoutElementDescription("Graphite_Textures2DArray", ResourceKind.TextureReadOnly, Graphics, Texture2DArrayTable) { ArrayCount = otherTextures },
        new ResourceLayoutElementDescription("Graphite_Textures3D", ResourceKind.TextureReadOnly, Graphics, Texture3DTable) { ArrayCount = otherTextures },
        new ResourceLayoutElementDescription("Graphite_TexturesCube", ResourceKind.TextureReadOnly, Graphics, TextureCubeTable) { ArrayCount = otherTextures },
        new ResourceLayoutElementDescription("Graphite_TexturesCubeArray", ResourceKind.TextureReadOnly, Graphics, TextureCubeArrayTable) { ArrayCount = otherTextures })
    {
        Table = true,
    };

    /// <summary>
    /// The view table every material program declares as set 1: the sampler table with the view's bias, the world's
    /// globals, the view blocks and per view trees, then the world's buffers.
    /// </summary>
    public static ResourceLayoutDescription ViewTable(uint samplers) => new(ViewSet,
        new ResourceLayoutElementDescription("Graphite_Samplers", ResourceKind.Sampler, Graphics, SamplerTable) { ArrayCount = samplers },
        new ResourceLayoutElementDescription("Graphite_Globals", ResourceKind.StructuredBufferReadOnly, Graphics, Globals),
        new ResourceLayoutElementDescription("Graphite_Views", ResourceKind.StructuredBufferReadOnly, Graphics, Views),
        new ResourceLayoutElementDescription("Graphite_SpatialRecords", ResourceKind.StructuredBufferReadOnly, Graphics, SpatialRecords) { ArrayCount = SpatialIndex.MaxSets * 2 },
        new ResourceLayoutElementDescription("Graphite_SpatialNodes", ResourceKind.StructuredBufferReadOnly, Graphics, SpatialNodes) { ArrayCount = SpatialIndex.MaxSets * 3 },
        new ResourceLayoutElementDescription("Graphite_SpatialRegions", ResourceKind.StructuredBufferReadOnly, Graphics, SpatialRegions) { ArrayCount = SpatialIndex.MaxSets },
        new ResourceLayoutElementDescription("Graphite_SpatialRoots", ResourceKind.StructuredBufferReadOnly, Graphics, SpatialRoots),
        new ResourceLayoutElementDescription("Graphite_Chunks", ResourceKind.StructuredBufferReadOnly, Graphics, Chunks),
        new ResourceLayoutElementDescription("Graphite_Transforms", ResourceKind.StructuredBufferReadOnly, Graphics, Transforms),
        new ResourceLayoutElementDescription("Graphite_PreviousTransforms", ResourceKind.StructuredBufferReadOnly, Graphics, PreviousTransforms),
        new ResourceLayoutElementDescription("Graphite_Objects", ResourceKind.StructuredBufferReadOnly, Graphics, Objects),
        new ResourceLayoutElementDescription("Graphite_MaterialSlots", ResourceKind.StructuredBufferReadOnly, Graphics, MaterialSlots),
        new ResourceLayoutElementDescription("Graphite_WorldLighting", ResourceKind.StructuredBufferReadOnly, Graphics, WorldLighting),
        new ResourceLayoutElementDescription("Graphite_ObjectProbes", ResourceKind.StructuredBufferReadOnly, Graphics, ObjectProbes),
        new ResourceLayoutElementDescription("Graphite_SkyAmbientSH", ResourceKind.StructuredBufferReadOnly, Graphics, SkyAmbient),
        new ResourceLayoutElementDescription("Graphite_Directionals", ResourceKind.StructuredBufferReadOnly, Graphics, Directionals),
        new ResourceLayoutElementDescription("Graphite_ShadowSampler", ResourceKind.Sampler, Graphics, ShadowSampler),
        new ResourceLayoutElementDescription("Graphite_ShadowFaces", ResourceKind.StructuredBufferReadOnly, Graphics, ShadowFaces),
        new ResourceLayoutElementDescription("Graphite_Cascades", ResourceKind.StructuredBufferReadOnly, Graphics, Cascades),
        new ResourceLayoutElementDescription("Graphite_ScreenVisibility", ResourceKind.TextureReadOnly, Graphics, ScreenVisibility),
        new ResourceLayoutElementDescription("Graphite_ScreenIrradiance", ResourceKind.TextureReadOnly, Graphics, ScreenIrradiance),
        new ResourceLayoutElementDescription("Graphite_ScreenSampler", ResourceKind.Sampler, Graphics, ScreenSampler),
        new ResourceLayoutElementDescription("Graphite_SceneColor", ResourceKind.TextureReadOnly, Graphics, SceneColor),
        new ResourceLayoutElementDescription("Graphite_SceneColorSampler", ResourceKind.Sampler, Graphics, SceneColorSampler))
    {
        Table = true,
    };

    public static int TextureTable(MaterialPropertyType type) => type switch
    {
        MaterialPropertyType.Texture2D => Texture2DTable,
        MaterialPropertyType.Texture2DArray => Texture2DArrayTable,
        MaterialPropertyType.Texture3D => Texture3DTable,
        MaterialPropertyType.TextureCube => TextureCubeTable,
        MaterialPropertyType.TextureCubeArray => TextureCubeArrayTable,
        _ => throw new ArgumentOutOfRangeException(nameof(type)),
    };
}

/// <summary>
/// The material rewrite of a compiled material pass. Loose uniforms that are material properties become loads from the
/// shader's material table at the draw's material slot, those named by a global pragma become loads from the globals
/// buffer, and material and global textures become accesses into the bindless tables at the index their record holds.
/// Works on SPIR-V after exhaustive inlining, so textures passed into functions are handled at their real use.
/// </summary>
internal static class MaterialRewrite
{
    public static ShaderDescription Rewrite(
        string shaderName,
        ShaderDescription description,
        MaterialSchema schema,
        IReadOnlyList<string> globals,
        GlobalRegistry registry,
        ResourceLayoutDescription frameTable,
        ResourceLayoutDescription viewTable)
    {
        RequireTableElement(frameTable, GraphiteBindings.Texture2DTable, ResourceKind.TextureReadOnly);
        RequireTableElement(viewTable, GraphiteBindings.SamplerTable, ResourceKind.Sampler);
        RequireTableElement(viewTable, GraphiteBindings.Globals, ResourceKind.StructuredBufferReadOnly);

        Plan plan = new(shaderName, description, schema, globals, registry, frameTable);
        ShaderDescription result = description;
        result.Stages = new ShaderStageDescription[description.Stages.Length];
        ShaderStages materialStages = 0;
        SpirvModule? vertex = null;
        for (int i = 0; i < description.Stages.Length; i++)
        {
            ShaderStageDescription stage = description.Stages[i];
            StageRewriter rewriter = new(plan, stage.Stage, new SpirvModule(stage.ShaderBytes));
            if (rewriter.Run())
                materialStages |= stage.Stage;
            if (stage.Stage == ShaderStages.Vertex)
                vertex = rewriter.Module;
            result.Stages[i] = stage with { ShaderBytes = rewriter.Module.ToBytes() };
        }

        if ((materialStages & ShaderStages.Fragment) != 0 && vertex?.FindVariableAtLocation(SpirvModule.Output, GraphiteBindings.MaterialSlotLocation) is null or 0)
            throw new ShaderLoadException($"{shaderName}: the fragment stage reads material properties but the vertex stage does not write the material slot. Include Graphite/Vertex.glsl.");

        result.ResourceLayouts = Layouts(plan, description.ResourceLayouts ?? [], frameTable, viewTable, materialStages);
        return result;
    }

    private static void RequireTableElement(ResourceLayoutDescription table, int binding, ResourceKind kind)
    {
        foreach (ResourceLayoutElementDescription element in table.Elements)
        {
            if (element.BindingIndex == binding && element.Kind == kind)
                return;
        }
        throw new ArgumentException($"The table of set {table.Set} needs a {kind} at binding {binding}.");
    }

    private static ResourceLayoutDescription[] Layouts(Plan plan, ResourceLayoutDescription[] original, ResourceLayoutDescription frameTable, ResourceLayoutDescription viewTable, ShaderStages materialStages)
    {
        List<ResourceLayoutElementDescription> pass = new();
        List<ResourceLayoutElementDescription> user = new();
        if (materialStages != 0)
            pass.Add(new ResourceLayoutElementDescription(GraphiteBindings.MaterialsName, ResourceKind.StructuredBufferReadOnly, materialStages, GraphiteBindings.Materials));

        foreach (ResourceLayoutDescription layout in original)
        {
            foreach (ResourceLayoutElementDescription element in layout.Elements)
            {
                if (plan.Removed.Contains((layout.Set, element.BindingIndex)))
                    continue;

                if (layout.Set == GraphiteBindings.FrameSet && plan.InTable(frameTable, element))
                    continue;
                if (layout.Set == GraphiteBindings.ViewSet)
                {
                    if (!plan.InTable(viewTable, element))
                        throw new ShaderLoadException($"{plan.ShaderName}: {element.Name.Text()} uses set 1, which belongs to the renderer's view data.");
                    continue;
                }

                List<ResourceLayoutElementDescription> target = layout.Set == GraphiteBindings.PassSet ? pass : user;
                if (element.ArrayCount > 1)
                    throw new ShaderLoadException($"{plan.ShaderName}: {element.Name.Text()} is an array of resources, which only the renderer's own tables hold.");
                if (target == user && element.Kind is not (ResourceKind.StructuredBufferReadOnly or ResourceKind.StructuredBufferReadWrite))
                    throw new ShaderLoadException($"{plan.ShaderName}: {element.Name.Text()} is a {element.Kind}, which a material pass only reads as a material or global property. Buffers come from Globals.SetBuffer.");
                if (target.Exists(e => e.BindingIndex == element.BindingIndex))
                    throw new ShaderLoadException($"{plan.ShaderName}: {element.Name.Text()} collides with another resource at binding {element.BindingIndex}.");
                target.Add(element);
            }
        }

        List<ResourceLayoutDescription> layouts = [frameTable, viewTable, new ResourceLayoutDescription(GraphiteBindings.PassSet, [.. pass])];
        if (user.Count > 0)
            layouts.Add(new ResourceLayoutDescription(GraphiteBindings.UserSet, [.. user]));
        return [.. layouts];
    }

    /// <summary>What the rewrite does with each resource of the program, shared by its stages.</summary>
    private sealed class Plan
    {
        public readonly string ShaderName;
        public readonly MaterialSchema Schema;
        public readonly GlobalRegistry Registry;
        public readonly HashSet<string> Globals;
        public readonly (uint Set, int Binding)? GlobalBlock;
        public readonly Dictionary<uint, string> GlobalFields = new();
        public readonly Dictionary<(uint Set, int Binding), (string Name, bool Global)> Textures = new();
        public readonly HashSet<(uint Set, int Binding)> Removed = new();
        public readonly HashSet<(uint Set, int Binding)> UserFrameResources = new();

        public Plan(string shaderName, ShaderDescription description, MaterialSchema schema, IReadOnlyList<string> globals, GlobalRegistry registry, ResourceLayoutDescription frameTable)
        {
            ShaderName = shaderName;
            Schema = schema;
            Registry = registry;
            Globals = new HashSet<string>(globals, StringComparer.Ordinal);

            foreach (ResourceLayoutDescription layout in description.ResourceLayouts ?? [])
            {
                foreach (ResourceLayoutElementDescription element in layout.Elements)
                {
                    string name = element.Name.Text();
                    if (element.Kind == ResourceKind.UniformBuffer && name == "$Global")
                    {
                        GlobalBlock = (layout.Set, element.BindingIndex);
                        Removed.Add((layout.Set, element.BindingIndex));
                        foreach (UniformBlockField field in element.UniformFields ?? [])
                            GlobalFields[field.Offset] = field.Name.Text();
                        continue;
                    }

                    int property = schema.Find(name);
                    bool isGlobal = Globals.Contains(name);
                    if (property < 0 && !isGlobal)
                    {
                        if (layout.Set == GraphiteBindings.FrameSet && !InTable(frameTable, element))
                            UserFrameResources.Add((layout.Set, element.BindingIndex));
                        continue;
                    }

                    bool texture = property >= 0 ? schema.Properties[property].Type >= MaterialPropertyType.Texture2D : element.Kind == ResourceKind.TextureReadOnly;
                    if (!texture || element.Kind != ResourceKind.TextureReadOnly)
                        throw new ShaderLoadException($"{shaderName}: {name} is declared as a {element.Kind} in the shader but as a {(property >= 0 ? schema.Properties[property].Type : "global")} property.");
                    if (element.ArrayCount > 1)
                        throw new ShaderLoadException($"{shaderName}: {name} is a texture array, material and global textures must be single textures.");

                    Textures[(layout.Set, element.BindingIndex)] = (name, property < 0);
                    Removed.Add((layout.Set, element.BindingIndex));
                }
            }
        }

        public bool InTable(ResourceLayoutDescription table, ResourceLayoutElementDescription element)
        {
            foreach (ResourceLayoutElementDescription candidate in table.Elements)
            {
                if (candidate.BindingIndex == element.BindingIndex && candidate.Name == element.Name)
                    return true;
            }
            return false;
        }
    }

    private enum Source : byte { Material, Global }

    private readonly record struct Member(Source Source, uint Offset, uint Type, bool Matrix);

    /// <summary>Rewrites one stage's module.</summary>
    private sealed class StageRewriter(Plan plan, ShaderStages stage, SpirvModule module)
    {
        public SpirvModule Module => module;

        private uint _globalBlock;
        private uint _globalStruct;
        private readonly Dictionary<uint, (string Name, bool Global)> _textureVars = new();
        private readonly Dictionary<(uint Struct, uint Member), uint> _memberOffsets = new();
        private readonly Dictionary<uint, uint> _memberTypes = new();

        private readonly List<Member> _materialMembers = new();
        private readonly List<Member> _globalMembers = new();
        private readonly Dictionary<uint, (Source Source, int Index)> _uniformTargets = new();
        private readonly Dictionary<uint, (Source Source, int Index, int Sampler, MaterialPropertyType Type, uint ImageType, bool Combined)> _textureTargets = new();

        private uint _materialsVar;
        private uint _globalsVar;
        private uint _samplerTable;
        private readonly Dictionary<uint, uint> _tableVars = new();
        private uint _slotVar;

        public bool Run()
        {
            Scan();
            if (_uniformTargets.Count == 0 && _textureTargets.Count == 0)
            {
                RemoveVariables();
                MoveUserResources();
                return false;
            }

            bool usesMaterial = false;
            foreach ((Source source, _) in _uniformTargets.Values)
                usesMaterial |= source == Source.Material;
            foreach (var target in _textureTargets.Values)
                usesMaterial |= target.Source == Source.Material;

            Declare(usesMaterial);
            Rewrite();
            RemoveVariables();
            MoveUserResources();
            return usesMaterial;
        }

        private void Scan()
        {
            foreach (uint[] i in module.Annotations())
            {
                if (SpirvModule.Op(i) == SpirvModule.OpMemberDecorate && i[3] == SpirvModule.DecorationOffset)
                    _memberOffsets[(i[1], i[2])] = i[4];
            }

            foreach (uint[] i in module.Globals())
            {
                if (SpirvModule.Op(i) != SpirvModule.OpVariable)
                    continue;

                uint id = i[2];
                if (!module.TryGetBinding(id, out uint set, out int binding))
                    continue;

                if (plan.GlobalBlock is { } block && block.Set == set && block.Binding == binding)
                {
                    _globalBlock = id;
                    _globalStruct = module.Pointee(i[1]);
                    uint[] structType = module.Type(_globalStruct);
                    for (uint m = 0; m + 2 < structType.Length; m++)
                        _memberTypes[m] = structType[2 + m];
                }
                else if (plan.Textures.TryGetValue((set, binding), out (string Name, bool Global) texture))
                {
                    _textureVars[id] = texture;
                }
            }

            foreach (uint[] i in module.Functions())
            {
                ushort op = SpirvModule.Op(i);
                if (op is SpirvModule.OpAccessChain or SpirvModule.OpInBoundsAccessChain && i[3] == _globalBlock && _globalBlock != 0)
                {
                    uint member = module.ConstantValue(i[4]);
                    if (!_uniformTargets.ContainsKey(member))
                        _uniformTargets[member] = UniformTarget(member);
                }
                else if (op == SpirvModule.OpLoad && _textureVars.TryGetValue(i[3], out (string Name, bool Global) texture) && !_textureTargets.ContainsKey(i[3]))
                    _textureTargets[i[3]] = TextureTarget(texture.Name, texture.Global, i[1]);
                else if (op == SpirvModule.OpFunctionCall)
                {
                    for (int a = 4; a < i.Length; a++)
                    {
                        if (_textureVars.ContainsKey(i[a]))
                            throw new ShaderLoadException($"{plan.ShaderName}: {_textureVars[i[a]].Name} is passed to a function that was not inlined. Material passes need the compiler's optimizer.");
                    }
                }
            }
        }

        private (Source, int) UniformTarget(uint member)
        {
            uint offset = _memberOffsets.TryGetValue((_globalStruct, member), out uint o) ? o : uint.MaxValue;
            if (!plan.GlobalFields.TryGetValue(offset, out string? name))
                throw new ShaderLoadException($"{plan.ShaderName}: a loose uniform at offset {offset} has no name.");

            uint type = _memberTypes[member];
            MaterialPropertyType shaderType = module.PropertyType(type)
                ?? throw new ShaderLoadException($"{plan.ShaderName}: the loose uniform {name} has a type material and global properties cannot hold.");

            int property = plan.Schema.Find(name);
            if (property >= 0)
            {
                MaterialProperty declared = plan.Schema.Properties[property];
                if (!SameShape(declared.Type, shaderType))
                    throw new ShaderLoadException($"{plan.ShaderName}: {name} is a {declared.Type} in Properties but a {shaderType} in the shader.");
                return (Source.Material, AddMember(_materialMembers, new Member(Source.Material, declared.Offset, type, shaderType == MaterialPropertyType.Matrix)));
            }

            if (plan.Globals.Contains(name))
            {
                if (!plan.Registry.Declare(name, shaderType, out GlobalProperty global, out string? error))
                    throw new ShaderLoadException($"{plan.ShaderName}: {error}");
                return (Source.Global, AddMember(_globalMembers, new Member(Source.Global, global.Offset * 4, type, shaderType == MaterialPropertyType.Matrix)));
            }

            throw new ShaderLoadException($"{plan.ShaderName}: the loose uniform {name} is neither a material property nor named by a global pragma, so it would read zero.");
        }

        private (Source, int, int, MaterialPropertyType, uint, bool) TextureTarget(string name, bool global, uint loadedType)
        {
            uint[] type = module.Type(loadedType);
            bool combined = SpirvModule.Op(type) == SpirvModule.OpTypeSampledImage;
            uint imageType = combined ? type[2] : loadedType;
            MaterialPropertyType shaderType = module.ImagePropertyType(imageType)
                ?? throw new ShaderLoadException($"{plan.ShaderName}: {name} has an image type material and global textures cannot hold.");
            uint uint32 = module.TypeUint();

            if (!global)
            {
                MaterialProperty declared = plan.Schema.Properties[plan.Schema.Find(name)];
                if (declared.Type != shaderType)
                    throw new ShaderLoadException($"{plan.ShaderName}: {name} is a {declared.Type} in Properties but a {shaderType} in the shader.");
                int index = AddMember(_materialMembers, new Member(Source.Material, declared.Offset, uint32, false));
                int sampler = AddMember(_materialMembers, new Member(Source.Material, declared.Offset + 4, uint32, false));
                return (Source.Material, index, sampler, shaderType, imageType, combined);
            }

            if (!plan.Registry.Declare(name, shaderType, out GlobalProperty property, out string? error))
                throw new ShaderLoadException($"{plan.ShaderName}: {error}");
            int globalIndex = AddMember(_globalMembers, new Member(Source.Global, property.Offset * 4, uint32, false));
            int globalSampler = AddMember(_globalMembers, new Member(Source.Global, property.Offset * 4 + 4, uint32, false));
            return (Source.Global, globalIndex, globalSampler, shaderType, imageType, combined);
        }

        private static int AddMember(List<Member> members, Member member)
        {
            int existing = members.FindIndex(m => m.Offset == member.Offset);
            if (existing >= 0)
                return existing;
            members.Add(member);
            return members.Count - 1;
        }

        private static bool SameShape(MaterialPropertyType declared, MaterialPropertyType shader)
            => declared == shader || (declared is MaterialPropertyType.Color && shader is MaterialPropertyType.Float4);

        // Members sit in the record struct sorted by offset, so the indices handed out while scanning are remapped here.
        private int[] Order(List<Member> members, out uint structType)
        {
            int[] order = new int[members.Count];
            List<int> sorted = new();
            for (int i = 0; i < members.Count; i++)
                sorted.Add(i);
            sorted.Sort((a, b) => members[a].Offset.CompareTo(members[b].Offset));
            uint[] types = new uint[members.Count];
            for (int position = 0; position < sorted.Count; position++)
            {
                order[sorted[position]] = position;
                types[position] = members[sorted[position]].Type;
            }

            structType = module.AddStruct(types);
            for (int position = 0; position < sorted.Count; position++)
            {
                Member member = members[sorted[position]];
                module.MemberDecorate(structType, (uint)position, SpirvModule.DecorationOffset, member.Offset);
                if (member.Matrix)
                {
                    module.MemberDecorate(structType, (uint)position, SpirvModule.DecorationColMajor);
                    module.MemberDecorate(structType, (uint)position, SpirvModule.DecorationMatrixStride, 16);
                }
                module.MemberDecorate(structType, (uint)position, SpirvModule.DecorationNonWritable);
            }
            return order;
        }

        private int[] _materialOrder = [];
        private int[] _globalOrder = [];

        private void Declare(bool usesMaterial)
        {
            if (_materialMembers.Count > 0)
            {
                _materialOrder = Order(_materialMembers, out uint record);
                uint array = module.AddGlobal(SpirvModule.OpTypeRuntimeArray, record);
                module.Decorate(array, SpirvModule.DecorationArrayStride, plan.Schema.StrideWords * 4);
                uint block = module.AddStruct([array]);
                module.Decorate(block, SpirvModule.DecorationBlock);
                module.MemberDecorate(block, 0, SpirvModule.DecorationOffset, 0);
                module.MemberDecorate(block, 0, SpirvModule.DecorationNonWritable);
                _materialsVar = module.AddVariable(module.Pointer(SpirvModule.StorageBuffer, block), SpirvModule.StorageBuffer);
                module.Decorate(_materialsVar, SpirvModule.DecorationDescriptorSet, GraphiteBindings.PassSet);
                module.Decorate(_materialsVar, SpirvModule.DecorationBinding, GraphiteBindings.Materials);
            }

            if (_globalMembers.Count > 0)
            {
                _globalOrder = Order(_globalMembers, out uint block);
                module.Decorate(block, SpirvModule.DecorationBlock);
                _globalsVar = module.AddVariable(module.Pointer(SpirvModule.StorageBuffer, block), SpirvModule.StorageBuffer);
                module.Decorate(_globalsVar, SpirvModule.DecorationDescriptorSet, GraphiteBindings.ViewSet);
                module.Decorate(_globalsVar, SpirvModule.DecorationBinding, GraphiteBindings.Globals);
            }

            if (_textureTargets.Count > 0)
            {
                module.AddCapability(SpirvModule.CapabilityShaderNonUniform);
                module.AddCapability(SpirvModule.CapabilityRuntimeDescriptorArray);
                module.AddCapability(SpirvModule.CapabilitySampledImageArrayNonUniformIndexing);
                foreach (var target in _textureTargets.Values)
                {
                    uint tableBinding = (uint)GraphiteBindings.TextureTable(target.Type);
                    if (!_tableVars.ContainsKey(target.ImageType) && module.FindTable(GraphiteBindings.FrameSet, tableBinding, target.ImageType) is var shared and not 0)
                        _tableVars[target.ImageType] = shared;
                    if (!_tableVars.ContainsKey(target.ImageType))
                    {
                        uint array = module.AddGlobal(SpirvModule.OpTypeRuntimeArray, target.ImageType);
                        uint table = module.AddVariable(module.Pointer(SpirvModule.UniformConstant, array), SpirvModule.UniformConstant);
                        module.Decorate(table, SpirvModule.DecorationDescriptorSet, GraphiteBindings.FrameSet);
                        module.Decorate(table, SpirvModule.DecorationBinding, (uint)GraphiteBindings.TextureTable(target.Type));
                        _tableVars[target.ImageType] = table;
                    }

                    if (target.Combined && _samplerTable == 0)
                        _samplerTable = module.FindTable(GraphiteBindings.ViewSet, (uint)GraphiteBindings.SamplerTable, module.TypeSampler());
                    if (target.Combined && _samplerTable == 0)
                    {
                        uint array = module.AddGlobal(SpirvModule.OpTypeRuntimeArray, module.TypeSampler());
                        _samplerTable = module.AddVariable(module.Pointer(SpirvModule.UniformConstant, array), SpirvModule.UniformConstant);
                        module.Decorate(_samplerTable, SpirvModule.DecorationDescriptorSet, GraphiteBindings.ViewSet);
                        module.Decorate(_samplerTable, SpirvModule.DecorationBinding, (uint)GraphiteBindings.SamplerTable);
                    }
                }
            }

            if (usesMaterial)
                _slotVar = SlotVariable();

            foreach (uint id in (uint[])[_materialsVar, _globalsVar, _samplerTable, _slotVar, .. _tableVars.Values])
            {
                if (id != 0)
                    module.AddToInterface(id);
            }
        }

        private uint SlotVariable()
        {
            uint storage = stage == ShaderStages.Fragment ? SpirvModule.Input : SpirvModule.Output;
            uint existing = module.FindVariableAtLocation(storage, GraphiteBindings.MaterialSlotLocation);
            if (existing != 0)
                return existing;

            if (stage != ShaderStages.Fragment)
                throw new ShaderLoadException($"{plan.ShaderName}: the {stage} stage reads material properties but does not write the material slot. Include Graphite/Vertex.glsl.");

            uint variable = module.AddVariable(module.Pointer(SpirvModule.Input, module.TypeUint()), SpirvModule.Input);
            module.Decorate(variable, SpirvModule.DecorationLocation, GraphiteBindings.MaterialSlotLocation);
            module.Decorate(variable, SpirvModule.DecorationFlat);
            return variable;
        }

        private void Rewrite()
        {
            uint uint32 = module.TypeUint();
            HashSet<uint> retyped = new();
            List<uint[]> output = new();
            foreach (uint[] i in module.Functions())
            {
                ushort op = SpirvModule.Op(i);
                if (op is SpirvModule.OpAccessChain or SpirvModule.OpInBoundsAccessChain)
                {
                    if (i[3] == _globalBlock && _globalBlock != 0)
                    {
                        (Source source, int index) = _uniformTargets[module.ConstantValue(i[4])];
                        uint pointer = module.Pointer(SpirvModule.StorageBuffer, module.Pointee(i[1]));
                        List<uint> words = [0, pointer, i[2]];
                        if (source == Source.Material)
                        {
                            uint slot = module.NewId();
                            output.Add([SpirvModule.Header(SpirvModule.OpLoad, 4), uint32, slot, _slotVar]);
                            words.AddRange([_materialsVar, module.ConstantUint(0), slot, module.ConstantUint((uint)_materialOrder[index])]);
                        }
                        else
                        {
                            words.AddRange([_globalsVar, module.ConstantUint((uint)_globalOrder[index])]);
                        }
                        for (int k = 5; k < i.Length; k++)
                            words.Add(i[k]);
                        words[0] = SpirvModule.Header(op, words.Count);
                        output.Add([.. words]);
                        retyped.Add(i[2]);
                        continue;
                    }

                    if (retyped.Contains(i[3]))
                    {
                        uint[] copy = (uint[])i.Clone();
                        copy[1] = module.Pointer(SpirvModule.StorageBuffer, module.Pointee(i[1]));
                        output.Add(copy);
                        retyped.Add(i[2]);
                        continue;
                    }
                }

                if (op == SpirvModule.OpLoad && _textureTargets.TryGetValue(i[3], out var target))
                {
                    EmitTexture(output, i[1], i[2], target.Source, target.Index, target.Sampler, target.ImageType, target.Combined);
                    continue;
                }

                output.Add(i);
            }
            PropagateNonUniform(output);
            RejectUnsupportedUses(output, retyped);
            module.ReplaceFunctions(output);
        }

        private readonly HashSet<uint> _nonUniform = new();

        private void NonUniform(uint id)
        {
            if (_nonUniform.Add(id))
                module.Decorate(id, SpirvModule.DecorationNonUniform);
        }

        // Values made from a non uniform image or sampler stay non uniform through copies, phis, selects and image extraction.
        private void PropagateNonUniform(List<uint[]> instructions)
        {
            bool changed = _nonUniform.Count > 0;
            while (changed)
            {
                changed = false;
                foreach (uint[] i in instructions)
                {
                    ushort op = SpirvModule.Op(i);
                    int first = op switch
                    {
                        SpirvModule.OpCopyObject or SpirvModule.OpImage or SpirvModule.OpSampledImage or SpirvModule.OpPhi => 3,
                        SpirvModule.OpSelect => 4,
                        _ => -1,
                    };
                    if (first < 0 || _nonUniform.Contains(i[2]))
                        continue;

                    int step = op == SpirvModule.OpPhi ? 2 : 1;
                    for (int k = first; k < i.Length; k += step)
                    {
                        if (_nonUniform.Contains(i[k]))
                        {
                            NonUniform(i[2]);
                            changed = true;
                            break;
                        }
                    }
                }
            }
        }

        // Rewritten pointers and removed variables may only be loaded or indexed. Anything else would leave an invalid module.
        private void RejectUnsupportedUses(List<uint[]> instructions, HashSet<uint> retyped)
        {
            HashSet<uint> watched = new(retyped);
            watched.UnionWith(_textureVars.Keys);
            if (_globalBlock != 0)
                watched.Add(_globalBlock);

            foreach (uint[] i in instructions)
            {
                ushort op = SpirvModule.Op(i);
                (int First, int Step) operands = op switch
                {
                    SpirvModule.OpStore or SpirvModule.OpCopyMemory => (1, 1),
                    SpirvModule.OpCopyObject => (3, 1),
                    SpirvModule.OpPhi => (3, 2),
                    SpirvModule.OpSelect => (4, 1),
                    SpirvModule.OpFunctionCall => (4, 1),
                    SpirvModule.OpLoad when i[3] == _globalBlock => (3, 1),
                    _ => (-1, 0),
                };
                if (operands.First < 0)
                    continue;

                for (int k = operands.First; k < i.Length; k += operands.Step)
                {
                    if (watched.Contains(i[k]))
                        throw new ShaderLoadException($"{plan.ShaderName}: a loose uniform or material texture is used in a way the material rewrite does not support (SPIR-V op {op}).");
                }
            }
        }

        private void EmitTexture(List<uint[]> output, uint resultType, uint result, Source source, int index, int sampler, uint imageType, bool combined)
        {
            uint uint32 = module.TypeUint();
            uint pointerUint = module.Pointer(SpirvModule.StorageBuffer, uint32);
            uint textureIndex = LoadRecordWord(output, source, index, pointerUint, uint32);
            NonUniform(textureIndex);

            uint imagePointer = module.NewId();
            output.Add([SpirvModule.Header(SpirvModule.OpAccessChain, 5), module.Pointer(SpirvModule.UniformConstant, imageType), imagePointer, _tableVars[imageType], textureIndex]);
            NonUniform(imagePointer);

            uint image = combined ? module.NewId() : result;
            output.Add([SpirvModule.Header(SpirvModule.OpLoad, 4), imageType, image, imagePointer]);
            NonUniform(image);
            if (!combined)
                return;

            uint samplerType = module.TypeSampler();
            uint samplerIndex = LoadRecordWord(output, source, sampler, pointerUint, uint32);
            NonUniform(samplerIndex);
            uint samplerPointer = module.NewId();
            output.Add([SpirvModule.Header(SpirvModule.OpAccessChain, 5), module.Pointer(SpirvModule.UniformConstant, samplerType), samplerPointer, _samplerTable, samplerIndex]);
            NonUniform(samplerPointer);
            uint samplerValue = module.NewId();
            output.Add([SpirvModule.Header(SpirvModule.OpLoad, 4), samplerType, samplerValue, samplerPointer]);
            NonUniform(samplerValue);

            output.Add([SpirvModule.Header(SpirvModule.OpSampledImage, 5), resultType, result, image, samplerValue]);
            NonUniform(result);
        }

        private uint LoadRecordWord(List<uint[]> output, Source source, int index, uint pointerUint, uint uint32)
        {
            uint pointer = module.NewId();
            if (source == Source.Material)
            {
                uint slot = module.NewId();
                output.Add([SpirvModule.Header(SpirvModule.OpLoad, 4), uint32, slot, _slotVar]);
                output.Add([SpirvModule.Header(SpirvModule.OpAccessChain, 7), pointerUint, pointer, _materialsVar, module.ConstantUint(0), slot, module.ConstantUint((uint)_materialOrder[index])]);
            }
            else
            {
                output.Add([SpirvModule.Header(SpirvModule.OpAccessChain, 5), pointerUint, pointer, _globalsVar, module.ConstantUint((uint)_globalOrder[index])]);
            }

            uint value = module.NewId();
            output.Add([SpirvModule.Header(SpirvModule.OpLoad, 4), uint32, value, pointer]);
            return value;
        }

        private void RemoveVariables()
        {
            HashSet<uint> removed = new(_textureVars.Keys);
            if (_globalBlock != 0)
                removed.Add(_globalBlock);
            module.RemoveVariables(removed);
        }

        private void MoveUserResources()
        {
            foreach ((uint Set, int Binding) resource in plan.UserFrameResources)
                module.MoveBinding(resource.Set, resource.Binding, GraphiteBindings.UserSet);
        }
    }
}

/// <summary>A SPIR-V module as a list of instructions, with what the material rewrite needs to read and add to it.</summary>
internal sealed class SpirvModule
{
    public const ushort OpName = 5;
    public const ushort OpMemberName = 6;
    public const ushort OpExtension = 10;
    public const ushort OpEntryPoint = 15;
    public const ushort OpCapability = 17;
    public const ushort OpTypeInt = 21;
    public const ushort OpTypeFloat = 22;
    public const ushort OpTypeVector = 23;
    public const ushort OpTypeMatrix = 24;
    public const ushort OpTypeImage = 25;
    public const ushort OpTypeSampler = 26;
    public const ushort OpTypeSampledImage = 27;
    public const ushort OpTypeArray = 28;
    public const ushort OpTypeRuntimeArray = 29;
    public const ushort OpTypeStruct = 30;
    public const ushort OpTypePointer = 32;
    public const ushort OpConstant = 43;
    public const ushort OpFunction = 54;
    public const ushort OpFunctionCall = 57;
    public const ushort OpVariable = 59;
    public const ushort OpLoad = 61;
    public const ushort OpAccessChain = 65;
    public const ushort OpInBoundsAccessChain = 66;
    public const ushort OpDecorate = 71;
    public const ushort OpMemberDecorate = 72;
    public const ushort OpSampledImage = 86;
    public const ushort OpImage = 100;
    public const ushort OpStore = 62;
    public const ushort OpCopyMemory = 63;
    public const ushort OpCopyObject = 83;
    public const ushort OpSelect = 169;
    public const ushort OpPhi = 245;

    public const uint DecorationBlock = 2;
    public const uint DecorationColMajor = 5;
    public const uint DecorationArrayStride = 6;
    public const uint DecorationMatrixStride = 7;
    public const uint DecorationFlat = 14;
    public const uint DecorationNonWritable = 24;
    public const uint DecorationLocation = 30;
    public const uint DecorationBinding = 33;
    public const uint DecorationDescriptorSet = 34;
    public const uint DecorationOffset = 35;
    public const uint DecorationNonUniform = 5300;

    public const uint UniformConstant = 0;
    public const uint Input = 1;
    public const uint Output = 3;
    public const uint StorageBuffer = 12;

    public const uint CapabilityShaderNonUniform = 5301;
    public const uint CapabilityRuntimeDescriptorArray = 5302;
    public const uint CapabilitySampledImageArrayNonUniformIndexing = 5307;

    private readonly uint[] _header;
    private readonly List<uint[]> _preamble = new();
    private readonly List<uint[]> _annotations = new();
    private readonly List<uint[]> _globals = new();
    private List<uint[]> _functions = new();
    private readonly Dictionary<uint, uint[]> _definitions = new();

    public SpirvModule(byte[] bytes)
    {
        ReadOnlySpan<uint> words = MemoryMarshal.Cast<byte, uint>(bytes);
        if (words.Length < 5 || words[0] != 0x07230203)
            throw new ArgumentException("Not a SPIR-V module.");

        _header = words[..5].ToArray();
        int at = 5;
        while (at < words.Length)
        {
            int count = (int)(words[at] >> 16);
            if (count == 0 || at + count > words.Length)
                throw new ArgumentException("Malformed SPIR-V module.");

            uint[] instruction = words.Slice(at, count).ToArray();
            at += count;
            ushort op = Op(instruction);
            if (_functions.Count > 0 || op == OpFunction)
                _functions.Add(instruction);
            else if (op is OpDecorate or OpMemberDecorate or 73 or 74 or 75 or 332 or 5632 or 5633)
                _annotations.Add(instruction);
            else if (_annotations.Count > 0 || IsTypeOrGlobal(op))
                AddDefinition(_globals, instruction);
            else
                _preamble.Add(instruction);
        }
    }

    public uint Bound
    {
        get => _header[3];
        private set => _header[3] = value;
    }

    public static ushort Op(uint[] instruction) => (ushort)(instruction[0] & 0xFFFF);

    public static uint Header(ushort op, int words) => ((uint)words << 16) | op;

    public uint NewId() => Bound++;

    public IEnumerable<uint[]> Annotations() => _annotations;

    public IEnumerable<uint[]> Globals() => _globals;

    public IReadOnlyList<uint[]> Functions() => _functions;

    public void ReplaceFunctions(List<uint[]> functions) => _functions = functions;

    public byte[] ToBytes()
    {
        List<uint> words = new(_header);
        foreach (List<uint[]> section in (List<uint[]>[])[_preamble, _annotations, _globals, _functions])
        {
            foreach (uint[] instruction in section)
                words.AddRange(instruction);
        }
        return MemoryMarshal.AsBytes(CollectionsMarshal.AsSpan(words)).ToArray();
    }

    public uint[] Type(uint id) => _definitions.TryGetValue(id, out uint[]? type) ? type : throw new ArgumentException($"SPIR-V id {id} is not a type.");

    public uint Pointee(uint pointerType) => Type(pointerType)[3];

    public uint ConstantValue(uint id)
    {
        uint[] constant = Type(id);
        if (Op(constant) != OpConstant)
            throw new ShaderLoadException("A loose uniform is indexed with a value that is not a constant.");
        return constant[3];
    }

    public bool TryGetBinding(uint id, out uint set, out int binding)
    {
        set = 0;
        binding = -1;
        bool hasSet = false;
        foreach (uint[] i in _annotations)
        {
            if (Op(i) != OpDecorate || i[1] != id)
                continue;
            if (i[2] == DecorationDescriptorSet)
            {
                set = i[3];
                hasSet = true;
            }
            else if (i[2] == DecorationBinding)
            {
                binding = (int)i[3];
            }
        }
        return hasSet && binding >= 0;
    }

    public MaterialPropertyType? PropertyType(uint type)
    {
        uint[] t = Type(type);
        switch (Op(t))
        {
            case OpTypeFloat when t[2] == 32:
                return MaterialPropertyType.Float;
            case OpTypeInt when t[2] == 32:
                return MaterialPropertyType.Int;
            case OpTypeVector when Op(Type(t[2])) == OpTypeFloat && Type(t[2])[2] == 32:
                return t[3] switch { 2 => MaterialPropertyType.Float2, 3 => MaterialPropertyType.Float3, 4 => MaterialPropertyType.Float4, _ => null };
            case OpTypeMatrix when t[3] == 4 && PropertyType(t[2]) == MaterialPropertyType.Float4:
                return MaterialPropertyType.Matrix;
            default:
                return null;
        }
    }

    public MaterialPropertyType? ImagePropertyType(uint imageType)
    {
        uint[] t = Type(imageType);
        if (Op(t) != OpTypeImage || t[6] != 0 || t[7] == 2)
            return null;
        bool arrayed = t[5] != 0;
        return t[3] switch
        {
            1 => arrayed ? MaterialPropertyType.Texture2DArray : MaterialPropertyType.Texture2D,
            2 when !arrayed => MaterialPropertyType.Texture3D,
            3 => arrayed ? MaterialPropertyType.TextureCubeArray : MaterialPropertyType.TextureCube,
            _ => null,
        };
    }

    /// <summary>A variable at a set and binding holding an array, sized or not, of the given element type, or 0.</summary>
    public uint FindTable(uint set, uint binding, uint element)
    {
        foreach (uint[] i in _globals)
        {
            if (Op(i) != OpVariable || i[3] != UniformConstant || !TryGetBinding(i[2], out uint s, out int b) || s != set || b != binding)
                continue;
            uint[] array = Type(Pointee(i[1]));
            if (Op(array) is OpTypeRuntimeArray or OpTypeArray && array[2] == element)
                return i[2];
        }
        return 0;
    }

    /// <summary>True when a function stores to the variable, directly or through an access chain into it.</summary>
    public bool Stores(uint variable)
    {
        HashSet<uint> pointers = [variable];
        foreach (uint[] i in _functions)
        {
            ushort op = Op(i);
            if (op is OpAccessChain or OpInBoundsAccessChain && pointers.Contains(i[3]))
                pointers.Add(i[2]);
            else if (op == OpStore && pointers.Contains(i[1]))
                return true;
        }
        return false;
    }

    public uint FindVariableAtLocation(uint storage, uint location)
    {
        foreach (uint[] i in _globals)
        {
            if (Op(i) != OpVariable || i[3] != storage)
                continue;
            foreach (uint[] d in _annotations)
            {
                if (Op(d) == OpDecorate && d[1] == i[2] && d[2] == DecorationLocation && d[3] == location)
                    return i[2];
            }
        }
        return 0;
    }

    public uint TypeUint() => FindOrAdd(t => Op(t) == OpTypeInt && t[2] == 32 && t[3] == 0, id => [Header(OpTypeInt, 4), id, 32, 0]);

    public uint TypeSampler() => FindOrAdd(t => Op(t) == OpTypeSampler, id => [Header(OpTypeSampler, 2), id]);

    public uint Pointer(uint storage, uint pointee)
        => FindOrAdd(t => Op(t) == OpTypePointer && t[2] == storage && t[3] == pointee, id => [Header(OpTypePointer, 4), id, storage, pointee]);

    public uint ConstantUint(uint value)
    {
        uint type = TypeUint();
        return FindOrAdd(t => Op(t) == OpConstant && t[1] == type && t[3] == value, id => [Header(OpConstant, 4), type, id, value]);
    }

    /// <summary>Adds a type whose operands follow its result id, like a runtime array.</summary>
    public uint AddGlobal(ushort op, params uint[] operands)
    {
        uint id = NewId();
        AddDefinition(_globals, [Header(op, operands.Length + 2), id, .. operands]);
        return id;
    }

    public uint AddStruct(uint[] members) => AddGlobal(OpTypeStruct, members);

    public uint AddVariable(uint pointerType, uint storage)
    {
        uint id = NewId();
        AddDefinition(_globals, [Header(OpVariable, 4), pointerType, id, storage]);
        return id;
    }

    public void Decorate(uint id, uint decoration, params uint[] operands)
        => _annotations.Add([Header(OpDecorate, operands.Length + 3), id, decoration, .. operands]);

    public void MemberDecorate(uint structType, uint member, uint decoration, params uint[] operands)
        => _annotations.Add([Header(OpMemberDecorate, operands.Length + 4), structType, member, decoration, .. operands]);

    public void AddCapability(uint capability)
    {
        foreach (uint[] i in _preamble)
        {
            if (Op(i) == OpCapability && i[1] == capability)
                return;
        }
        _preamble.Insert(0, [Header(OpCapability, 2), capability]);
    }

    /// <summary>Adds a global variable to every entry point's interface, which SPIR-V 1.4 and later require for all of them.</summary>
    public void AddToInterface(uint variable)
    {
        if (_header[1] < 0x00010400 && !IsInputOrOutput(variable))
            return;

        for (int k = 0; k < _preamble.Count; k++)
        {
            uint[] i = _preamble[k];
            if (Op(i) != OpEntryPoint || Array.IndexOf(i, variable, InterfaceStart(i)) >= 0)
                continue;
            uint[] grown = [.. i, variable];
            grown[0] = Header(OpEntryPoint, grown.Length);
            _preamble[k] = grown;
        }
    }

    /// <summary>Removes variables with their names, decorations and interface entries.</summary>
    public void RemoveVariables(HashSet<uint> variables)
    {
        if (variables.Count == 0)
            return;

        _globals.RemoveAll(i => Op(i) == OpVariable && variables.Contains(i[2]));
        _annotations.RemoveAll(i => Op(i) is OpDecorate or 332 or 5632 && variables.Contains(i[1]));
        _preamble.RemoveAll(i => Op(i) == OpName && variables.Contains(i[1]));
        for (int k = 0; k < _preamble.Count; k++)
        {
            uint[] i = _preamble[k];
            if (Op(i) != OpEntryPoint)
                continue;
            int start = InterfaceStart(i);
            List<uint> kept = new(i[..start]);
            for (int w = start; w < i.Length; w++)
            {
                if (!variables.Contains(i[w]))
                    kept.Add(i[w]);
            }
            kept[0] = Header(OpEntryPoint, kept.Count);
            _preamble[k] = [.. kept];
        }
    }

    public void MoveBinding(uint set, int binding, uint newSet)
    {
        List<uint> ids = new();
        foreach (uint[] i in _globals)
        {
            if (Op(i) == OpVariable && TryGetBinding(i[2], out uint s, out int b) && s == set && b == binding)
                ids.Add(i[2]);
        }

        foreach (uint[] i in _annotations)
        {
            if (Op(i) == OpDecorate && ids.Contains(i[1]) && i[2] == DecorationDescriptorSet)
                i[3] = newSet;
        }
    }

    private bool IsInputOrOutput(uint variable)
        => _definitions.TryGetValue(variable, out uint[]? v) && Op(v) == OpVariable && v[3] is Input or Output;

    private static int InterfaceStart(uint[] entryPoint)
    {
        int w = 3;
        while (w < entryPoint.Length)
        {
            uint word = entryPoint[w++];
            if ((word & 0xFF000000) == 0 || (word & 0x00FF0000) == 0 || (word & 0x0000FF00) == 0 || (word & 0x000000FF) == 0)
                break;
        }
        return w;
    }

    private uint FindOrAdd(Func<uint[], bool> match, Func<uint, uint[]> create)
    {
        foreach (uint[] i in _globals)
        {
            if (match(i))
                return ResultId(i);
        }

        uint id = NewId();
        AddDefinition(_globals, create(id));
        return id;
    }

    private void AddDefinition(List<uint[]> section, uint[] instruction)
    {
        section.Add(instruction);
        uint id = ResultId(instruction);
        if (id != 0)
            _definitions[id] = instruction;
    }

    private static uint ResultId(uint[] instruction)
    {
        ushort op = Op(instruction);
        if (op is >= 19 and <= 39 && instruction.Length > 1)
            return instruction[1];
        if (op is OpConstant or OpVariable or 41 or 42 or 44 or 45 or 46 or 48 or 49 or 50 or 51 or 52 && instruction.Length > 2)
            return instruction[2];
        return 0;
    }

    private static bool IsTypeOrGlobal(ushort op) => op is >= 19 and <= 39 or >= 41 and <= 52 or OpVariable or 322 or 4456;
}
