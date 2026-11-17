using Prowl.Prism.Compiler;
using Prowl.Prism.SpirV;
using Xunit;

namespace Prowl.Prism.Tests;

/// <summary>
/// A block's array members each get their own laid-out type, never de-duplicated, so two members of the same GLSL array
/// type are distinct SPIR-V types. Every extract and access chain into such a block must name the type of the exact member
/// it reaches, which spirv-val checks and mobile drivers refuse to link without.
/// </summary>
public sealed class LaidOutTypeTests
{
    const string CopiesABlockStruct = """
        #version 460
        struct Record
        {
            vec4 cascades[4];
            vec4 heights;
            vec4 right[4];
            vec4 up[4];
            vec4 toSun[4];
        };
        layout(set = 0, binding = 0, std430) readonly buffer Data { Record data; };
        layout(location = 0) in flat int c;
        layout(location = 0) out vec4 color;
        void main()
        {
            Record r = data;
            color = r.cascades[c] + r.right[c] + r.up[c] + r.toSun[c] + r.heights;
        }
        """;

    [Theory]
    [InlineData(SpirVVersion.V1_0, false)]
    [InlineData(SpirVVersion.V1_3, false)]
    [InlineData(SpirVVersion.V1_3, true)]
    [InlineData(SpirVVersion.V1_5, true)]
    public void CopyingABlockStructNamesEachMembersOwnType(SpirVVersion target, bool optimize)
    {
        uint[] words = StructValidator.Valid(GlslCompiler.CompileToBytes(CopiesABlockStruct, ShaderStage.Fragment, target, optimize: optimize));
        Assert.Empty(MistypedParts(words));
    }

    // Extracts and access chains whose result type is not the type indexing their operand reaches
    static List<string> MistypedParts(uint[] w)
    {
        const int OpTypeArray = 28, OpTypeStruct = 30, OpTypePointer = 32, OpConstant = 43, OpVariable = 59, OpLoad = 61, OpAccessChain = 65, OpCompositeExtract = 81;
        Dictionary<uint, uint[]> structs = [];
        Dictionary<uint, uint> arrays = [], pointees = [], constants = [], types = [];
        List<string> wrong = [];

        uint Reach(uint type, ReadOnlySpan<uint> indices, bool constantIds)
        {
            foreach (uint index in indices)
            {
                if (structs.TryGetValue(type, out uint[]? members))
                    type = constantIds ? constants.TryGetValue(index, out uint value) ? members[value] : 0 : members[index];
                else
                    type = arrays.GetValueOrDefault(type);
                if (type == 0)
                    return 0;
            }
            return type;
        }

        for (int i = 5; i < w.Length;)
        {
            int count = (int)(w[i] >> 16), op = (int)(w[i] & 0xFFFF);
            ReadOnlySpan<uint> inst = w.AsSpan(i, count);
            switch (op)
            {
                case OpTypeStruct: structs[inst[1]] = inst[2..].ToArray(); break;
                case OpTypeArray: arrays[inst[1]] = inst[2]; break;
                case OpTypePointer: pointees[inst[1]] = inst[3]; break;
                case OpConstant: constants[inst[2]] = inst[3]; break;
                case OpVariable: types[inst[2]] = inst[1]; break;
                case OpLoad: types[inst[2]] = inst[1]; break;
                case OpAccessChain:
                {
                    uint reached = types.TryGetValue(inst[3], out uint basePointer) ? Reach(pointees.GetValueOrDefault(basePointer), inst[4..], constantIds: true) : 0;
                    if (reached != 0 && reached != pointees.GetValueOrDefault(inst[1]))
                        wrong.Add($"access chain %{inst[2]}");
                    types[inst[2]] = inst[1];
                    break;
                }
                case OpCompositeExtract:
                {
                    uint reached = types.TryGetValue(inst[3], out uint composite) ? Reach(composite, inst[4..], constantIds: false) : 0;
                    if (reached != 0 && reached != inst[1])
                        wrong.Add($"extract %{inst[2]}");
                    types[inst[2]] = inst[1];
                    break;
                }
            }
            i += count;
        }
        return wrong;
    }
}
