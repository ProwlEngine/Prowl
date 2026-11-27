namespace Prowl.Prism.Optimizer;

public static partial class SpirVOptimizer
{
    // spirv-opt IfConversion: at the merge of a two-way structured selection, a phi of scalar/vector/pointer
    // type whose incoming values both dominate the merge becomes an OpSelect on the header's condition
    // (vector data gets the condition splatted). When both incoming values have the same value number, the
    // phi is replaced by one of them instead, hoisting it into the header if it sits in a branch.
    static void IfConversion(SpvModule m)
    {
        if (!HasShaderCapability(m)) return;   // Shader capability
        var def = m.DefMap();
        var vn = ValueNumbers(m, def);
        uint nextId = m.Header[3];
        var newTypes = new List<SpvInstr>();
        var toKill = new HashSet<SpvInstr>();
        var replace = new Dictionary<uint, uint>();
        uint R(uint id) { for (int g = 0; g < 64 && replace.TryGetValue(id, out uint r); g++) id = r; return id; }

        for (int f = 0; f < m.Instrs.Count; f++)
        {
            if (m.Instrs[f].Opcode != OpFunction) continue;
            int fe = f; while (m.Instrs[fe].Opcode != OpFunctionEnd) fe++;
            var blocks = ParseBlocks(m, f, fe, out int firstLabel);
            if (blocks.Count > 0 && IfConvertFunction(blocks))
                RebuildFunction(m, f, fe, firstLabel, blocks);
            fe = f; while (m.Instrs[fe].Opcode != OpFunctionEnd) fe++;
            f = fe;
        }

        bool IfConvertFunction(List<Blk> blocks)
        {
            var byLabel = blocks.ToDictionary(b => b.Label);
            var blockOfId = new Dictionary<uint, uint>();
            foreach (var b in blocks) foreach (var ins in b.Insts) { uint r = ins.ResultId; if (r != 0) blockOfId[r] = b.Label; }
            // CFG predecessors in layout order of the predecessor (spirv-opt's CFG construction order).
            var preds = blocks.ToDictionary(b => b.Label, _ => new List<uint>());
            foreach (var b in blocks) foreach (var s in BlockSuccs(b)) if (preds.TryGetValue(s, out var pl) && !pl.Contains(b.Label)) pl.Add(b.Label);
            var idom = ImmediateDominators(blocks, preds);
            bool Dom(uint a, uint b) { for (uint x = b; ; x = idom[x]) { if (x == a) return true; if (!idom.ContainsKey(x) || idom[x] == x) return false; } }
            uint CommonDom(uint a, uint b)
            {
                var anc = new HashSet<uint>();
                for (uint x = a; ; x = idom[x]) { anc.Add(x); if (!idom.ContainsKey(x) || idom[x] == x) break; }
                for (uint x = b; ; x = idom[x]) { if (anc.Contains(x)) return x; if (!idom.ContainsKey(x) || idom[x] == x) return 0; }
            }
            var users = new Dictionary<uint, List<SpvInstr>>();
            foreach (var b in blocks) foreach (var ins in b.Insts)
            {
                ins.Classify(out int ri, out bool[] isId);
                for (int i = 0; i < ins.Operands.Length; i++) if (isId[i] && i != ri) (users.TryGetValue(ins.Operands[i], out var l) ? l : users[ins.Operands[i]] = []).Add(ins);
            }

            bool modified = false;
            foreach (var block in blocks)
            {
                if (!idom.ContainsKey(block.Label)) continue;
                var pl = preds[block.Label];
                if (pl.Count != 2) continue;
                uint inc0 = pl[0], inc1 = pl[1];
                if (!idom.ContainsKey(inc0) || !idom.ContainsKey(inc1) || Dom(block.Label, inc0) || Dom(block.Label, inc1) || inc0 == inc1) continue;
                uint commonId = CommonDom(inc0, inc1);
                if (commonId == 0) continue;
                var common = byLabel[commonId];
                if (common.Term.Opcode != 250 || common.Merge is not { Opcode: 247 } merge) continue;
                if (merge.Operands[1] == 2 || merge.Operands[0] != block.Label) continue;   // DontFlatten

                int insertAt = 0; while (insertAt < block.Insts.Count && block.Insts[insertAt].Opcode == 245) insertAt++;
                var phis = block.Insts.Take(insertAt).ToList();
                foreach (var phi in phis)
                {
                    if (!def.TryGetValue(phi.Operands[0], out var pt) || !(pt.Opcode is 20 or 21 or 22 or 23 or 32)) continue;   // bool/int/float/vector/pointer
                    if (users.TryGetValue(phi.Operands[1], out var pus) && pus.Any(u => u.Opcode == 245 && block.Insts.Contains(u))) continue;
                    uint phiInc0 = phi.Operands[3];
                    uint cond = common.Term.Operands[0], thenId = common.Term.Operands[1];
                    bool firstIsTrue = (thenId == block.Label && phiInc0 == commonId) || (idom.ContainsKey(phiInc0) && Dom(thenId, phiInc0));
                    uint tv = R(firstIsTrue ? phi.Operands[2] : phi.Operands[4]);
                    uint fv = R(firstIsTrue ? phi.Operands[4] : phi.Operands[2]);
                    uint tb = blockOfId.GetValueOrDefault(tv), fb = blockOfId.GetValueOrDefault(fv);
                    uint tvn = vn.GetValueOrDefault(tv), fvn = vn.GetValueOrDefault(fv);
                    if (tvn != 0 && tvn == fvn)
                    {
                        uint use = 0;
                        if (tb == 0 || Dom(tb, block.Label)) use = tv;
                        else if (fb == 0 || Dom(fb, block.Label)) use = fv;
                        else if (CanHoist(tv, commonId)) use = tv;
                        else if (CanHoist(fv, commonId)) use = fv;
                        if (use != 0)
                        {
                            Hoist(use, commonId);
                            replace[phi.Operands[1]] = use;
                            block.Insts.Remove(phi); insertAt--;
                            modified = true;
                        }
                        continue;
                    }
                    if (tb != 0 && !Dom(tb, block.Label)) continue;
                    if (fb != 0 && !Dom(fb, block.Label)) continue;
                    uint tvType = def.TryGetValue(tv, out var tvd) ? TypeOfResult(tvd) : 0;
                    if (def.TryGetValue(tvType, out var dt) && dt.Opcode == 23)
                    {
                        uint bvec = BoolVector(dt.Operands[2]);
                        uint splat = nextId++;
                        var cops = new uint[2 + dt.Operands[2]]; cops[0] = bvec; cops[1] = splat;
                        for (int k = 0; k < dt.Operands[2]; k++) cops[2 + k] = cond;
                        block.Insts.Insert(insertAt++, new SpvInstr(80, cops));
                        blockOfId[splat] = block.Label;
                        cond = splat;
                    }
                    uint sel = nextId++;
                    var select = new SpvInstr(169, [phi.Operands[0], sel, cond, tv, fv]);
                    def[sel] = select;
                    blockOfId[sel] = block.Label;
                    block.Insts.Insert(insertAt++, select);
                    replace[phi.Operands[1]] = sel;
                    block.Insts.Remove(phi); insertAt--;
                    modified = true;
                }
            }
            return modified;

            bool CanHoist(uint id, uint target)
            {
                if (!blockOfId.TryGetValue(id, out uint ib)) return true;
                if (Dom(ib, target)) return true;
                var d = def[id];
                if (!CodeMotionSafe(d.Opcode)) return false;
                d.Classify(out int ri, out bool[] isId);
                for (int i = 0; i < d.Operands.Length; i++) if (isId[i] && i != ri && i != ri - 1 && !CanHoist(d.Operands[i], target)) return false;
                return true;
            }
            void Hoist(uint id, uint target)
            {
                if (!blockOfId.TryGetValue(id, out uint ib) || Dom(ib, target)) return;
                var d = def[id];
                d.Classify(out int ri, out bool[] isId);
                for (int i = 0; i < d.Operands.Length; i++) if (isId[i] && i != ri && i != ri - 1) Hoist(d.Operands[i], target);
                byLabel[ib].Insts.Remove(d);
                var tbk = byLabel[target];
                int pos = tbk.Insts.Count - 1;
                if (pos > 0 && tbk.Insts[pos - 1].Opcode == 247) pos--;
                tbk.Insts.Insert(pos, d);
                blockOfId[id] = target;
            }
        }

        uint BoolVector(uint n)
        {
            uint boolType = 0;
            foreach (var ins in m.Instrs.Concat(newTypes)) if (ins.Opcode == 20) { boolType = ins.Operands[0]; break; }
            foreach (var ins in m.Instrs.Concat(newTypes)) if (ins.Opcode == 23 && ins.Operands[1] == boolType && ins.Operands[2] == n) return ins.Operands[0];
            uint id = nextId++;
            newTypes.Add(new SpvInstr(23, [id, boolType, n]));
            return id;
        }

        if (replace.Count == 0 && newTypes.Count == 0) return;
        foreach (var ins in m.Instrs)
        {
            ins.Classify(out int ri, out bool[] isId);
            for (int i = 0; i < ins.Operands.Length; i++)
                if (isId[i] && i != ri)
                    for (int g = 0; g < 64 && replace.TryGetValue(ins.Operands[i], out uint r); g++) ins.Operands[i] = r;
        }
        if (newTypes.Count > 0) m.Instrs.InsertRange(m.Instrs.FindIndex(x => x.Opcode == OpFunction), newTypes);
        m.Header[3] = nextId;
    }

    // Instruction::IsOpcodeCodeMotionSafe.
    static readonly HashSet<int> CodeMotionSafeOps =
    [
        0, 1, 61, 65, 66, 68, 77, 78, 79, 80, 81, 82, 83, 84, 109, 110, 111, 112, 113, 114, 115, 116, 124, 126, 127, 128,
        129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146, 147, 148, 149, 150,
        151, 152, 154, 155, 156, 157, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179,
        180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191, 194, 195, 196, 197, 198, 199, 200, 201, 202, 203,
        204, 205, 321,
    ];
    static bool CodeMotionSafe(int op) => CodeMotionSafeOps.Contains(op);

    static uint TypeOfResult(SpvInstr d) { d.Classify(out int ri, out _); return ri >= 1 ? d.Operands[ri - 1] : 0; }

    static Dictionary<uint, uint> ImmediateDominators(List<Blk> blocks, Dictionary<uint, List<uint>> preds)
    {
        var byLabel = blocks.ToDictionary(b => b.Label);
        var order = new List<uint>(); var seen = new HashSet<uint>();
        void Dfs(uint l) { if (!seen.Add(l) || !byLabel.TryGetValue(l, out var b)) return; foreach (var s in BlockSuccs(b)) Dfs(s); order.Add(l); }
        Dfs(blocks[0].Label); order.Reverse();
        var rpo = new Dictionary<uint, int>(); for (int i = 0; i < order.Count; i++) rpo[order[i]] = i;
        var idom = new Dictionary<uint, uint> { [order[0]] = order[0] };
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (var b in order.Skip(1))
            {
                uint nd = 0;
                foreach (var p in preds[b])
                {
                    if (!idom.ContainsKey(p)) continue;
                    if (nd == 0) { nd = p; continue; }
                    uint x = p, y = nd;
                    while (x != y) { while (rpo[x] > rpo[y]) x = idom[x]; while (rpo[y] > rpo[x]) y = idom[y]; }
                    nd = x;
                }
                if (nd != 0 && (!idom.TryGetValue(b, out var cur) || cur != nd)) { idom[b] = nd; changed = true; }
            }
        }
        return idom;
    }

    // spirv-opt ValueNumberTable, over the whole module in layout order: combinators (and read-only loads that
    // are not image/sampler loads) with equal opcode, type, operand value numbers and decorations share a value
    // number; OpCopyObject and an all-equal OpPhi take their operand's.
    static Dictionary<uint, uint> ValueNumbers(SpvModule m, DefTable def)
    {
        uint glslSet = 0;
        foreach (var ins in m.Instrs) if (ins.Opcode == 11 && ins.Operands.Length >= 2 && DecodeSpvString(ins.Operands, 1) == "GLSL.std.450") { glslSet = ins.Operands[0]; break; }
        var bufferBlocks = new HashSet<uint>();
        var nonWritable = new HashSet<uint>();
        var decor = new Dictionary<uint, List<string>>();
        foreach (var ins in m.Instrs)
        {
            if (ins.Opcode == 71 && ins.Operands.Length >= 2)
            {
                if (ins.Operands[1] == 3) bufferBlocks.Add(ins.Operands[0]);
                if (ins.Operands[1] == 24) nonWritable.Add(ins.Operands[0]);
                (decor.TryGetValue(ins.Operands[0], out var l) ? l : decor[ins.Operands[0]] = []).Add(string.Join(',', ins.Operands[1..]));
            }
        }
        string Decor(uint id) => decor.TryGetValue(id, out var l) ? string.Join(";", l.OrderBy(x => x, StringComparer.Ordinal)) : "";
        uint RootVar(uint id)
        {
            for (int g = 0; g < 64 && def.TryGetValue(id, out var d); g++)
            {
                if (d.Opcode is OpAccessChain or OpInBoundsAccessChain or OpPtrAccessChain or OpInBoundsPtrAccessChain or OpCopyObject) id = d.Operands[2];
                else break;
            }
            return id;
        }
        bool ReadOnlyLoad(SpvInstr ins)
        {
            if (!def.TryGetValue(RootVar(ins.Operands[2]), out var v) || v.Opcode != OpVariable || v.Operands.Length < 3) return false;
            uint sc = v.Operands[2];
            if (sc is 0 or 1 or 9) return true;
            if (sc == 2 && def.TryGetValue(v.Operands[0], out var pt) && pt.Operands.Length >= 3 && !bufferBlocks.Contains(UnwrapArray(pt.Operands[2]))) return true;
            return nonWritable.Contains(v.Operands[1]);
        }
        // SPIR-V <=1.2 `buffer B{} bs[2]`: the pointer's pointee is an OpTypeArray/RuntimeArray of the
        // BufferBlock struct, so unwrap to the element type before testing the decoration.
        uint UnwrapArray(uint id)
        {
            for (int g = 0; g < 64 && def.TryGetValue(id, out var at) && at.Opcode is 28 or 29 && at.Operands.Length >= 2; g++)
                id = at.Operands[1];
            return id;
        }
        bool Combinator(SpvInstr ins) => AdceCombinators.Contains(ins.Opcode)
            || ins.Opcode == OpExtInst && glslSet != 0 && ins.Operands.Length >= 4 && ins.Operands[2] == glslSet && AdceGlslCombinators.Contains(ins.Operands[3]);
        bool Commutative(int op) => IsCommutativeBinary(op);

        var vn = new Dictionary<uint, uint>();
        var table = new Dictionary<string, uint>();
        uint next = 1;
        foreach (var ins in m.Instrs)
        {
            uint r = ins.ResultId;
            if (r == 0 || ins.Opcode is OpFunction or 55) continue;
            if (!Combinator(ins) || ins.Opcode is 86 or 100 or OpVariable) { vn[r] = next++; continue; }
            if (ins.Opcode == OpLoad)
            {
                if (def.TryGetValue(ins.Operands[0], out var lt) && lt.Opcode is 25 or 26 or 27) { vn[r] = next++; continue; }
                if (!ReadOnlyLoad(ins)) { vn[r] = next++; continue; }
            }
            if (ins.Opcode == OpCopyObject && Decor(r) == Decor(ins.Operands[2]) && vn.TryGetValue(ins.Operands[2], out uint cv)) { vn[r] = cv; continue; }
            if (ins.Opcode == 245 && ins.Operands.Length > 2 && Decor(r) == Decor(ins.Operands[2]) && vn.TryGetValue(ins.Operands[2], out uint pv))
            {
                bool same = true;
                for (int i = 4; i < ins.Operands.Length; i += 2) if (vn.GetValueOrDefault(ins.Operands[i]) != pv) { same = false; break; }
                if (same) { vn[r] = pv; continue; }
            }
            ins.Classify(out int ri, out bool[] isId);
            var ops = new List<uint>();
            for (int i = 0; i < ins.Operands.Length; i++)
            {
                if (i == ri || i == ri - 1) continue;   // result id, result type
                uint w = ins.Operands[i];
                ops.Add(isId[i] && vn.TryGetValue(w, out uint ov) ? (1u << 31) | ov : w);
            }
            if (Commutative(ins.Opcode) && ops.Count == 2 && ops[0] > ops[1]) (ops[0], ops[1]) = (ops[1], ops[0]);
            string key = $"{ins.Opcode}|{(ri >= 1 ? ins.Operands[ri - 1] : 0)}|{string.Join(',', ops)}|{Decor(r)}";
            if (table.TryGetValue(key, out uint existing)) vn[r] = existing;
            else { vn[r] = next; table[key] = next++; }
        }
        return vn;
    }
}
