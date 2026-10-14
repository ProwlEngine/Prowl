namespace Prowl.Prism.Optimizer;

public static partial class SpirVOptimizer
{
    // spirv-opt ReduceLoadSize (threshold 0.9, the -O default): a load of a whole struct/array from Uniform,
    // UniformConstant or Input memory that is only consumed by OpCompositeExtracts touching under 90% of its
    // elements is replaced, per extract, by an access chain to that element and a smaller load (inserted
    // before the original load, which later DCE removes).
    static void ReduceLoadSize(SpvModule m)
    {
        var def = m.DefMap();
        var users = m.UserMap();
        uint nextId = m.Header[3];
        var newGlobals = new List<SpvInstr>();
        var cache = new Dictionary<uint, bool>();
        var insertBefore = new Dictionary<SpvInstr, List<SpvInstr>>();
        var kill = new HashSet<SpvInstr>();
        var replace = new Dictionary<uint, uint>();

        uint Find(Func<SpvInstr, bool> pred) { foreach (var g in m.Instrs) if (pred(g)) return g.ResultId; foreach (var g in newGlobals) if (pred(g)) return g.ResultId; return 0; }
        uint NewGlobal(SpvInstr g) { newGlobals.Add(g); def[g.ResultId] = g; return g.ResultId; }
        uint PointerTo(uint pointee, uint sc) =>
            Find(g => g.Opcode == OpTypePointer && g.Operands[1] == sc && g.Operands[2] == pointee) is uint p and not 0 ? p : NewGlobal(new SpvInstr(OpTypePointer, [nextId++, sc, pointee]));
        uint UintType() =>
            Find(g => g.Opcode == 21 && g.Operands[1] == 32 && g.Operands[2] == 0) is uint t and not 0 ? t : NewGlobal(new SpvInstr(21, [nextId++, 32, 0]));
        uint UintConst(uint v)
        {
            uint t = UintType();
            return Find(g => g.Opcode == OpConstant && g.Operands[0] == t && g.Operands.Length == 3 && g.Operands[2] == v) is uint c and not 0 ? c : NewGlobal(new SpvInstr(OpConstant, [t, nextId++, v]));
        }
        SpvInstr? BaseVariable(uint ptr)
        {
            for (int g = 0; g < 64 && def.TryGetValue(ptr, out var d); g++)
            {
                if (d.Opcode == OpVariable) return d;
                if (d.Opcode is OpAccessChain or OpInBoundsAccessChain or OpPtrAccessChain or OpInBoundsPtrAccessChain or 60 or OpCopyObject) ptr = d.Operands[2];
                else return null;
            }
            return null;
        }

        bool ShouldReplace(SpvInstr load)
        {
            if (cache.TryGetValue(load.Operands[1], out bool hit)) return hit;
            var used = new HashSet<uint>();
            bool all = false;
            foreach (var u in users.GetValueOrDefault(load.Operands[1]) ?? [])
            {
                if (u.Opcode != OpCompositeExtract || u.Operands.Length <= 3) { all = true; break; }
                used.Add(u.Operands[3]);
            }
            bool result = false;
            if (!all)
            {
                var t = def[load.Operands[0]];
                double total = t.Opcode switch
                {
                    28 => def.TryGetValue(t.Operands[2], out var len) && len.Opcode == OpConstant ? len.Operands[2] : uint.MaxValue,   // OpTypeArray
                    30 => t.Operands.Length - 1,                                                                                   // OpTypeStruct
                    _ => 1,
                };
                result = used.Count / total < 0.9;
            }
            return cache[load.Operands[1]] = result;
        }

        foreach (var inst in m.Instrs)
        {
            if (inst.Opcode != OpCompositeExtract || !def.TryGetValue(inst.Operands[2], out var load) || load.Opcode != OpLoad) continue;
            if (!ShouldReplace(load)) continue;
            if (def[load.Operands[0]].Opcode is OpTypeVector or 24) continue;   // vector / matrix
            var v = BaseVariable(load.Operands[2]);
            if (v is null || v.Operands.Length < 3 || v.Operands[2] is not (2 or 0 or 1)) continue;   // Uniform, UniformConstant, Input
            uint ptrType = PointerTo(inst.Operands[0], v.Operands[2]);
            var idx = new List<uint>();
            for (int i = 3; i < inst.Operands.Length; i++) idx.Add(UintConst(inst.Operands[i]));
            uint chainId = nextId++, loadId = nextId++;
            var seq = insertBefore.TryGetValue(load, out var l) ? l : insertBefore[load] = [];
            seq.Add(new SpvInstr(OpAccessChain, [ptrType, chainId, load.Operands[2], .. idx]));
            seq.Add(new SpvInstr(OpLoad, [inst.Operands[0], loadId, chainId]));
            replace[inst.Operands[1]] = loadId;
            kill.Add(inst);
        }
        if (kill.Count == 0) return;

        var rebuilt = SpvModule.RentInstrList(m.Instrs.Count + newGlobals.Count + 2 * kill.Count);
        bool globalsPlaced = false;
        foreach (var ins in m.Instrs)
        {
            if (!globalsPlaced && ins.Opcode == OpFunction) { rebuilt.AddRange(newGlobals); globalsPlaced = true; }
            if (kill.Contains(ins)) continue;
            if (insertBefore.TryGetValue(ins, out var seq)) rebuilt.AddRange(seq);
            rebuilt.Add(ins);
        }
        m.SetInstrs(rebuilt);
        foreach (var (from, to) in replace) m.ReplaceUses(from, to);
        m.Header[3] = nextId;
    }
}
