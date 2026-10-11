using System;
using System.Collections.Generic;
using System.Text;


namespace Prowl.Graphite.ShaderDef.Compiler;


/// <summary>
/// Reads resources, vertex inputs and the thread group size out of SPIR-V. Names come from the front end
/// layout, keyed by the bindings and locations it assigned, because optimized SPIR-V carries none.
/// </summary>
internal static class SpirvReflector
{
    private const int OpEntryPoint = 15;
    private const int OpExecutionMode = 16;
    private const int OpTypeInt = 21;
    private const int OpTypeFloat = 22;
    private const int OpTypeVector = 23;
    private const int OpTypeMatrix = 24;
    private const int OpTypeImage = 25;
    private const int OpTypeSampler = 26;
    private const int OpTypeSampledImage = 27;
    private const int OpTypeArray = 28;
    private const int OpTypeRuntimeArray = 29;
    private const int OpTypeStruct = 30;
    private const int OpTypePointer = 32;
    private const int OpConstant = 43;
    private const int OpVariable = 59;
    private const int OpDecorate = 71;
    private const int OpMemberDecorate = 72;
    private const int OpTypeAccelerationStructure = 5341;

    private const uint DecorationBlock = 2;
    private const uint DecorationBufferBlock = 3;
    private const uint DecorationRowMajor = 4;
    private const uint DecorationArrayStride = 6;
    private const uint DecorationMatrixStride = 7;
    private const uint DecorationBuiltIn = 11;
    private const uint DecorationNonWritable = 24;
    private const uint DecorationLocation = 30;
    private const uint DecorationBinding = 33;
    private const uint DecorationDescriptorSet = 34;
    private const uint DecorationOffset = 35;

    private const uint StorageUniformConstant = 0;
    private const uint StorageInput = 1;
    private const uint StorageUniform = 2;
    private const uint StoragePushConstant = 9;
    private const uint StorageStorageBuffer = 12;

    private const uint ExecutionModeLocalSize = 17;
    private const uint ExecutionModeLocalSizeId = 38;

    private const uint DimBuffer = 5;
    private const uint DimSubpassData = 6;


    private sealed class Module
    {
        public uint Model;
        public uint[] Interface = [];
        public uint[] LocalSize = [1, 1, 1];
        public uint[]? LocalSizeIds;
        public readonly Dictionary<uint, (int Op, uint[] Operands)> Types = [];
        public readonly Dictionary<uint, uint> Constants = [];
        public readonly Dictionary<uint, (uint PointerType, uint Storage)> Variables = [];
        public readonly Dictionary<(uint Id, uint Decoration), uint> Decorations = [];
        public readonly Dictionary<(uint Id, uint Member, uint Decoration), uint> MemberDecorations = [];

        public bool Has(uint id, uint decoration) => Decorations.ContainsKey((id, decoration));

        public bool HasMember(uint id, uint member, uint decoration) => MemberDecorations.ContainsKey((id, member, decoration));

        public uint Get(uint id, uint decoration) => Decorations.TryGetValue((id, decoration), out uint value) ? value : 0;

        public uint GetMember(uint id, uint member, uint decoration) => MemberDecorations.TryGetValue((id, member, decoration), out uint value) ? value : 0;
    }


    public static (ShaderDescription Description, uint X, uint Y, uint Z) Reflect(IReadOnlyList<(GlslStage Stage, byte[] Spirv)> stages, GlslProgramLayout layout)
    {
        Dictionary<(uint Set, int Binding), ResourceLayoutElementDescription> elements = [];
        List<VertexLayoutDescription> vertexLayouts = [];
        ShaderStageDescription[] stageDescriptions = new ShaderStageDescription[stages.Count];
        PushConstantRange pushConstants = default;
        uint x = 1, y = 1, z = 1;

        for (int s = 0; s < stages.Count; s++)
        {
            (GlslStage stage, byte[] spirv) = stages[s];
            ShaderStages flag = stage switch
            {
                GlslStage.Vertex => ShaderStages.Vertex,
                GlslStage.Fragment => ShaderStages.Fragment,
                _ => ShaderStages.Compute,
            };

            stageDescriptions[s] = new ShaderStageDescription(flag, spirv, "main");
            Module module = Parse(spirv);

            foreach (uint id in module.Interface)
            {
                if (!module.Variables.TryGetValue(id, out var variable))
                    continue;

                uint pointee = module.Types[variable.PointerType].Operands[1];
                switch (variable.Storage)
                {
                    case StorageUniformConstant or StorageUniform or StorageStorageBuffer:
                        ReflectResource(module, layout, id, variable.Storage, pointee, flag, elements);
                        break;

                    case StoragePushConstant:
                        pushConstants = new PushConstantRange(pushConstants.Stages | flag, Math.Max(pushConstants.SizeInBytes, Align4(TypeSize(module, pointee, 0))));
                        break;

                    case StorageInput when stage == GlslStage.Vertex && !IsBuiltIn(module, id, pointee):
                        vertexLayouts.Add(ReflectVertexInput(module, layout, id, pointee));
                        break;
                }
            }

            if (stage == GlslStage.Compute)
            {
                uint[] size = module.LocalSize;
                if (module.LocalSizeIds is { } ids)
                    size = [module.Constants[ids[0]], module.Constants[ids[1]], module.Constants[ids[2]]];
                (x, y, z) = (size[0], size[1], size[2]);
            }
        }

        vertexLayouts.Sort((a, b) => a.Location.CompareTo(b.Location));

        SortedDictionary<uint, List<ResourceLayoutElementDescription>> bySet = [];
        foreach (((uint set, int binding), ResourceLayoutElementDescription element) in elements)
        {
            if (!bySet.TryGetValue(set, out List<ResourceLayoutElementDescription>? list))
                bySet[set] = list = [];
            list.Add(element);
        }

        List<ResourceLayoutDescription> layouts = [];
        foreach ((uint set, List<ResourceLayoutElementDescription> list) in bySet)
        {
            list.Sort((a, b) => a.BindingIndex.CompareTo(b.BindingIndex));
            layouts.Add(new ResourceLayoutDescription(set, [.. list]));
        }

        ShaderDescription description = new()
        {
            Stages = stageDescriptions,
            VertexLayouts = [.. vertexLayouts],
            ResourceLayouts = [.. layouts],
            PushConstants = pushConstants,
        };

        return (description, x, y, z);
    }


    private static Module Parse(byte[] bytes)
    {
        if (bytes.Length < 20 || bytes.Length % 4 != 0)
            throw new ShaderCompileException("error: the compiler produced invalid SPIR-V");

        uint[] w = new uint[bytes.Length / 4];
        Buffer.BlockCopy(bytes, 0, w, 0, bytes.Length);
        if (w[0] != 0x07230203)
            throw new ShaderCompileException("error: the compiler produced invalid SPIR-V");

        Module m = new();
        for (int i = 5; i < w.Length;)
        {
            int count = (int)(w[i] >> 16);
            int op = (int)(w[i] & 0xFFFF);
            if (count == 0)
                throw new ShaderCompileException("error: the compiler produced invalid SPIR-V");

            switch (op)
            {
                case OpEntryPoint:
                    m.Model = w[i + 1];
                    int after = i + 3 + StringWords(w, i + 3);
                    m.Interface = w[after..(i + count)];
                    break;

                case OpExecutionMode when w[i + 2] == ExecutionModeLocalSize:
                    m.LocalSize = [w[i + 3], w[i + 4], w[i + 5]];
                    break;

                case OpExecutionMode when w[i + 2] == ExecutionModeLocalSizeId:
                    m.LocalSizeIds = [w[i + 3], w[i + 4], w[i + 5]];
                    break;

                case OpDecorate:
                    m.Decorations[(w[i + 1], w[i + 2])] = count > 3 ? w[i + 3] : 0;
                    break;

                case OpMemberDecorate:
                    m.MemberDecorations[(w[i + 1], w[i + 2], w[i + 3])] = count > 4 ? w[i + 4] : 0;
                    break;

                case >= 19 and <= OpTypeStruct or OpTypePointer or OpTypeAccelerationStructure:
                    m.Types[w[i + 1]] = (op, w[(i + 2)..(i + count)]);
                    break;

                case OpConstant:
                    m.Constants[w[i + 2]] = w[i + 3];
                    break;

                case OpVariable:
                    m.Variables[w[i + 2]] = (w[i + 1], w[i + 3]);
                    break;
            }

            i += count;
        }

        return m;
    }


    private static int StringWords(uint[] w, int at)
    {
        for (int i = at; i < w.Length; i++)
        {
            if ((w[i] & 0xFF000000) == 0 || (w[i] & 0xFF0000) == 0 || (w[i] & 0xFF00) == 0 || (w[i] & 0xFF) == 0)
                return i - at + 1;
        }

        return w.Length - at;
    }


    private static bool IsBuiltIn(Module m, uint id, uint pointee)
    {
        if (m.Has(id, DecorationBuiltIn))
            return true;

        return m.Types[pointee].Op == OpTypeStruct && m.HasMember(pointee, 0, DecorationBuiltIn);
    }


    private static void ReflectResource(Module m, GlslProgramLayout layout, uint id, uint storage, uint pointee, ShaderStages stage, Dictionary<(uint, int), ResourceLayoutElementDescription> elements)
    {
        uint set = m.Get(id, DecorationDescriptorSet);
        int binding = (int)m.Get(id, DecorationBinding);
        if (!layout.ByBinding.TryGetValue((set, binding), out GlslResource? resource))
            throw new ShaderCompileException($"error: SPIR-V uses set {set} binding {binding} which no declaration has");

        (int op, uint[] operands) = m.Types[pointee];
        ResourceKind kind;
        ResourceLayoutElementOptions options = ResourceLayoutElementOptions.None;
        UniformBlockField[] fields = [];

        switch (op)
        {
            case OpTypeStruct when storage == StorageUniform && m.Has(pointee, DecorationBlock):
                kind = ResourceKind.UniformBuffer;
                List<UniformBlockField> list = [];
                Flatten(m, layout, pointee, resource.Members, "", 0, list);
                fields = [.. list];
                break;

            case OpTypeStruct when storage == StorageStorageBuffer || m.Has(pointee, DecorationBufferBlock):
                bool readOnly = m.Has(id, DecorationNonWritable);
                if (!readOnly)
                {
                    readOnly = true;
                    for (uint member = 0; member < operands.Length; member++)
                        readOnly &= m.HasMember(pointee, member, DecorationNonWritable);
                }
                kind = readOnly ? ResourceKind.StructuredBufferReadOnly : ResourceKind.StructuredBufferReadWrite;
                break;

            case OpTypeImage:
                CheckImage(m, pointee, resource);
                kind = operands[5] == 2 ? ResourceKind.TextureReadWrite : ResourceKind.TextureReadOnly;
                break;

            case OpTypeSampledImage:
                CheckImage(m, operands[0], resource);
                kind = ResourceKind.TextureReadOnly;
                options = ResourceLayoutElementOptions.CombinedImageSampler;
                break;

            case OpTypeSampler:
                kind = ResourceKind.Sampler;
                break;

            case OpTypeArray or OpTypeRuntimeArray:
                throw new ShaderCompileException($"error: '{resource.SourceName}' is an array of resources, which is not supported yet");

            default:
                throw new ShaderCompileException($"error: '{resource.SourceName}' is a resource type that is not supported");
        }

        if (elements.TryGetValue((set, binding), out ResourceLayoutElementDescription existing))
        {
            if (existing.Kind != kind)
                throw new ShaderCompileException($"error: '{resource.SourceName}' reflects as {existing.Kind} in one stage and {kind} in another");

            existing.Stages |= stage;
            elements[(set, binding)] = existing;
            return;
        }

        elements[(set, binding)] = new ResourceLayoutElementDescription(resource.Name, kind, stage, binding, options, fields);
    }


    private static uint Align4(uint size) => (size + 3) & ~3u;


    // Bytes a type covers under its explicit layout decorations, the end of its last member for a struct.
    private static uint TypeSize(Module m, uint type, uint matrixStride)
    {
        (int op, uint[] operands) = m.Types[type];
        switch (op)
        {
            case OpTypeStruct:
                uint end = 0;
                for (uint i = 0; i < operands.Length; i++)
                    end = Math.Max(end, m.GetMember(type, i, DecorationOffset) + TypeSize(m, operands[i], m.GetMember(type, i, DecorationMatrixStride)));
                return end;
            case OpTypeArray:
                return m.Constants[operands[1]] * m.Get(type, DecorationArrayStride);
            case OpTypeMatrix:
                return operands[1] * matrixStride;
            case OpTypeVector:
                return operands[1] * TypeSize(m, operands[0], 0);
            case OpTypeFloat or OpTypeInt:
                return operands[0] / 8;
            default:
                throw new ShaderCompileException("error: a push constant block holds a type that is not supported");
        }
    }


    private static void CheckImage(Module m, uint image, GlslResource resource)
    {
        uint dim = m.Types[image].Operands[1];
        if (dim == DimBuffer)
            throw new ShaderCompileException($"error: '{resource.SourceName}' is a texel buffer, which is not supported, use a storage buffer");
        if (dim == DimSubpassData)
            throw new ShaderCompileException($"error: '{resource.SourceName}' is a subpass input, which is not supported");
    }


    private static void Flatten(Module m, GlslProgramLayout layout, uint structType, List<GlslMember> members, string prefix, uint baseOffset, List<UniformBlockField> fields)
    {
        uint[] memberTypes = m.Types[structType].Operands;
        if (memberTypes.Length != members.Count)
            throw new ShaderCompileException($"error: block '{prefix.TrimEnd('.')}' has {memberTypes.Length} members in SPIR-V but {members.Count} in source");

        for (uint i = 0; i < memberTypes.Length; i++)
        {
            if (m.HasMember(structType, i, DecorationRowMajor) && m.Types[memberTypes[i]].Op != OpTypeStruct)
                throw new ShaderCompileException($"error: '{prefix}{members[(int)i].Name}' is row_major, which is not supported");

            uint offset = baseOffset + m.GetMember(structType, i, DecorationOffset);
            uint matrixStride = m.GetMember(structType, i, DecorationMatrixStride);
            Emit(m, layout, memberTypes[i], members[(int)i], prefix + members[(int)i].Name, offset, matrixStride, fields);
        }
    }


    private static void Emit(Module m, GlslProgramLayout layout, uint type, GlslMember member, string name, uint offset, uint matrixStride, List<UniformBlockField> fields)
    {
        (int op, uint[] operands) = m.Types[type];
        if (op == OpTypeArray)
        {
            uint element = operands[0];
            uint length = m.Constants[operands[1]];
            uint stride = m.Get(type, DecorationArrayStride);
            int elementOp = m.Types[element].Op;
            if (elementOp is OpTypeArray or OpTypeStruct)
            {
                for (uint k = 0; k < length; k++)
                    Emit(m, layout, element, member, $"{name}[{k}]", offset + k * stride, matrixStride, fields);
                return;
            }

            (UniformScalarType elementType, _) = Scalar(m, element, matrixStride, name);
            fields.Add(new UniformBlockField(name, offset, elementType, length, stride));
            return;
        }

        if (op == OpTypeRuntimeArray)
            throw new ShaderCompileException($"error: '{name}' is a runtime array, which only storage buffers can hold");

        if (op == OpTypeStruct)
        {
            if (!layout.TryGetStruct(member.TypeName, out List<GlslMember> nested))
                throw new ShaderCompileException($"error: struct '{member.TypeName}' of '{name}' was not found");
            Flatten(m, layout, type, nested, name + ".", offset, fields);
            return;
        }

        (UniformScalarType scalar, uint size) = Scalar(m, type, matrixStride, name);
        fields.Add(new UniformBlockField(name, offset, size, scalar));
    }


    private static (UniformScalarType Type, uint Size) Scalar(Module m, uint type, uint matrixStride, string name)
    {
        (int op, uint[] operands) = m.Types[type];
        switch (op)
        {
            case OpTypeFloat or OpTypeInt:
                return (Component(m, type, 1, name), operands[0] / 8);

            case OpTypeVector:
                uint count = operands[1];
                return (Component(m, operands[0], (int)count, name), m.Types[operands[0]].Operands[0] / 8 * count);

            case OpTypeMatrix:
                uint columns = operands[1];
                (int _, uint[] column) = m.Types[operands[0]];
                uint rows = column[1];
                bool isDouble = m.Types[column[0]].Operands[0] == 64;
                UniformScalarType matrix = (columns, rows, isDouble) switch
                {
                    (4, 4, false) => UniformScalarType.Float4x4,
                    (3, 3, false) => UniformScalarType.Float3x3,
                    (2, 2, false) => UniformScalarType.Float2x2,
                    (4, 4, true) => UniformScalarType.Double4x4,
                    _ => throw new ShaderCompileException($"error: uniform '{name}' is a {columns}x{rows} matrix, only square matrices are supported"),
                };
                return (matrix, columns * matrixStride);

            default:
                throw new ShaderCompileException($"error: uniform '{name}' has a type that is not supported");
        }
    }


    private static UniformScalarType Component(Module m, uint type, int count, string name)
    {
        (int op, uint[] operands) = m.Types[type];
        int baseType = (op, operands[0], op == OpTypeInt ? operands[1] : 0u) switch
        {
            (OpTypeFloat, 32, _) => (int)UniformScalarType.Float1,
            (OpTypeInt, 32, 1) => (int)UniformScalarType.Int1,
            (OpTypeInt, 32, 0) => (int)UniformScalarType.UInt1,
            (OpTypeFloat, 64, _) => (int)UniformScalarType.Double1,
            _ => throw new ShaderCompileException($"error: uniform '{name}' has a component type that is not supported"),
        };

        return (UniformScalarType)(baseType + count - 1);
    }


    private static VertexLayoutDescription ReflectVertexInput(Module m, GlslProgramLayout layout, uint id, uint type)
    {
        int location = (int)m.Get(id, DecorationLocation);
        if (!layout.VertexInputs.TryGetValue(location, out string? name))
            throw new ShaderCompileException($"error: vertex input at location {location} has no declaration");

        (int op, uint[] operands) = m.Types[type];
        (uint component, uint count) = op == OpTypeVector ? (operands[0], operands[1]) : (type, 1u);
        (int componentOp, uint[] componentOperands) = m.Types[component];

        VertexElementFormat format = (componentOp, componentOperands[0], componentOp == OpTypeInt ? componentOperands[1] : 0u, count) switch
        {
            (OpTypeFloat, 32, _, 1) => VertexElementFormat.Float1,
            (OpTypeFloat, 32, _, 2) => VertexElementFormat.Float2,
            (OpTypeFloat, 32, _, 3) => VertexElementFormat.Float3,
            (OpTypeFloat, 32, _, 4) => VertexElementFormat.Float4,
            (OpTypeInt, 32, 1, 1) => VertexElementFormat.Int1,
            (OpTypeInt, 32, 1, 2) => VertexElementFormat.Int2,
            (OpTypeInt, 32, 1, 3) => VertexElementFormat.Int3,
            (OpTypeInt, 32, 1, 4) => VertexElementFormat.Int4,
            (OpTypeInt, 32, 0, 1) => VertexElementFormat.UInt1,
            (OpTypeInt, 32, 0, 2) => VertexElementFormat.UInt2,
            (OpTypeInt, 32, 0, 3) => VertexElementFormat.UInt3,
            (OpTypeInt, 32, 0, 4) => VertexElementFormat.UInt4,
            _ => throw new ShaderCompileException($"error: vertex input '{name}' has a type that is not supported, use 32 bit scalars or vectors"),
        };

        return new VertexLayoutDescription((uint)location, format.GetSizeInBytes(), new VertexElementDescription(name, format));
    }
}
