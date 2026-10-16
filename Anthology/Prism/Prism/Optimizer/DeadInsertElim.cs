namespace Prowl.Prism.Optimizer;

public static partial class SpirVOptimizer
{
    // spirv-opt DeadInsertElimPass: an OpCompositeInsert whose component no extract, phi chain or other use ever
    // reads is bypassed (its users take the composite it inserted into), then MemPass::DCEInst deletes it and
    // whatever only it kept alive. Array inserts are never optimized. Repeats per reachable function until stable.
    static void DeadInsertElim(SpvModule m)
    {
        var def = m.DefMap();
        var users = new Dictionary<uint, List<SpvInstr>>();
        foreach (var ins in m.Instrs)
        {
            ins.Classify(out int ri, out bool[] isId);
            for (int k = 0; k < ins.Operands.Length; k++)
                if (isId[k] && k != ri) (users.TryGetValue(ins.Operands[k], out var l) ? l : users[ins.Operands[k]] = []).Add(ins);
        }
        uint glslSet = 0;
        foreach (var ins in m.Instrs) if (ins.Opcode == 11 && ins.Operands.Length >= 2 && DecodeSpvString(ins.Operands, 1) == "GLSL.std.450") { glslSet = ins.Operands[0]; break; }
        var killed = new HashSet<SpvInstr>();

        List<SpvInstr> UsersOf(uint id) => users.TryGetValue(id, out var l) ? l : [];
        uint TypeOf(SpvInstr i) { i.Classify(out int ri, out _); return ri >= 1 ? i.Operands[ri - 1] : 0; }
        static bool IsAnnotation(int op) => op is 5 or 6 or 71 or 72 or 332 or 5632 or 5633;
        bool OnlyNamesAndDecorates(uint id) => UsersOf(id).All(u => u.Opcode is 5 or OpDecorate or 332);
        bool Combinator(SpvInstr i) => AdceCombinators.Contains(i.Opcode)
            || i.Opcode == OpExtInst && glslSet != 0 && i.Operands.Length >= 4 && i.Operands[2] == glslSet && AdceGlslCombinators.Contains(i.Operands[3]);

        uint NumComponents(SpvInstr type)
        {
            switch (type.Opcode)
            {
                case 23: case 24: return type.Operands[2];   // OpTypeVector, OpTypeMatrix
                case 28:   // OpTypeArray
                    if (!def.TryGetValue(type.Operands[2], out var len) || len.Opcode != OpConstant) return 0;
                    return def[len.Operands[0]].Operands[1] != 32 ? 0 : len.Operands[2];
                case 30: return (uint)(type.Operands.Length - 1);   // OpTypeStruct
                default: return 0;
            }
        }

        // ExtInsMatch / ExtInsConflict (source/opt/composite.cpp): an insert's literal indices start at operand 4.
        static bool Match(List<uint> ext, SpvInstr ins, int off)
        {
            int n = ext.Count - off;
            if (n != ins.Operands.Length - 4) return false;
            for (int i = 0; i < n; i++) if (ext[i + off] != ins.Operands[i + 4]) return false;
            return true;
        }
        static bool Conflict(List<uint> ext, SpvInstr ins, int off)
        {
            int extN = ext.Count - off, insN = ins.Operands.Length - 4;
            if (extN == insN) return false;
            for (int i = 0; i < Math.Min(extN, insN); i++) if (ext[i + off] != ins.Operands[i + 4]) return false;
            return true;
        }

        var live = new HashSet<uint>();
        void MarkChain(SpvInstr chain, List<uint>? ext, int off, HashSet<uint> visitedPhis)
        {
            var type = def[TypeOf(chain)];
            if (type.Opcode == 28) return;
            if (chain.Opcode is not (82 or 245)) return;
            if (ext is null)
            {
                uint cnum = NumComponents(type);
                if (cnum > 0)
                {
                    for (uint i = 0; i < cnum; i++) MarkChain(chain, [i], 0, []);
                    return;
                }
            }
            var ins = chain;
            while (ins.Opcode == 82)
            {
                if (ext is null)
                {
                    live.Add(ins.ResultId);
                    MarkChain(def[ins.Operands[2]], null, 0, []);
                }
                else if (Match(ext, ins, off))
                {
                    live.Add(ins.ResultId);
                    MarkChain(def[ins.Operands[2]], null, 0, []);
                    break;
                }
                else if (Conflict(ext, ins, off))
                {
                    live.Add(ins.ResultId);
                    int insN = ins.Operands.Length - 4;
                    if (ext.Count - off > insN)
                    {
                        MarkChain(def[ins.Operands[2]], ext, off + insN, []);
                        break;
                    }
                    MarkChain(def[ins.Operands[2]], null, 0, []);
                }
                ins = def[ins.Operands[3]];
            }
            if (ins.Opcode != 245 || !visitedPhis.Add(ins.ResultId)) return;
            var ids = new SortedSet<uint>();
            for (int k = 2; k < ins.Operands.Length; k += 2) ids.Add(ins.Operands[k]);
            foreach (uint id in ids) MarkChain(def[id], ext, off, visitedPhis);
        }

        // MemPass::GetPtr's base variable of a load's pointer.
        uint BaseVar(uint ptr)
        {
            for (int g = 0; g < 64 && def.TryGetValue(ptr, out var d); g++)
            {
                if (d.Opcode is OpVariable or 55) return ptr;   // OpFunctionParameter
                if (d.Opcode is OpAccessChain or OpInBoundsAccessChain or OpPtrAccessChain or OpInBoundsPtrAccessChain or 60 or OpCopyObject)
                    ptr = d.Operands[2];
                else return 0;
            }
            return 0;
        }
        bool HasLoads(uint id) => UsersOf(id).Any(u =>
            u.Opcode is OpAccessChain or OpInBoundsAccessChain or OpCopyObject ? HasLoads(u.ResultId)
            : u.Opcode is not (OpStore or 5 or OpDecorate or 332));
        bool IsLiveVar(uint id) =>
            !def.TryGetValue(id, out var v) || v.Opcode != OpVariable || v.Operands[2] != OpTypePointerSc || HasLoads(id);
        void AddStores(uint ptr, Queue<SpvInstr> q)
        {
            foreach (var u in UsersOf(ptr).ToArray())
                if (u.Opcode is OpAccessChain or OpInBoundsAccessChain) AddStores(u.ResultId, q);
                else if (u.Opcode == OpStore) q.Enqueue(u);
        }

        // IRContext::KillInst: the instruction, and the names and decorations that target its result.
        void Kill(SpvInstr di)
        {
            killed.Add(di);
            di.Classify(out int ri, out bool[] isId);
            for (int k = 0; k < di.Operands.Length; k++)
                if (isId[k] && k != ri && users.TryGetValue(di.Operands[k], out var l)) l.Remove(di);
            if (ri < 0) return;
            uint r = di.Operands[ri];
            foreach (var u in UsersOf(r).ToArray())
                if (IsAnnotation(u.Opcode) && u.Operands[0] == r) Kill(u);
        }

        void DceInst(SpvInstr inst, List<SpvInstr> pending)
        {
            var q = new Queue<SpvInstr>();
            q.Enqueue(inst);
            while (q.Count > 0)
            {
                var di = q.Dequeue();
                if (di.Opcode == OpLabel || killed.Contains(di)) continue;
                var ids = new SortedSet<uint>();
                di.Classify(out int ri, out bool[] isId);
                for (int k = 0; k < di.Operands.Length; k++) if (isId[k] && k != ri && !(ri >= 1 && k == ri - 1)) ids.Add(di.Operands[k]);
                uint varId = di.Opcode == OpLoad ? BaseVar(di.Operands[2]) : 0;
                pending.Remove(di);
                Kill(di);
                foreach (uint id in ids)
                    if (OnlyNamesAndDecorates(id) && def.TryGetValue(id, out var odi) && !killed.Contains(odi) && Combinator(odi)) q.Enqueue(odi);
                if (varId != 0 && !IsLiveVar(varId)) AddStores(varId, q);
            }
        }

        void OnePass(int fs, int fe)
        {
            live.Clear();
            for (int i = fs; i < fe; i++)
            {
                var ins = m.Instrs[i];
                if (killed.Contains(ins) || ins.Opcode is not (82 or 245)) continue;
                var type = def[TypeOf(ins)];
                if (ins.Opcode == 245 && type.Opcode is not (23 or 24 or 28 or 29 or 30 or 4456 or 5358)) continue;
                if (ins.Opcode == 82 && type.Opcode == 28) { live.Add(ins.ResultId); continue; }
                foreach (var u in UsersOf(ins.ResultId).ToArray())
                {
                    if (u.Opcode is 82 or 245) continue;
                    if (u.Opcode == OpCompositeExtract) MarkChain(ins, [.. u.Operands.Skip(3)], 0, []);
                    else MarkChain(ins, null, 0, []);
                }
            }
            var dead = new List<SpvInstr>();
            for (int i = fs; i < fe; i++)
            {
                var ins = m.Instrs[i];
                if (killed.Contains(ins) || ins.Opcode != 82 || live.Contains(ins.ResultId)) continue;
                uint id = ins.ResultId, repl = ins.Operands[3];
                foreach (var u in UsersOf(id).ToArray())
                {
                    u.Classify(out int ri, out bool[] isId);
                    for (int k = 0; k < u.Operands.Length; k++)
                        if (isId[k] && k != ri && u.Operands[k] == id) u.Operands[k] = repl;
                    (users.TryGetValue(repl, out var l) ? l : users[repl] = []).Add(u);
                }
                users.Remove(id);
                dead.Add(ins);
            }
            while (dead.Count > 0)
            {
                var inst = dead[^1];
                dead.RemoveAt(dead.Count - 1);
                DceInst(inst, dead);
            }
        }

        foreach (uint fn in ReachableFunctions(m))
        {
            int fs = m.Instrs.FindIndex(x => x.Opcode == OpFunction && x.Operands[1] == fn);
            if (fs < 0) continue;
            int fe = fs; while (m.Instrs[fe].Opcode != OpFunctionEnd) fe++;
            while (true)
            {
                int before = killed.Count;
                OnePass(fs, fe);
                if (killed.Count == before) break;
            }
        }
        if (killed.Count > 0) m.Instrs.RemoveAll(killed.Contains);
    }
}
