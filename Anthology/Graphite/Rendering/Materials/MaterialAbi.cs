using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

using Prowl.Graphite.ShaderDef;

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
    public const int ShadowTexture2DTable = 5;


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
        new ResourceLayoutElementDescription("Graphite_TexturesCubeArray", ResourceKind.TextureReadOnly, Graphics, TextureCubeArrayTable) { ArrayCount = otherTextures },
        new ResourceLayoutElementDescription("Graphite_ShadowTextures2D", ResourceKind.TextureReadOnly, Graphics, ShadowTexture2DTable) { ArrayCount = textures2D })
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
/// The material ABI: how a material pass reads its properties, its textures and the renderer's globals, as GLSL the
/// compiler puts in place of the pass's loose uniforms, and the resource layouts its programs bind with.
/// </summary>
internal static class MaterialLayout
{
    private const string Record = "graphite_MaterialRecords[graphite_MaterialSlot]";

    /// <summary>The ABI of a shader's material record.</summary>
    public static MaterialAbi Abi(MaterialSchema schema)
    {
        StringBuilder record = new("struct Graphite_MaterialRecord\n{\n");
        Dictionary<string, (string Type, string Read, string With)> properties = new(StringComparer.Ordinal);
        uint offset = 0;
        int padding = 0;
        void Member(string type, string name, uint at, uint size)
        {
            for (; offset < at; offset += 4)
                record.Append("    uint pad").Append(padding++).Append(";\n");
            record.Append("    ").Append(type).Append(' ').Append(name).Append(";\n");
            offset = at + size;
        }

        foreach (MaterialProperty property in schema.Properties)
        {
            string name = property.Name.Text();
            string member = "m" + name;
            if (property.Type >= MaterialPropertyType.Texture2D)
            {
                Member("uint", member, property.Offset, 4);
                Member("uint", member + "_Sampler", property.Offset + 4, 4);
                Member("vec4", member + "_ST", property.ScaleOffsetOffset, 16);
                (string sampler, string table) = Texture(property.Type);
                string image = $"{table}[nonuniformEXT({Record}.{member})]";
                string samplerSlot = $"Graphite_Samplers[nonuniformEXT({Record}.{member}_Sampler)]";
                properties[name] = (sampler, $"{sampler}({image}, {samplerSlot})", $"#define {name}_Texture {image}\n#define {name}_Sampler {samplerSlot}\n");
                properties.TryAdd(name + "_ST", ("vec4", $"({Record}.{member}_ST)", ""));
            }
            else
            {
                string type = Number(property.Type);
                Member(type, member, property.Offset, MaterialSchema.Std430(property.Type).Size);
                properties[name] = (type, $"({Record}.{member})", "");
            }
        }
        for (; offset < schema.StrideWords * 4; offset += 4)
            record.Append("    uint pad").Append(padding++).Append(";\n");
        record.Append("};");

        List<(string, string)> declarations =
        [
            ("GL_EXT_nonuniform_qualifier", "#extension GL_EXT_nonuniform_qualifier : require"),
            ("Graphite_MaterialRecord", record.ToString()),
            (GraphiteBindings.MaterialsName, $"layout(set = {GraphiteBindings.PassSet}, binding = {GraphiteBindings.Materials}, std430) readonly buffer {GraphiteBindings.MaterialsName} {{ Graphite_MaterialRecord graphite_MaterialRecords[]; }};"),
            ("Graphite_Textures2D", Table("texture2D", "Graphite_Textures2D", GraphiteBindings.Texture2DTable, RendererTextures.Textures2D)),
            ("Graphite_Textures2DArray", Table("texture2DArray", "Graphite_Textures2DArray", GraphiteBindings.Texture2DArrayTable, RendererTextures.OtherTextures)),
            ("Graphite_Textures3D", Table("texture3D", "Graphite_Textures3D", GraphiteBindings.Texture3DTable, RendererTextures.OtherTextures)),
            ("Graphite_TexturesCube", Table("textureCube", "Graphite_TexturesCube", GraphiteBindings.TextureCubeTable, RendererTextures.OtherTextures)),
            ("Graphite_TexturesCubeArray", Table("textureCubeArray", "Graphite_TexturesCubeArray", GraphiteBindings.TextureCubeArrayTable, RendererTextures.OtherTextures)),
            ("Graphite_ShadowTextures2D", Table("texture2D", "Graphite_ShadowTextures2D", GraphiteBindings.ShadowTexture2DTable, RendererTextures.Textures2D)),
            ("Graphite_Samplers", $"layout(set = {GraphiteBindings.ViewSet}, binding = {GraphiteBindings.SamplerTable}) uniform sampler Graphite_Samplers[{RendererTextures.Samplers}];"),
            ("Graphite_Globals", $"layout(set = {GraphiteBindings.ViewSet}, binding = {GraphiteBindings.Globals}, std430) readonly buffer Graphite_Globals {{ uint graphite_GlobalWords[]; }};"),
            ("Graphite_Global_float", "#define Graphite_Global_float(o) uintBitsToFloat(graphite_GlobalWords[o])"),
            ("Graphite_Global_int", "#define Graphite_Global_int(o) int(graphite_GlobalWords[o])"),
            ("Graphite_Global_vec2", "#define Graphite_Global_vec2(o) uintBitsToFloat(uvec2(graphite_GlobalWords[o], graphite_GlobalWords[(o) + 1u]))"),
            ("Graphite_Global_vec3", "#define Graphite_Global_vec3(o) uintBitsToFloat(uvec3(graphite_GlobalWords[o], graphite_GlobalWords[(o) + 1u], graphite_GlobalWords[(o) + 2u]))"),
            ("Graphite_Global_vec4", "#define Graphite_Global_vec4(o) uintBitsToFloat(uvec4(graphite_GlobalWords[o], graphite_GlobalWords[(o) + 1u], graphite_GlobalWords[(o) + 2u], graphite_GlobalWords[(o) + 3u]))"),
            ("Graphite_Global_ivec2", "#define Graphite_Global_ivec2(o) ivec2(graphite_GlobalWords[o], graphite_GlobalWords[(o) + 1u])"),
            ("Graphite_Global_ivec3", "#define Graphite_Global_ivec3(o) ivec3(graphite_GlobalWords[o], graphite_GlobalWords[(o) + 1u], graphite_GlobalWords[(o) + 2u])"),
            ("Graphite_Global_ivec4", "#define Graphite_Global_ivec4(o) ivec4(graphite_GlobalWords[o], graphite_GlobalWords[(o) + 1u], graphite_GlobalWords[(o) + 2u], graphite_GlobalWords[(o) + 3u])"),
            ("Graphite_Global_mat4", "#define Graphite_Global_mat4(o) mat4(Graphite_Global_vec4(o), Graphite_Global_vec4((o) + 4u), Graphite_Global_vec4((o) + 8u), Graphite_Global_vec4((o) + 12u))"),
        ];
        foreach (MaterialPropertyType type in (MaterialPropertyType[])[MaterialPropertyType.Texture2D, MaterialPropertyType.Texture2DArray, MaterialPropertyType.Texture3D, MaterialPropertyType.TextureCube, MaterialPropertyType.TextureCubeArray])
        {
            (string sampler, string table) = Texture(type);
            declarations.Add(($"Graphite_Global_{sampler}", $"#define Graphite_Global_{sampler}(o) {sampler}({table}[graphite_GlobalWords[o]], Graphite_Samplers[graphite_GlobalWords[(o) + 1u]])"));
        }

        return new MaterialAbi(declarations, ("graphite_MaterialSlot", $"layout(location = {GraphiteBindings.MaterialSlotLocation}) flat in uint graphite_MaterialSlot;"), properties, GraphiteBindings.UserSet);
    }

    /// <summary>Declares a pass's globals, giving each one's word offset as the specialization constant its reads go through.</summary>
    public static SpecializationConstant[] GlobalOffsets(IReadOnlyList<ShaderGlobal> passGlobals, GlobalRegistry registry)
    {
        SpecializationConstant[] offsets = new SpecializationConstant[passGlobals.Count];
        for (int i = 0; i < offsets.Length; i++)
        {
            if (!registry.Declare(passGlobals[i].Name, GlobalType(passGlobals[i].Type), out GlobalProperty global, out string? error))
                throw new ShaderLoadException(error!);
            offsets[i] = new SpecializationConstant((uint)i, global.Offset);
        }
        return offsets;
    }

    /// <summary>The property type a global pragma's GLSL type is stored as.</summary>
    public static MaterialPropertyType GlobalType(string type) => type switch
    {
        "float" => MaterialPropertyType.Float,
        "int" => MaterialPropertyType.Int,
        "vec2" => MaterialPropertyType.Float2,
        "vec3" => MaterialPropertyType.Float3,
        "vec4" => MaterialPropertyType.Float4,
        "ivec2" => MaterialPropertyType.Int2,
        "ivec3" => MaterialPropertyType.Int3,
        "ivec4" => MaterialPropertyType.Int4,
        "mat4" => MaterialPropertyType.Matrix,
        "sampler2D" => MaterialPropertyType.Texture2D,
        "sampler2DArray" => MaterialPropertyType.Texture2DArray,
        "sampler3D" => MaterialPropertyType.Texture3D,
        "samplerCube" => MaterialPropertyType.TextureCube,
        "samplerCubeArray" => MaterialPropertyType.TextureCubeArray,
        _ => throw new ShaderLoadException($"{type} is not a global type."),
    };

    /// <summary>
    /// A material program's resource layouts as the renderer binds them: its whole frame and view tables, the pass set,
    /// and the pass's own buffers in the user set. Throws for resources a material pass cannot have.
    /// </summary>
    public static ResourceLayoutDescription[] Layouts(string shaderName, ResourceLayoutDescription[] reflected, ResourceLayoutDescription frameTable, ResourceLayoutDescription viewTable)
    {
        List<ResourceLayoutElementDescription> pass = new();
        List<ResourceLayoutElementDescription> user = new();
        foreach (ResourceLayoutDescription layout in reflected)
        {
            foreach (ResourceLayoutElementDescription element in layout.Elements)
            {
                string name = element.Name.Text();
                if (layout.Set is GraphiteBindings.FrameSet or GraphiteBindings.ViewSet)
                {
                    if (!InTable(layout.Set == GraphiteBindings.FrameSet ? frameTable : viewTable, element))
                        throw new ShaderLoadException($"{shaderName}: {name} uses set {layout.Set}, which belongs to the renderer's {(layout.Set == GraphiteBindings.FrameSet ? "textures" : "view data")}.");
                    continue;
                }
                if (layout.Set is not (GraphiteBindings.PassSet or GraphiteBindings.UserSet))
                    throw new ShaderLoadException($"{shaderName}: {name} uses set {layout.Set}, a material pass has sets {GraphiteBindings.FrameSet} to {GraphiteBindings.UserSet}.");

                List<ResourceLayoutElementDescription> target = layout.Set == GraphiteBindings.PassSet ? pass : user;
                if (element.ArrayCount > 1)
                    throw new ShaderLoadException($"{shaderName}: {name} is an array of resources, which only the renderer's own tables hold.");
                if (target == user && element.Kind is not (ResourceKind.StructuredBufferReadOnly or ResourceKind.StructuredBufferReadWrite))
                    throw new ShaderLoadException($"{shaderName}: {name} is a {element.Kind}, which a material pass only reads as a property or global. Buffers come from Globals.SetBuffer.");
                if (target.Exists(e => e.BindingIndex == element.BindingIndex))
                    throw new ShaderLoadException($"{shaderName}: {name} collides with another resource at binding {element.BindingIndex}.");
                target.Add(element);
            }
        }

        List<ResourceLayoutDescription> layouts = [frameTable, viewTable, new ResourceLayoutDescription(GraphiteBindings.PassSet, [.. pass])];
        if (user.Count > 0)
            layouts.Add(new ResourceLayoutDescription(GraphiteBindings.UserSet, [.. user]));
        return [.. layouts];
    }

    /// <summary>Whether the fragment stage stores to its output at a location, like a G-buffer target.</summary>
    public static bool Stores(in ShaderDescription description, uint location)
    {
        foreach (ShaderStageDescription stage in description.Stages)
        {
            if (stage.Stage == ShaderStages.Fragment)
                return Stores(stage.ShaderBytes, location);
        }
        return false;
    }

    // Whether the module stores to the output at a location, directly or through an access chain
    private static bool Stores(byte[] spirv, uint location)
    {
        const uint OpVariable = 59, OpStore = 62, OpAccessChain = 65, OpInBoundsAccessChain = 66, OpDecorate = 71, DecorationLocation = 30, Output = 3;
        ReadOnlySpan<uint> words = MemoryMarshal.Cast<byte, uint>(spirv);
        HashSet<uint> located = new();
        HashSet<uint> pointers = new();
        for (int at = 5; at < words.Length;)
        {
            int count = (int)(words[at] >> 16);
            if (count == 0 || at + count > words.Length)
                return false;
            ReadOnlySpan<uint> i = words.Slice(at, count);
            at += count;
            uint op = i[0] & 0xFFFF;
            if (op == OpDecorate && i.Length > 3 && i[2] == DecorationLocation && i[3] == location)
                located.Add(i[1]);
            else if (op == OpVariable && i[3] == Output && located.Contains(i[2]))
                pointers.Add(i[2]);
            else if (op is OpAccessChain or OpInBoundsAccessChain && pointers.Contains(i[3]))
                pointers.Add(i[2]);
            else if (op == OpStore && pointers.Contains(i[1]))
                return true;
        }
        return false;
    }

    private static bool InTable(ResourceLayoutDescription table, ResourceLayoutElementDescription element)
    {
        foreach (ResourceLayoutElementDescription candidate in table.Elements)
        {
            if (candidate.BindingIndex == element.BindingIndex && candidate.Name == element.Name)
                return true;
        }
        return false;
    }

    private static string Table(string type, string name, int binding, uint count)
        => $"layout(set = {GraphiteBindings.FrameSet}, binding = {binding}) uniform {type} {name}[{count}];";

    private static (string Sampler, string Table) Texture(MaterialPropertyType type) => type switch
    {
        MaterialPropertyType.Texture2D => ("sampler2D", "Graphite_Textures2D"),
        MaterialPropertyType.Texture2DArray => ("sampler2DArray", "Graphite_Textures2DArray"),
        MaterialPropertyType.Texture3D => ("sampler3D", "Graphite_Textures3D"),
        MaterialPropertyType.TextureCube => ("samplerCube", "Graphite_TexturesCube"),
        _ => ("samplerCubeArray", "Graphite_TexturesCubeArray"),
    };

    private static string Number(MaterialPropertyType type) => type switch
    {
        MaterialPropertyType.Float => "float",
        MaterialPropertyType.Int => "int",
        MaterialPropertyType.Float2 => "vec2",
        MaterialPropertyType.Float3 => "vec3",
        MaterialPropertyType.Matrix => "mat4",
        _ => "vec4",
    };
}
