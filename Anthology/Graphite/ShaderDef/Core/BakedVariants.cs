using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// Compiled variants shipped with a build, for players without a compiler. A pass is found by its source name and a hash
/// of its source and material ABI, so a pass made anywhere from the same source gets them, and an edited one gets none.
/// </summary>
public static class BakedVariants
{
    public sealed class Pass
    {
        public required VariantSpace[] Axes;
        public required ShaderGlobal[] Globals;
        public List<Variant> Variants = new();

        /// <summary>Compute variants by backend and variant key.</summary>
        public Dictionary<(GraphicsBackend Backend, int Key), ComputeDescription> Compute = new();
    }

    private const uint Magic = 0x56424750; // PGBV
    private const int Version = 2;

    private static readonly ConcurrentDictionary<(string Name, ulong Hash), Pass> s_passes = new();

    public static int Count => s_passes.Count;

    public static void Add(string sourceName, string source, Pass pass) => s_passes[(sourceName, Hash(source))] = pass;

    public static Pass? Find(ShaderPass pass) => s_passes.TryGetValue((pass.SourceName, Hash(pass.Fingerprint)), out Pass? baked) ? baked : null;

    public static void Clear() => s_passes.Clear();

    /// <summary>FNV-1a over the source's characters, the same in every run.</summary>
    public static ulong Hash(string source)
    {
        ulong hash = 14695981039346656037ul;
        foreach (char c in source)
        {
            hash = (hash ^ c) * 1099511628211ul;
        }
        return hash;
    }

    /// <summary>Takes in the compiled variants of a pass at the given keys.</summary>
    public static void Capture(PassVariants variants, IEnumerable<int> keys)
    {
        Pass baked = new() { Axes = [.. variants.Axes], Globals = [.. variants.Globals] };
        foreach (int key in keys)
        {
            if (variants.Compiled(key) is { } variant)
                baked.Variants.Add(variant);
        }
        if (baked.Variants.Count > 0)
            Add(variants.Pass.SourceName, variants.Pass.Fingerprint, baked);
    }

    public static void Write(Stream stream)
    {
        using BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        KeyValuePair<(string Name, ulong Hash), Pass>[] passes = s_passes.ToArray();
        writer.Write(passes.Length);
        foreach (((string name, ulong hash), Pass pass) in passes)
        {
            writer.Write(name);
            writer.Write(hash);
            writer.Write(pass.Axes.Length);
            foreach (VariantSpace axis in pass.Axes)
            {
                writer.Write(axis.Name);
                writer.Write((int)axis.Scope);
                writer.Write(axis.IsToggle);
                writer.Write(axis.Values.Count);
                foreach (string value in axis.Values)
                    writer.Write(value);
            }
            writer.Write(pass.Globals.Length);
            foreach (ShaderGlobal global in pass.Globals)
            {
                writer.Write(global.Type);
                writer.Write(global.Name);
            }
            writer.Write(pass.Variants.Count);
            foreach (Variant variant in pass.Variants)
            {
                writer.Write(variant.Keywords.Length);
                foreach (Keyword keyword in variant.Keywords)
                {
                    writer.Write(keyword.Name);
                    writer.Write(keyword.Value);
                }
                writer.Write(variant.Compiled.Length);
                foreach ((GraphicsBackend backend, ShaderDescription description) in variant.Compiled)
                {
                    writer.Write((int)backend);
                    WriteDescription(writer, description);
                }
            }
            writer.Write(pass.Compute.Count);
            foreach (((GraphicsBackend backend, int key), ComputeDescription compute) in pass.Compute)
            {
                writer.Write((int)backend);
                writer.Write(key);
                WriteStage(writer, compute.Stage);
                WriteLayouts(writer, compute.ResourceLayouts);
                writer.Write(compute.ThreadGroupSizeX);
                writer.Write(compute.ThreadGroupSizeY);
                writer.Write(compute.ThreadGroupSizeZ);
                writer.Write((byte)compute.PushConstants.Stages);
                writer.Write(compute.PushConstants.SizeInBytes);
            }
        }
    }

    public static void Read(Stream stream)
    {
        using BinaryReader reader = new(stream, Encoding.UTF8, leaveOpen: true);
        if (reader.ReadUInt32() != Magic)
            throw new InvalidDataException("Not a baked shader variant file.");
        int version = reader.ReadInt32();
        if (version != Version)
            throw new InvalidDataException($"Baked shader variants of version {version}, this build reads {Version}.");
        int passes = reader.ReadInt32();
        for (int p = 0; p < passes; p++)
        {
            string name = reader.ReadString();
            ulong hash = reader.ReadUInt64();
            VariantSpace[] axes = new VariantSpace[reader.ReadInt32()];
            for (int a = 0; a < axes.Length; a++)
            {
                string axis = reader.ReadString();
                VariantScope scope = (VariantScope)reader.ReadInt32();
                bool toggle = reader.ReadBoolean();
                string[] values = new string[reader.ReadInt32()];
                for (int v = 0; v < values.Length; v++)
                    values[v] = reader.ReadString();
                axes[a] = toggle ? VariantSpace.Toggle(axis, scope) : new VariantSpace(axis, values, scope);
            }
            ShaderGlobal[] globals = new ShaderGlobal[reader.ReadInt32()];
            for (int g = 0; g < globals.Length; g++)
            {
                string type = reader.ReadString();
                globals[g] = new ShaderGlobal(reader.ReadString(), type);
            }
            Pass pass = new() { Axes = axes, Globals = globals };
            int variants = reader.ReadInt32();
            for (int v = 0; v < variants; v++)
            {
                Keyword[] keywords = new Keyword[reader.ReadInt32()];
                for (int k = 0; k < keywords.Length; k++)
                    keywords[k] = new Keyword(reader.ReadString(), reader.ReadString());
                (GraphicsBackend, ShaderDescription)[] compiled = new (GraphicsBackend, ShaderDescription)[reader.ReadInt32()];
                for (int c = 0; c < compiled.Length; c++)
                    compiled[c] = ((GraphicsBackend)reader.ReadInt32(), ReadDescription(reader));
                pass.Variants.Add(new Variant(keywords, compiled));
            }
            int computes = reader.ReadInt32();
            for (int c = 0; c < computes; c++)
            {
                GraphicsBackend backend = (GraphicsBackend)reader.ReadInt32();
                int key = reader.ReadInt32();
                ShaderStageDescription stage = ReadStage(reader);
                ResourceLayoutDescription[] layouts = ReadLayouts(reader);
                ComputeDescription compute = new(stage, layouts, reader.ReadUInt32(), reader.ReadUInt32(), reader.ReadUInt32())
                {
                    PushConstants = new PushConstantRange((ShaderStages)reader.ReadByte(), reader.ReadUInt32()),
                };
                pass.Compute[(backend, key)] = compute;
            }
            s_passes[(name, hash)] = pass;
        }
    }

    // Render state is left out, every user of a description sets its own
    private static void WriteDescription(BinaryWriter writer, ShaderDescription description)
    {
        ShaderStageDescription[] stages = description.Stages ?? [];
        writer.Write(stages.Length);
        foreach (ShaderStageDescription stage in stages)
            WriteStage(writer, stage);
        VertexLayoutDescription[] vertex = description.VertexLayouts ?? [];
        writer.Write(vertex.Length);
        foreach (VertexLayoutDescription layout in vertex)
        {
            writer.Write(layout.Location);
            writer.Write(layout.Stride);
            writer.Write((int)layout.StepRate);
            VertexElementDescription[] elements = layout.Elements ?? [];
            writer.Write(elements.Length);
            foreach (VertexElementDescription element in elements)
            {
                writer.Write(VertexAttributeID.ToString(element.Name) ?? "");
                writer.Write((byte)element.Format);
                writer.Write(element.Offset);
            }
        }
        WriteLayouts(writer, description.ResourceLayouts);
        writer.Write((byte)description.PushConstants.Stages);
        writer.Write(description.PushConstants.SizeInBytes);
    }

    private static ShaderDescription ReadDescription(BinaryReader reader)
    {
        ShaderStageDescription[] stages = new ShaderStageDescription[reader.ReadInt32()];
        for (int s = 0; s < stages.Length; s++)
            stages[s] = ReadStage(reader);
        VertexLayoutDescription[] vertex = new VertexLayoutDescription[reader.ReadInt32()];
        for (int v = 0; v < vertex.Length; v++)
        {
            uint location = reader.ReadUInt32();
            uint stride = reader.ReadUInt32();
            VertexStepRate rate = (VertexStepRate)reader.ReadInt32();
            VertexElementDescription[] elements = new VertexElementDescription[reader.ReadInt32()];
            for (int e = 0; e < elements.Length; e++)
                elements[e] = new VertexElementDescription(reader.ReadString(), (VertexElementFormat)reader.ReadByte(), reader.ReadUInt32());
            vertex[v] = new VertexLayoutDescription(location, stride, rate, elements);
        }
        ShaderDescription description = new(stages)
        {
            VertexLayouts = vertex,
            ResourceLayouts = ReadLayouts(reader),
        };
        description.PushConstants = new PushConstantRange((ShaderStages)reader.ReadByte(), reader.ReadUInt32());
        return description;
    }

    private static void WriteStage(BinaryWriter writer, ShaderStageDescription stage)
    {
        writer.Write((byte)stage.Stage);
        writer.Write(stage.ShaderBytes.Length);
        writer.Write(stage.ShaderBytes);
        writer.Write(stage.EntryPoint ?? "main");
    }

    private static ShaderStageDescription ReadStage(BinaryReader reader)
    {
        ShaderStages stage = (ShaderStages)reader.ReadByte();
        byte[] bytes = reader.ReadBytes(reader.ReadInt32());
        return new ShaderStageDescription(stage, bytes, reader.ReadString());
    }

    private static void WriteLayouts(BinaryWriter writer, ResourceLayoutDescription[]? layouts)
    {
        layouts ??= [];
        writer.Write(layouts.Length);
        foreach (ResourceLayoutDescription layout in layouts)
        {
            writer.Write(layout.Set);
            writer.Write(layout.Table);
            ResourceLayoutElementDescription[] elements = layout.Elements ?? [];
            writer.Write(elements.Length);
            foreach (ResourceLayoutElementDescription element in elements)
            {
                writer.Write(PropertyID.ToString(element.Name) ?? "");
                writer.Write((byte)element.Kind);
                writer.Write((byte)element.Stages);
                writer.Write(element.BindingIndex);
                writer.Write((int)element.Options);
                writer.Write(element.ArrayCount);
                UniformBlockField[]? fields = element.UniformFields;
                writer.Write(fields?.Length ?? -1);
                foreach (UniformBlockField field in fields ?? [])
                {
                    writer.Write(PropertyID.ToString(field.Name) ?? "");
                    writer.Write(field.Offset);
                    writer.Write(field.Size);
                    writer.Write((byte)field.Type);
                    writer.Write(field.ArrayLength);
                    writer.Write(field.ArrayStride);
                }
            }
        }
    }

    private static ResourceLayoutDescription[] ReadLayouts(BinaryReader reader)
    {
        ResourceLayoutDescription[] layouts = new ResourceLayoutDescription[reader.ReadInt32()];
        for (int l = 0; l < layouts.Length; l++)
        {
            uint set = reader.ReadUInt32();
            bool table = reader.ReadBoolean();
            ResourceLayoutElementDescription[] elements = new ResourceLayoutElementDescription[reader.ReadInt32()];
            for (int e = 0; e < elements.Length; e++)
            {
                ResourceLayoutElementDescription element = default;
                element.Name = PropertyID.Intern(reader.ReadString());
                element.Kind = (ResourceKind)reader.ReadByte();
                element.Stages = (ShaderStages)reader.ReadByte();
                element.BindingIndex = reader.ReadInt32();
                element.Options = (ResourceLayoutElementOptions)reader.ReadInt32();
                element.ArrayCount = reader.ReadUInt32();
                int fieldCount = reader.ReadInt32();
                if (fieldCount >= 0)
                {
                    UniformBlockField[] fields = new UniformBlockField[fieldCount];
                    for (int f = 0; f < fields.Length; f++)
                    {
                        PropertyID name = PropertyID.Intern(reader.ReadString());
                        uint offset = reader.ReadUInt32();
                        uint size = reader.ReadUInt32();
                        UniformScalarType type = (UniformScalarType)reader.ReadByte();
                        fields[f] = new UniformBlockField(name, offset, type, reader.ReadUInt32(), reader.ReadUInt32()) { Size = size };
                    }
                    element.UniformFields = fields;
                }
                elements[e] = element;
            }
            layouts[l] = new ResourceLayoutDescription(set, elements) { Table = table };
        }
        return layouts;
    }
}
