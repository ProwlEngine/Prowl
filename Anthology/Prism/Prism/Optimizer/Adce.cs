namespace Prowl.Prism.Optimizer;

public static partial class SpirVOptimizer
{
    // spirv-opt's AggressiveDCEPass, function level: an instruction is live only if it has an effect outside the
    // function (a store to non-local memory, a call, a return, ...) or feeds one. Liveness also flows through
    // control dependence: a live instruction keeps its block, the branch of the construct around it and the
    // breaks/continues of that construct. A structured if or loop with nothing live inside is cut down to a
    // branch to its merge, and the blocks that become unreachable are removed (MemPass::CFGCleanup).
    // Global values are left to DeadCodeElim's module-level sweep.
    [ThreadStatic] static UserIndex? s_adceUsers;
    static void AdceFunctions(SpvModule m)
    {
        uint glslSet = 0;
        foreach (var ins in m.Instrs) if (ins.Opcode == 11 && ins.Operands.Length >= 2 && DecodeSpvString(ins.Operands, 1) == "GLSL.std.450") { glslSet = ins.Operands[0]; break; }
        var entryPoints = new HashSet<uint>();
        foreach (var ins in m.Instrs) if (ins.Opcode == 15 && ins.Operands.Length >= 2) entryPoints.Add(ins.Operands[1]);
        // One def map for the whole sweep: a function only rewrites its own body, so after it changes just its
        // re-created labels need refreshing (stale entries for deleted locals are never looked up again).
        // A new OpUndef shifts the module, so that case rebuilds the map.
        var def = m.DefMap();
        foreach (uint fnId in ReachableFunctions(m))
        {
            var (fs, fe, undefs) = AdceFunction(m, fnId, glslSet, entryPoints, def);
            if (undefs) def = m.DefMap();
            else for (int i = fs; i < fe; i++) if (m.Instrs[i].Opcode == OpLabel) def[m.Instrs[i].Operands[0]] = m.Instrs[i];
        }
    }

    // spirv-opt IRContext combinators under the Shader capability: pure instructions ADCE may delete.
    static readonly HashSet<int> AdceCombinators =
    [
        0, 1, 43, 41, 42, 44, 45, 46, 19, 20, 21, 22, 23, 24, 25, 26, 27, 5341, 4472, 5281, 5313, 28, 29, 5076, 30, 31, 32,
        4417, 33, 34, 35, 36, 37, 38, 39, 59, 4418, 60, 61, 65, 66, 4419, 68, 77, 78, 79, 80, 81, 82, 400, 83, 84, 86, 87,
        88, 89, 90, 91, 92, 93, 94, 95, 96, 97, 98, 100, 101, 102, 103, 104, 106, 107, 109, 110, 111, 112, 113, 114, 115,
        116, 124, 126, 127, 128, 129, 130, 131, 132, 133, 134, 135, 136, 137, 138, 139, 140, 141, 142, 143, 144, 145, 146,
        147, 148, 149, 150, 151, 152, 154, 155, 156, 157, 164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175, 176,
        177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188, 189, 190, 191, 194, 195, 196, 197, 198, 199, 200, 201,
        202, 203, 204, 205, 245, 305, 306, 307, 308, 309, 310, 311, 312, 313, 314, 315, 316, 320, 321,
    ];

    // GLSL.std.450 combinators (everything except the pointer-writing Modf/Frexp and a few non-shader ops).
    static readonly HashSet<uint> AdceGlslCombinators =
    [
        1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30,
        31, 32, 33, 34, 36, 37, 38, 39, 40, 41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 52, 53, 54, 55, 56, 57, 58, 59, 60,
        61, 62, 63, 64, 65, 66, 67, 68, 69, 70, 71, 72, 73, 74, 75, 76, 77, 78, 79, 80, 81,
    ];

    // Returns the function's (rebuilt) instruction range and whether module-level undefs were created.
    static (int Fs, int Fe, bool Undefs) AdceFunction(SpvModule m, uint fnId, uint glslSet, HashSet<uint> entryPoints, DefTable def)
    {
        int fs = m.FindFunction(fnId);
        if (fs < 0) return (0, 0, false);
        int fe = fs; while (m.Instrs[fe].Opcode != OpFunctionEnd) fe++;
        var blocks = ParseBlocks(m, fs, fe, out int firstLabel);
        if (blocks.Count == 0) return (0, 0, false);
        // Only this function's users are ever consulted (every lookup is filtered to the function).
        var users = s_adceUsers ??= new(); users.Build(m.Instrs, fs, fe, m.Header[3]);
        var byLabel = new Dictionary<uint, Blk>(blocks.Count);
        foreach (var b in blocks) byLabel[b.Label] = b;

        // The function's own instructions carry a stamp (in the function / live) and their block in Tag, so the
        // liveness walk below is field reads, not set and map lookups.
        int inFn = SpvInstr.NewStamp(), isLive = SpvInstr.NewStamp();
        for (int i = fs; i < firstLabel; i++) { m.Instrs[i].Mark = inFn; m.Instrs[i].Tag = null; }
        bool hasCalls = false;
        {
            Blk? cur = null;
            for (int i = firstLabel; i < fe; i++)
            {
                var ins = m.Instrs[i];
                if (ins.Opcode == OpLabel) { cur = byLabel[ins.Operands[0]]; cur.LabelInst = ins; cur.Containing = null; }
                else if (ins.Opcode == OpFunctionCall) hasCalls = true;
                ins.Mark = inFn; ins.Tag = cur;
            }
        }
        bool entryNoCalls = entryPoints.Contains(fnId) && !hasCalls;
        static Blk? BlockOf(SpvInstr ins) => ins.Tag as Blk;

        // StructuredCFGAnalysis: each block's innermost containing construct (a header is in the outer one, a
        // merge block back in the construct around its header).
        var order = StructuredOrder(blocks);
        {
            var state = new List<(Blk? Construct, uint Merge)> { (null, 0) };
            foreach (var b in order)
            {
                if (b.Label == state[^1].Merge) state.RemoveAt(state.Count - 1);
                b.Containing = state[^1].Construct;
                if (b.Merge is { } mg) state.Add((b, mg.Operands[0]));
            }
        }
        static bool IsLoopHeader(Blk b) => b.Merge is { Opcode: 246 };
        static Blk? HeaderBlock(Blk? b) => b is null ? null : IsLoopHeader(b) ? b : b.Containing;
        static SpvInstr? HeaderBranch(Blk? b) => HeaderBlock(b)?.Term;
        static bool InConstruct(Blk header, Blk b)
        {
            for (Blk? cur = b; cur is not null; cur = cur.Containing)
                if (cur == header) return true;
            return false;
        }

        uint RootVar(uint ptr)
        {
            for (int g = 0; g < 64 && def.TryGetValue(ptr, out var d); g++)
            {
                if (d.Opcode is OpAccessChain or OpInBoundsAccessChain or OpPtrAccessChain or OpInBoundsPtrAccessChain or OpCopyObject) ptr = d.Operands[2];
                else break;
            }
            return ptr;
        }
        bool IsLocalVar(uint v)
        {
            if (!def.TryGetValue(v, out var d) || d.Opcode != OpVariable || d.Operands.Length < 3) return false;
            if (d.Operands[2] == 7) return true;                      // Function
            return d.Operands[2] is 6 or 4 && entryNoCalls;            // Private / Workgroup: one instance per entry point
        }
        bool IsPointer(uint id) => def.TryGetValue(id, out var d) && d.ResultId == id && TypeOfResult(d) is uint t && def.TryGetValue(t, out var td) && td.Opcode == 32;
        static uint? TypeOfResult(SpvInstr d) { d.Classify(out int ri, out _); return ri >= 1 ? d.Operands[ri - 1] : null; }

        bool SafeToDelete(SpvInstr ins)
        {
            if (AdceCombinators.Contains(ins.Opcode)) return true;
            if (ins.Opcode == OpExtInst && glslSet != 0 && ins.Operands.Length >= 4 && ins.Operands[2] == glslSet && AdceGlslCombinators.Contains(ins.Operands[3])) return true;
            return ins.Opcode is >= 207 and <= 215 or 105;   // derivatives, OpImageQueryLod
        }
        var work = new Queue<SpvInstr>();
        void Add(SpvInstr? ins) { if (ins is not null && ins.Mark == inFn) { ins.Mark = isLive; work.Enqueue(ins); } }
        bool InFunction(SpvInstr ins) => ins.Mark == inFn || ins.Mark == isLive;

        var liveLocals = new HashSet<uint>();
        void AddStores(uint ptr)
        {
            var us = users[ptr];
            foreach (var u in us)
            {
                if (!InFunction(u)) continue;
                switch (u.Opcode)
                {
                    case OpAccessChain or OpInBoundsAccessChain or OpCopyObject: AddStores(u.ResultId); break;
                    case OpLoad: break;
                    case OpCopyMemory or OpCopyMemorySized: if (u.Operands[0] == ptr) Add(u); break;
                    case OpStore: if (u.Operands[0] == ptr) Add(u); break;
                    default: Add(u); break;
                }
            }
        }
        void ProcessLoad(uint v) { if (v != 0 && IsLocalVar(v) && liveLocals.Add(v)) AddStores(v); }

        void AddBreaksAndContinues(SpvInstr mergeInst)
        {
            var header = BlockOf(mergeInst)!;
            uint mergeId = mergeInst.Operands[0];
            foreach (var u in users[mergeId])
            {
                if (u.Opcode is not (249 or 250 or 251) || !InFunction(u) || BlockOf(u) is not { } ub) continue;
                if (InConstruct(header, ub)) { Add(u); Add(ub.Merge); }
            }
            if (mergeInst.Opcode != 246) return;
            uint contId = mergeInst.Operands[1];
            foreach (var u in users[contId])
            {
                if (!InFunction(u) || BlockOf(u) is not { } ub) continue;
                if (u.Opcode is 250 or 251)
                {
                    if (ub.Merge is { Opcode: 247 } hm)
                    {
                        if (hm.Operands[0] == contId) continue;
                        Add(hm);
                    }
                }
                else if (u.Opcode == 249)
                {
                    var hb = HeaderBranch(ub);
                    if (hb is null) continue;
                    var hm = BlockOf(hb)!.Merge;
                    if (hm is null || hm.Opcode == 246 || hm.Operands[0] == contId) continue;
                }
                else continue;
                Add(u);
            }
        }

        void MarkBlockLive(SpvInstr ins)
        {
            if (BlockOf(ins) is not { } b) return;
            // Everything but the merge-instruction case depends only on the block (and on whether this is its
            // label), so it is done once per block for the label and once for the other instructions.
            bool isLabel = ins.Opcode == OpLabel;
            if (isLabel ? b.LiveA == isLive : b.LiveB == isLive) { if (ins.Opcode is 246 or 247) AddBreaksAndContinues(ins); return; }
            if (isLabel) b.LiveA = isLive; else b.LiveB = isLive;
            Add(b.LabelInst);
            if (b.Merge is { } mg) Add(byLabel.TryGetValue(mg.Operands[0], out var mb) ? mb.LabelInst : null); else Add(b.Term);
            if (ins.Opcode != OpLabel && IsLoopHeader(b)) { Add(b.Term); Add(b.Merge); }
            // GetBranchForNextHeader: the branch of the header whose construct holds this block.
            var nb = IsLoopHeader(b) ? b.Containing : b;
            var next = HeaderBranch(nb);
            if (next is not null) { Add(next); Add(BlockOf(next)!.Merge); }
            if (ins.Opcode is 246 or 247) AddBreaksAndContinues(ins);
        }

        // Initial live set: the function itself, its parameters, the entry block, and every instruction with
        // an effect outside the function.
        for (int i = fs; i < firstLabel; i++) Add(m.Instrs[i]);
        MarkBlockLive(blocks[0].LabelInst!);
        foreach (var b in order)
            foreach (var ins in b.Insts)
            {
                if (ins.Opcode is 249 or 250 or 251) continue;
                switch (ins.Opcode)
                {
                    case OpStore: if (!IsLocalVar(RootVar(ins.Operands[0]))) Add(ins); break;
                    case OpCopyMemory or OpCopyMemorySized: if (!IsLocalVar(RootVar(ins.Operands[0]))) Add(ins); break;
                    case 246 or 247 or 255: break;
                    default: if (!SafeToDelete(ins)) Add(ins); break;
                }
            }
        while (work.Count > 0)
        {
            var ins = work.Dequeue();
            ins.Classify(out int ri, out bool[] isId);
            for (int i = 0; i < ins.Operands.Length; i++)
                if (isId[i] && i != ri && def.TryGetValue(ins.Operands[i], out var d)) Add(d);
            MarkBlockLive(ins);
            switch (ins.Opcode)
            {
                case OpLoad or 60: ProcessLoad(RootVar(ins.Operands[2])); break;                         // OpLoad, OpImageTexelPointer
                case OpCopyMemory or OpCopyMemorySized: ProcessLoad(RootVar(ins.Operands[1])); break;
                case OpExtInst when ins.Operands.Length >= 5 && ins.Operands[2] == glslSet && ins.Operands[3] is 76 or 77 or 78:
                    ProcessLoad(RootVar(ins.Operands[4])); break;                                         // InterpolateAt*
                case 227 or 229 or 230 or 231 or 232 or 233 or 234 or 235 or 236 or 237 or 238 or 239 or 240 or 241 or 242 or 5614 or 5615 or 6035:
                    ProcessLoad(RootVar(ins.Operands[2])); break;                                         // atomics that read
                case OpFunctionCall:
                    for (int i = 3; i < ins.Operands.Length; i++) if (IsPointer(ins.Operands[i])) ProcessLoad(RootVar(ins.Operands[i]));
                    break;
            }
        }
        // KillDeadInstructions over the structured order.
        bool modified = false;
        uint retType = m.Instrs[fs].Operands[0];
        bool voidRet = def.TryGetValue(retType, out var rtd) && rtd.Opcode == 19;
        var pendingReturnUndef = new List<SpvInstr>();
        for (int bi = 0; bi < order.Count;)
        {
            var b = order[bi];
            uint mergeBlockId = 0;
            int before = b.Insts.Count;
            foreach (var ins in b.Insts) if (ins.Mark != isLive && ins.Opcode is 246 or 247) mergeBlockId = ins.Operands[0];
            bool termLive = b.Term.Mark == isLive;
            b.Insts.RemoveAll(ins => ins.Mark != isLive);
            if (b.Insts.Count != before) modified = true;
            if (mergeBlockId != 0)
            {
                b.Insts.Add(new SpvInstr(249, [mergeBlockId]));
                for (bi++; order[bi].Label != mergeBlockId; bi++) { }
                var mb = order[bi];
                if (mb.Term.Opcode == 255)
                {
                    // The merge was unreachable: undefined behaviour, so return (an undef) there instead.
                    var ret = voidRet ? new SpvInstr(253, []) : new SpvInstr(254, [0]);
                    if (!voidRet) pendingReturnUndef.Add(ret);
                    mb.Insts[^1] = ret;
                    ret.Mark = isLive;
                }
            }
            else
            {
                if (!termLive) b.Insts.Add(new SpvInstr(255, []));   // nothing live: the block is unreachable
                bi++;
            }
        }
        // MemPass::CFGCleanup: drop blocks no longer reachable (through successors, merges or continues), and the
        // phi operands that came from them; an argument defined in a removed block becomes undef.
        int reachStamp = SpvInstr.NewStamp(), reachCount = 0;
        bool Reached(uint label) => byLabel.TryGetValue(label, out var rb) && rb.Reach == reachStamp;
        {
            var q = new Queue<Blk>();
            void Visit(uint label) { var vb = byLabel[label]; if (vb.Reach != reachStamp) { vb.Reach = reachStamp; reachCount++; q.Enqueue(vb); } }
            Visit(blocks[0].Label);
            while (q.Count > 0)
            {
                var b = q.Dequeue();
                var t = b.Term;
                switch (t.Opcode)
                {
                    case 249: if (byLabel.ContainsKey(t.Operands[0])) Visit(t.Operands[0]); break;
                    case 250: if (byLabel.ContainsKey(t.Operands[1])) Visit(t.Operands[1]); if (byLabel.ContainsKey(t.Operands[2])) Visit(t.Operands[2]); break;
                    case 251:
                        if (byLabel.ContainsKey(t.Operands[1])) Visit(t.Operands[1]);
                        for (int k = 3; k < t.Operands.Length; k += 2) if (byLabel.ContainsKey(t.Operands[k])) Visit(t.Operands[k]);
                        break;
                }
                if (b.Merge is { } mg) { Visit(mg.Operands[0]); if (mg.Opcode == 246) Visit(mg.Operands[1]); }
            }
        }
        if (reachCount != blocks.Count) modified = true;
        if (!modified) return (0, 0, false);
        var undefFixups = new List<(SpvInstr Phi, int Index, uint Type)>();
        foreach (var b in blocks)
        {
            if (!Reached(b.Label)) continue;
            foreach (var phi in b.Insts.TakeWhile(x => x.Opcode == 245))
            {
                var ops = new List<uint> { phi.Operands[0], phi.Operands[1] };
                for (int i = 2; i + 1 < phi.Operands.Length; i += 2)
                {
                    if (!Reached(phi.Operands[i + 1])) continue;
                    // A value defined in a dropped block becomes undef.
                    if (def.TryGetValue(phi.Operands[i], out var dv) && dv.Mark == isLive && dv.Tag is Blk db && !Reached(db.Label)) undefFixups.Add((phi, ops.Count, phi.Operands[0]));
                    ops.Add(phi.Operands[i]); ops.Add(phi.Operands[i + 1]);
                }
                if (ops.Count != phi.Operands.Length) phi.Operands = [.. ops];
            }
        }
        blocks.RemoveAll(b => !Reached(b.Label));
        int oldCount = m.Instrs.Count;
        RebuildFunction(m, fs, fe, firstLabel, blocks);
        int newFe = fe + (m.Instrs.Count - oldCount);

        // Undefs last: creating one inserts a global before the first function, shifting indices.
        foreach (var ret in pendingReturnUndef) ret.Operands = [GetDbeUndef(m, retType)];
        foreach (var (phi, index, type) in undefFixups) { var ops = phi.Operands; ops[index] = GetDbeUndef(m, type); phi.Operands = ops; }
        return (fs, newFe, pendingReturnUndef.Count > 0 || undefFixups.Count > 0);
    }
}
