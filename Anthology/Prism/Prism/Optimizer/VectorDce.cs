namespace Prowl.Prism.Optimizer;

public static partial class SpirVOptimizer
{
    // spvOpcodeIsScalarizable plus the component-wise GLSL.std.450 instructions (Instruction::IsScalarizable).
    static readonly HashSet<int> ScalarizableOps =
    [
        245, 83, 109, 110, 111, 112, 113, 114, 115, 116, 78, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136,
        137, 138, 139, 140, 141, 142, 149, 150, 151, 152, 194, 195, 196, 197, 199, 200, 201, 202, 203, 204, 205, 156,
        157, 158, 159, 160, 161, 162, 163, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175, 176, 177, 178,
        179, 180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191,
    ];
    static readonly HashSet<uint> ScalarizableGlsl =
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31,
        32, 35, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 48, 49, 50, 51, 53, 73, 74, 75, 79, 80, 81,
    ];

    // spirv-opt VectorDCE: tracks which components of every vector value are actually read. A combinator whose
    // result has no live component becomes undef; an OpCompositeInsert into a component nobody reads is
    // bypassed (its users take the original composite); an insert that overwrites the only live component
    // drops its composite input to undef.
    static void VectorDce(SpvModule m)
    {
        const uint All = 0xFFFF;
        var def = m.DefMap();
        uint glslSet = 0;
        foreach (var ins in m.Instrs) if (ins.Opcode == 11 && ins.Operands.Length >= 2 && DecodeSpvString(ins.Operands, 1) == "GLSL.std.450") { glslSet = ins.Operands[0]; break; }
        uint nextId = m.Header[3];
        var newGlobals = new List<SpvInstr>();
        var replace = new Dictionary<uint, uint>();
        var kill = new HashSet<SpvInstr>();

        uint ResultType(SpvInstr i) { i.Classify(out int ri, out _); return ri >= 1 ? i.Operands[ri - 1] : 0; }
        bool IsVector(SpvInstr i) => def.TryGetValue(ResultType(i), out var t) && t.Opcode == 23;
        bool IsScalar(SpvInstr i) => def.TryGetValue(ResultType(i), out var t) && t.Opcode is 20 or 21 or 22;
        uint Count(uint type) => def[type].Operands[2];
        bool Combinator(SpvInstr i) => AdceCombinators.Contains(i.Opcode)
            || i.Opcode == OpExtInst && glslSet != 0 && i.Operands.Length >= 4 && i.Operands[2] == glslSet && AdceGlslCombinators.Contains(i.Operands[3]);
        bool Scalarizable(SpvInstr i) => ScalarizableOps.Contains(i.Opcode)
            || i.Opcode == OpExtInst && glslSet != 0 && i.Operands.Length >= 4 && i.Operands[2] == glslSet && ScalarizableGlsl.Contains(i.Operands[3]);
        // MemPass::Type2Undef: a per-pass map, so the first use of a type declares a fresh OpUndef even when the
        // module already has one.
        uint Undef(uint type)
        {
            foreach (var g in newGlobals) if (g.Operands[0] == type) return g.Operands[1];
            var u = new SpvInstr(1, [type, nextId++]); newGlobals.Add(u); def[u.Operands[1]] = u; return u.Operands[1];
        }

        for (int f = 0; f < m.Instrs.Count; f++)
        {
            if (m.Instrs[f].Opcode != OpFunction) continue;
            int fe = f; while (m.Instrs[fe].Opcode != OpFunctionEnd) fe++;
            var fn = m.Instrs.GetRange(f, fe - f + 1);
            var live = new Dictionary<uint, uint>();
            var work = new List<(SpvInstr Inst, uint Mask)>();
            void Add(SpvInstr inst, uint mask)
            {
                uint r = inst.ResultId;
                if (!live.TryGetValue(r, out uint cur)) { live[r] = mask; work.Add((inst, mask)); }
                else if ((cur | mask) != cur) { live[r] = cur | mask; work.Add((inst, mask)); }
            }
            void MarkUses(SpvInstr inst, uint mask)
            {
                inst.Classify(out int ri, out bool[] isId);
                for (int i = 0; i < inst.Operands.Length; i++)
                {
                    if (!isId[i] || i == ri || i == ri - 1 || !def.TryGetValue(inst.Operands[i], out var d)) continue;
                    if (IsVector(d)) Add(d, mask);
                    else if (IsScalar(d)) Add(d, 1);
                }
            }
            foreach (var inst in fn)
                if (!((IsVector(inst) || IsScalar(inst)) && Combinator(inst))) MarkUses(inst, All);
            for (int w = 0; w < work.Count; w++)
            {
                var (inst, mask) = work[w];
                switch (inst.Opcode)
                {
                    case OpCompositeExtract:
                    {
                        if (!def.TryGetValue(inst.Operands[2], out var c) || !(IsVector(c) || IsScalar(c))) break;
                        if (inst.Operands.Length < 4) Add(c, mask);
                        else { uint idx = inst.Operands[3]; Add(c, idx < Count(ResultType(c)) ? 1u << (int)idx : 0); }
                        break;
                    }
                    case 82:   // OpCompositeInsert
                    {
                        if (inst.Operands.Length > 4)
                        {
                            int pos = (int)inst.Operands[4];
                            Add(def[inst.Operands[3]], mask & ~(1u << pos));
                            if ((mask & (1u << pos)) != 0) Add(def[inst.Operands[2]], 1);
                        }
                        else Add(def[inst.Operands[2]], mask);
                        break;
                    }
                    case 79:   // OpVectorShuffle
                    {
                        var a = def[inst.Operands[2]]; var b = def[inst.Operands[3]];
                        uint na = Count(ResultType(a)), nb = Count(ResultType(b)), ma = 0, mb = 0;
                        for (int k = 4; k < inst.Operands.Length; k++)
                        {
                            if ((mask & (1u << (k - 4))) == 0) continue;
                            uint idx = inst.Operands[k];
                            if (idx < na) ma |= 1u << (int)idx;
                            else if (idx - na < nb) mb |= 1u << (int)(idx - na);
                        }
                        Add(a, ma); Add(b, mb);
                        break;
                    }
                    case OpCompositeConstruct:
                    {
                        int comp = 0;
                        for (int k = 2; k < inst.Operands.Length; k++)
                        {
                            var o = def[inst.Operands[k]];
                            if (IsScalar(o)) { Add(o, (mask & (1u << comp)) != 0 ? 1u : 0); comp++; }
                            else
                            {
                                uint n = Count(ResultType(o)), om = 0;
                                for (int j = 0; j < n; j++, comp++) if ((mask & (1u << comp)) != 0) om |= 1u << j;
                                Add(o, om);
                            }
                        }
                        break;
                    }
                    default:
                        MarkUses(inst, Scalarizable(inst) ? mask : All);
                        break;
                }
            }

            uint Final(uint id) { for (int g = 0; g < 64 && replace.TryGetValue(id, out uint r); g++) id = r; return id; }
            foreach (var inst in fn)
            {
                if (!Combinator(inst) || !live.TryGetValue(inst.ResultId, out uint mask)) continue;
                if (mask == 0) { replace[inst.ResultId] = Undef(ResultType(inst)); kill.Add(inst); continue; }
                if (inst.Opcode != 82) continue;
                if (inst.Operands.Length <= 4) { replace[inst.ResultId] = Final(inst.Operands[2]); continue; }
                int pos = (int)inst.Operands[4];
                if ((mask & (1u << pos)) == 0) { replace[inst.ResultId] = Final(inst.Operands[3]); continue; }
                if ((mask & ~(1u << pos)) == 0) inst.Operands[3] = Undef(inst.Operands[0]);
            }
            f = fe;
        }
        if (replace.Count == 0 && kill.Count == 0 && newGlobals.Count == 0) return;
        if (kill.Count > 0) m.Instrs.RemoveAll(kill.Contains);
        foreach (var ins in m.Instrs)
        {
            ins.Classify(out int ri, out bool[] isId);
            for (int i = 0; i < ins.Operands.Length; i++)
                if (isId[i] && i != ri)
                    for (int g = 0; g < 64 && replace.TryGetValue(ins.Operands[i], out uint r); g++) ins.Operands[i] = r;
        }
        if (newGlobals.Count > 0) m.Instrs.InsertRange(m.Instrs.FindIndex(x => x.Opcode == OpFunction), newGlobals);
        m.Header[3] = nextId;
    }
}
