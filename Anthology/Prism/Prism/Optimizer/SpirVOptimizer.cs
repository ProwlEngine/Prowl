using System.Collections.Generic;
using System.Linq;
using Prowl.Prism.Glsl;

namespace Prowl.Prism.Optimizer;

/// <summary>
/// Post-lowering optimizer. Takes the straight-lowered SPIR-V word stream and returns an optimized one,
/// targeting byte-for-byte parity with spirv-opt's Performance pipeline (the level shaderc runs at
/// <c>OptimizationLevel.Performance</c>).
///
/// Phase 2 adds passes here one at a time, in spirv-opt's order. Until the pipeline reproduces every
/// transformation for a given module, its optimized parity test stays red and marks what is left to do.
///
/// The full, verified feature-gap list (front-end AND optimizer) lives in /GAPS.md at the repo root. Passes
/// present here: StripDebug, DeadBranchElim, MergeReturn, Inline(=InlineExhaustive, O(n)), EliminateDeadFunctions,
///   PrivateToLocal, LocalSingleBlockElim, LocalSingleStoreElim, ScalarReplacement, LocalAccessChainConvert,
///   SsaRewrite(=LocalMultiStoreElim), GlobalCse(=RedundancyElimination), RedundantLoadElim, BlockMerge,
///   RecomputeBound. ReciprocalFDiv and FoldInsertWithConstants are folding rules inside Simplification/Ccp.
/// The dominant remaining blocker is NOT missing capability -- it is byte-parity: matching spirv-opt's exact
/// scratch/throwaway id-allocation and fold ORDER. Nearly every remaining red is functionally correct + valid.
/// </summary>
public static partial class SpirVOptimizer
{
    const int OpString = 7, OpExtInst = 12, OpFunction = 54, OpFunctionCall = 57, OpVariable = 59,
        OpLoad = 61, OpStore = 62, OpCopyMemory = 63, OpCopyMemorySized = 64, OpAccessChain = 65,
        OpInBoundsAccessChain = 66, OpPtrAccessChain = 67, OpInBoundsPtrAccessChain = 70, OpCopyObject = 83,
        OpImageWrite = 99, OpControlBarrier = 224, OpMemoryBarrier = 225, OpLabel = 248;

    const int OpFunctionEnd = 56, OpTypePointerSc = 7;   // StorageClass Function == 7

    /// <summary>Runs the optimization pipeline over a lowered module's words.</summary>
    // The Performance pipeline, in spirv-opt's order. Kept as a named list so it can be timed per pass
    // (set PRISM_TRACE_TIME) without changing the sequence.
    static readonly (string Name, Action<SpvModule> Pass)[] Pipeline =
    {
        ("StripDebug", StripDebug), ("DeadBranchElim", DeadBranchElim), ("MergeReturn", MergeReturn),
        ("Inline", Inline), ("EliminateDeadFunctions", EliminateDeadFunctions), ("DeadCodeElim", DeadCodeElim),
        ("PrivateToLocal", PrivateToLocal), ("LocalSingleBlockElim", LocalSingleBlockElim),
        ("LocalSingleStoreElim", LocalSingleStoreElim), ("DeadCodeElim", DeadCodeElim),
        ("ScalarReplacement", ScalarReplacement), ("LocalAccessChainConvert", LocalAccessChainConvert),
        ("LocalSingleBlockElim", LocalSingleBlockElim), ("LocalSingleStoreElim", LocalSingleStoreElim),
        ("DeadCodeElim", DeadCodeElim), ("SsaRewrite", SsaRewrite), ("DeadCodeElim", DeadCodeElim),
        ("Ccp", Ccp), ("DeadCodeElim", DeadCodeElim), ("LoopUnroll", LoopUnroll), ("DeadBranchElim", DeadBranchElim),
        ("GlobalCse", GlobalCse), ("CombineAccessChains", CombineAccessChains), ("Simplification", Simplification),
        ("ScalarReplacement", ScalarReplacement), ("LocalAccessChainConvert", LocalAccessChainConvert),
        ("LocalSingleBlockElim", LocalSingleBlockElim), ("LocalSingleStoreElim", LocalSingleStoreElim),
        ("DeadCodeElim", DeadCodeElim), ("SsaRewrite", SsaRewrite), ("DeadCodeElim", DeadCodeElim),
        ("VectorDce", VectorDce),
        ("DeadBranchElim", DeadBranchElim), ("Simplification", Simplification), ("IfConversion", IfConversion),
        ("ReduceLoadSize", ReduceLoadSize),
        ("DeadCodeElim", DeadCodeElim), ("BlockMerge", BlockMerge), ("GlobalCse", GlobalCse),
        ("DeadBranchElim", DeadBranchElim), ("BlockMerge", BlockMerge), ("Simplification", Simplification),
        ("RecomputeBound", RecomputeBound),
    };

    // Per-pass time + allocation accumulators for warm profiling (enabled by PRISM_TRACE_TIME); keyed by name.
    // Concurrent compiles all write here, so it is a concurrent map and updated atomically -- a plain
    // Dictionary can corrupt its buckets or spin on resize when two threads profile at once.
    public static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (double Ms, long Bytes, int N)> PassAccum = new();

    static void Accum(string name, double ms, long bytes) =>
        PassAccum.AddOrUpdate(name, (ms, bytes, 1), (_, c) => (c.Ms + ms, c.Bytes + bytes, c.N + 1));

    public static uint[] Optimize(uint[] words)
    {
        Validate(words);
        bool trace = Environment.GetEnvironmentVariable("PRISM_TRACE_TIME") is not null;
        long pb0 = trace ? GC.GetAllocatedBytesForCurrentThread() : 0; var psw = trace ? System.Diagnostics.Stopwatch.StartNew() : null;
        var m = SpvModule.Parse(words);
        if (trace) Accum("(bridge) Parse", psw!.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - pb0);
        try
        {
            if (!trace)
                foreach (var (_, pass) in Pipeline) pass(m);
            else
                foreach (var (name, pass) in Pipeline)
                {
                    long b0 = GC.GetAllocatedBytesForCurrentThread();
                    var sw = System.Diagnostics.Stopwatch.StartNew();
                    pass(m);
                    Accum(name, sw.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - b0);
                }
        }
        catch (Exception e) when (e is not GlslParseException)
        {
            // The module passed structural validation but broke a pass's semantic assumptions - a zeroed
            // operand used as an id, say. Public contract: garbage in -> a clean GlslParseException, never
            // a raw NRE/KeyNotFound escaping to the host. Compiler output never lands here (the whole corpus
            // runs through this pipeline under byte-parity verification).
            throw new GlslParseException($"optimizer: module rejected - structurally valid but semantically inconsistent ({e.GetType().Name}: {e.Message}); input may be corrupt");
        }
        if (!trace) return m.Serialize();
        long sb0 = GC.GetAllocatedBytesForCurrentThread(); var ssw = System.Diagnostics.Stopwatch.StartNew();
        var outWords = m.Serialize();
        Accum("(bridge) Serialize", ssw.Elapsed.TotalMilliseconds, GC.GetAllocatedBytesForCurrentThread() - sb0);
        return outWords;
    }

    // Structural gate for the public entry point: the pipeline assumes a well-formed word stream
    // (instruction word counts, a sane header), so reject garbage up front with a precise message
    // instead of letting passes trip over it. SpvModule.Parse itself stays lenient - after this gate
    // it only ever sees a stream the validator accepted. Also public API: hosts that accept untrusted
    // modules can run the same cheap gate before handing words to anything else.
    public static void Validate(uint[] words)
    {
        if (words.Length < 5) throw new GlslParseException($"optimizer: module too short ({words.Length} words)");
        if (words[0] != 0x07230203) throw new GlslParseException($"optimizer: bad magic 0x{words[0]:X8}");
        uint major = (words[1] >> 16) & 0xFF;
        if (major is 0 or > 7) throw new GlslParseException($"optimizer: implausible SPIR-V version {major}");
        if (words[3] == 0) throw new GlslParseException("optimizer: id bound is zero");
        if (words[4] != 0) throw new GlslParseException($"optimizer: reserved schema word is {words[4]}, must be 0");
        int p = 5;
        while (p < words.Length)
        {
            int count = (int)(words[p] >> 16);
            if (count == 0) throw new GlslParseException($"optimizer: zero-length instruction at word {p}");
            if (p + count > words.Length) throw new GlslParseException($"optimizer: instruction at word {p} overruns the module ({count} words, {words.Length - p} left)");
            p += count;
        }
    }

    // spirv-opt private_to_local_pass: a Private variable whose every use is a load/store/access-chain in a
    // single function is moved into that function as a Function-storage local, so the mem2reg passes can then
    // promote it away. The Function pointer type is reused if it exists, else created; access-chain result
    // types rooted at the variable are re-pointed to Function storage.
    static void PrivateToLocal(SpvModule m)
    {
        var def = m.DefMap();
        var users = m.UserMap();
        uint nextId = m.Header[3];
        var newTypes = new List<SpvInstr>();
        var fnPtrCache = new Dictionary<uint, uint>();
        foreach (var ins in m.Instrs)
            if (ins.Opcode == OpTypePointer && ins.Operands.Length >= 3 && ins.Operands[1] == 7) fnPtrCache[ins.Operands[2]] = ins.Operands[0];

        uint FnPtr(uint oldPtrType)
        {
            uint pointee = def[oldPtrType].Operands[2];
            if (fnPtrCache.TryGetValue(pointee, out var id)) return id;
            id = nextId++;
            newTypes.Add(new SpvInstr(OpTypePointer, [id, 7, pointee]));
            fnPtrCache[pointee] = id;
            return id;
        }

        // Every instruction inside a function maps to that function's entry OpLabel.
        // (Stamped, with the entry label in Tag.)
        int hasEntry = SpvInstr.NewStamp();
        SpvInstr? entry = null; bool inFn = false;
        foreach (var ins in m.Instrs)
        {
            if (ins.Opcode == OpFunction) { inFn = true; entry = null; }
            if (ins.Opcode == OpLabel && inFn && entry is null) entry = ins;
            if (inFn && entry is not null) { ins.Mark = hasEntry; ins.Tag = entry; }
            if (ins.Opcode == OpFunctionEnd) inFn = false;
        }

        bool ValidUse(SpvInstr u, uint ptrId)
        {
            switch (u.Opcode)
            {
                case OpLoad: return true;
                case OpStore: return u.Operands.Length >= 2 && u.Operands[1] != ptrId;
                case OpAccessChain: case OpInBoundsAccessChain:
                    if (u.Operands.Length < 3 || u.Operands[2] != ptrId) return false;
                    if (!users.TryGetValue(u.Operands[1], out var uu)) return true;
                    foreach (var x in uu) if (!ValidUse(x, u.Operands[1])) return false;
                    return true;
                case 71: case 72: case 332: case 15: return true;   // decorations, entry-point interface list
                default: return false;
            }
        }

        var toMove = new List<(SpvInstr Var, SpvInstr Entry)>();
        foreach (var v in m.Instrs)
        {
            if (v.Opcode != OpVariable || v.Operands.Length < 3 || v.Operands[2] != 6) continue;   // Private only
            if (!users.TryGetValue(v.Operands[1], out var us)) continue;
            SpvInstr? target = null; bool ok = true;
            foreach (var u in us)
            {
                if (!ValidUse(u, v.Operands[1])) { ok = false; break; }
                if (u.Opcode is 71 or 72 or 332 or 15) continue;   // module-level uses (decorations, entry point) have no block
                if (u.Mark != hasEntry) { ok = false; break; }
                var e = (SpvInstr)u.Tag!;
                if (target is null) target = e; else if (target != e) { ok = false; break; }
            }
            if (ok && target is not null) toMove.Add((v, target));
        }
        if (toMove.Count == 0) return;

        void UpdateChain(uint ptrId)
        {
            if (!users.TryGetValue(ptrId, out var us)) return;
            foreach (var u in us)
                if (u.Opcode is OpAccessChain or OpInBoundsAccessChain && u.Operands.Length >= 3 && u.Operands[2] == ptrId)
                {
                    uint nt = FnPtr(u.Operands[0]);
                    u.Operands = (uint[])u.Operands.Clone(); u.Operands[0] = nt;
                    UpdateChain(u.Operands[1]);
                }
        }
        foreach (var (v, _) in toMove)
        {
            uint nt = FnPtr(v.Operands[0]);
            v.Operands = (uint[])v.Operands.Clone(); v.Operands[0] = nt; v.Operands[2] = 7;
            UpdateChain(v.Operands[1]);
        }

        // SPIR-V 1.4+ lists all statically-used globals in the entry-point interface; a localized variable is
        // no longer module-scope, so drop it from that list (private_to_local_pass.cpp:65).
        if (m.Header[1] >= 0x00010400u)
        {
            var moved = new HashSet<uint>();
            foreach (var (v, _) in toMove) moved.Add(v.Operands[1]);
            foreach (var ins in m.Instrs)
                if (ins.Opcode == 15)
                {
                    SpvGrammar.Classify(15, ins.Operands, out int ri, out bool[] isId);
                    var kept = new List<uint>();
                    for (int i = 0; i < ins.Operands.Length; i++)
                        if (!(isId[i] && i != ri && moved.Contains(ins.Operands[i]))) kept.Add(ins.Operands[i]);
                    ins.Operands = kept.ToArray();
                }
        }

        // Relocate the moved vars in a single rebuild (was O(moved*N) via per-var IndexOf/Insert). Each var was
        // inserted right after its entry label, so successive inserts for one entry land in reverse order; prepend
        // to preserve that exact sequence. newTypes go just before the first function, as before.
        var afterEntry = new Dictionary<SpvInstr, List<SpvInstr>>();
        var movedSet = new HashSet<SpvInstr>();
        foreach (var (v, e) in toMove) { movedSet.Add(v); (afterEntry.TryGetValue(e, out var l) ? l : afterEntry[e] = []).Insert(0, v); }
        var relocated = SpvModule.RentInstrList(m.Instrs.Count + newTypes.Count);
        bool typesPlaced = false;
        foreach (var ins in m.Instrs)
        {
            if (movedSet.Contains(ins)) continue;
            if (!typesPlaced && newTypes.Count > 0 && ins.Opcode == OpFunction) { relocated.AddRange(newTypes); typesPlaced = true; }
            relocated.Add(ins);
            if (afterEntry.TryGetValue(ins, out var vs)) relocated.AddRange(vs);
        }
        m.SetInstrs(relocated);
        m.Header[3] = nextId;
    }

    // spirv-opt tightens the module's id bound to (highest id in use) + 1 after removing dead instructions.

    static void RecomputeBound(SpvModule m)
    {
        uint max = 0;
        foreach (var ins in m.Instrs)
        {
            ins.Classify(out _, out bool[] isId);
            for (int i = 0; i < ins.Operands.Length; i++)
                if (isId[i] && ins.Operands[i] > max) max = ins.Operands[i];
        }
        m.Header[3] = max + 1;
    }

    // Drops debug and naming instructions (OpName/OpMemberName/OpSource*/OpLine/OpNoLine/OpModuleProcessed),
    // which carry no semantics. OpString is kept when a surviving instruction still references it (e.g. a
    // NonSemantic debugPrintf format string). Ids/bound are untouched.
    static void StripDebug(SpvModule m)
    {
        var referenced = new HashSet<uint>();
        foreach (var ins in m.Instrs)
        {
            if (IsDebug(ins.Opcode)) continue;
            ins.Classify(out int ri, out bool[] isId);
            for (int i = 0; i < ins.Operands.Length; i++)
                if (isId[i] && i != ri) referenced.Add(ins.Operands[i]);
        }
        m.Instrs.RemoveAll(ins => IsDebug(ins.Opcode) && (ins.Opcode != OpString || !referenced.Contains(ins.ResultId)));
    }

    static bool IsDebug(int opcode) => opcode is
        2 or 3 or 4 or    // OpSourceContinued, OpSource, OpSourceExtension
        5 or 6 or         // OpName, OpMemberName
        7 or              // OpString (may be retained)
        8 or 317 or       // OpLine, OpNoLine
        330;              // OpModuleProcessed


    const int OpTypeFloat = 22, OpTypeVector = 23, OpConstant = 43, OpConstantComposite = 44,
        OpConstantNull = 46, OpDecorate = 71, OpCompositeConstruct = 80, OpCompositeExtract = 81,
        OpFDiv = 136, OpFMul = 133, OpFAdd = 129, OpDot = 148;

    static bool Cseable(int opcode) => IsPure(opcode) && opcode is not (OpLoad or OpVariable or 245 or 1 or 86 or 100);   // exclude OpPhi, OpUndef, OpSampledImage, OpImage (must stay in-block)

    // Dominator-tree value numbering: a pure instruction is redundant when an identical one (same opcode,
    // type and operands) already exists in a dominating block, so its result is rewritten to that earlier
    // one and it is removed. This is the cross-block form of spirv-opt's RedundancyElimination, restricted
    // to pure combinator instructions (loads/memory are handled separately). Ids are not renumbered.
    static void GlobalCse(SpvModule m)
    {
        var def = m.DefMap();
        var bufferBlocks = new HashSet<uint>();
        var nonWritable = new HashSet<uint>();
        foreach (var ins in m.Instrs)
        {
            if (ins.Opcode == OpDecorate && ins.Operands.Length >= 2 && ins.Operands[1] == 3) bufferBlocks.Add(ins.Operands[0]);   // Decoration BufferBlock
            if (ins.Opcode == OpDecorate && ins.Operands.Length >= 2 && ins.Operands[1] == 24) nonWritable.Add(ins.Operands[0]);   // Decoration NonWritable
        }

        uint RootVar(uint id)
        {
            for (int g = 0; g < 64 && def.TryGetValue(id, out var d); g++)
            {
                if (d.Opcode is OpAccessChain or OpInBoundsAccessChain or OpPtrAccessChain or OpInBoundsPtrAccessChain or OpCopyObject) id = d.Operands[2];
                else break;
            }
            return id;
        }
        // spirv-opt value-numbers a load only when it is read-only: non-opaque loaded type, from an
        // Input/PushConstant variable or a UBO (Uniform, not an SSBO/BufferBlock struct). Read-only memory
        // never changes, so such loads common across the whole dominator tree like pure instructions.
        bool ReadOnlyLoad(SpvInstr ins)
        {
            if (ins.Opcode != OpLoad || ins.Operands.Length != 3) return false;
            if (!def.TryGetValue(RootVar(ins.Operands[2]), out var v) || v.Opcode != OpVariable || v.Operands.Length < 3) return false;
            uint sc = v.Operands[2];
            def.TryGetValue(ins.Operands[0], out var t);
            // spirv-opt gives a load of an image, sampler, or sampled image its own value number so it never
            // commons -- such loads must stay in the basic block that uses them. Only other read-only loads number.
            if (t is not null && t.Opcode is 25 or 26 or 27) return false;
            if (sc is 0 or 1 or 9) return true;
            if (sc == 2 && def.TryGetValue(v.Operands[0], out var pt) && pt.Operands.Length >= 3 && !bufferBlocks.Contains(pt.Operands[2])) return true;
            return nonWritable.Contains(v.Operands[1]);   // IsReadOnlyPointerShaders: a NonWritable variable
        }
        // GLSL.std.450 extended instructions are pure combinators, so identical ones common like arithmetic.
        uint glslExt = 0;
        foreach (var ins in m.Instrs) if (ins.Opcode == 11 && ins.Operands.Length >= 1) { glslExt = ins.Operands[0]; break; }   // OpExtInstImport (only GLSL.std.450 is imported)
        bool PureExtInst(SpvInstr ins) => ins.Opcode == 12 && glslExt != 0 && ins.Operands.Length >= 4 && ins.Operands[2] == glslExt && AdceGlslCombinators.Contains(ins.Operands[3]);
        // Phis value-number by their (value, label) operands too; identical ones can only share a block.
        // ValueNumberTable: only combinators (IRContext's shader set) share value numbers -- derivatives,
        // for instance, are not combinators and always stay distinct.
        bool CanValueNumber(SpvInstr ins) => (Cseable(ins.Opcode) && AdceCombinators.Contains(ins.Opcode)) || ReadOnlyLoad(ins) || PureExtInst(ins) || ins.Opcode == 245;

        // ValueTableEqual also requires HaveTheSameDecorations: the decoration payloads (target dropped) as sets.
        var decoSig = new Dictionary<uint, SortedSet<string>>();
        foreach (var ins in m.Instrs)
        {
            if (ins.Opcode is not (OpDecorate or 72 or 332 or 5632) || ins.Operands.Length < 1) continue;
            var sig = ins.Opcode + ":" + string.Join(",", ins.Operands.Skip(1));
            (decoSig.TryGetValue(ins.Operands[0], out var set) ? set : decoSig[ins.Operands[0]] = new SortedSet<string>(System.StringComparer.Ordinal)).Add(sig);
        }

        var kill = new HashSet<SpvInstr>();
        int f = 0;
        while (f < m.Instrs.Count)
        {
            if (m.Instrs[f].Opcode != OpFunction) { f++; continue; }
            int end = f;
            while (end < m.Instrs.Count && m.Instrs[end].Opcode != OpFunctionEnd) end++;
            CseFunction(m, f, end, kill, CanValueNumber, decoSig);
            f = end + 1;
        }
        if (kill.Count > 0) m.Instrs.RemoveAll(kill.Contains);
    }

    // spvOpcodeIsCommutativeBinaryOperator.
    static bool IsCommutativeBinary(int op) => op is 401 or 402 or 128 or 129 or 132 or 133 or 148 or 149 or 151 or 152
        or 197 or 198 or 199 or 162 or 163 or 164 or 165 or 166 or 167 or 170 or 171 or 180 or 181 or 182 or 183;

    static void CseFunction(SpvModule m, int start, int end, HashSet<SpvInstr> kill, System.Func<SpvInstr, bool> canVN, Dictionary<uint, SortedSet<string>> decoSig)
    {
        // Blocks: each OpLabel begins one; the instruction range runs to its terminator.
        var labelOf = new List<uint>();
        var blockStart = new List<int>();   // index of the OpLabel
        var succs = new List<List<int>>();
        var idOfBlock = new Dictionary<uint, int>();
        for (int i = start; i <= end; i++)
        {
            if (m.Instrs[i].Opcode != OpLabel) continue;
            idOfBlock[m.Instrs[i].Operands.Length > 0 ? m.Instrs[i].Operands[0] : 0] = labelOf.Count;
            labelOf.Add(m.Instrs[i].Operands.Length > 0 ? m.Instrs[i].Operands[0] : 0);
            blockStart.Add(i);
            succs.Add([]);
        }
        if (labelOf.Count == 0) return;

        for (int b = 0; b < labelOf.Count; b++)
        {
            int s = blockStart[b] + 1;
            int e = b + 1 < labelOf.Count ? blockStart[b + 1] : end;
            for (int i = s; i < e; i++)
            {
                var t = m.Instrs[i];
                if (t.Opcode == 249 && t.Operands.Length >= 1) AddSucc(b, t.Operands[0]);                 // OpBranch
                else if (t.Opcode == 250 && t.Operands.Length >= 3) { AddSucc(b, t.Operands[1]); AddSucc(b, t.Operands[2]); }   // OpBranchConditional
                else if (t.Opcode == 251 && t.Operands.Length >= 2) { AddSucc(b, t.Operands[1]); for (int k = 3; k < t.Operands.Length; k += 2) AddSucc(b, t.Operands[k]); }   // OpSwitch
            }
        }
        void AddSucc(int b, uint label) { if (idOfBlock.TryGetValue(label, out int tb)) succs[b].Add(tb); }

        int[] idom = Dominators(labelOf.Count, succs);
        var children = new List<int>[labelOf.Count];
        for (int b = 0; b < labelOf.Count; b++) children[b] = [];
        for (int b = 1; b < labelOf.Count; b++) if (idom[b] >= 0) children[idom[b]].Add(b);

        // Function-scoped user map so common-subexpression replacement is O(uses), not a whole-module scan per
        // CSE (value-numbered results are function-local SSA, so no cross-function or decoration uses to touch).
        var users = m.UserMapRange(start, end + 1);
        void Replace(uint from, uint to)
        {
            if (!users.TryGetValue(from, out var us)) return;
            foreach (var u in us)
            {
                u.Classify(out int ri, out bool[] isId);
                for (int k = 0; k < u.Operands.Length; k++) if (isId[k] && k != ri && u.Operands[k] == from) u.Operands[k] = to;
                (users.TryGetValue(to, out var tl) ? tl : users[to] = []).Add(u);
            }
            us.Clear();
        }

        var table = new Dictionary<ValueKey, uint>();
        void Dfs(int b)
        {
            var added = new List<ValueKey>();
            int s = blockStart[b] + 1;
            int e = b + 1 < labelOf.Count ? blockStart[b + 1] : end;
            for (int i = s; i < e; i++)
            {
                var ins = m.Instrs[i];
                if (!canVN(ins)) continue;
                ins.Classify(out int ri, out _);
                if (ri < 0) continue;
                var key = new ValueKey(ins, ri, decoSig.GetValueOrDefault(ins.Operands[ri]));
                if (table.TryGetValue(key, out uint rep)) { Replace(ins.Operands[ri], rep); kill.Add(ins); }
                else { table[key] = ins.Operands[ri]; added.Add(key); }
            }
            foreach (int c in children[b]) Dfs(c);
            foreach (var key in added) table.Remove(key);
        }
        Dfs(0);
    }

    // A value-number key: opcode, the operands but the result (commutative pair in normal order) and the
    // result's decoration set, snapshotted when made -- later rewrites of the instruction do not move it.
    sealed class ValueKey : IEquatable<ValueKey>
    {
        readonly int _opcode, _hash;
        readonly uint[] _ops;
        readonly SortedSet<string>? _deco;

        public ValueKey(SpvInstr ins, int ri, SortedSet<string>? deco)
        {
            _opcode = ins.Opcode; _deco = deco;
            var o = ins.Operands;
            bool swap = IsCommutativeBinary(ins.Opcode) && ri == 1 && o.Length == 4 && o[2] > o[3];
            _ops = new uint[o.Length - 1];
            var h = new HashCode(); h.Add(_opcode);
            for (int k = 0, j = 0; k < o.Length; k++) if (k != ri) { uint w = swap && k >= 2 ? o[5 - k] : o[k]; _ops[j++] = w; h.Add(w); }
            _hash = h.ToHashCode();
        }

        public bool Equals(ValueKey? x) => x is not null && x._hash == _hash && x._opcode == _opcode && x._ops.AsSpan().SequenceEqual(_ops)
            && (x._deco is null ? _deco is null : _deco is not null && x._deco.SetEquals(_deco));
        public override bool Equals(object? obj) => Equals(obj as ValueKey);
        public override int GetHashCode() => _hash;
    }

    // spirv-opt's BlockMerge: a block P ending in an unconditional OpBranch to B, where B has exactly one
    // predecessor, absorbs B (P's branch and B's label are dropped, B's instructions append to P). Guards:
    // two merge/continue targets never merge; a loop header only absorbs its (non-merge) test block whose
    // terminator is a branch. Phi predecessor labels for B's successors are rewritten to P's label.
    static void BlockMerge(SpvModule m)
    {
        int f = 0;
        var newList = SpvModule.RentInstrList(m.Instrs.Count);
        while (f < m.Instrs.Count)
        {
            if (m.Instrs[f].Opcode != OpFunction) { newList.Add(m.Instrs[f]); f++; continue; }
            int end = f;
            while (end < m.Instrs.Count && m.Instrs[end].Opcode != OpFunctionEnd) end++;
            MergeFunc(m, f, end, newList);
            newList.Add(m.Instrs[end]);   // OpFunctionEnd
            f = end + 1;
        }
        m.SetInstrs(newList);
    }

    static void MergeFunc(SpvModule m, int start, int end, List<SpvInstr> outList)
    {
        var blockStart = new List<int>();
        var idOfBlock = new Dictionary<uint, int>();
        int firstLabel = -1;
        for (int i = start; i <= end; i++)
            if (m.Instrs[i].Opcode == OpLabel) { if (firstLabel < 0) firstLabel = i; idOfBlock[m.Instrs[i].Operands[0]] = blockStart.Count; blockStart.Add(i); }
        for (int i = start; i < (firstLabel < 0 ? end : firstLabel); i++) outList.Add(m.Instrs[i]);   // OpFunction + params
        int n = blockStart.Count;
        if (n == 0) return;
        int BlockEnd(int b) => b + 1 < n ? blockStart[b + 1] : end;
        uint LabelId(int b) => m.Instrs[blockStart[b]].Operands[0];

        // Merge/continue targets and headers.
        var mergeTargets = new HashSet<uint>();
        var continueTargets = new HashSet<uint>();
        var isHeader = new bool[n];
        var preds = new Dictionary<uint, int>();   // label -> pred count
        for (int b = 0; b < n; b++)
            for (int i = blockStart[b] + 1; i < BlockEnd(b); i++)
            {
                var t = m.Instrs[i];
                if (t.Opcode == 246) { mergeTargets.Add(t.Operands[0]); continueTargets.Add(t.Operands[1]); isHeader[b] = true; }   // OpLoopMerge
                else if (t.Opcode == 247) { mergeTargets.Add(t.Operands[0]); isHeader[b] = true; }                                   // OpSelectionMerge
            }
        void CountPred(uint lbl) { preds[lbl] = preds.GetValueOrDefault(lbl) + 1; }
        // A switch's case targets (other than its merge): such a block must stay structurally dominated by the
        // OpSwitch, so it never absorbs a merge/continue target (CanMergeWithSuccessor's switch rule).
        var caseTargets = new HashSet<uint>();
        for (int b = 0; b < n; b++)
        {
            var term = m.Instrs[BlockEnd(b) - 1];
            if (term.Opcode == 249) CountPred(term.Operands[0]);
            else if (term.Opcode == 250) { CountPred(term.Operands[1]); CountPred(term.Operands[2]); }
            else if (term.Opcode == 251)
            {
                CountPred(term.Operands[1]); for (int k = 3; k < term.Operands.Length; k += 2) CountPred(term.Operands[k]);
                uint switchMerge = 0;
                for (int i = blockStart[b] + 1; i < BlockEnd(b); i++) if (m.Instrs[i].Opcode == 247) switchMerge = m.Instrs[i].Operands[0];
                for (int k = 1; k < term.Operands.Length; k += 2) if (term.Operands[k] != switchMerge) caseTargets.Add(term.Operands[k]);
            }
        }

        // BlockMergePass: in layout order, each live block keeps absorbing its successor while
        // CanMergeWithSuccessor holds for the MERGED block -- it keeps the first block's label (so a merge block
        // stays a merge block), carries whichever merge instruction it currently holds, and ends with the last
        // absorbed block's terminator.
        var absorbs = new Dictionary<int, int>();   // P -> B
        var absorbedBy = new Dictionary<int, int>();
        var tail = new int[n];        // last block of each live chain
        var chainMerge = new uint[n]; // label named by the chain's merge instruction (0 = none)
        for (int b = 0; b < n; b++)
        {
            tail[b] = b;
            for (int i = blockStart[b] + 1; i < BlockEnd(b); i++) if (m.Instrs[i].Opcode is 246 or 247) { chainMerge[b] = m.Instrs[i].Operands[0]; break; }
        }
        var droppedMerges = new HashSet<uint>();   // headers whose construct vanished by absorbing their merge
        bool IsMergeTarget(uint l) => mergeTargets.Contains(l) && !droppedMerges.Contains(l);
        for (int b = 0; b < n; b++)
        {
            if (absorbedBy.ContainsKey(b)) continue;
            while (true)
            {
                var term = m.Instrs[BlockEnd(tail[b]) - 1];
                if (term.Opcode != 249) break;                          // must end in unconditional OpBranch
                if (!idOfBlock.TryGetValue(term.Operands[0], out int sb) || sb == b) break;
                uint succ = LabelId(sb);
                if (preds.GetValueOrDefault(succ) != 1) break;         // successor has exactly one predecessor
                if (absorbedBy.ContainsKey(sb)) break;
                bool predMerge = IsMergeTarget(LabelId(b));
                bool succMerge = IsMergeTarget(succ);
                bool succContinue = continueTargets.Contains(succ);
                if (predMerge && succMerge) break;                     // two merges cannot become one block
                if (predMerge && succContinue) break;                  // nor a merge and a continue target
                if (chainMerge[b] != 0 && succ != chainMerge[b])
                {
                    // A header only absorbs its own merge block, or a non-header successor ending in a branch.
                    if (chainMerge[sb] != 0) break;
                    var st = m.Instrs[BlockEnd(tail[sb]) - 1].Opcode;
                    if (st is not (249 or 250)) break;
                }
                if ((succMerge || succContinue) && caseTargets.Contains(LabelId(b))) break;
                // Merge: a header absorbing the very block its merge instruction names drops that instruction.
                absorbs[tail[b]] = sb; absorbedBy[sb] = tail[b];
                if (chainMerge[b] != 0 && succ == chainMerge[b]) { droppedMerges.Add(succ); chainMerge[b] = 0; }
                if (chainMerge[sb] != 0) chainMerge[b] = chainMerge[sb];
                tail[b] = tail[sb];
            }
        }

        if (absorbs.Count == 0) { for (int b = 0; b < n; b++) for (int i = blockStart[b]; i < BlockEnd(b); i++) outList.Add(m.Instrs[i]); return; }

        // Rewrite phi predecessor labels: an absorbed block's label becomes its merge-chain root's label.
        int Root(int b) { while (absorbedBy.TryGetValue(b, out int p)) b = p; return b; }
        // One pass over the module for all of them: every target is a chain root, which is never itself absorbed.
        var relabel = new Dictionary<uint, uint>();
        foreach (var (b, _) in absorbedBy) { uint from = LabelId(b), to = LabelId(Root(b)); if (from != to) relabel[from] = to; }
        m.ReplaceUses(relabel);

        // Emit a merge chain as one block: all body instructions in order, then the (relocated) structured
        // merge instruction, then the final terminator (a structured merge must immediately precede it).
        void EmitChain(int root)
        {
            var body = new List<SpvInstr>();
            SpvInstr? merge = null;
            int b = root; bool withLabel = true;
            while (true)
            {
                int s = withLabel ? blockStart[b] : blockStart[b] + 1, e = BlockEnd(b);
                for (int i = s; i < e - 1; i++)   // all but the terminator
                {
                    var ins = m.Instrs[i];
                    if (ins.Opcode is 246 or 247) merge = ins; else body.Add(ins);
                }
                if (absorbs.TryGetValue(b, out int sb)) { b = sb; withLabel = false; continue; }
                outList.AddRange(body);
                // When a header absorbed the very block its merge instruction named, the construct no longer
                // exists and the declaration would name its own block (invalid). Drop it, as spirv-opt does
                // in MergeWithSuccessor. The absorbed label was already rewritten to the chain root's.
                if (merge is not null && merge.Operands[0] == LabelId(root)) merge = null;
                if (merge is not null) outList.Add(merge);
                outList.Add(m.Instrs[e - 1]);   // terminator
                return;
            }
        }
        for (int b = 0; b < n; b++) if (!absorbedBy.ContainsKey(b)) EmitChain(b);
    }

    const int OpFunctionParameter = 55, OpReturn = 253, OpReturnValue = 254, OpTypeVoid = 19, OpTypePointer = 32;

    // spirv-opt's DeadBranchElim: folds constant-condition OpBranchConditional / constant-selector OpSwitch to
    // an unconditional branch, drops the blocks that become unreachable, fixes the phis in the survivors, and
    // reorders every reachable function's blocks into structured order. Runs before MergeReturn (its pipeline
    // position). A no-op on shaders with no constant branches and no dead blocks, so it cannot touch a green
    // shader (which by definition has neither, else spirv-opt would already diverge from us).
    static void DeadBranchElim(SpvModule m)
    {
        s_dbeUndefs?.Clear();
        if (m.Instrs.Any(i => i.Opcode == 73)) return;   // OpGroupDecorate: KillNamesAndDecorates unsupported
        var reach = new HashSet<uint>(ReachableFunctions(m));
        // Single forward pass over the module: find each function's range locally and process it in place
        // (was an O(functions) whole-module re-scan inside each of DbeFunction/DbeReorder).
        bool modified = false;
        // One def map for the pass: a function only consults global ids (constants, undefs) and its own locals,
        // which no other function touches. Undefs a function creates become visible from the next function on,
        // as when each function rebuilt the map at its start.
        var def = m.DefMap();
        s_undefSink = [];
        for (int i = 0; i < m.Instrs.Count; i++)
        {
            if (m.Instrs[i].Opcode != OpFunction) continue;
            if (reach.Contains(m.Instrs[i].Operands[1]))
            {
                modified |= DbeFunction(m, i, def);
                foreach (var u in s_undefSink) def[u.ResultId] = u;
                s_undefSink.Clear();
            }
            while (i < m.Instrs.Count && m.Instrs[i].Opcode != OpFunctionEnd) i++;
        }
        s_undefSink = null;
        if (modified)
            for (int i = 0; i < m.Instrs.Count; i++)
            {
                if (m.Instrs[i].Opcode != OpFunction) continue;
                if (reach.Contains(m.Instrs[i].Operands[1])) DbeReorder(m, i);
                while (i < m.Instrs.Count && m.Instrs[i].Opcode != OpFunctionEnd) i++;
            }
    }

    static List<uint> ReachableFunctions(SpvModule m)
    {
        var ranges = new Dictionary<uint, (int, int)>();
        for (int i = 0; i < m.Instrs.Count; i++)
            if (m.Instrs[i].Opcode == OpFunction) { int e = i; while (e < m.Instrs.Count && m.Instrs[e].Opcode != OpFunctionEnd) e++; ranges[m.Instrs[i].Operands[1]] = (i, e); }
        var order = new List<uint>(); var seen = new HashSet<uint>(); var q = new Queue<uint>();
        foreach (var ins in m.Instrs) if (ins.Opcode == 15 && ins.Operands.Length >= 2 && seen.Add(ins.Operands[1])) q.Enqueue(ins.Operands[1]);
        while (q.Count > 0)
        {
            uint fn = q.Dequeue(); order.Add(fn);
            if (ranges.TryGetValue(fn, out var r)) for (int i = r.Item1; i < r.Item2; i++) if (m.Instrs[i].Opcode == OpFunctionCall && seen.Add(m.Instrs[i].Operands[2])) q.Enqueue(m.Instrs[i].Operands[2]);
        }
        return order;
    }

    // spirv-opt combine_access_chains.cpp: flatten a nested access chain
    // AccessChain(AccessChain(base, i..), j..) -> AccessChain(base, i.., j..). Processed in RPO so a
    // dominator chain is flattened before the chains that feed off it. Faithful port; the plain (non-Ptr,
    // non-64-bit, no-ArrayStride) case only rewrites operands in place -- no new ids. PtrAccessChain index
    // combining (which would allocate an IAdd/constant) is bailed on (absent in the graphics corpus).
    static void CombineAccessChains(SpvModule m)
    {
        var def = m.DefMap();
        var arrayStride = new Dictionary<uint, uint>();
        foreach (var ins in m.Instrs)
            if (ins.Opcode == 71 && ins.Operands.Length >= 3 && ins.Operands[1] == 6) arrayStride[ins.Operands[0]] = ins.Operands[2];

        static bool IsChain(int op) => op is 65 or 66 or 67 or 70;
        static bool IsPtr(int op) => op is 67 or 70;
        static int InNum(SpvInstr a) => a.Operands.Length - 2;
        bool Has64(SpvInstr ac)
        {
            for (int i = 1; i < InNum(ac); i++)
            {
                if (!def.TryGetValue(ac.Operands[2 + i], out var idx) || idx.Operands.Length == 0) return true;
                if (!def.TryGetValue(idx.Operands[0], out var td) || td.Opcode != 21 || td.Operands.Length < 2 || td.Operands[1] != 32) return true;
            }
            return false;
        }
        static int UpdateOpcode(int baseOp, int inputOp)
        {
            static bool InB(int o) => o is 70 or 66;
            if (inputOp == 70) { if (!InB(baseOp)) return 67; }
            else if (inputOp == 66) { if (!InB(baseOp)) return 65; }
            return inputOp;
        }
        void Combine(SpvInstr inst)
        {
            if (!def.TryGetValue(inst.Operands[2], out var ptr) || !IsChain(ptr.Opcode)) return;
            if (Has64(inst) || Has64(ptr)) return;
            if (arrayStride.TryGetValue(ptr.Operands[0], out var st) && st != 0) return;
            int pNum = InNum(ptr), iNum = InNum(inst);
            if (pNum == 1) { inst.Operands[2] = ptr.Operands[2]; }
            else if (iNum == 1) { inst.Opcode = OpCopyObject; inst.Operands = [inst.Operands[0], inst.Operands[1], inst.Operands[2]]; }
            else
            {
                if (IsPtr(inst.Opcode)) return;   // element-operand combine needs id allocation -- bail (corpus has none)
                var newIn = new List<uint>();
                for (int i = 0; i < pNum; i++) newIn.Add(ptr.Operands[2 + i]);     // base + all of ptr's indices
                for (int i = 1; i < iNum; i++) newIn.Add(inst.Operands[2 + i]);    // inst's indices (skip its base)
                inst.Opcode = UpdateOpcode(inst.Opcode, ptr.Opcode);
                inst.Operands = [inst.Operands[0], inst.Operands[1], .. newIn];
            }
        }

        for (int fi = 0; fi < m.Instrs.Count; fi++)
        {
            if (m.Instrs[fi].Opcode != OpFunction) continue;
            int fs = fi, fe = fi; while (m.Instrs[fe].Opcode != OpFunctionEnd) fe++;
            var blocks = ParseBlocks(m, fs, fe, out _);
            fi = fe;
            if (blocks.Count == 0) continue;
            var byLabel = blocks.ToDictionary(b => b.Label);
            var post = new List<Blk>(); var vis = new HashSet<uint>();
            void Dfs(Blk b) { if (!vis.Add(b.Label)) return; foreach (var s in BlockSuccs(b)) if (byLabel.TryGetValue(s, out var nb)) Dfs(nb); post.Add(b); }
            Dfs(blocks[0]); post.Reverse();
            foreach (var b in post) foreach (var inst in b.Insts) if (IsChain(inst.Opcode)) Combine(inst);
        }
    }

    static List<Blk> ParseBlocks(SpvModule m, int fs, int fe, out int firstLabel)
    {
        firstLabel = fs; while (m.Instrs[firstLabel].Opcode != OpLabel) firstLabel++;
        var blocks = new List<Blk>();
        for (int i = firstLabel; i < fe; i++)
            if (m.Instrs[i].Opcode == OpLabel)
            {
                int j = i + 1; while (j < fe && m.Instrs[j].Opcode != OpLabel) j++;
                var b = new Blk { Label = m.Instrs[i].Operands[0], Insts = new List<SpvInstr>(j - i - 1 + 2) };   // room for a couple of edits
                for (int k = i + 1; k < j; k++) b.Insts.Add(m.Instrs[k]);
                blocks.Add(b); i = j - 1;
            }
        return blocks;
    }

    static Succs BlockSuccs(Blk b) => new(b.Term);

    // A block's successor labels in terminator order (branch target; true, false; switch default then cases),
    // enumerated straight off the terminator without allocating.
    readonly struct Succs(SpvInstr term) : IEnumerable<uint>
    {
        public int Count => term.Opcode switch { 249 => 1, 250 => 2, 251 => 1 + (term.Operands.Length - 2) / 2, _ => 0 };
        public uint this[int i] => term.Opcode switch { 249 => term.Operands[0], 250 => term.Operands[1 + i], _ => i == 0 ? term.Operands[1] : term.Operands[1 + 2 * i] };
        public bool Contains(uint label) { for (int i = 0, n = Count; i < n; i++) if (this[i] == label) return true; return false; }
        public List<uint> ToList() { var l = new List<uint>(Count); for (int i = 0, n = Count; i < n; i++) l.Add(this[i]); return l; }
        public Enumerator GetEnumerator() => new(this);
        IEnumerator<uint> IEnumerable<uint>.GetEnumerator() { for (int i = 0, n = Count; i < n; i++) yield return this[i]; }
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => ((IEnumerable<uint>)this).GetEnumerator();
        public struct Enumerator(Succs s)
        {
            int _i = -1;
            readonly int _n = s.Count;
            public bool MoveNext() => ++_i < _n;
            public readonly uint Current => s[_i];
        }
    }

    static bool DbeFunction(SpvModule m, int fs, DefTable def)
    {
        int fe = fs; while (m.Instrs[fe].Opcode != OpFunctionEnd) fe++;
        if (fs < 0) return false;
        var blocks = ParseBlocks(m, fs, fe, out int firstLabel);
        if (blocks.Count == 0) return false;
        var byLabel = blocks.ToDictionary(b => b.Label);

        uint MergeId(Blk b) => b.Merge?.Operands[0] ?? 0;
        uint ContId(Blk b) => b.Merge is { Opcode: 246 } mg ? mg.Operands[1] : 0;

        bool ConstCond(uint id, out bool val)
        {
            val = false;
            if (!def.TryGetValue(id, out var d)) return false;
            switch (d.Opcode) { case 42: case 46: val = false; return true; case 41: val = true; return true;
                case 168: if (ConstCond(d.Operands[2], out bool nv)) { val = !nv; return true; } return false; default: return false; }
        }
        bool ConstInt(uint id, out uint val)
        {
            val = 0;
            if (!def.TryGetValue(id, out var d)) return false;
            if (d.Opcode == 46) return true;   // OpConstantNull -> 0
            if (d.Opcode == OpConstant && def.TryGetValue(d.Operands[0], out var t) && t.Opcode == 21 && t.Operands[1] == 32) { val = d.Operands[2]; return true; }
            return false;
        }

        // Blocks with a back edge to a loop header (spirv-opt AddBlocksWithBackEdge), and each such block's header.
        var backEdge = new HashSet<uint>(); var backEdgeHeader = new Dictionary<uint, uint>();
        void AddBackEdges(uint contId, uint headerId, uint mergeId)
        {
            var vis = new HashSet<uint> { contId, headerId, mergeId }; var wl = new Stack<uint>(); wl.Push(contId);
            while (wl.Count > 0) { uint bid = wl.Pop(); if (!byLabel.TryGetValue(bid, out var bb)) continue; bool hb = false; foreach (var s in BlockSuccs(bb)) { if (vis.Add(s)) wl.Push(s); if (s == headerId) hb = true; } if (hb) { backEdge.Add(bid); backEdgeHeader[bid] = headerId; } }
        }

        var live = new HashSet<uint>();
        var toSimplify = new List<(uint Block, uint Live)>();
        var stack = new Stack<uint>(); stack.Push(blocks[0].Label);
        while (stack.Count > 0)
        {
            uint lbl = stack.Pop();
            if (!live.Add(lbl)) continue;
            if (!byLabel.TryGetValue(lbl, out var b)) continue;
            uint cont = ContId(b);
            if (cont != 0) AddBackEdges(cont, lbl, MergeId(b));
            uint liveLab = 0;
            var t = b.Term;
            if (t.Opcode == 250) { if (ConstCond(t.Operands[0], out bool cv)) liveLab = cv ? t.Operands[1] : t.Operands[2]; }
            else if (t.Opcode == 251) { if (ConstInt(t.Operands[0], out uint sv)) { liveLab = t.Operands[1]; for (int k = 3; k < t.Operands.Length; k += 2) if (t.Operands[k - 1] == sv) { liveLab = t.Operands[k]; break; } } }
            bool simplify = liveLab != 0 && (!backEdge.Contains(lbl) || (backEdgeHeader.TryGetValue(lbl, out uint h) && liveLab == h));
            if (simplify) { toSimplify.Add((lbl, liveLab)); stack.Push(liveLab); }
            else foreach (var s in BlockSuccs(b)) stack.Push(s);
        }

        // Structured info: containing loop/switch of each block, and construct merges/continues.
        var contLoop = new Dictionary<uint, uint>(); var contSwitch = new Dictionary<uint, uint>(); var contConstruct = new Dictionary<uint, uint>();
        var loopMerge = new Dictionary<uint, uint>(); var loopCont = new Dictionary<uint, uint>(); var switchMerge = new Dictionary<uint, uint>();
        {
            var st = new List<(uint Header, bool IsLoop, uint Merge, uint Cont)>();
            foreach (var b in StructuredOrder(blocks))
            {
                while (st.Count > 0 && st[^1].Merge == b.Label) st.RemoveAt(st.Count - 1);
                uint lh = 0, sh = 0;
                for (int i = st.Count - 1; i >= 0; i--) { if (st[i].IsLoop && lh == 0) lh = st[i].Header; if (!st[i].IsLoop && sh == 0) sh = st[i].Header; }
                contLoop[b.Label] = lh; contSwitch[b.Label] = sh; contConstruct[b.Label] = st.Count > 0 ? st[^1].Header : 0;
                if (b.Merge is { } mg) { bool isLoop = mg.Opcode == 246; st.Add((b.Label, isLoop, mg.Operands[0], isLoop ? mg.Operands[1] : 0)); if (isLoop) { loopMerge[b.Label] = mg.Operands[0]; loopCont[b.Label] = mg.Operands[1]; } else switchMerge[b.Label] = mg.Operands[0]; }
            }
        }

        bool modified = false;
        for (int i = toSimplify.Count - 1; i >= 0; i--) modified |= SimplifyBranch(byLabel[toSimplify[i].Block], toSimplify[i].Live);

        bool SimplifyBranch(Blk b, uint liveLab)
        {
            var mergeInst = b.Merge;
            if (mergeInst is { Opcode: 247 })
            {
                if (b.Term.Opcode == 251 && SwitchHasNestedBreak(b))
                {
                    if (b.Term.Operands.Length == 2) return false;
                    b.Insts[^1] = new SpvInstr(251, [b.Term.Operands[0], liveLab]);
                }
                else
                {
                    var (firstBreakBlk, firstBreakIdx) = FindFirstExit(liveLab, mergeInst.Operands[0]);
                    b.Insts[b.Insts.Count - 1] = new SpvInstr(249, [liveLab]);
                    b.Insts.Remove(mergeInst);
                    if (firstBreakBlk != null) firstBreakBlk.Insts.Insert(firstBreakIdx, mergeInst);
                }
            }
            else b.Insts[^1] = new SpvInstr(249, [liveLab]);   // OpLoopMerge kept, or no merge
            return true;
        }

        // spirv-opt's predicate, kept as written there: the switch "has a nested break" when some branch to its
        // merge (other than the header's) comes from a block that is NOT directly inside the switch construct
        // or that carries its own merge instruction.
        bool SwitchHasNestedBreak(Blk switchHeader)
        {
            uint mergeId = MergeId(switchHeader);
            foreach (var bb in blocks)
            {
                if (bb.Label == switchHeader.Label || !BlockSuccs(bb).Contains(mergeId)) continue;
                if (!(contConstruct.GetValueOrDefault(bb.Label) == switchHeader.Label && bb.Merge == null)) return true;
            }
            return false;
        }

        (Blk?, int) FindFirstExit(uint startId, uint mergeId)
        {
            uint loopMergeId = contLoop.TryGetValue(startId, out uint lh) && lh != 0 ? loopMerge[lh] : 0;
            uint loopContId = lh != 0 ? loopCont[lh] : 0;
            uint switchMergeId = contSwitch.TryGetValue(startId, out uint sh) && sh != 0 ? switchMerge[sh] : 0;
            uint cur = startId;
            // The walk assumes structured control flow terminates at a merge; a corrupt module (a zeroed
            // branch target, say) can make the successor chain cycle forever, spinning this loop - so cap
            // it at one step per block: a valid-module walk never revisits (the corpus would hang otherwise).
            int steps = 0;
            while (cur != mergeId && cur != loopMergeId && cur != loopContId)
            {
                if (++steps > blocks.Count) return (null, 0);
                if (!byLabel.TryGetValue(cur, out var sb)) return (null, 0);
                var br = sb.Term; uint next = MergeId(sb);
                if (br.Opcode == 250)
                {
                    if (next == 0)
                    {
                        for (int i = 1; i < 3; i++) { uint tg = br.Operands[i]; if ((tg == loopMergeId && loopMergeId != mergeId) || (tg == loopContId && loopContId != mergeId) || (tg == switchMergeId && switchMergeId != mergeId)) { next = br.Operands[3 - i]; break; } }
                        if (next == 0) return (sb, sb.Insts.Count - 1);
                    }
                }
                else if (br.Opcode == 251)
                {
                    if (next == 0)
                    {
                        bool foundBreak = false;
                        for (int i = 1; i < br.Operands.Length; i += 2) { uint tg = i == 1 ? br.Operands[1] : br.Operands[i]; if (tg == mergeId) foundBreak = true; else if (tg != loopMergeId && tg != loopContId) next = tg; }
                        if (next == 0) return (null, 0);
                        if (foundBreak) return (sb, sb.Insts.Count - 1);
                    }
                }
                else if (br.Opcode == 249) { if (next == 0) next = br.Operands[0]; }
                else return (null, 0);
                cur = next;
            }
            return (null, 0);
        }

        // Unreachable structured targets.
        var unreachMerges = new HashSet<uint>(); var unreachCont = new Dictionary<uint, uint>();
        foreach (uint lbl in live)
        {
            var b = byLabel[lbl];
            uint mid = MergeId(b);
            if (mid != 0 && !live.Contains(mid)) unreachMerges.Add(mid);
            uint cid = ContId(b);
            if (mid != 0 && cid != 0 && !live.Contains(cid)) unreachCont[cid] = lbl;
        }

        modified |= FixPhis();
        modified |= EraseDead();

        bool FixPhis()
        {
            bool ch = false;
            foreach (var b in blocks)
            {
                if (!live.Contains(b.Label)) continue;
                for (int pi = 0; pi < b.Insts.Count && b.Insts[pi].Opcode == 245; pi++)
                {
                    var phi = b.Insts[pi];
                    var ops = new List<uint> { phi.Operands[0], phi.Operands[1] };
                    bool changed = false, backAdded = false;
                    for (int i = 2; i < phi.Operands.Length; i += 2)
                    {
                        uint val = phi.Operands[i], inc = phi.Operands[i + 1];
                        if (unreachCont.TryGetValue(inc, out uint hdr) && hdr == b.Label && phi.Operands.Length > 6)
                        {
                            if (def.TryGetValue(val, out var vd) && vd.Opcode == 1) { ops.Add(val); ops.Add(inc); backAdded = true; }
                            else { ops.Add(GetDbeUndef(m, phi.Operands[0])); ops.Add(inc); changed = true; backAdded = true; }
                        }
                        else if (live.Contains(inc) && byLabel.TryGetValue(inc, out var ib) && BlockSuccs(ib).Contains(b.Label)) { ops.Add(val); ops.Add(inc); }
                        else changed = true;
                    }
                    if (!changed) continue;
                    ch = true;
                    uint contId = ContId(b);
                    if (!backAdded && contId != 0 && unreachCont.ContainsKey(contId) && ops.Count > 4) { ops.Add(GetDbeUndef(m, phi.Operands[0])); ops.Add(contId); }
                    if (ops.Count == 4) { m.ReplaceUses(phi.Operands[1], ops[2]); b.Insts.RemoveAt(pi); pi--; }
                    else phi.Operands = ops.ToArray();
                }
            }
            return ch;
        }

        bool EraseDead()
        {
            bool ch = false;
            foreach (var b in blocks)
            {
                if (unreachCont.TryGetValue(b.Label, out uint hdr))
                {
                    uint cid = hdr;
                    if (b.Insts.Count != 1 || b.Term.Opcode != 249 || b.Term.Operands[0] != cid) { b.Insts.Clear(); b.Insts.Add(new SpvInstr(249, [cid])); ch = true; }
                }
                else if (unreachMerges.Contains(b.Label))
                {
                    if (b.Insts.Count != 1 || b.Term.Opcode != 255) { b.Insts.Clear(); b.Insts.Add(new SpvInstr(255, [])); ch = true; }
                }
            }
            var deadLabels = blocks.Where(b => !live.Contains(b.Label) && !unreachMerges.Contains(b.Label) && !unreachCont.ContainsKey(b.Label)).Select(b => b.Label).ToHashSet();
            if (deadLabels.Count > 0) { blocks.RemoveAll(b => deadLabels.Contains(b.Label)); ch = true; }
            return ch;
        }

        if (modified) RebuildFunction(m, fs, fe, firstLabel, blocks);
        return modified;
    }

    [ThreadStatic] static List<SpvInstr>? s_rebuild;
    static void RebuildFunction(SpvModule m, int fs, int fe, int firstLabel, List<Blk> blocks)
    {
        var rebuilt = s_rebuild ??= []; rebuilt.Clear();
        for (int i = fs; i < firstLabel; i++) rebuilt.Add(m.Instrs[i]);
        foreach (var b in blocks) { rebuilt.Add(new SpvInstr(OpLabel, [b.Label])); rebuilt.AddRange(b.Insts); }
        // Overwrite the old range in place and only shift the tail by the size difference.
        int old = fe - fs, common = Math.Min(old, rebuilt.Count);
        for (int i = 0; i < common; i++) m.Instrs[fs + i] = rebuilt[i];
        if (rebuilt.Count > old) m.Instrs.InsertRange(fs + old, rebuilt.GetRange(old, rebuilt.Count - old));
        else if (old > rebuilt.Count) m.Instrs.RemoveRange(fs + common, old - common);
        rebuilt.Clear();
    }

    // MemPass::Type2Undef: a per-pass map (module OpUndefs are never reused), reset per DeadBranchElim run.
    [ThreadStatic] static Dictionary<uint, uint>? s_dbeUndefs;
    [ThreadStatic] static List<SpvInstr>? s_undefSink;   // DBE collects the undefs it creates to keep its def map current
    static uint GetDbeUndef(SpvModule m, uint typeId)
    {
        s_dbeUndefs ??= [];
        if (s_dbeUndefs.TryGetValue(typeId, out uint u)) return u;
        uint id = m.Header[3]++;
        s_dbeUndefs[typeId] = id;
        int ff = m.Instrs.FindIndex(x => x.Opcode == OpFunction);
        var undef = new SpvInstr(1, [typeId, id]);
        m.Instrs.Insert(ff, undef);
        s_undefSink?.Add(undef);
        return id;
    }

    // Reverse-post-order of the structured-successor graph (merge + continue first, then real successors).
    static List<Blk> StructuredOrder(List<Blk> blocks)
    {
        var byLabel = blocks.ToDictionary(b => b.Label);
        var post = new List<Blk>(); var vis = new HashSet<uint>();
        void Dfs(Blk b)
        {
            if (!vis.Add(b.Label)) return;
            var succs = new List<uint>();
            if (b.Merge is { } mg) { succs.Add(mg.Operands[0]); if (mg.Opcode == 246) succs.Add(mg.Operands[1]); }
            succs.AddRange(BlockSuccs(b));
            foreach (var s in succs) if (byLabel.TryGetValue(s, out var nb)) Dfs(nb);
            post.Add(b);
        }
        Dfs(blocks[0]); post.Reverse(); return post;
    }

    // spirv-opt FixBlockOrder: reorder a function's blocks into structured order.
    static void DbeReorder(SpvModule m, int fs)
    {
        int fe = fs; while (m.Instrs[fe].Opcode != OpFunctionEnd) fe++;
        if (fs < 0) return;
        var blocks = ParseBlocks(m, fs, fe, out int firstLabel);
        if (blocks.Count == 0) return;
        var ordered = StructuredOrder(blocks);
        if (ordered.Count != blocks.Count) return;   // safety: only reorder when every block is structurally reachable
        if (ordered.Select(b => b.Label).SequenceEqual(blocks.Select(b => b.Label))) return;
        RebuildFunction(m, fs, fe, firstLabel, ordered);
    }

    // A basic block: its label id and the instructions after the label (structured merge inst + terminator
    // last). Used by the MergeReturn port, which needs real block surgery (split, insert, add phis).
    sealed class Blk { public uint Label; public List<SpvInstr> Insts = []; public SpvInstr Term => Insts[^1];
        public SpvInstr? LabelInst; public Blk? Containing; public int LiveA, LiveB, Reach;   // scratch for passes that fill them (ADCE)
        public SpvInstr? Merge => Insts.Count >= 2 && Insts[^2].Opcode is 246 or 247 ? Insts[^2] : null; }

    // spirv-opt's CCP (conditional constant propagation): a sparse-conditional propagator over the SSA graph and
    // CFG. Values are Undefined (absent) / a constant id / Varying. Phis meet incoming values from executable
    // edges only; foldable instructions with all-constant operands fold to a constant; a branch with a constant
    // condition only makes its taken edge executable. Const-valued results are then replaced by the constant,
    // and the const-condition branches are folded by the following DeadBranchElim. Runs before LoopUnroll.
    // FeatureManager::HasCapability(Shader): Shader or any declared capability that implies it (Geometry,
    // Tessellation, ...), per the grammar's implicit-capability chains.
    static readonly HashSet<uint> ShaderImplyingCaps =
    [
        1, 2, 3, 21, 23, 24, 25, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 40, 41, 42, 45, 48, 49, 50, 51, 52, 53, 54,
        55, 56, 57, 4422, 4427, 4428, 4429, 4430, 4439, 4441, 4442, 4445, 4471, 4472, 4478, 4479, 4495, 5008, 5009, 5010,
        5013, 5015, 5067, 5249, 5251, 5254, 5255, 5259, 5260, 5265, 5266, 5283, 5288, 5291, 5301, 5302, 5303, 5306, 5307,
        5308, 5309, 5310, 5311, 5312, 5336, 5340, 5341, 5347, 5350, 5353, 5357, 5363, 5372, 5373, 5378, 5379, 5380, 5381,
        5383, 5388, 5391, 5409, 5423, 5437, 6032,
    ];
    static bool HasShaderCapability(SpvModule m) => m.Instrs.Any(i => i.Opcode == 17 && i.Operands.Length >= 1 && ShaderImplyingCaps.Contains(i.Operands[0]));

    static void Ccp(SpvModule m)
    {
        if (!HasShaderCapability(m)) return;
        const uint VARYING = uint.MaxValue;
        var folder = new Folder(m);
        foreach (uint fnId in ReachableFunctions(m))
            if (m.Instrs.Any(x => x.Opcode == OpFunction && x.Operands[1] == fnId))
                CcpFunction(m, fnId, folder, VARYING);
        folder.Commit();
    }

    // spirv-opt's Simplification pass (simplification_pass.cpp): fold every instruction to a fixpoint via the
    // shared InstructionFolder, in reverse-post-order, with a work list for values whose inputs later change.
    // An instruction that folds to a constant becomes OpCopyObject of it; OpCopyObject then has its uses
    // replaced by its source and is killed. Replaces the old SimplifyFolds with the faithful engine.
    static void Simplification(SpvModule m)
    {
        var folder = new Folder(m);
        foreach (uint fnId in ReachableFunctions(m))
            if (m.Instrs.Any(x => x.Opcode == OpFunction && x.Operands[1] == fnId))
                SimplifyFunction(m, fnId, folder);
        folder.Commit();
    }

    // Immediate dominators over the real CFG (Cooper/Harvey/Kennedy), keyed by block label.
    static Dictionary<uint, uint> ComputeIdom(List<Blk> blocks)
    {
        var byLabel = blocks.ToDictionary(b => b.Label);
        var ord = new List<Blk>(); var vis = new HashSet<uint>();
        void Dfs(Blk b) { if (!vis.Add(b.Label)) return; foreach (var s in BlockSuccs(b)) if (byLabel.TryGetValue(s, out var nb)) Dfs(nb); ord.Add(b); }
        Dfs(blocks[0]); ord.Reverse();
        var rpo = new Dictionary<uint, int>(); for (int i = 0; i < ord.Count; i++) rpo[ord[i].Label] = i;
        var preds = new Dictionary<uint, List<uint>>(); foreach (var b in blocks) preds[b.Label] = [];
        foreach (var b in blocks) foreach (var s in BlockSuccs(b)) if (preds.ContainsKey(s)) preds[s].Add(b.Label);
        var idom = new Dictionary<uint, uint> { [ord[0].Label] = ord[0].Label };
        bool changed = true;
        while (changed)
        {
            changed = false;
            for (int i = 1; i < ord.Count; i++)
            {
                uint b = ord[i].Label, nd = 0;
                foreach (var p in preds[b])
                {
                    if (!rpo.ContainsKey(p) || !idom.ContainsKey(p)) continue;
                    if (nd == 0) nd = p;
                    else { uint x = p, y = nd; while (x != y) { while (rpo[x] > rpo[y]) x = idom[x]; while (rpo[y] > rpo[x]) y = idom[y]; } nd = x; }
                }
                if (nd != 0 && (!idom.TryGetValue(b, out var cur) || cur != nd)) { idom[b] = nd; changed = true; }
            }
        }
        return idom;
    }
    static bool Dominates(uint a, uint b, Dictionary<uint, uint> idom) { for (uint x = b; ; ) { if (x == a) return true; if (!idom.TryGetValue(x, out var nx) || nx == x) return false; x = nx; } }

    [ThreadStatic] static DefTable? s_simpDefs;
    static void SimplifyFunction(SpvModule m, uint fnId, Folder folder)
    {
        int fs = -1, fe = -1;
        { int i = m.FindFunction(fnId); if (i >= 0) { fs = i; fe = i; while (m.Instrs[fe].Opcode != OpFunctionEnd) fe++; } }
        var blocks = ParseBlocks(m, fs, fe, out int firstLabel);
        if (blocks.Count == 0) return;
        var byLabel = blocks.ToDictionary(b => b.Label);

        // Reverse post-order over the CFG.
        var post = new List<Blk>(); var vis = new HashSet<uint>();
        void Dfs(Blk b) { if (!vis.Add(b.Label)) return; foreach (var s in BlockSuccs(b)) if (byLabel.TryGetValue(s, out var nb)) Dfs(nb); post.Add(b); }
        Dfs(blocks[0]); post.Reverse();

        // Maintained user map (id -> instructions referencing it) over this function's instructions (pooled).
        var users = m.UserMapOverHash(blocks.SelectMany(b => b.Insts));
        void Index(SpvInstr ins) { ins.Classify(out int ri, out bool[] isId); for (int k = 0; k < ins.Operands.Length; k++) if (isId[k] && k != ri) (users.TryGetValue(ins.Operands[k], out var l) ? l : users[ins.Operands[k]] = []).Add(ins); }
        void Deindex(SpvInstr ins) { ins.Classify(out int ri, out bool[] isId); for (int k = 0; k < ins.Operands.Length; k++) if (isId[k] && k != ri && users.TryGetValue(ins.Operands[k], out var l)) l.Remove(ins); }
        // Each instruction's original result id: stamped, in Pos. Defs by id in a pooled table.
        int hasResult = SpvInstr.NewStamp();
        var defInstr = s_simpDefs ??= new(); defInstr.Reset(m.Header[3]);
        void SetResult(SpvInstr ins, uint r) { ins.Mark = hasResult; ins.Pos = (int)r; defInstr[r] = ins; }
        uint ResultOf(SpvInstr ins) => ins.Mark == hasResult ? (uint)ins.Pos : 0;
        foreach (var b in blocks) foreach (var ins in b.Insts) { uint r = ins.ResultId; if (r != 0) SetResult(ins, r); }

        // Use count for single-use-guarded rules. The instruction being folded is deindexed first, so its own
        // use of an operand is not in the map; the +1 restores the true count for the operand it is querying.
        folder.UseCountFn = id => (users.TryGetValue(id, out var s) ? s.Count : 0) + 1;

        var work = new List<SpvInstr>(); var inWork = Pool.InstrSet();
        var processPhis = Pool.InstrSet(); var seen = Pool.InstrSet(); var kill = Pool.InstrSet();

        // Replace all uses of `from` with `to` inside this function, keeping the user map current.
        void ReplaceUses(uint from, uint to)
        {
            if (!users.TryGetValue(from, out var us)) return;
            foreach (var u in us.ToList())
            {
                u.Classify(out int ri, out bool[] isId);
                for (int k = 0; k < u.Operands.Length; k++) if (isId[k] && k != ri && u.Operands[k] == from) u.Operands[k] = to;
                (users.TryGetValue(to, out var tl) ? tl : users[to] = []).Add(u);
            }
            us.Clear();
        }

        // Handle a folded instruction: re-index its (changed) operands, queue phi/other users, propagate a copy.
        void OnFolded(SpvInstr inst, bool phase2)
        {
            // The instruction's operands changed after folding -> re-index. (Deindex happened before Fold.)
            Index(inst);
            uint rid = ResultOf(inst);
            if (rid != 0 && users.TryGetValue(rid, out var us))
                foreach (var u in us.ToList())
                    if ((phase2 || processPhis.Contains(u)) && !(u.Opcode is 5 or 71 or 72) && inWork.Add(u)) work.Add(u);
            // AddNewOperands: newly referenced operand-defs join the work list.
            inst.Classify(out int ri2, out bool[] isId2);
            for (int k = 0; k < inst.Operands.Length; k++)
                if (isId2[k] && k != ri2 && ByResult(inst.Operands[k]) is { } d && seen.Add(d)) work.Add(d);
            if (inst.Opcode == 0) { kill.Add(inst); inWork.Add(inst); return; }   // OpNop (e.g. StoringUndef) -> remove
            if (inst.Opcode == 83)   // OpCopyObject -> propagate + kill
            {
                if (rid != 0) ReplaceUses(rid, inst.Operands[2]);
                kill.Add(inst); inWork.Add(inst);
            }
        }
        SpvInstr? ByResult(uint id) => defInstr.TryGetValue(id, out var d) ? d : null;

        // Splice any body instructions a rule created (ReassociateNestedAddSub's inner add) before the fold site.
        void InsertPending(SpvInstr anchor)
        {
            var pend = folder.DrainPendingBody();
            if (folder.PendingAnchor is { } other) { anchor = other; folder.PendingAnchor = null; }
            if (pend.Count == 0) return;
            foreach (var b in blocks) { int p = b.Insts.IndexOf(anchor); if (p < 0) continue; foreach (var ni in pend) { b.Insts.Insert(p++, ni); uint r = ni.ResultId; if (r != 0) SetResult(ni, r); Index(ni); } return; }
        }

        // Phase 1: reverse post-order.
        foreach (var b in post) foreach (var inst in b.Insts.ToList())
        {
            seen.Add(inst);
            if (inst.Opcode == 245) processPhis.Add(inst);
            Deindex(inst);
            bool changed = folder.FoldInstruction(inst);
            if (changed) { InsertPending(inst); OnFolded(inst, phase2: false); } else { InsertPending(inst); Index(inst); }   // a rule may rewrite yet report no change
        }
        // Phase 2: work list.
        for (int i = 0; i < work.Count; i++)
        {
            var inst = work[i]; inWork.Remove(inst); seen.Add(inst);
            Deindex(inst);
            bool changed = folder.FoldInstruction(inst);
            if (changed) { InsertPending(inst); OnFolded(inst, phase2: true); } else { InsertPending(inst); Index(inst); }
        }
        // Phase 3: remove killed OpCopyObjects (their uses were replaced by their source) and OpNops.
        if (kill.Count > 0) foreach (var b in blocks) b.Insts.RemoveAll(kill.Contains);
        Pool.Free(inWork); Pool.Free(processPhis); Pool.Free(seen); Pool.Free(kill);

        RebuildFunction(m, fs, fe, firstLabel, blocks);
    }

    // CCPPass over spirv-opt's SSAPropagator, ported step for step: the visit order decides which transient
    // constants get declared (a loop phi is provisionally constant on its first visit), so the ids depend on it.
    [ThreadStatic] static DefTable? s_ccpDefs;
    static void CcpFunction(SpvModule m, uint fnId, Folder folder, uint VARYING)
    {
        int fs = -1, fe = -1;
        { int i = m.FindFunction(fnId); if (i >= 0) { fs = i; fe = i; while (m.Instrs[fe].Opcode != OpFunctionEnd) fe++; } }
        var blocks = ParseBlocks(m, fs, fe, out _);
        if (blocks.Count == 0) return;
        var byLabel = blocks.ToDictionary(b => b.Label);
        // The block's label is an instruction too (BasicBlock::WhileEachInst visits it first).
        var labelInstr = new Dictionary<uint, SpvInstr>();
        foreach (var b in blocks) labelInstr[b.Label] = new SpvInstr(OpLabel, [b.Label]);
        var users = m.UserMapOver(blocks.SelectMany(b => b.Insts));
        // Def-use users in creation order (DefUseManager orders them by unique id).
        foreach (var l in users.Values) if (l.Count > 1) l.Sort((x, y) => x.Serial.CompareTo(y.Serial));
        // Labels are used by branches and phis; route those uses through the label pseudo-instructions.
        // Each instruction's block label rides in Tag (boxed once per block).
        foreach (var b in blocks) { object lb = b.Label; labelInstr[b.Label].Tag = lb; foreach (var ins in b.Insts) ins.Tag = lb; }
        static uint BlockOfI(SpvInstr i) => (uint)i.Tag!;
        var defOf = s_ccpDefs ??= new(); defOf.Reset(m.Header[3]);   // function-local defs (labels as their pseudo-instructions)
        foreach (var b in blocks) { defOf[b.Label] = labelInstr[b.Label]; foreach (var ins in b.Insts) if (ins.ResultId != 0) defOf[ins.ResultId] = ins; }
        var succs = new Dictionary<uint, List<uint>>();
        foreach (var b in blocks) succs[b.Label] = BlockSuccs(b).ToList();

        var values = new Dictionary<uint, uint>();       // result id -> constant id, or VARYING
        // CCPPass::Initialize: every module-scope constant is its own value, every other global -- specialization
        // constants included (Instruction::IsConstant excludes them) -- is varying.
        foreach (var g in m.Instrs)
        {
            if (g.Opcode == OpFunction) break;
            uint r = g.ResultId;
            if (r != 0) values[r] = g.Opcode is >= 41 and <= 46 ? r : VARYING;
        }
        // Function parameters are varying.
        for (int i = fs + 1; i < fe && m.Instrs[i].Opcode == 55; i++) values[m.Instrs[i].Operands[1]] = VARYING;

        const int NotInteresting = 0, Interesting = 1, Varying = 2;
        // Per-instruction propagator state on the instructions themselves: Pos holds the last visit status
        // (-1 = never visited), a Mark stamp means "don't simulate again".
        int dontSim = SpvInstr.NewStamp();
        foreach (var b in blocks) { labelInstr[b.Label].Pos = -1; foreach (var ins in b.Insts) ins.Pos = -1; }
        var simulated = new HashSet<uint>();
        var execEdge = new HashSet<(uint, uint)>();
        var blockQ = new Queue<uint>();
        var ssaQ = new Queue<SpvInstr>();
        uint Val(uint id) => values.TryGetValue(id, out uint v) && v != VARYING ? v : id;
        Func<uint, uint> valFn = Val;

        bool ShouldSimulateAgain(SpvInstr? d) => d is null || d.Mark != dontSim;
        SpvInstr? DefInstr(uint id) => defOf.TryGetValue(id, out var d) ? d : null;   // null: a global (never retired)
        void AddControlEdge(uint from, uint to) { if (byLabel.ContainsKey(to) && execEdge.Add((from, to))) blockQ.Enqueue(to); }
        bool PhiArgExec(SpvInstr phi, int valIdx) => execEdge.Contains((phi.Operands[valIdx + 1], BlockOfI(phi)));
        void AddSsaEdges(SpvInstr ins)
        {
            uint rid = ins.Opcode == OpLabel ? ins.Operands[0] : ins.ResultId;
            if (rid == 0 || !users.TryGetValue(rid, out var us)) return;
            foreach (var u in us)
                if (u.Tag is uint ub && simulated.Contains(ub) && ShouldSimulateAgain(u)) ssaQ.Enqueue(u);
        }
        int MarkVarying(SpvInstr ins) { uint r = ins.Opcode == OpLabel ? ins.Operands[0] : ins.ResultId; values[r] = VARYING; return Varying; }
        uint Meet(uint rid, uint val2)
        {
            if (!values.TryGetValue(rid, out uint val1)) return val2;
            if (val1 == VARYING) return val1;
            if (val2 == VARYING) return val2;
            return val1 != val2 ? VARYING : val2;
        }

        int VisitPhi(SpvInstr phi)
        {
            uint meet = 0;
            for (int i = 2; i + 1 < phi.Operands.Length; i += 2)
            {
                if (!PhiArgExec(phi, i)) continue;
                if (!values.TryGetValue(phi.Operands[i], out uint av)) continue;   // not interesting
                if (av == VARYING) return MarkVarying(phi);
                if (meet == 0) meet = av; else if (av != meet) return MarkVarying(phi);
            }
            if (meet == 0) return NotInteresting;
            values[phi.Operands[1]] = meet;
            return Interesting;
        }

        int VisitBranch(SpvInstr ins, out uint dest)
        {
            dest = 0;
            if (ins.Opcode == 249) { dest = ins.Operands[0]; return Interesting; }
            if (ins.Opcode == 250)
            {
                if (!values.TryGetValue(ins.Operands[0], out uint cv) || cv == VARYING) return Varying;
                if (!folder.AsBool(cv, out bool bv)) bv = false;   // a null constant takes the false target
                dest = bv ? ins.Operands[1] : ins.Operands[2];
                return Interesting;
            }
            // OpSwitch: the case whose literal equals the selector's first word, else the default (literals are
            // read at stride 2 whatever the selector width, as spirv-opt does).
            if (!values.TryGetValue(ins.Operands[0], out uint sv) || sv == VARYING) return Varying;
            folder.SwitchSelectorWord(sv, out uint word);
            dest = ins.Operands[1];
            for (int k = 2; k + 1 < ins.Operands.Length; k += 2) if (ins.Operands[k] == word) { dest = ins.Operands[k + 1]; break; }
            return Interesting;
        }

        int VisitAssignment(SpvInstr ins)
        {
            if (ins.Opcode == OpLabel) return MarkVarying(ins);
            if (ins.Opcode == 83)   // OpCopyObject
            {
                if (!values.TryGetValue(ins.Operands[2], out uint v)) return NotInteresting;
                if (v == VARYING) return MarkVarying(ins);
                uint nv = Meet(ins.ResultId, v);
                values[ins.ResultId] = nv;
                return nv == VARYING ? Varying : Interesting;
            }
            if (!folder.IsFoldable(ins)) return MarkVarying(ins);
            uint folded = folder.FoldToConstant(ins, valFn);
            if (folded != 0)
            {
                uint nv = Meet(ins.ResultId, folded);
                values[ins.ResultId] = nv;
                return nv == VARYING ? Varying : Interesting;
            }
            ins.Classify(out int ri, out bool[] isId);
            int typeIdx = ri - 1;
            for (int k = 0; k < ins.Operands.Length; k++)
                if (isId[k] && k != ri && k != typeIdx && values.TryGetValue(ins.Operands[k], out uint v) && v == VARYING) return MarkVarying(ins);
            for (int k = 0; k < ins.Operands.Length; k++)
                if (isId[k] && k != ri && k != typeIdx && !values.ContainsKey(ins.Operands[k])) return NotInteresting;
            return MarkVarying(ins);
        }

        int VisitInstruction(SpvInstr ins, out uint dest)
        {
            dest = 0;
            if (ins.Opcode == 245) return VisitPhi(ins);
            if (ins.Opcode is 249 or 250 or 251) return VisitBranch(ins, out dest);
            if (ins.Opcode == OpLabel || ins.ResultId != 0) return VisitAssignment(ins);
            return Varying;
        }

        void Simulate(SpvInstr ins)
        {
            if (!ShouldSimulateAgain(ins)) return;
            int st = VisitInstruction(ins, out uint dest);
            bool changed = ins.Pos != st;
            if (changed) ins.Pos = st;
            uint blk = BlockOfI(ins);
            if (st == Varying)
            {
                ins.Mark = dontSim;
                if (changed) AddSsaEdges(ins);
                if (ins.Opcode is 249 or 250 or 251 or 253 or 254 or 252 or 255 or 4416 or 4448 or 4449 or 5294)
                    foreach (uint s in succs[blk]) AddControlEdge(blk, s);
                return;
            }
            if (st == Interesting)
            {
                if (changed) AddSsaEdges(ins);
                if (dest != 0) AddControlEdge(blk, dest);
            }
            bool again = false;
            if (ins.Opcode == 245)
            {
                for (int i = 2; i + 1 < ins.Operands.Length && !again; i += 2)
                    if (!PhiArgExec(ins, i) || ShouldSimulateAgain(DefInstr(ins.Operands[i]))) again = true;
            }
            else if (ins.Opcode != OpLabel)
            {
                ins.Classify(out int ri, out bool[] isId);
                for (int k = 0; k < ins.Operands.Length && !again; k++)
                    if (isId[k] && k != ri && k != ri - 1 && ShouldSimulateAgain(DefInstr(ins.Operands[k]))) again = true;
            }
            if (!again) ins.Mark = dontSim;
        }

        void SimulateBlock(uint label)
        {
            var b = byLabel[label];
            foreach (var ins in b.Insts) if (ins.Opcode == 245) Simulate(ins);
            if (simulated.Contains(label)) return;
            Simulate(labelInstr[label]);
            foreach (var ins in b.Insts) Simulate(ins);
            simulated.Add(label);
            if (succs[label].Count == 1) AddControlEdge(label, succs[label][0]);
        }

        AddControlEdge(0, blocks[0].Label);
        while (blockQ.Count > 0 || ssaQ.Count > 0)
        {
            if (blockQ.Count > 0) { SimulateBlock(blockQ.Dequeue()); continue; }
            Simulate(ssaQ.Dequeue());
        }

        foreach (var (id, cst) in values) if (cst != VARYING && id != cst) MapReplace(users, id, cst);
    }

    // spirv-opt's LoopUnroll (fully_unroll). Only loops carrying the Unroll loop-control hint ([[unroll]]) and
    // meeting CanPerformUnroll (constant trip count, no breaks/continues/returns, latch is an unconditional
    // branch to the header, inner loop) are unrolled. The body is duplicated (trip count - 1) times, induction
    // phis are threaded to the previous iteration's value, the exit conditions are folded to unconditional
    // branches, and the loop structure is removed -- matching spirv-opt's copy/thread/fold/close id order.
    static void LoopUnroll(SpvModule m)
    {
        if (!HasShaderCapability(m)) return;
        foreach (uint fn in ReachableFunctions(m)) { int guard = 0; while (UnrollOneLoop(m, fn) && ++guard < 64) { } }
    }

    static bool UnrollOneLoop(SpvModule m, uint fnId)
    {
        int fs = -1, fe = -1;
        { int i = m.FindFunction(fnId); if (i >= 0) { fs = i; fe = i; while (m.Instrs[fe].Opcode != OpFunctionEnd) fe++; } }
        if (fs < 0) return false;
        var blocks = ParseBlocks(m, fs, fe, out int firstLabel);
        var byLabel = blocks.ToDictionary(b => b.Label);
        var def = m.DefMap();
        List<uint> Preds(uint lbl) { var r = new List<uint>(); foreach (var bb in blocks) if (BlockSuccs(bb).Contains(lbl)) r.Add(bb.Label); return r; }

        foreach (var header in blocks)
        {
            if (header.Merge is not { Opcode: 246 } lm || lm.Operands[2] != 1) continue;   // OpLoopMerge with control == Unroll(1)
            uint mergeId = lm.Operands[0], contId = lm.Operands[1];
            if (Preds(mergeId).Count != 1 || Preds(contId).Count != 1) continue;            // no breaks / no continues

            // Loop blocks in structured order: DFS from header over successors, not leaving via the merge.
            var loopSet = new HashSet<uint>(); var order = new List<Blk>();
            void Dfs(Blk b) { if (b.Label == mergeId || !loopSet.Add(b.Label)) return; order.Add(b); foreach (var s in BlockSuccs(b)) if (byLabel.TryGetValue(s, out var nb)) Dfs(nb); }
            Dfs(header);
            if (order.Any(b => b.Term.Opcode is OpReturn or OpReturnValue or 252 or 255)) continue;   // no returns/kills

            var latch = byLabel[contId];
            if (latch.Term.Opcode != 249 || latch.Term.Operands[0] != header.Label) continue;   // latch is uncond branch to header

            // Condition block: the loop block whose conditional branch exits to the merge.
            var cond = order.FirstOrDefault(b => b.Term.Opcode == 250 && (b.Term.Operands[1] == mergeId || b.Term.Operands[2] == mergeId));
            if (cond is null) continue;
            uint condVal = cond.Term.Operands[0];
            bool exitOnFalse = cond.Term.Operands[2] == mergeId;   // true target stays in loop
            uint bodyTarget = exitOnFalse ? cond.Term.Operands[1] : cond.Term.Operands[2];
            if (!def.TryGetValue(condVal, out var cmp) || cmp.Operands.Length != 4) continue;

            // Induction: a header phi feeding the comparison; find init (preheader value) and step.
            var phis = header.Insts.Where(x => x.Opcode == 245).ToList();
            uint indId = cmp.Operands[2], boundId = cmp.Operands[3];
            var ind = phis.FirstOrDefault(p => p.Operands[1] == indId);
            if (ind is null) continue;
            uint preLabel = Preds(header.Label).FirstOrDefault(p => p != contId);
            uint initId = PhiVal(ind, preLabel), latchVal = PhiVal(ind, contId);
            if (!def.TryGetValue(latchVal, out var stepInst) || stepInst.Opcode != 128) continue;   // i = i + step
            uint stepId = stepInst.Operands[2] == indId ? stepInst.Operands[3] : stepInst.Operands[2];
            if (!ConstInt(def, initId, out long init) || !ConstInt(def, boundId, out long bound) || !ConstInt(def, stepId, out long step) || step <= 0) continue;
            // Trip count for the supported comparisons (SLessThan 177 / ULessThan 176 / SLessThanEqual 179 / ULessThanEqual 178).
            long trip = cmp.Opcode switch { 176 or 177 => (bound - init + step - 1) / step, 178 or 179 => (bound - init) / step + 1, _ => -1 };
            if (trip < 1 || trip > 128) continue;

            UnrollLoop(m, fs, fe, firstLabel, blocks, order, header, latch, cond, mergeId, (int)trip, ind, phis, preLabel, contId, exitOnFalse ? 1 : 2);
            return true;
        }
        return false;

        static uint PhiVal(SpvInstr phi, uint label) { for (int i = 2; i + 1 < phi.Operands.Length; i += 2) if (phi.Operands[i + 1] == label) return phi.Operands[i]; return 0; }
    }

    static bool ConstInt(DefTable def, uint id, out long v)
    {
        v = 0;
        if (!def.TryGetValue(id, out var c) || c.Opcode != OpConstant || c.Operands.Length != 3) return false;
        v = unchecked((int)c.Operands[2]); return true;   // 32-bit
    }

    static void UnrollLoop(SpvModule m, int fs, int fe, int firstLabel, List<Blk> blocks, List<Blk> order, Blk header, Blk latch, Blk cond, uint mergeId, int trip, SpvInstr ind, List<SpvInstr> phis, uint preLabel, uint contId, int trueIdx)
    {
        uint nextId = m.Header[3];
        uint PhiV(SpvInstr phi, uint lbl) { for (int i = 2; i + 1 < phi.Operands.Length; i += 2) if (phi.Operands[i + 1] == lbl) return phi.Operands[i]; return 0; }
        void Remap(SpvInstr s, Dictionary<uint, uint> map) { s.Classify(out int ri, out bool[] isId); for (int k = 0; k < s.Operands.Length; k++) if (isId[k] && k != ri && map.TryGetValue(s.Operands[k], out uint nv)) s.Operands[k] = nv; }
        void FoldCond(Blk b) { b.Insts[^1] = new SpvInstr(249, [b.Term.Operands[trueIdx]]); }   // OpBranchConditional -> OpBranch true-target

        // The value each induction holds entering the next copy (iteration 1 uses the original body's results).
        var incoming = phis.Select(p => PhiV(p, contId)).ToArray();
        var prevLatch = latch;
        var newBlocks = new List<Blk>();

        for (int iter = 1; iter < trip; iter++)
        {
            var map = new Dictionary<uint, uint>();
            var copies = new List<Blk>();
            Blk newHeader = null!, newLatch = null!, newCond = null!;
            foreach (var lb in order)
            {
                uint newLabel = nextId++; map[lb.Label] = newLabel;
                var nb = new Blk { Label = newLabel };
                foreach (var ins in lb.Insts)
                {
                    if (ins.Opcode == 246) continue;                                   // drop OpLoopMerge
                    uint rid = ins.ResultId;
                    if (ins.Opcode == 245) { map[rid] = nextId++; continue; }           // clone phi id (dead, becomes a gap) but omit it
                    var c = new SpvInstr(ins.Opcode, (uint[])ins.Operands.Clone());
                    if (rid != 0) { uint nid = nextId++; map[rid] = nid; c.Classify(out int ri, out _); if (ri >= 0) c.Operands[ri] = nid; }
                    nb.Insts.Add(c);
                }
                copies.Add(nb);
                if (lb == header) newHeader = nb; if (lb == latch) newLatch = nb; if (lb == cond) newCond = nb;
            }
            var nextIncoming = phis.Select(p => map[PhiV(p, contId)]).ToArray();
            for (int pi = 0; pi < phis.Count; pi++) map[phis[pi].Operands[1]] = incoming[pi];   // induction uses -> previous iteration value
            map[header.Label] = header.Label;
            prevLatch.Insts[^1] = new SpvInstr(249, [newHeader.Label]);                          // previous latch -> this copy's header
            newLatch.Insts[^1] = new SpvInstr(249, [header.Label]);                              // this latch -> header (temp)
            FoldCond(newCond);
            foreach (var nb in copies) foreach (var ins in nb.Insts) Remap(ins, map);
            newBlocks.AddRange(copies);
            incoming = nextIncoming; prevLatch = newLatch;
        }

        // Fold the original condition and close the loop.
        FoldCond(cond);
        header.Insts.Remove(header.Merge!);                                                     // remove OpLoopMerge
        prevLatch.Insts[^1] = new SpvInstr(249, [mergeId]);                                      // last latch -> merge
        // Replace the original induction phis (now dead) with their preheader initializers, and thread uses.
        var closeMap = new Dictionary<uint, uint>();
        foreach (var p in phis) closeMap[p.Operands[1]] = PhiV(p, preLabel);
        foreach (var lb in order) { foreach (var p in phis) lb.Insts.Remove(p); foreach (var ins in lb.Insts) Remap(ins, closeMap); }
        foreach (var nb in newBlocks) foreach (var ins in nb.Insts) Remap(ins, closeMap);
        // Uses of the induction outside the loop take the final iteration's value.
        for (int pi = 0; pi < phis.Count; pi++) m.ReplaceUses(phis[pi].Operands[1], incoming[pi]);

        // Reassemble: original blocks (loop blocks now transformed) with the copies inserted right before merge.
        int mergeIdx = blocks.FindIndex(b => b.Label == mergeId);
        blocks.InsertRange(mergeIdx, newBlocks);
        RebuildFunction(m, fs, fe, firstLabel, blocks);
        m.Header[3] = nextId;
    }

    // spirv-opt's MergeReturn (ProcessStructured, shader path). Faithful port so the ids it allocates match by
    // construction. Runs before Inline so an early-return callee becomes single-return and is then inlinable.
    // Only touches functions with an early return (a return inside a construct or more than one return); every
    // such function is currently in a red shader, so this pass cannot regress a green one.
    static void MergeReturn(SpvModule m)
    {
        if (!HasShaderCapability(m)) return;   // Shader capability

        var fnRange = new Dictionary<uint, (int Start, int End)>();
        for (int i = 0; i < m.Instrs.Count; i++)
            if (m.Instrs[i].Opcode == OpFunction) { int e = i; while (e < m.Instrs.Count && m.Instrs[e].Opcode != OpFunctionEnd) e++; fnRange[m.Instrs[i].Operands[1]] = (i, e); }

        // Reachable call tree, BFS from entry points (spirv-opt's ProcessReachableCallTree order).
        var order = new List<uint>(); var seen = new HashSet<uint>(); var q = new Queue<uint>();
        foreach (var ins in m.Instrs) if (ins.Opcode == 15 && ins.Operands.Length >= 2 && seen.Add(ins.Operands[1])) q.Enqueue(ins.Operands[1]);
        while (q.Count > 0)
        {
            uint fn = q.Dequeue(); order.Add(fn);
            if (fnRange.TryGetValue(fn, out var r))
                for (int i = r.Start; i < r.End; i++) if (m.Instrs[i].Opcode == OpFunctionCall && seen.Add(m.Instrs[i].Operands[2])) q.Enqueue(m.Instrs[i].Operands[2]);
        }
        foreach (uint fn in order) if (fnRange.ContainsKey(fn)) ProcessFunctionMergeReturn(m, fn);
    }

    static void ProcessFunctionMergeReturn(SpvModule m, uint fnId)
    {
        int fs = -1, fe = -1;
        { int i = m.FindFunction(fnId); if (i >= 0) { fs = i; fe = i; while (m.Instrs[fe].Opcode != OpFunctionEnd) fe++; } }
        uint retType = m.Instrs[fs].Operands[0];

        // Parse blocks (skip OpFunction/params, up to OpFunctionEnd).
        int firstLabel = fs; while (m.Instrs[firstLabel].Opcode != OpLabel) firstLabel++;
        var blocks = new List<Blk>();
        for (int i = firstLabel; i < fe; i++)
            if (m.Instrs[i].Opcode == OpLabel) { var b = new Blk { Label = m.Instrs[i].Operands[0] }; int j = i + 1; while (j < fe && m.Instrs[j].Opcode != OpLabel) { b.Insts.Add(m.Instrs[j]); j++; } blocks.Add(b); i = j - 1; }

        var returnBlocks = blocks.Where(b => b.Term.Opcode is OpReturn or OpReturnValue).ToList();
        // Merge/continue and successor helpers.
        List<uint> Succs(Blk b) => b.Term.Opcode switch
        {
            249 => [b.Term.Operands[0]],
            250 => [b.Term.Operands[1], b.Term.Operands[2]],
            251 => Enumerable.Repeat(b.Term.Operands[1], 1).Concat(Enumerable.Range(0, (b.Term.Operands.Length - 2) / 2).Select(k => b.Term.Operands[3 + k * 2])).ToList(),
            _ => [],
        };

        // Skip if <=1 return that already ends the function outside any construct (spirv-opt's Process gate).
        // "In a construct" is the structured nesting depth (merge blocks belong to the enclosing construct),
        // not raw dominance: a loop-merge holding the return is dominated by an inner if-header via the break,
        // yet is not inside that if.
        if (returnBlocks.Count <= 1)
        {
            if (returnBlocks.Count == 0) return;
            uint rb = returnBlocks[0].Label;
            bool inConstruct = false;
            var depth = new List<uint> { 0 };
            foreach (var b in StructOrder())
            {
                while (depth.Count > 1 && depth[^1] == b.Label) depth.RemoveAt(depth.Count - 1);
                if (b.Label == rb) { inConstruct = depth.Count > 1; break; }
                if (b.Merge is { } mg) depth.Add(mg.Operands[0]);
            }
            bool endsFn = blocks[^1] == returnBlocks[0];
            if (!inConstruct && endsFn) return;
        }

        var def = m.DefMap();
        bool isVoid = def.TryGetValue(retType, out var rt) && rt.Opcode == OpTypeVoid;
        uint nextId = m.Header[3];
        var newGlobals = new List<SpvInstr>();
        var pool = new Dictionary<string, uint>();
        foreach (var ins in m.Instrs)
        {
            if (ins.Opcode == 20) pool["bool"] = ins.Operands[0];                                   // OpTypeBool
            if (ins.Opcode == 21 && ins.Operands.Length == 3 && ins.Operands[1] == 32 && ins.Operands[2] == 0) pool["uint"] = ins.Operands[0];
            if (ins.Opcode == 41) pool["true:" + ins.Operands[0]] = ins.Operands[1];                // OpConstantTrue
            if (ins.Opcode == 42) pool["false:" + ins.Operands[0]] = ins.Operands[1];               // OpConstantFalse
            if (ins.Opcode == OpConstant && ins.Operands.Length == 3 && ins.Operands[2] == 0) pool["uint0:" + ins.Operands[0]] = ins.Operands[1];
            if (ins.Opcode == 1) pool["undef:" + ins.Operands[0]] = ins.Operands[1];                // OpUndef
            if (ins.Opcode == OpTypePointer && ins.Operands.Length >= 3 && ins.Operands[1] == 7) pool["ptr:" + ins.Operands[2]] = ins.Operands[0];
        }
        uint BoolType() { if (pool.TryGetValue("bool", out var id)) return id; id = nextId++; newGlobals.Add(new SpvInstr(20, [id])); pool["bool"] = id; return id; }
        uint UintType() { if (pool.TryGetValue("uint", out var id)) return id; id = nextId++; newGlobals.Add(new SpvInstr(21, [id, 32, 0])); pool["uint"] = id; return id; }
        uint PtrTo(uint t) { if (pool.TryGetValue("ptr:" + t, out var id)) return id; id = nextId++; newGlobals.Add(new SpvInstr(OpTypePointer, [id, 7, t])); pool["ptr:" + t] = id; return id; }
        uint TrueConst() { uint bt = BoolType(); if (pool.TryGetValue("true:" + bt, out var id)) return id; id = nextId++; newGlobals.Add(new SpvInstr(41, [bt, id])); pool["true:" + bt] = id; return id; }
        uint FalseConst() { uint bt = BoolType(); if (pool.TryGetValue("false:" + bt, out var id)) return id; id = nextId++; newGlobals.Add(new SpvInstr(42, [bt, id])); pool["false:" + bt] = id; return id; }
        uint Uint0() { uint ut = UintType(); if (pool.TryGetValue("uint0:" + ut, out var id)) return id; id = nextId++; newGlobals.Add(new SpvInstr(OpConstant, [ut, id, 0])); pool["uint0:" + ut] = id; return id; }
        uint Undef(uint t) { if (pool.TryGetValue("undef:" + t, out var id)) return id; id = nextId++; newGlobals.Add(new SpvInstr(1, [t, id])); pool["undef:" + t] = id; return id; }

        // ---- ProcessStructured ----
        Blk BlkOf(uint lbl) => blocks.First(b => b.Label == lbl);
        // Structured successors: merge (+continue) first, then real successors. Structured order = reverse
        // post-order of that graph. Recomputed on demand as the CFG changes.
        List<uint> StructSuccs(Blk b) { var r = new List<uint>(); if (b.Merge is { } mg) { r.Add(mg.Operands[0]); if (mg.Opcode == 246) r.Add(mg.Operands[1]); } r.AddRange(Succs(b)); return r; }
        List<Blk> StructOrder()
        {
            var post = new List<Blk>(); var vis = new HashSet<uint>();
            void Dfs(Blk b) { if (!vis.Add(b.Label)) return; foreach (var s in StructSuccs(b)) if (blocks.Any(x => x.Label == s)) Dfs(BlkOf(s)); post.Add(b); }
            Dfs(blocks[0]); post.Reverse(); return post;
        }
        // Immediate dominators keyed by label (Cooper/Harvey/Kennedy over the real CFG).
        Dictionary<uint, uint> Idoms()
        {
            var ord = new List<Blk>(); var vis = new HashSet<uint>();
            void Dfs(Blk b) { if (!vis.Add(b.Label)) return; foreach (var s in Succs(b)) if (blocks.Any(x => x.Label == s)) Dfs(BlkOf(s)); ord.Add(b); }
            Dfs(blocks[0]); ord.Reverse();
            var rpoIndex = new Dictionary<uint, int>(); for (int i = 0; i < ord.Count; i++) rpoIndex[ord[i].Label] = i;
            var preds = new Dictionary<uint, List<uint>>(); foreach (var b in blocks) preds[b.Label] = [];
            foreach (var b in blocks) foreach (var s in Succs(b)) if (preds.ContainsKey(s)) preds[s].Add(b.Label);
            var idom = new Dictionary<uint, uint>(); idom[ord[0].Label] = ord[0].Label;
            bool changed = true;
            while (changed) { changed = false; for (int i = 1; i < ord.Count; i++) { uint b = ord[i].Label, nd = 0; foreach (var p in preds[b]) { if (!rpoIndex.ContainsKey(p)) continue; if (!idom.ContainsKey(p)) continue; if (nd == 0) nd = p; else { uint a = p, c = nd; while (a != c) { while (rpoIndex[a] > rpoIndex[c]) a = idom[a]; while (rpoIndex[c] > rpoIndex[a]) c = idom[c]; } nd = a; } } if (nd != 0 && (!idom.TryGetValue(b, out var cur) || cur != nd)) { idom[b] = nd; changed = true; } } }
            return idom;
        }
        bool Dominates(uint a, uint b, Dictionary<uint, uint> idom) { for (uint x = b; ; x = idom[x]) { if (x == a) return true; if (!idom.TryGetValue(x, out var nx) || nx == x) return false; } }

        var origIdom = Idoms();

        // AddSingleCaseSwitchAroundFunction.
        uint finalRetLabel = nextId++;
        var finalRet = new Blk { Label = finalRetLabel };
        uint returnValueVar = 0;
        if (!isVoid)
        {
            uint ptr = PtrTo(retType); returnValueVar = nextId++;
            blocks[0].Insts.Insert(0, new SpvInstr(OpVariable, [ptr, returnValueVar, 7]));
            uint loadId = nextId++;
            finalRet.Insts.Add(new SpvInstr(OpLoad, [retType, loadId, returnValueVar]));
            finalRet.Insts.Add(new SpvInstr(OpReturnValue, [loadId]));
        }
        else finalRet.Insts.Add(new SpvInstr(OpReturn, []));
        blocks.Add(finalRet);
        var entry = blocks[0];
        int svar = 0; while (svar < entry.Insts.Count && entry.Insts[svar].Opcode == OpVariable) svar++;
        uint bodyLabel = nextId++;
        var bodyBlk = new Blk { Label = bodyLabel };
        bodyBlk.Insts.AddRange(entry.Insts.GetRange(svar, entry.Insts.Count - svar));
        entry.Insts.RemoveRange(svar, entry.Insts.Count - svar);
        uint uint0 = Uint0();
        entry.Insts.Add(new SpvInstr(247, [finalRetLabel, 0]));       // OpSelectionMerge finalRet None
        entry.Insts.Add(new SpvInstr(251, [uint0, bodyLabel]));       // OpSwitch uint0 default=body (no cases)
        blocks.Insert(1, bodyBlk);
        // The entry's outgoing edges moved to the body block, so successor phis that named the entry as a
        // predecessor now come from the body block.
        foreach (var bb in blocks) foreach (var ph in bb.Insts) if (ph.Opcode == 245) for (int k = 3; k < ph.Operands.Length; k += 2) if (ph.Operands[k] == entry.Label) ph.Operands[k] = bodyLabel;

        // A structured-nesting state entry (spirv-opt's StructuredControlState): the merge inst that a break
        // targets and the merge inst of the current construct (both null for the placeholder switch level).
        uint MergeId(SpvInstr? mi) => mi?.Operands[0] ?? finalRetLabel;
        uint ContId(SpvInstr? mi) => mi is { Opcode: 246 } ? mi.Operands[1] : 0;
        List<(SpvInstr? Break, SpvInstr? Cur)> GenState(List<(SpvInstr? Break, SpvInstr? Cur)> st, Blk b)
        {
            if (b.Merge is not { } mg) return st;
            if (mg.Opcode == 246) st.Add((mg, mg));                                                     // loop
            else if (b.Term.Opcode == 251) st.Add(st[^1].Break is { Opcode: 246 } ? (st[^1].Break, mg) : (mg, mg));   // switch
            else st.Add((st[^1].Break, mg));                                                            // selection
            return st;
        }

        // First pass: turn each return into `store flag; [store value;] branch <break merge>`.
        uint flagVar = 0;
        var newEdges = new Dictionary<uint, HashSet<uint>>();   // merge block -> predecessor blocks newly branching to it
        var returnBlockLabels = new HashSet<uint>();
        var order = StructOrder();
        var stack = new List<(SpvInstr? Break, SpvInstr? Cur)> { (null, null) };
        foreach (var b in order)
        {
            if (b.Label == finalRetLabel) continue;
            if (stack.Count > 1 && MergeId(stack[^1].Cur) == b.Label) stack.RemoveAt(stack.Count - 1);

            if (b.Term.Opcode is OpReturn or OpReturnValue)
            {
                if (flagVar == 0)
                {
                    uint fc = FalseConst(); uint bptr = PtrTo(BoolType());   // bool type, then false const, then Function ptr (spirv-opt order)
                    flagVar = nextId++;
                    blocks[0].Insts.Insert(0, new SpvInstr(OpVariable, [bptr, flagVar, 7, fc]));
                }
                uint brk = MergeId(stack[^1].Break);
                b.Insts.Insert(b.Insts.Count - 1, new SpvInstr(OpStore, [flagVar, TrueConst()]));
                if (b.Term.Opcode == OpReturnValue) b.Insts.Insert(b.Insts.Count - 1, new SpvInstr(OpStore, [returnValueVar, b.Term.Operands[0]]));
                (newEdges.TryGetValue(brk, out var se) ? se : newEdges[brk] = []).Add(b.Label);
                b.Insts[^1] = new SpvInstr(249, [brk]);   // OpBranch
                returnBlockLabels.Add(b.Label);
            }
            else if (b.Term.Opcode == 255)
            {
                // ProcessStructuredBlock treats an OpUnreachable block in the structured order (an unreachable
                // merge) like a return without a value: it now breaks to the enclosing break merge. It is not one
                // of the original return blocks, so it is never predicated.
                uint brk = MergeId(stack[^1].Break);
                UpdatePhis(b.Label, brk);
                (newEdges.TryGetValue(brk, out var se) ? se : newEdges[brk] = []).Add(b.Label);
                b.Insts[^1] = new SpvInstr(249, [brk]);
            }
            GenState(stack, b);
        }

        // Second pass: predicate blocks reached after a return that broke to an inner (loop/switch) merge, so
        // the post-return code runs only when no return happened (spirv-opt PredicateBlocks/BreakFromConstruct).
        List<uint> PredsOf(uint label) { var r = new List<uint>(); foreach (var bb in blocks) if (Succs(bb).Contains(label)) r.Add(bb.Label); return r; }
        Blk SplitBlk(Blk bb, uint newLabel, int at) { var nb = new Blk { Label = newLabel }; nb.Insts.AddRange(bb.Insts.GetRange(at, bb.Insts.Count - at)); bb.Insts.RemoveRange(at, bb.Insts.Count - at); blocks.Insert(blocks.IndexOf(bb) + 1, nb); return nb; }
        int PhiEnd(Blk bb) { int i = 0; while (i < bb.Insts.Count && bb.Insts[i].Opcode == 245) i++; return i; }
        void UpdatePhis(uint newSource, uint targetLabel)
        {
            var t = BlkOf(targetLabel);
            foreach (var ins in t.Insts) if (ins.Opcode == 245) { uint u = Undef(ins.Operands[0]); ins.Operands = [.. ins.Operands, u, newSource]; }
        }

        // SplitLoopHeader: a loop header that must be predicated is split into a preheader (keeps the loop's
        // entry phis) and the real header (keeps the back-edge phis). Returns the new header label.
        uint SplitLoopHeader(Blk hb)
        {
            uint newHeaderId = nextId++;
            var preds = PredsOf(hb.Label);
            uint contId = ContId(hb.Merge);
            uint latch = contId != 0 ? contId : preds.LastOrDefault(p => p != finalRetLabel);
            int at = PhiEnd(hb);
            var newHeader = SplitBlk(hb, newHeaderId, at);
            foreach (var ph in hb.Insts.Where(i => i.Opcode == 245).ToList())
            {
                var pre = new List<uint>(); var hdr = new List<uint>();
                for (int i = 2; i < ph.Operands.Length; i += 2) { if (ph.Operands[i + 1] == latch) { hdr.Add(ph.Operands[i]); hdr.Add(ph.Operands[i + 1]); } else { pre.Add(ph.Operands[i]); pre.Add(ph.Operands[i + 1]); } }
                uint headVal;
                if (pre.Count > 2) { uint np = nextId++; newHeader.Insts.Insert(0, new SpvInstr(245, [ph.Operands[0], np, .. pre])); headVal = np; }
                else headVal = pre[0];
                ph.Operands = [ph.Operands[0], ph.Operands[1], .. hdr, headVal, hb.Label];
                hb.Insts.Remove(ph); newHeader.Insts.Insert(0, ph);
            }
            hb.Insts.Add(new SpvInstr(249, [newHeaderId]));   // OpBranch preheader -> new header
            foreach (var bb in blocks) { var t = bb.Term; for (int i = 0; i < t.Operands.Length; i++) if (t.Operands[i] == hb.Label && bb.Label != hb.Label && Succs(bb).Contains(hb.Label) && bb.Label == latch) t.Operands[i] = newHeaderId; }
            return newHeaderId;
        }

        void BreakFromConstruct(Blk block, SpvInstr breakMergeInst)
        {
            if (block.Merge is { Opcode: 246 }) { uint nh = SplitLoopHeader(block); block = BlkOf(nh); }
            uint mergeId = breakMergeInst.Operands[0];
            var mergeBlk = BlkOf(mergeId);
            if (mergeBlk.Merge is { Opcode: 246 }) SplitLoopHeader(mergeBlk);
            int at = PhiEnd(block);
            uint oldBodyId = nextId++;
            var oldBody = SplitBlk(block, oldBodyId, at);
            if (returnBlockLabels.Contains(block.Label)) returnBlockLabels.Add(oldBodyId);
            if (breakMergeInst.Opcode == 246 && breakMergeInst.Operands[1] == block.Label) breakMergeInst.Operands[1] = oldBodyId;
            uint loadId = nextId++;
            block.Insts.Add(new SpvInstr(OpLoad, [BoolType(), loadId, flagVar]));
            block.Insts.Add(new SpvInstr(247, [oldBodyId, 0]));                        // OpSelectionMerge oldBody None
            block.Insts.Add(new SpvInstr(250, [loadId, mergeId, oldBodyId]));          // OpBranchConditional
            if (!(newEdges.TryGetValue(mergeId, out var ne) ? ne : newEdges[mergeId] = []).Add(block.Label)) newEdges[mergeId].Add(oldBodyId);
            UpdatePhis(block.Label, mergeId);
        }

        void PredicateBlocks(Blk returnBlock, HashSet<uint> predicated)
        {
            if (!predicated.Add(returnBlock.Label)) return;
            uint blockLabel = Succs(returnBlock)[0];
            // Rebuild the state up to this return block by walking the structured order.
            var st = new List<(SpvInstr? Break, SpvInstr? Cur)> { (null, null) };
            foreach (var bb in order) { if (bb.Label == finalRetLabel) continue; if (st.Count > 1 && MergeId(st[^1].Cur) == bb.Label) st.RemoveAt(st.Count - 1); if (bb == returnBlock) break; GenState(st, bb); }
            int si = st.Count - 1;
            if (blockLabel == MergeId(st[si].Cur)) si--;
            else if (blockLabel == MergeId(st[si].Break)) while (si >= 0 && MergeId(st[si].Break) == blockLabel) si--;
            while (blockLabel != 0 && blockLabel != finalRetLabel)
            {
                if (!predicated.Add(blockLabel)) break;
                var breakMergeInst = st[si].Break!;
                uint mergeBlockId = breakMergeInst.Operands[0];
                while (si >= 0 && MergeId(st[si].Break) == mergeBlockId) si--;
                BreakFromConstruct(BlkOf(blockLabel), breakMergeInst);
                blockLabel = mergeBlockId;
            }
        }

        var predicated = new HashSet<uint>();
        {
            var st = new List<(SpvInstr? Break, SpvInstr? Cur)> { (null, null) };
            foreach (var b in order.ToList())
            {
                if (st.Count > 1 && MergeId(st[^1].Cur) == b.Label) st.RemoveAt(st.Count - 1);
                if (returnBlockLabels.Contains(b.Label)) PredicateBlocks(b, predicated);
                GenState(st, b);
            }
        }

        // AddNewPhiNodes: values that used to dominate a block but no longer do (because of the new edges) need
        // a phi. Walk from each block's original immediate dominator up to its current one.
        var curIdom = Idoms();
        uint ResultTypeOf(SpvInstr ins) { ins.Classify(out int ri, out _); return ri >= 1 ? ins.Operands[ri - 1] : 0; }
        // Maintained user map + block-of-instruction over the function, so finding the users of a value is
        // O(uses) rather than a full-function scan per CreatePhiNodesForInst call (which was O(insts^2)).
        var resultBlock = new Dictionary<uint, uint>();
        var funcUsers = new Dictionary<uint, List<SpvInstr>>();
        var blockOf = new Dictionary<SpvInstr, uint>();
        void IndexUser(SpvInstr ins, uint blk)
        {
            blockOf[ins] = blk;
            ins.Classify(out int ri, out bool[] isId);
            for (int k = 0; k < ins.Operands.Length; k++) if (isId[k] && k != ri) (funcUsers.TryGetValue(ins.Operands[k], out var l) ? l : funcUsers[ins.Operands[k]] = []).Add(ins);
        }
        foreach (var bb in blocks) foreach (var ins in bb.Insts) { uint r = ins.ResultId; if (r != 0) resultBlock[r] = bb.Label; IndexUser(ins, bb.Label); }

        void CreatePhiNodesForInst(uint mergeLabel, SpvInstr inst)
        {
            uint rid = inst.ResultId;
            if (rid == 0 || !resultBlock.TryGetValue(rid, out uint instBlk)) return;
            var toUpdate = new List<SpvInstr>();
            foreach (var user in funcUsers.TryGetValue(rid, out var us) ? us.ToList() : [])
            {
                uint userBlk;
                if (user.Opcode == 245) { userBlk = 0; for (int i = 3; i < user.Operands.Length; i += 2) if (user.Operands[i - 1] == rid) { userBlk = user.Operands[i]; break; } }
                else userBlk = blockOf.GetValueOrDefault(user);
                if (userBlk != 0 && !Dominates(instBlk, userBlk, curIdom)) toUpdate.Add(user);
            }
            if (toUpdate.Count == 0) return;

            uint typeId = ResultTypeOf(inst);
            var typeInst = def.GetValueOrDefault(typeId) ?? newGlobals.FirstOrDefault(x => x.ResultId == typeId);
            uint phiResult;
            var merge = BlkOf(mergeLabel);
            if (typeInst is { Opcode: OpTypePointer } && typeInst.Operands[1] is not (4 or 12))   // pointer, no VariablePointers -> regenerate
            {
                phiResult = nextId++;
                var clone = new SpvInstr(inst.Opcode, (uint[])inst.Operands.Clone());
                clone.Classify(out int cri, out _);
                clone.Operands[cri] = phiResult;
                merge.Insts.Insert(PhiEnd(merge), clone);
                resultBlock[phiResult] = mergeLabel;
                IndexUser(clone, mergeLabel);
                clone.Classify(out int cri2, out bool[] cIsId);
                foreach (var uid in clone.Operands.Where((o, i) => cIsId[i] && i != cri2).ToList())
                    if (resultBlock.TryGetValue(uid, out uint ub) && !Dominates(ub, mergeLabel, curIdom)) CreatePhiNodesForInst(mergeLabel, def[uid]);
            }
            else
            {
                uint undef = Undef(typeId);
                phiResult = nextId++;
                var ops = new List<uint> { typeId, phiResult };
                foreach (uint p in PredsOf(mergeLabel)) { ops.Add(newEdges.TryGetValue(mergeLabel, out var ne) && ne.Contains(p) ? undef : rid); ops.Add(p); }
                var phiInst = new SpvInstr(245, ops.ToArray());
                merge.Insts.Insert(PhiEnd(merge), phiInst);
                resultBlock[phiResult] = mergeLabel;
                IndexUser(phiInst, mergeLabel);
            }
            var phiUsers = funcUsers.TryGetValue(phiResult, out var pu) ? pu : funcUsers[phiResult] = [];
            var ridUsers = funcUsers.GetValueOrDefault(rid);
            foreach (var user in toUpdate) { user.Classify(out int ri, out bool[] isId); for (int i = 0; i < user.Operands.Length; i++) if (isId[i] && i != ri && user.Operands[i] == rid) user.Operands[i] = phiResult; ridUsers?.Remove(user); phiUsers.Add(user); }
        }

        foreach (var bb in StructOrder())
        {
            if (!curIdom.TryGetValue(bb.Label, out uint dom)) continue;
            if (!origIdom.TryGetValue(bb.Label, out uint start)) continue;
            for (uint cur = start; cur != 0 && cur != dom && curIdom.ContainsKey(cur); )
            {
                foreach (var inst in BlkOf(cur).Insts.ToList()) CreatePhiNodesForInst(bb.Label, inst);
                if (cur == curIdom[cur]) break;
                cur = curIdom[cur];
            }
        }

        // Rebuild the function's instruction range from the transformed block list.
        var rebuilt = new List<SpvInstr>();
        for (int i = fs; i < firstLabel; i++) rebuilt.Add(m.Instrs[i]);   // OpFunction + params
        foreach (var b in blocks) { rebuilt.Add(new SpvInstr(OpLabel, [b.Label])); rebuilt.AddRange(b.Insts); }
        m.Instrs.RemoveRange(fs, fe - fs);
        m.Instrs.InsertRange(fs, rebuilt);
        if (newGlobals.Count > 0) { int ff = m.Instrs.FindIndex(x => x.Opcode == OpFunction); m.Instrs.InsertRange(ff, newGlobals); }
        m.Header[3] = nextId;
    }

    // Removes functions unreachable from any entry point (spirv-opt's EliminateDeadFunctions), e.g. helpers
    // whose every call site has been inlined.
    static void EliminateDeadFunctions(SpvModule m)
    {
        var ranges = new Dictionary<uint, (int Start, int End)>();
        for (int i = 0; i < m.Instrs.Count; i++)
            if (m.Instrs[i].Opcode == OpFunction) { int e = i; while (e < m.Instrs.Count && m.Instrs[e].Opcode != OpFunctionEnd) e++; ranges[m.Instrs[i].Operands[1]] = (i, e); }

        var live = new HashSet<uint>();
        var work = new Stack<uint>();
        foreach (var ins in m.Instrs) if (ins.Opcode == 15 && ins.Operands.Length >= 2) { if (live.Add(ins.Operands[1])) work.Push(ins.Operands[1]); }   // OpEntryPoint
        while (work.Count > 0)
        {
            uint fn = work.Pop();
            if (!ranges.TryGetValue(fn, out var r)) continue;
            for (int i = r.Start; i <= r.End; i++)
                if (m.Instrs[i].Opcode == OpFunctionCall && live.Add(m.Instrs[i].Operands[2])) work.Push(m.Instrs[i].Operands[2]);
        }

        var kill = new HashSet<SpvInstr>();
        foreach (var (fn, r) in ranges) if (!live.Contains(fn)) for (int i = r.Start; i <= r.End; i++) kill.Add(m.Instrs[i]);
        if (kill.Count > 0) m.Instrs.RemoveAll(kill.Contains);
    }

    // spirv-opt's exhaustive inliner, narrowed to the common case: a non-recursive callee with a single
    // basic block (straight-line helper). Inlines one call per invocation (loop to fixpoint). Id allocation
    // order matches inline_pass.cpp GenInlineCode: clone locals, create the return var (find-or-create a
    // Function pointer to the return type first), then take fresh ids for the callee's label+body in order.
    // spirv-opt's InlineExhaustive only processes the reachable call tree, and because it fully inlines each
    // function (restarting at the inlined body so nested calls are inlined too) BEFORE adding callees to the
    // work queue, every call collapses into the entry points and the helper functions are never processed --
    // they just become dead and are removed later. So allocate inline ids ONLY for calls inside entry points.
    // A callee parsed once for inlining: its instruction slice (OpFunction..return, no OpFunctionEnd) plus the
    // metadata the expander needs. Immutable during inlining (callees are cloned, never mutated).
    sealed class Callee
    {
        public SpvInstr[] Insts;
        public uint RetType;
        public uint[] ParamIds;
        public uint EntryLabelId;
        public int Term;                        // index of the (single) return instruction
        public int[] VarIdx;                    // entry-block OpVariable indices
        public List<(int Label, int End)> Blocks = [];
        public bool Inlinable;
        public bool HasAbort;                   // OpKill and the other aborts besides OpUnreachable
        public bool AbortBlock;                 // some block ends in an abort (OpUnreachable included)

        public Callee(SpvInstr[] insts, uint voidType)
        {
            Insts = insts;
            RetType = insts[0].Operands[0];
            var pids = new List<uint>(); int entry = -1;
            for (int i = 1; i < insts.Length; i++)
            {
                if (insts[i].Opcode == OpFunctionParameter) pids.Add(insts[i].Operands[1]);
                else if (insts[i].Opcode == OpLabel) { entry = i; break; }
            }
            ParamIds = [.. pids];
            EntryLabelId = entry >= 0 ? insts[entry].Operands[0] : 0;
            Term = insts.Length - 1;
            var vars = new List<int>();
            for (int i = entry + 1; i < insts.Length && insts[i].Opcode == OpVariable; i++) vars.Add(i);
            VarIdx = [.. vars];
            for (int i = entry < 0 ? insts.Length : entry; i < insts.Length; i++)
                if (insts[i].Opcode == OpLabel) { int e = i; while (e + 1 < insts.Length && insts[e + 1].Opcode != OpLabel) e++; Blocks.Add((i, e)); }
            int returns = 0;
            foreach (var ins in insts) { if (ins.Opcode is OpReturn or OpReturnValue) returns++; else if (ins.Opcode is 252 or 4416 or 4448 or 4449 or 5294) HasAbort = true; }
            foreach (var (_, e) in Blocks) if (insts[e].Opcode is 252 or 255 or 4416 or 4448 or 4449 or 5294) AbortBlock = true;
            // Exactly one return, at the end. An abort only matters at a call from a continue construct (CanInline).
            Inlinable = entry >= 0 && returns == 1 && insts[^1].Opcode is OpReturn or OpReturnValue;
        }
    }

    // StructuredCFGAnalysis::FindFuncsCalledFromContinue: functions called (directly or transitively) from a
    // block of some loop's continue construct -- the blocks reachable from the continue target without passing
    // back through the loop header.
    static HashSet<uint> FuncsCalledFromContinue(SpvModule m)
    {
        var direct = new List<uint>();
        var callsOf = new Dictionary<uint, List<uint>>();
        for (int f = 0; f < m.Instrs.Count; f++)
        {
            if (m.Instrs[f].Opcode != OpFunction) continue;
            uint fnId = m.Instrs[f].Operands[1];
            int fe = f; while (m.Instrs[fe].Opcode != OpFunctionEnd) fe++;
            var blocks = ParseBlocks(m, f, fe, out _);
            var byLabel = blocks.ToDictionary(b => b.Label);
            var calls = callsOf[fnId] = [];
            foreach (var b in blocks) foreach (var ins in b.Insts) if (ins.Opcode == OpFunctionCall) calls.Add(ins.Operands[2]);
            foreach (var h in blocks)
            {
                if (h.Merge is not { Opcode: 246 } lm) continue;   // OpLoopMerge
                var seen = new HashSet<uint>();
                var stack = new Stack<uint>(); stack.Push(lm.Operands[1]);
                while (stack.Count > 0)
                {
                    uint l = stack.Pop();
                    if (l == h.Label || l == lm.Operands[0] || !seen.Add(l) || !byLabel.TryGetValue(l, out var b)) continue;
                    foreach (var ins in b.Insts) if (ins.Opcode == OpFunctionCall) direct.Add(ins.Operands[2]);
                    foreach (var sl in BlockSuccs(b)) stack.Push(sl);
                }
            }
            f = fe;
        }
        var result = new HashSet<uint>();
        var q = new Queue<uint>(direct);
        while (q.Count > 0) { uint fn = q.Dequeue(); if (!result.Add(fn)) continue; if (callsOf.TryGetValue(fn, out var cs)) foreach (var c in cs) q.Enqueue(c); }
        return result;
    }

    static List<uint> BlockSuccsOf(List<SpvInstr> body)
    {
        var t = body[^1];
        return t.Opcode switch
        {
            249 => [t.Operands[0]],
            250 => [t.Operands[1], t.Operands[2]],
            251 => [.. Enumerable.Range(0, (t.Operands.Length - 1) / 2).Select(k => t.Operands[1 + 2 * k])],
            _ => [],
        };
    }

    static SpvInstr RemapInst(SpvInstr src, Dictionary<uint, uint> map)
    {
        src.Classify(out _, out bool[] isId);
        var ops = (uint[])src.Operands.Clone();
        for (int k = 0; k < ops.Length; k++) if (isId[k] && map.TryGetValue(ops[k], out uint nv)) ops[k] = nv;
        return new SpvInstr(src.Opcode, ops);
    }

    // spirv-opt's InlineExhaustive, as a single forward pass that expands each entry function in place (O(n),
    // no re-parsing). Callees are inlined depth-first left-to-right, allocating ids in exactly the order the
    // old per-call GenInlineCode port did, so output is byte-identical to it -- just without the O(n^2) driver.
    static void Inline(SpvModule m)
    {
        var entryFns = new List<uint>();
        foreach (var ins in m.Instrs) if (ins.Opcode == 15 && ins.Operands.Length >= 2 && !entryFns.Contains(ins.Operands[1])) entryFns.Add(ins.Operands[1]);
        if (entryFns.Count == 0) return;

        uint voidType = 0; foreach (var ins in m.Instrs) if (ins.Opcode == OpTypeVoid) { voidType = ins.Operands[0]; break; }

        var callees = new Dictionary<uint, Callee>();
        for (int i = 0; i < m.Instrs.Count; i++)
            if (m.Instrs[i].Opcode == OpFunction)
            {
                int e = i; while (e < m.Instrs.Count && m.Instrs[e].Opcode != OpFunctionEnd) e++;
                var slice = new SpvInstr[e - i];
                for (int k = i; k < e; k++) slice[k - i] = m.Instrs[k];
                callees[m.Instrs[i].Operands[1]] = new Callee(slice, voidType);
                i = e;
            }

        var calledFromContinue = FuncsCalledFromContinue(m);
        // DecorationManager::CloneDecorations: the direct decorations of each id, cloned onto inlined copies and
        // appended after the module's existing annotations.
        var decoOf = new Dictionary<uint, List<SpvInstr>>();
        foreach (var ins in m.Instrs)
            if (ins.Opcode is OpDecorate or 332 or 5632 && ins.Operands.Length >= 1)
                (decoOf.TryGetValue(ins.Operands[0], out var dl) ? dl : decoOf[ins.Operands[0]] = []).Add(ins);
        var newDecos = new List<SpvInstr>();
        // Function::IsRecursive: a function that can reach itself through calls is never inlined. GLSL input
        // can't recurse (the frontend rejects it), but Optimize() also takes arbitrary SPIR-V.
        var callGraph = callees.ToDictionary(kv => kv.Key, kv => kv.Value.Insts.Where(x => x.Opcode == OpFunctionCall).Select(x => x.Operands[2]).ToList());
        var recursive = new HashSet<uint>();
        foreach (uint fn in callGraph.Keys)
        {
            var seen = new HashSet<uint>(); var stack = new Stack<uint>(callGraph[fn]);
            while (stack.Count > 0)
            {
                uint c = stack.Pop();
                if (c == fn) { recursive.Add(fn); break; }
                if (seen.Add(c) && callGraph.TryGetValue(c, out var next)) foreach (uint n in next) stack.Push(n);
            }
        }
        uint nextId = m.Header[3];
        var fnPtr = new Dictionary<uint, uint>();
        foreach (var ins in m.Instrs) if (ins.Opcode == OpTypePointer && ins.Operands.Length >= 3 && ins.Operands[1] == 7) fnPtr[ins.Operands[2]] = ins.Operands[0];
        var newGlobals = new List<SpvInstr>();
        uint PtrFor(uint pointee) { if (fnPtr.TryGetValue(pointee, out uint id)) return id; id = nextId++; fnPtr[pointee] = id; newGlobals.Add(new SpvInstr(OpTypePointer, [id, 7, pointee])); return id; }

        // CloneDecorations, done at the moment spirv-opt does it; a clone's own decorations become cloneable too.
        void CloneDecorations(uint from, uint to)
        {
            if (!decoOf.TryGetValue(from, out var ds)) return;
            foreach (var d in ds.ToArray())
            {
                var copy = (uint[])d.Operands.Clone(); copy[0] = to;
                var nd = new SpvInstr(d.Opcode, copy);
                newDecos.Add(nd);
                (decoOf.TryGetValue(to, out var tl) ? tl : decoOf[to] = []).Add(nd);
            }
        }
        static bool IsSameBlockOp(SpvInstr i) => i.Opcode is 86 or 100;   // OpSampledImage, OpImage
        static bool IsAbort(int op) => op is 252 or 255 or 4416 or 4448 or 4449 or 5294;
        // In-operand ids (everything but the result type and result id).
        static void ForEachInId(SpvInstr ins, Func<uint, uint> f)
        {
            ins.Classify(out int ri, out bool[] isId);
            for (int k = 0; k < ins.Operands.Length; k++)
                if (isId[k] && k != ri && !(ri >= 1 && k == ri - 1)) ins.Operands[k] = f(ins.Operands[k]);
        }

        foreach (uint efn in entryFns)
        {
            int fs = -1, fe = -1;
            { int i = m.FindFunction(efn); if (i >= 0) { fs = i; fe = i; while (m.Instrs[fe].Opcode != OpFunctionEnd) fe++; } }
            if (fs < 0) continue;
            int firstLabel = fs; while (firstLabel < fe && m.Instrs[firstLabel].Opcode != OpLabel) firstLabel++;
            if (firstLabel >= fe) continue;

            bool CanInline(uint fnId) => fnId != efn && !recursive.Contains(fnId) && callees.TryGetValue(fnId, out var c) && c.Inlinable
                && !(c.HasAbort && calledFromContinue.Contains(fnId));

            // The function as a list of blocks (label instruction + body).
            var blocks = new List<(SpvInstr Label, List<SpvInstr> Insts)>();
            for (int i = firstLabel; i < fe; i++)
            {
                if (m.Instrs[i].Opcode != OpLabel) continue;
                var body = new List<SpvInstr>();
                int k = i + 1; while (k < fe && m.Instrs[k].Opcode != OpLabel) body.Add(m.Instrs[k++]);
                blocks.Add((m.Instrs[i], body));
                i = k - 1;
            }
            var blockById = new Dictionary<uint, List<SpvInstr>>();
            foreach (var (l, ins) in blocks) blockById[l.Operands[0]] = ins;

            // InlinePass::GenInlineCode for the call at callBlock[callIdx].
            List<(SpvInstr Label, List<SpvInstr> Insts)> GenInlineCode((SpvInstr Label, List<SpvInstr> Insts) callBlock, int callIdx, List<SpvInstr> newVars)
            {
                var callInst = callBlock.Insts[callIdx];
                var cal = callees[callInst.Operands[2]];
                var C = cal.Insts;
                var map = new Dictionary<uint, uint>();
                for (int p = 0; p < cal.ParamIds.Length; p++) map[cal.ParamIds[p]] = callInst.Operands[3 + p];
                // CloneAndMapLocals.
                foreach (int vi in cal.VarIdx)
                {
                    uint nid = nextId++;
                    CloneDecorations(C[vi].Operands[1], nid);
                    var v = new SpvInstr(C[vi].Opcode, (uint[])C[vi].Operands.Clone()); v.Operands[1] = nid;
                    map[C[vi].Operands[1]] = nid;
                    newVars.Add(v);
                }
                uint callBlockId = callBlock.Label.Operands[0];
                map[cal.EntryLabelId] = callBlockId;
                var newBlocks = new List<(SpvInstr Label, List<SpvInstr> Insts)>();
                var blk = (Label: new SpvInstr(OpLabel, [callBlockId]), Insts: new List<SpvInstr>());
                // MoveInstsBeforeEntryBlock: the caller's instructions before the call, remembering same-block ops.
                var preCallSB = new Dictionary<uint, SpvInstr>();
                for (int i = 0; i < callIdx; i++)
                {
                    var ins = callBlock.Insts[i];
                    if (IsSameBlockOp(ins)) preCallSB[ins.ResultId] = ins;
                    blk.Insts.Add(ins);
                }
                bool callerIsLoopHeader = callBlock.Insts.Any(x => x.Opcode == 246);
                int entryEnd = cal.Blocks[0].End;
                bool calleeEntryHasMerge = false;
                for (int i = cal.Blocks[0].Label + 1; i <= entryEnd; i++) if (C[i].Opcode is 246 or 247) calleeEntryHasMerge = true;
                if (callerIsLoopHeader && calleeEntryHasMerge)
                {
                    // AddGuardBlock: the caller's OpLoopMerge cannot share a block with the callee's merge.
                    uint guard = nextId++;
                    blk.Insts.Add(new SpvInstr(249, [guard]));
                    newBlocks.Add(blk);
                    blk = (new SpvInstr(OpLabel, [guard]), new List<SpvInstr>());
                    map[cal.EntryLabelId] = guard;
                }
                uint retVar = 0;
                if (cal.RetType != voidType)
                {
                    uint pt = PtrFor(cal.RetType);
                    retVar = nextId++;
                    newVars.Add(new SpvInstr(OpVariable, [pt, retVar, 7]));
                    CloneDecorations(callInst.Operands[2], retVar);
                }
                for (int i = 0; i <= cal.Term; i++) { uint rid = C[i].ResultId; if (rid != 0 && !map.ContainsKey(rid)) map[rid] = nextId++; }

                // InlineSingleInstruction: clone, remap the input ids, take the mapped result id and its decorations.
                void InlineSingle(SpvInstr src)
                {
                    if (src.Opcode is OpReturn or OpReturnValue) return;
                    var cp = new SpvInstr(src.Opcode, (uint[])src.Operands.Clone());
                    ForEachInId(cp, id => map.TryGetValue(id, out uint nv) ? nv : id);
                    uint rid = src.ResultId;
                    if (rid != 0) { cp.Classify(out int ri, out _); cp.Operands[ri] = map[rid]; CloneDecorations(rid, map[rid]); }
                    blk.Insts.Add(cp);
                }
                // InlineEntryBlock: the initializer stores, then the entry block's instructions.
                foreach (int vi in cal.VarIdx)
                    if (C[vi].Operands.Length >= 4) blk.Insts.Add(new SpvInstr(OpStore, [map[C[vi].Operands[1]], C[vi].Operands[3]]));
                for (int i = cal.Blocks[0].Label + 1; i <= entryEnd; i++) if (C[i].Opcode != OpVariable) InlineSingle(C[i]);
                // InlineBasicBlocks.
                for (int bi2 = 1; bi2 < cal.Blocks.Count; bi2++)
                {
                    var (lbl, end) = cal.Blocks[bi2];
                    newBlocks.Add(blk);
                    blk = (new SpvInstr(OpLabel, [map[C[lbl].Operands[0]]]), new List<SpvInstr>());
                    for (int i = lbl + 1; i <= end; i++) InlineSingle(C[i]);
                }
                // InlineReturn.
                var ret = C[cal.Term];
                if (ret.Opcode == OpReturnValue) blk.Insts.Add(new SpvInstr(OpStore, [retVar, map.GetValueOrDefault(ret.Operands[0], ret.Operands[0])]));
                if (cal.AbortBlock)
                {
                    uint rl = nextId++;
                    if (ret.Opcode is OpReturn or OpReturnValue) blk.Insts.Add(new SpvInstr(249, [rl]));
                    newBlocks.Add(blk);
                    blk = (new SpvInstr(OpLabel, [rl]), new List<SpvInstr>());
                }
                if (retVar != 0) blk.Insts.Add(new SpvInstr(OpLoad, [cal.RetType, callInst.Operands[1], retVar]));
                // MoveCallerInstsAfterFunctionCall: regenerate a pre-call same-block op used after the call.
                var postCallSB = new Dictionary<uint, uint>();
                void CloneSameBlockOps(SpvInstr ins)
                {
                    ForEachInId(ins, id =>
                    {
                        if (postCallSB.TryGetValue(id, out uint mapped)) return mapped;
                        if (!preCallSB.TryGetValue(id, out var pre)) return id;
                        var sb = new SpvInstr(pre.Opcode, (uint[])pre.Operands.Clone());
                        CloneSameBlockOps(sb);
                        uint nid = nextId++;
                        CloneDecorations(id, nid);
                        sb.Classify(out int ri, out _); sb.Operands[ri] = nid;
                        postCallSB[id] = nid;
                        blk.Insts.Add(sb);
                        return nid;
                    });
                }
                for (int i = callIdx + 1; i < callBlock.Insts.Count; i++)
                {
                    var ins = callBlock.Insts[i];
                    CloneSameBlockOps(ins);
                    if (IsSameBlockOp(ins)) postCallSB[ins.ResultId] = ins.ResultId;
                    blk.Insts.Add(ins);
                }
                newBlocks.Add(blk);
                if (callerIsLoopHeader && newBlocks.Count > 1)
                {
                    // MoveLoopMergeInstToFirstBlock.
                    var first = newBlocks[0].Insts; var last = newBlocks[^1].Insts;
                    int mi = last.FindLastIndex(x => x.Opcode == 246);
                    var mergeCopy = new SpvInstr(246, (uint[])last[mi].Operands.Clone());
                    first.Insert(first.Count - 1, mergeCopy);
                    last.RemoveAt(mi);
                    if (mergeCopy.Operands[1] == newBlocks[0].Label.Operands[0])
                    {
                        // UpdateSingleBlockLoopContinueTarget: split the back edge into a trivial continue block.
                        uint nb = nextId++;
                        var back = newBlocks[^1].Insts;
                        var br = back[^1]; back.RemoveAt(back.Count - 1);
                        back.Add(new SpvInstr(249, [nb]));
                        newBlocks.Add((new SpvInstr(OpLabel, [nb]), new List<SpvInstr> { br }));
                        mergeCopy.Operands[1] = nb;
                    }
                }
                return newBlocks;
            }

            // InlineExhaustivePass::InlineExhaustive.
            for (int bi = 0; bi < blocks.Count; bi++)
            {
                int ii = 0;
                while (ii < blocks[bi].Insts.Count)
                {
                    var ins = blocks[bi].Insts[ii];
                    if (ins.Opcode == OpFunctionCall && CanInline(ins.Operands[2]))
                    {
                        var prev = ii == 0 ? null : blocks[bi].Insts[ii - 1];
                        var newVars = new List<SpvInstr>();
                        var newBlocks = GenInlineCode(blocks[bi], ii, newVars);
                        if (newBlocks.Count > 1)
                        {
                            // UpdateSucceedingPhis: the last new block now branches to the old block's successors.
                            uint firstId = newBlocks[0].Label.Operands[0], lastId = newBlocks[^1].Label.Operands[0];
                            foreach (uint succ in BlockSuccsOf(newBlocks[^1].Insts))
                                if (blockById.TryGetValue(succ, out var sbody))
                                    foreach (var phi in sbody.TakeWhile(x => x.Opcode == 245))
                                        for (int k = 2; k < phi.Operands.Length; k++) if (phi.Operands[k] == firstId) phi.Operands[k] = lastId;
                        }
                        blocks.RemoveAt(bi);
                        blocks.InsertRange(bi, newBlocks);
                        foreach (var nb in newBlocks) blockById[nb.Label.Operands[0]] = nb.Insts;
                        if (newVars.Count > 0) blocks[0].Insts.InsertRange(0, newVars);
                        ii = prev is null ? 0 : blocks[bi].Insts.IndexOf(prev) + 1;
                    }
                    else ii++;
                }
            }

            var outE = new List<SpvInstr>();
            foreach (var (l, ins) in blocks) { outE.Add(l); outE.AddRange(ins); }
            m.Instrs.RemoveRange(firstLabel, fe - firstLabel);
            m.Instrs.InsertRange(firstLabel, outE);
        }

        if (newGlobals.Count > 0) { int ff = m.Instrs.FindIndex(x => x.Opcode == OpFunction); m.Instrs.InsertRange(ff, newGlobals); }
        if (newDecos.Count > 0)
        {
            int at = m.Instrs.FindLastIndex(x => x.Opcode is 71 or 72 or 73 or 74 or 75 or 332 or 5632 or 5633) + 1;
            if (at == 0) at = m.Instrs.FindIndex(x => x.Opcode is >= 19 and <= 39 or >= 41 and <= 52 or OpVariable or OpFunction or 1);
            m.Instrs.InsertRange(at, newDecos);
        }
        m.Header[3] = nextId;
    }

    const int OpTypeStruct = 30, OpTypeArray = 28, OpTypeMatrix = 24, OpUndef = 1;

    // spirv-opt's ScalarReplacement: an aggregate Function local (struct/array/matrix/vector) accessed only
    // by whole load/store and constant-index access chains is split into one Function variable per member.
    // Whole loads become per-member loads + OpCompositeConstruct; whole stores become per-member
    // OpCompositeExtract + stores; an access chain into member i becomes (a use of / an access chain rooted
    // at) member i's variable. New ids (member pointer types, member vars, per-member loads/composites/
    // extracts/sub-chains) are taken in the exact order spirv-opt does, so the following SSA byte-matches.
    static void ScalarReplacement(SpvModule m)
    {
        uint nextId = m.Header[3];
        int firstFunc = m.Instrs.FindIndex(x => x.Opcode == OpFunction);
        var newTypes = new List<SpvInstr>();
        var ptrCache = new Dictionary<uint, uint>();   // pointee type -> Function pointer type id
        var newDecor = new List<SpvInstr>();

        uint FnPointerTo(uint pointee)
        {
            if (ptrCache.TryGetValue(pointee, out uint id)) return id;
            foreach (var ins in m.Instrs) if (ins.Opcode == OpTypePointer && ins.Operands.Length >= 3 && ins.Operands[1] == 7 && ins.Operands[2] == pointee) { ptrCache[pointee] = ins.Operands[0]; return ins.Operands[0]; }
            foreach (var ins in newTypes) if (ins.Opcode == OpTypePointer && ins.Operands[1] == 7 && ins.Operands[2] == pointee) { ptrCache[pointee] = ins.Operands[0]; return ins.Operands[0]; }
            id = nextId++; newTypes.Add(new SpvInstr(OpTypePointer, [id, 7, pointee])); ptrCache[pointee] = id; return id;
        }

        // Built ONCE: the loop only ever looks up TYPE and CONSTANT defs, which are never removed (the pass only
        // rewrites variables/loads/stores). New member pointer types/undefs live in newTypes and are seeded in
        // each iteration. Rebuilding this O(n) map per variable was a big chunk of the per-iteration cost.
        var def = m.DefMap();
        // Built ONCE and maintained incrementally (like the Simplification pass). Rebuilding the user map per
        // processed variable was the O(K*N) cost. New member loads/stores are Indexed in their spliced order so
        // each user list stays in instruction order (which the id-allocation order, and thus byte parity, rides on).
        var users = m.UserMap();
        void Index(SpvInstr ins) { ins.Classify(out int ri, out bool[] isId); for (int k = 0; k < ins.Operands.Length; k++) if (isId[k] && k != ri) (users.TryGetValue(ins.Operands[k], out var l) ? l : users[ins.Operands[k]] = []).Add(ins); }
        void Deindex(SpvInstr ins) { ins.Classify(out int ri, out bool[] isId); for (int k = 0; k < ins.Operands.Length; k++) if (isId[k] && k != ri && users.TryGetValue(ins.Operands[k], out var l)) l.Remove(ins); }
        var scratch = SpvModule.RentInstrList(m.Instrs.Count + 64);   // ping-pong rebuild buffer, reused across variables
        var scanUsed = new HashSet<int>();   // reused per candidate in the find-scan (was a HashSet per candidate)
        // Function variables are the only candidates, and they sit in a short run at the top of each entry
        // block. Scanning the whole module for them on every replacement was O(variables * module); keep the
        // candidate list instead, in module order, and swap a replaced aggregate for its member variables in
        // place so the scan order (and therefore id-allocation order and byte parity) is unchanged.
        var candidates = new List<SpvInstr>();
        foreach (var ins in m.Instrs)
            if (ins.Opcode == OpVariable && ins.Operands.Length == 3 && ins.Operands[2] == 7) candidates.Add(ins);
        bool progress = true;
        while (progress)
        {
            progress = false;
            foreach (var t in newTypes) { uint r = t.ResultId; if (r != 0) def[r] = t; }

            // Member types of an aggregate, or null if not a replaceable aggregate.
            uint[]? Members(uint typeId)
            {
                if (!def.TryGetValue(typeId, out var t)) return null;
                // spirv-opt's ScalarReplacement targets only structs and arrays (matrix/vector are disabled);
                // vector-component locals go through LocalAccessChainConvert instead.
                if (t.Opcode == OpTypeStruct && t.Operands.Length > 1) { var r = new uint[t.Operands.Length - 1]; for (int i = 1; i < t.Operands.Length; i++) r[i - 1] = t.Operands[i]; return r; }
                if (t.Opcode == OpTypeArray && def.TryGetValue(t.Operands[2], out var lenC) && lenC.Opcode == OpConstant) { int n = (int)lenC.Operands[2]; var r = new uint[n]; for (int i = 0; i < n; i++) r[i] = t.Operands[1]; return r; }
                return null;
            }

            // Find one replaceable variable (Function aggregate, uses only whole load/store/const-index chain).
            // NOTE: this must stay one-at-a-time. Replacing a variable can expose a nested member aggregate
            // that the next scan picks up BEFORE later candidates, and that order drives id allocation, so
            // batching independent variables into a single rebuild changes the output.
            SpvInstr? target = null; uint[]? members = null; bool allUsed = false;
            var usedIdx = scanUsed;
            int targetIdx = -1;
            for (int ci = 0; ci < candidates.Count; ci++)
            {
                var v = candidates[ci];
                var mem = Members(def[v.Operands[0]].Operands[2]);
                // A rejection only depends on the variable's own uses, which replacing other variables never
                // touches, so a rejected candidate is dropped for good (the scan order of the rest is unchanged).
                if (mem is null || !users.TryGetValue(v.Operands[1], out var us)) { candidates.RemoveAt(ci--); continue; }
                bool ok = true, whole = false, all = false; scanUsed.Clear();
                foreach (var u in us)
                {
                    if (u.Opcode is OpLoad or OpStore && (u.Opcode == OpLoad ? u.Operands[2] : u.Operands[0]) == v.Operands[1])
                    {
                        whole = true;
                        // GetUsedComponents: a whole store uses no component; a whole load uses the components its
                        // extracts read, or all of them when anything else consumes the loaded value.
                        if (u.Opcode == OpLoad)
                        {
                            if (users.TryGetValue(u.Operands[1], out var lus) && lus.All(x => x.Opcode == OpCompositeExtract && x.Operands.Length >= 4 && x.Operands[2] == u.Operands[1]))
                                foreach (var x in lus) scanUsed.Add((int)x.Operands[3]);
                            else if (lus is { Count: > 0 }) all = true;
                        }
                    }
                    else if (u.Opcode is OpAccessChain or OpInBoundsAccessChain && u.Operands[2] == v.Operands[1] && u.Operands.Length > 3
                             && def.TryGetValue(u.Operands[3], out var idc) && idc.Opcode == OpConstant) { scanUsed.Add((int)idc.Operands[2]); }
                    else if (u.Opcode is 5 or 71 or 72) { }
                    else { ok = false; break; }
                }
                if (!ok || (!whole && scanUsed.Count == 0)) { candidates.RemoveAt(ci--); continue; }
                target = v; members = mem; allUsed = all; targetIdx = ci; break;
            }
            if (target is null) break;

            uint varId = target.Operands[1];
            var repl = new uint[members!.Length];   // per member: variable id, or an OpUndef id for unused members
            var newVars = new List<SpvInstr>();
            var undefs = new Dictionary<uint, uint>();
            uint Undef(uint typeId)
            {
                if (undefs.TryGetValue(typeId, out uint u)) return u;
                // MemPass::Type2Undef keeps a per-pass map: module OpUndefs are never reused.
                foreach (var ins in newTypes) if (ins.Opcode == OpUndef && ins.Operands[0] == typeId) { undefs[typeId] = ins.Operands[1]; return ins.Operands[1]; }
                uint id = nextId++; newTypes.Add(new SpvInstr(OpUndef, [typeId, id])); undefs[typeId] = id; return id;
            }
            for (int i = 0; i < members.Length; i++)
            {
                if (allUsed || usedIdx.Contains(i)) { uint pt = FnPointerTo(members[i]); uint id = nextId++; newVars.Add(new SpvInstr(OpVariable, [pt, id, 7])); repl[i] = id; }
                else repl[i] = Undef(members[i]);
            }

            // Rewrite users (rebuild the instruction stream). A rewritten user is stamped; its replacement
            // sequence (if any) rides in Tag.
            var kill = new List<SpvInstr>();
            int killStamp = SpvInstr.NewStamp();
            void Expand(SpvInstr u, List<SpvInstr> seq) { u.Tag = seq; }
            // WhileEachUser: def-use users in creation (unique id) order, which drives the id allocation below.
            foreach (var u in users[varId].OrderBy(x => x.Serial).ToList())
            {
                if (u.Opcode == OpLoad && u.Operands[2] == varId)
                {
                    var seq = new List<SpvInstr>(); var comps = new List<uint>();
                    for (int i = 0; i < members.Length; i++)
                    {
                        bool isVar = allUsed || usedIdx.Contains(i);
                        // ReplaceWholeLoad inserts each member load before the previous one (where = where.InsertBefore).
                        if (isVar) { uint ld = nextId++; seq.Insert(0, new SpvInstr(OpLoad, [members[i], ld, repl[i]])); comps.Add(ld); }
                        else comps.Add(repl[i]);
                    }
                    uint comp = nextId++;
                    var cops = new List<uint> { u.Operands[0], comp }; cops.AddRange(comps);
                    seq.Add(new SpvInstr(OpCompositeConstruct, cops.ToArray()));
                    MapReplace(users, u.Operands[1], comp);
                    Expand(u, seq); u.Mark = killStamp; kill.Add(u);
                }
                else if (u.Opcode == OpStore && u.Operands[0] == varId)
                {
                    var seq = new List<SpvInstr>(); uint srcVal = u.Operands[1];
                    for (int i = 0; i < members.Length; i++)
                    {
                        if (!(allUsed || usedIdx.Contains(i))) continue;
                        uint ex = nextId++;
                        seq.Add(new SpvInstr(OpCompositeExtract, [members[i], ex, srcVal, (uint)i]));
                        seq.Add(new SpvInstr(OpStore, [repl[i], ex]));
                    }
                    Expand(u, seq); u.Mark = killStamp; kill.Add(u);
                }
                else if (u.Opcode is OpAccessChain or OpInBoundsAccessChain && u.Operands[2] == varId)
                {
                    int idx = (int)def[u.Operands[3]].Operands[2];
                    if (u.Operands.Length > 4)   // remaining indices -> new access chain rooted at member var
                    {
                        uint nc = nextId++;
                        var cops = new List<uint> { u.Operands[0], nc, repl[idx] };
                        for (int k = 4; k < u.Operands.Length; k++) cops.Add(u.Operands[k]);
                        Expand(u, [new SpvInstr(u.Opcode, cops.ToArray())]);
                        MapReplace(users, u.Operands[1], nc);
                    }
                    else { u.Tag = null; MapReplace(users, u.Operands[1], repl[idx]); }
                    u.Mark = killStamp; kill.Add(u);
                }
            }

            // Splice: insert new member vars at the entry-block top (where the original var was), and each
            // user's replacement sequence before it; drop the original var and rewritten users. New instructions
            // are Indexed in this emitted (instruction) order and killed ones Deindexed, so `users` stays exact.
            Deindex(target);
            foreach (var k in kill) Deindex(k);
            // CreateVariable puts each member var at the front of the variable's block (block->begin().InsertBefore),
            // so the members land at the top of the entry block in reverse creation order.
            int tIdx = m.Instrs.IndexOf(target), entry = tIdx;
            while (entry > 0 && m.Instrs[entry].Opcode != OpFunction) entry--;
            while (m.Instrs[entry].Opcode != OpLabel) entry++;
            var entryLabel = m.Instrs[entry];
            scratch.Clear();
            for (int i = 0; i < m.Instrs.Count; i++)
            {
                var ins = m.Instrs[i];
                if (ins == entryLabel) { scratch.Add(ins); for (int k = newVars.Count - 1; k >= 0; k--) { scratch.Add(newVars[k]); Index(newVars[k]); } continue; }
                if (ins == target) continue;
                if (ins.Mark == killStamp)
                {
                    if (ins.Tag is List<SpvInstr> seq) foreach (var s in seq) { scratch.Add(s); Index(s); }
                    continue;
                }
                scratch.Add(ins);
            }
            (m.Instrs, scratch) = (scratch, m.Instrs);   // swap; the old list becomes next iteration's buffer
            candidates.RemoveAt(targetIdx);
            candidates.AddRange(newVars);   // spirv-opt's worklist is a queue: members are tried after every earlier candidate
            progress = true;
        }

        SpvModule.ReturnInstrList(scratch);
        if (newTypes.Count > 0 && firstFunc >= 0)
        {
            firstFunc = m.Instrs.FindIndex(x => x.Opcode == OpFunction);
            m.Instrs.InsertRange(firstFunc, newTypes);
        }
        m.Header[3] = nextId;
    }

    // spirv-opt's LocalAccessChainConvert: for a Function variable accessed only through constant-index
    // access chains, each `OpLoad(AccessChain(v, i..))` becomes `whole = OpLoad v; OpCompositeExtract whole i..`
    // and each `OpStore(AccessChain(v, i..), val)` becomes `whole = OpLoad v; ins = OpCompositeInsert val whole i..;
    // OpStore v ins`. The var is then whole-access only, so SSARewrite can eliminate it. New ids are taken in
    // instruction order (matching the oracle); the dead access chains are removed by the following DeadCodeElim.
    static void LocalAccessChainConvert(SpvModule m)
    {
        var def = m.DefMap();
        var users = m.UserMap();
        uint nextId = m.Header[3];

        bool ConstIdx(uint id) => def.TryGetValue(id, out var d) && d.Opcode == OpConstant;
        bool AcOk(SpvInstr ac)   // access chain into a target var with constant indices, used only by load/store
        {
            for (int k = 3; k < ac.Operands.Length; k++) if (!ConstIdx(ac.Operands[k])) return false;
            return users.TryGetValue(ac.Operands[1], out var us) && us.All(u => u.Opcode is OpLoad or OpStore);
        }
        bool IsTarget(uint varId)
        {
            if (!def.TryGetValue(varId, out var v) || v.Opcode != OpVariable || v.Operands.Length < 3 || v.Operands[2] != 7) return false;
            if (!users.TryGetValue(varId, out var us)) return false;
            bool anyAc = false;
            foreach (var u in us)
            {
                if ((u.Opcode == OpLoad && u.Operands[2] == varId) || (u.Opcode == OpStore && u.Operands[0] == varId) || u.Opcode is 5 or 71 or 72) continue;
                if ((u.Opcode is OpAccessChain or OpInBoundsAccessChain) && u.Operands[2] == varId && u.Operands.Length > 3 && AcOk(u)) { anyAc = true; continue; }
                return false;
            }
            return anyAc;
        }
        uint Pointee(uint varId) => def.TryGetValue(def[varId].Operands[0], out var pt) && pt.Operands.Length >= 3 ? pt.Operands[2] : 0;
        uint[] Indices(SpvInstr ac) { var r = new uint[ac.Operands.Length - 3]; for (int k = 3; k < ac.Operands.Length; k++) r[k - 3] = def[ac.Operands[k]].Operands[2]; return r; }

        var targetAc = new Dictionary<uint, SpvInstr>();   // access-chain result id -> its access chain, for target vars
        foreach (var ins in m.Instrs)
            if (ins.Opcode is OpAccessChain or OpInBoundsAccessChain && ins.Operands.Length > 3 && IsTarget(ins.Operands[2])) targetAc[ins.Operands[1]] = ins;
        if (targetAc.Count == 0) return;

        var rebuilt = SpvModule.RentInstrList(m.Instrs.Count + targetAc.Count * 2);
        foreach (var ins in m.Instrs)
        {
            if (ins.Opcode == OpLoad && ins.Operands.Length >= 3 && targetAc.TryGetValue(ins.Operands[2], out var acL))
            {
                uint var = acL.Operands[2], pte = Pointee(var), ld = nextId++;
                rebuilt.Add(new SpvInstr(OpLoad, [pte, ld, var]));
                var ops = new List<uint> { ins.Operands[0], ins.Operands[1], ld }; ops.AddRange(Indices(acL));
                ins.Opcode = OpCompositeExtract; ins.Operands = ops.ToArray();
                rebuilt.Add(ins);
            }
            else if (ins.Opcode == OpStore && targetAc.TryGetValue(ins.Operands[0], out var acS))
            {
                uint var = acS.Operands[2], pte = Pointee(var), ld = nextId++, ins2 = nextId++;
                rebuilt.Add(new SpvInstr(OpLoad, [pte, ld, var]));
                var iops = new List<uint> { pte, ins2, ins.Operands[1], ld }; iops.AddRange(Indices(acS));
                rebuilt.Add(new SpvInstr(OpCompositeInsert, iops.ToArray()));
                rebuilt.Add(new SpvInstr(OpStore, [var, ins2]));
            }
            else rebuilt.Add(ins);
        }
        m.SetInstrs(rebuilt);
        m.Header[3] = nextId;
    }

    const int OpCompositeInsert = 82;

    sealed class PhiCand
    {
        public uint Var, Id;
        public int Block;
        public List<uint> Args = [];
        public HashSet<uint> Users = [];
        public bool Complete, Incomplete;
        public uint CopyOf;
        public bool Ready => Complete && CopyOf == 0;
    }

    // spirv-opt's SSARewrite (Braun et al.): converts multi-store Function variables into SSA values,
    // inserting OpPhi at join points. Blocks are visited in reverse post-order; reaching definitions are
    // resolved backwards from loads; phi result ids come from a fresh-id counter in creation order (run
    // early, before any id-allocating fold, so the counter matches the oracle). Only whole scalar/vector
    // locals with no access-chain use are handled (spirv-opt relies on ScalarReplacement/LocalAccessChain
    // to remove those first, which are not implemented here).
    [ThreadStatic] static Dictionary<ulong, uint>? s_ssaCurDef;
    [ThreadStatic] static Dictionary<uint, uint>? s_ssaLoadRepl;
    [ThreadStatic] static Dictionary<uint, PhiCand>? s_ssaPhiAll, s_ssaPhis;
    [ThreadStatic] static Dictionary<uint, List<(uint, int)>>? s_ssaByVal;
    static void SsaRewrite(SpvModule m)
    {
        var def = m.DefMap();
        var users = m.UserMap();
        uint nextId = m.Header[3];

        // Pooled across compiles (cleared here; a cleared Dictionary enumerates in insertion order like a new one).
        var loadRepl = s_ssaLoadRepl ??= new(); loadRepl.Clear();     // load result id -> value id (module-wide)
        var phiCandsAll = s_ssaPhiAll ??= new(); phiCandsAll.Clear();
        var phiByBlockLabel = new Dictionary<SpvInstr, List<SpvInstr>>();
        var kill = new HashSet<SpvInstr>();
        var newGlobals = new List<SpvInstr>();
        var fnUndefs = new Dictionary<uint, uint>();
        int firstFunc = m.Instrs.FindIndex(i => i.Opcode == OpFunction);

        int f = 0;
        while (f < m.Instrs.Count)
        {
            if (m.Instrs[f].Opcode != OpFunction) { f++; continue; }
            int end = f;
            while (end < m.Instrs.Count && m.Instrs[end].Opcode != OpFunctionEnd) end++;
            fnUndefs.Clear();   // CollectTargetVars resets MemPass's type2undefs_ per function
            RewriteFunc(f, end);
            f = end + 1;
        }

        // Apply: rewrite load uses, then rebuild instruction list with phis inserted and dead ones removed.
        // MapReplace (O(uses)) over the user map built above; Resolve gives a final value so replacements are
        // independent (no eliminated load is ever a replacement target).
        foreach (var (loadId, val) in loadRepl) MapReplace(users, loadId, Resolve(val));
        if (newGlobals.Count > 0 && firstFunc >= 0) m.Instrs.InsertRange(firstFunc, newGlobals);
        var rebuilt = SpvModule.RentInstrList(m.Instrs.Count);
        foreach (var ins in m.Instrs)
        {
            if (kill.Contains(ins)) continue;
            rebuilt.Add(ins);
            if (ins.Opcode == OpLabel && phiByBlockLabel.TryGetValue(ins, out var phis)) rebuilt.AddRange(phis);
        }
        m.SetInstrs(rebuilt);
        m.Header[3] = nextId;

        // Resolve a value to its final form, chaining trivial-phi copies AND load eliminations -- a load's
        // reaching definition can itself be another load being eliminated this pass, so a use must never be
        // rewritten to land on a removed load (order-independent because every load maps to a real value).
        uint Resolve(uint val)
        {
            for (int g = 0; g < 1_000_000; g++)
            {
                if (phiCandsAll.TryGetValue(val, out var pc) && pc.CopyOf != 0) { val = pc.CopyOf; continue; }
                if (loadRepl.TryGetValue(val, out uint lv) && lv != val) { val = lv; continue; }
                break;
            }
            return val;
        }

        void RewriteFunc(int start, int fend)
        {
            // Blocks in layout order.
            var blockStart = new List<int>();
            var idOfBlock = new Dictionary<uint, int>();
            for (int i = start; i <= fend; i++)
                if (m.Instrs[i].Opcode == OpLabel) { idOfBlock[m.Instrs[i].Operands[0]] = blockStart.Count; blockStart.Add(i); }
            int n = blockStart.Count;
            if (n == 0) return;
            uint LabelId(int b) => m.Instrs[blockStart[b]].Operands[0];
            int BlockEnd(int b) => b + 1 < n ? blockStart[b + 1] : fend;

            var succs = new List<List<int>>();
            var preds = new List<List<int>>();
            for (int b = 0; b < n; b++) { succs.Add([]); preds.Add([]); }
            for (int b = 0; b < n; b++)
                for (int i = blockStart[b] + 1; i < BlockEnd(b); i++)
                {
                    var t = m.Instrs[i];
                    void S(uint lbl) { if (idOfBlock.TryGetValue(lbl, out int tb)) succs[b].Add(tb); }
                    if (t.Opcode == 249 && t.Operands.Length >= 1) S(t.Operands[0]);
                    else if (t.Opcode == 250 && t.Operands.Length >= 3) { S(t.Operands[1]); S(t.Operands[2]); }
                    else if (t.Opcode == 251 && t.Operands.Length >= 2) { S(t.Operands[1]); for (int k = 3; k < t.Operands.Length; k += 2) S(t.Operands[k]); }
                }
            for (int b = 0; b < n; b++) foreach (int s in succs[b]) preds[s].Add(b);   // layout order

            // Reverse post-order from entry.
            var rpo = new List<int>();
            var visited = new bool[n];
            void Dfs(int b) { visited[b] = true; foreach (int s in succs[b]) if (!visited[s]) Dfs(s); rpo.Add(b); }
            Dfs(0);
            rpo.Reverse();

            // Target vars: Function-storage OpVariable in entry block, used only by whole load/store/name/decorate.
            var target = new Dictionary<uint, uint>();   // var id -> pointee type id
            for (int i = blockStart[0] + 1; i < BlockEnd(0); i++)
            {
                var v = m.Instrs[i];
                if (v.Opcode != OpVariable || v.Operands.Length < 3 || v.Operands[2] != 7) continue;
                uint varId = v.Operands[1];
                if (!users.TryGetValue(varId, out var us)) { }
                bool ok = true;
                foreach (var u in us ?? [])
                    if (!((u.Opcode == OpStore && u.Operands[0] == varId) || u.Opcode == OpLoad || u.Opcode is 5 or 71 or 72)) { ok = false; break; }
                if (!ok) continue;
                if (!def.TryGetValue(v.Operands[0], out var pt) || pt.Operands.Length < 3) continue;   // pointer type -> pointee
                target[varId] = pt.Operands[2];
            }
            if (target.Count == 0) return;

            var curDef = s_ssaCurDef ??= new(); curDef.Clear();   // (var << 32 | block) -> reaching value; pooled
            // Reverse index (reaching value -> the (var,block) slots that hold it) so a trivial phi's collapse
            // touches only the slots that named it instead of scanning the whole curDef map. Entries left stale by
            // a later overwrite are filtered by re-checking curDef at use.
            var curDefByVal = s_ssaByVal ??= new(); curDefByVal.Clear();
            var sealedB = new bool[n];
            var phiCands = s_ssaPhis ??= new(); phiCands.Clear();
            var incomplete = new Queue<PhiCand>();
            var toGen = new List<PhiCand>();

            void Write(uint var, int b, uint val)
            {
                // A slot already holding val is already listed under it, so rewriting it adds nothing (repeating the
                // entry would only grow the list, which is what made this pass allocate cubically in loop depth).
                ulong key = (ulong)var << 32 | (uint)b;
                if (curDef.TryGetValue(key, out uint prev) && prev == val) return;
                curDef[key] = val;
                (curDefByVal.TryGetValue(val, out var l) ? l : curDefByVal[val] = []).Add((var, b));
            }
            uint Undef(uint var) => GetUndef(target[var]);

            uint Reaching(uint var, int b)
            {
                if (curDef.TryGetValue((ulong)var << 32 | (uint)b, out uint hit)) return hit;
                uint val = 0;
                if (preds[b].Count == 1) val = Reaching(var, preds[b][0]);
                else if (preds[b].Count > 1)
                {
                    var phi = new PhiCand { Var = var, Id = nextId++, Block = b };
                    phiCands[phi.Id] = phi; phiCandsAll[phi.Id] = phi;
                    Write(var, b, phi.Id);
                    val = AddPhiOps(phi);
                }
                if (val == 0) val = Undef(var);
                Write(var, b, val);
                return val;
            }

            uint AddPhiOps(PhiCand phi)
            {
                bool found0 = false;
                foreach (int p in preds[phi.Block])
                {
                    uint arg = sealedB[p] ? Reaching(phi.Var, p) : 0;
                    phi.Args.Add(arg);
                    if (arg == 0) found0 = true;
                    else if (phiCands.TryGetValue(arg, out var d) && d != phi) d.Users.Add(phi.Id);
                }
                if (found0) { phi.Incomplete = true; incomplete.Enqueue(phi); return phi.Id; }
                uint repl = TryTrivial(phi);
                if (repl == phi.Id) { phi.Complete = true; toGen.Add(phi); }
                return repl;
            }

            uint TryTrivial(PhiCand phi)
            {
                uint same = 0;
                foreach (uint arg in phi.Args)
                {
                    if (arg == same || arg == phi.Id) continue;
                    if (same != 0) return phi.Id;
                    same = arg;
                }
                phi.CopyOf = same;
                // phi.Users records every id referencing this phi (other phi candidates and promoted-load results),
                // so the collapse is applied directly instead of scanning the whole loadRepl map per user.
                foreach (uint userId in phi.Users)
                {
                    if (phiCands.TryGetValue(userId, out var up)) { for (int i = 0; i < up.Args.Count; i++) if (up.Args[i] == phi.Id) up.Args[i] = same; }
                    else if (loadRepl.TryGetValue(userId, out var lv) && lv == phi.Id) loadRepl[userId] = same;
                }
                // The collapsed phi's slots now hold `same`. Their reverse-index entries move to `same`'s list (the
                // smaller list merges into the larger one) instead of being re-added one write at a time.
                if (curDefByVal.Remove(phi.Id, out var slots))
                {
                    foreach (var key in slots)
                    {
                        ulong k = (ulong)key.Item1 << 32 | (uint)key.Item2;
                        if (curDef.TryGetValue(k, out uint cur) && cur == phi.Id) curDef[k] = same;
                    }
                    if (!curDefByVal.TryGetValue(same, out var into)) curDefByVal[same] = slots;
                    else
                    {
                        if (into.Count < slots.Count) (into, slots) = (slots, into);
                        into.AddRange(slots);
                        curDefByVal[same] = into;
                    }
                }
                return same;
            }

            void Generate(int b)
            {
                for (int i = blockStart[b] + 1; i < BlockEnd(b); i++)
                {
                    var ins = m.Instrs[i];
                    if (ins.Opcode == OpVariable && ins.Operands.Length > 3 && target.ContainsKey(ins.Operands[1])) Write(ins.Operands[1], b, ins.Operands[3]);
                    else if (ins.Opcode == OpStore && target.ContainsKey(ins.Operands[0])) Write(ins.Operands[0], b, ins.Operands[1]);
                    else if (ins.Opcode == OpLoad && target.ContainsKey(ins.Operands[2]))
                    {
                        uint val = Reaching(ins.Operands[2], b);
                        loadRepl[ins.Operands[1]] = val;
                        if (phiCands.TryGetValue(val, out var d)) d.Users.Add(ins.Operands[1]);
                        kill.Add(ins);
                    }
                }
            }

            foreach (int b in rpo) { Generate(b); sealedB[b] = true; }

            while (incomplete.Count > 0)
            {
                var phi = incomplete.Dequeue();
                for (int i = 0; i < phi.Args.Count; i++)
                    if (phi.Args[i] == 0) phi.Args[i] = sealedB[preds[phi.Block][i]] ? Reaching(phi.Var, preds[phi.Block][i]) : Undef(phi.Var);
                phi.Complete = true;
                if (TryTrivial(phi) == phi.Id) toGen.Add(phi);
            }

            // Emit phis (reverse generation order within a block) and mark vars/stores for removal.
            foreach (var phi in toGen)
            {
                uint type = target[phi.Var];
                var ops = new List<uint> { type, phi.Id };
                var seen = new HashSet<uint>();
                var pl = preds[phi.Block];
                for (int i = 0; i < pl.Count; i++)
                {
                    uint predLabel = LabelId(pl[i]);
                    if (!seen.Add(predLabel)) continue;
                    uint arg = PhiArg(phi, i);
                    while (loadRepl.TryGetValue(arg, out uint lv)) arg = lv;   // a stored value that was itself a promoted (killed) load
                    ops.Add(Resolve(arg)); ops.Add(predLabel);
                }
                var label = m.Instrs[blockStart[phi.Block]];
                if (!phiByBlockLabel.TryGetValue(label, out var list)) phiByBlockLabel[label] = list = [];
                list.Insert(0, new SpvInstr(245, ops.ToArray()));   // OpPhi, inserted before earlier phis
            }
            // spirv-opt's SSARewrite only removes the loads it replaces; the now-dead stores and variables are
            // left for AggressiveDCE (so when ADCE is skipped for an unsupported extension, they survive).

            uint PhiArg(PhiCand phi, int ix)
            {
                uint arg = phi.Args[ix];
                while (arg != 0 && phiCands.TryGetValue(arg, out var pu) && !pu.Ready) arg = pu.CopyOf;
                return arg;
            }
        }

        uint GetUndef(uint typeId)
        {
            // MemPass::Type2Undef: module OpUndefs are never reused; one fresh undef per type per function.
            if (fnUndefs.TryGetValue(typeId, out uint u)) return u;
            uint id = nextId++;
            newGlobals.Add(new SpvInstr(1, [typeId, id]));   // OpUndef
            fnUndefs[typeId] = id;
            return id;
        }
    }

    // spirv-opt's LocalSingleStoreElim: a Function variable written by exactly ONE store (a full, direct
    // store, or an initializer) has that stored value forwarded to every load the store dominates; the
    // load is removed. Simplified vs the pass by rejecting any variable touched through an access chain
    // (only whole-variable scalar/vector locals are handled). The dead store/variable are cleared by DCE.
    // Replace uses of `from` with `to` through a maintained user map -- O(uses), not the whole-module scan
    // m.ReplaceUses does. Keeps ReplaceUses semantics (decorations/names 5/6/71/72/73/74 keep the old id) and
    // moves the rewritten users to `to`'s list so chained replacements resolve. The map must hold every user
    // of `from` when called (built once, mutated only through this helper).
    static void MapReplace(UserTable users, uint from, uint to)
    {
        if (!users.TryGetValue(from, out var us)) return;
        if (!users.TryGetValue(to, out var tl)) users[to] = tl = [];
        foreach (var u in us)
        {
            if (u.Opcode is 5 or 6 or 71 or 72 or 73 or 74) continue;
            u.Classify(out int ri, out bool[] isId);
            for (int i = 0; i < u.Operands.Length; i++) if (isId[i] && i != ri && u.Operands[i] == from) u.Operands[i] = to;
            tl.Add(u);
        }
        us.Clear();
    }

    static void LocalSingleStoreElim(SpvModule m)
    {
        var users = m.UserMap();
        for (int i = 0; i < m.Instrs.Count; i++) m.Instrs[i].Pos = i;   // module position, valid for this pass
        var kill = new HashSet<SpvInstr>();

        int f = 0;
        while (f < m.Instrs.Count)
        {
            if (m.Instrs[f].Opcode != OpFunction) { f++; continue; }
            int end = f;
            while (end < m.Instrs.Count && m.Instrs[end].Opcode != OpFunctionEnd) end++;
            var (blockStart, idom) = BuildCfg(m, f, end);
            int n = blockStart.Count;
            if (n == 0) { f = end + 1; continue; }

            int BlockOf(int idx) { int k = blockStart.BinarySearch(idx); return k >= 0 ? k : Math.Max(0, ~k - 1); }   // last block starting at or before idx
            bool BlockDom(int a, int b) { while (b > 0 && b != a) b = idom[b]; return b == a; }

            int entryEnd = n > 1 ? blockStart[1] : end;
            for (int i = blockStart[0] + 1; i < entryEnd; i++)
            {
                var v = m.Instrs[i];
                if (v.Opcode != OpVariable) continue;
                if (v.Operands.Length < 3 || v.Operands[2] != 7) continue;   // Function storage
                uint varId = v.Operands[1];
                if (!users.TryGetValue(varId, out var direct)) continue;
                // FindUses: the variable's users, and through OpCopyObject the copies' users too.
                var us = new List<SpvInstr>();
                void FindUses(List<SpvInstr> l) { foreach (var u in l) { us.Add(u); if (u.Opcode == OpCopyObject && users.TryGetValue(u.Operands[1], out var cl)) FindUses(cl); } }
                FindUses(direct);

                // FeedsAStore: a partial store through an access chain (or anything unknown) disqualifies it.
                bool FeedsAStore(SpvInstr chain)
                {
                    if (!users.TryGetValue(chain.Operands[1], out var cus)) return false;
                    foreach (var u in cus)
                    {
                        if (u.Opcode == OpStore) return true;
                        if (u.Opcode is OpAccessChain or OpInBoundsAccessChain or OpCopyObject) { if (FeedsAStore(u)) return true; }
                        else if (u.Opcode is OpLoad or 60 or 5 or 71 or 72 or 332) { }   // load, OpImageTexelPointer, name, decorations
                        else return true;
                    }
                    return false;
                }

                SpvInstr? store = v.Operands.Length > 3 ? v : null;   // an initializer counts as the store
                bool ok = true;
                foreach (var u in us)
                {
                    if (u.Opcode == OpStore) { if (store is null) store = u; else { ok = false; break; } }
                    else if (u.Opcode is OpAccessChain or OpInBoundsAccessChain) { if (FeedsAStore(u)) { ok = false; break; } }
                    else if (u.Opcode is OpLoad or OpCopyObject or 60) { }
                    else if (u.Opcode is 5 or 71 or 72 or 332) { }   // OpName / decorations
                    else { ok = false; break; }                       // a call or anything else may write it
                }
                if (!ok || store is null) continue;

                uint storedId = store == v ? v.Operands[3] : store.Operands[1];
                int storeBlock = store == v ? 0 : BlockOf(store.Pos);
                foreach (var u in us)
                {
                    if (u.Opcode != OpLoad || kill.Contains(u)) continue;
                    int loadBlock = BlockOf(u.Pos);
                    bool dominates = storeBlock == loadBlock ? (store == v || store.Pos < u.Pos) : BlockDom(storeBlock, loadBlock);
                    if (dominates) { MapReplace(users, u.Operands[1], storedId); kill.Add(u); }
                }
            }
            f = end + 1;
        }
        if (kill.Count > 0) m.Instrs.RemoveAll(kill.Contains);
    }

    // Builds the block list (index of each OpLabel) and immediate dominators for a function's instruction
    // range. Block 0 is the entry; successors come from the branch/switch terminators.
    static (List<int> BlockStart, int[] Idom) BuildCfg(SpvModule m, int start, int end)
    {
        var blockStart = new List<int>();
        var idOfBlock = new Dictionary<uint, int>();
        for (int i = start; i <= end; i++)
        {
            if (m.Instrs[i].Opcode != OpLabel) continue;
            idOfBlock[m.Instrs[i].Operands.Length > 0 ? m.Instrs[i].Operands[0] : 0] = blockStart.Count;
            blockStart.Add(i);
        }
        int n = blockStart.Count;
        var succs = new List<List<int>>();
        for (int b = 0; b < n; b++) succs.Add([]);
        void AddSucc(int b, uint label) { if (idOfBlock.TryGetValue(label, out int tb)) succs[b].Add(tb); }
        for (int b = 0; b < n; b++)
        {
            int s = blockStart[b] + 1, e = b + 1 < n ? blockStart[b + 1] : end;
            for (int i = s; i < e; i++)
            {
                var t = m.Instrs[i];
                if (t.Opcode == 249 && t.Operands.Length >= 1) AddSucc(b, t.Operands[0]);
                else if (t.Opcode == 250 && t.Operands.Length >= 3) { AddSucc(b, t.Operands[1]); AddSucc(b, t.Operands[2]); }
                else if (t.Opcode == 251 && t.Operands.Length >= 2) { AddSucc(b, t.Operands[1]); for (int k = 3; k < t.Operands.Length; k += 2) AddSucc(b, t.Operands[k]); }
            }
        }
        return (blockStart, Dominators(n, succs));
    }

    // Immediate dominators (Cooper/Harvey/Kennedy) over blocks 0..n-1 with block 0 the entry.
    static int[] Dominators(int n, List<List<int>> succs)
    {
        var preds = new List<int>[n];
        for (int i = 0; i < n; i++) preds[i] = [];
        for (int b = 0; b < n; b++) foreach (int s in succs[b]) preds[s].Add(b);

        // Reverse post-order.
        var order = new List<int>();
        var seen = new bool[n];
        void Dfs(int b) { seen[b] = true; foreach (int s in succs[b]) if (!seen[s]) Dfs(s); order.Add(b); }
        Dfs(0);
        order.Reverse();
        var rpo = new int[n];
        for (int i = 0; i < order.Count; i++) rpo[order[i]] = i;

        var idom = new int[n];
        for (int i = 0; i < n; i++) idom[i] = -1;
        idom[0] = 0;
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (int b in order)
            {
                if (b == 0) continue;
                int newIdom = -1;
                foreach (int p in preds[b])
                {
                    if (idom[p] < 0) continue;
                    newIdom = newIdom < 0 ? p : Intersect(p, newIdom, idom, rpo);
                }
                if (newIdom >= 0 && idom[b] != newIdom) { idom[b] = newIdom; changed = true; }
            }
        }
        idom[0] = -1;   // entry has no dominator parent
        return idom;
    }

    static int Intersect(int a, int b, int[] idom, int[] rpo)
    {
        while (a != b)
        {
            while (rpo[a] > rpo[b]) a = idom[a];
            while (rpo[b] > rpo[a]) b = idom[b];
        }
        return a;
    }

    // Local value numbering: within a block, a pure value-producing instruction (access chain, arithmetic,

    // spirv-opt's LocalSingleBlockLoadStoreElim, restricted (as the pass is) to Function-storage variables
    // whose only uses are load/store/access-chain. Per block it forwards the last store (or load) of a
    // variable to later loads and drops loads/overwritten stores. The dead OpVariable/OpStore left behind is
    // cleaned up by the following DeadCodeElim.
    static void LocalSingleBlockElim(SpvModule m)
    {
        var def = m.DefMap();
        var users = m.UserMap();
        var supported = new Dictionary<uint, bool>();

        bool IsFunctionVar(uint id) => def.TryGetValue(id, out var d) && d.Opcode == OpVariable && d.Operands.Length >= 3 && d.Operands[2] == OpTypePointerSc;
        uint RootVar(uint id)
        {
            for (int g = 0; g < 64 && def.TryGetValue(id, out var d); g++)
            {
                if (d.Opcode is OpAccessChain or OpInBoundsAccessChain or OpPtrAccessChain or OpInBoundsPtrAccessChain or OpCopyObject) id = d.Operands[2];
                else break;
            }
            return id;
        }
        bool OnlySupportedRefs(uint id)
        {
            if (supported.TryGetValue(id, out bool ok)) return ok;
            supported[id] = true;   // guard against cycles
            ok = users.TryGetValue(id, out var us) ? us.All(u =>
                u.Opcode is OpLoad or OpStore ||
                ((u.Opcode is OpAccessChain or OpInBoundsAccessChain or OpCopyObject) && OnlySupportedRefs(u.ResultId))) : true;
            return supported[id] = ok;
        }

        var kill = new HashSet<SpvInstr>();
        var save = new HashSet<SpvInstr>();
        var var2store = new Dictionary<uint, SpvInstr>();
        var var2load = new Dictionary<uint, SpvInstr>();

        foreach (var ins in m.Instrs)
        {
            switch (ins.Opcode)
            {
                case OpLabel:
                    var2store.Clear(); var2load.Clear();
                    break;
                case OpStore:
                {
                    uint varId = RootVar(ins.Operands[0]);
                    if (!IsFunctionVar(varId) || !OnlySupportedRefs(varId)) break;
                    bool direct = def.TryGetValue(ins.Operands[0], out var pd) && pd.Opcode == OpVariable;
                    if (direct)
                    {
                        if (var2store.TryGetValue(varId, out var prev) && !save.Contains(prev)) kill.Add(prev);
                        bool killStore = var2load.TryGetValue(varId, out var ld) && ins.Operands[1] == ld.Operands[1];
                        if (!killStore) { var2store[varId] = ins; var2load.Remove(varId); }
                        else kill.Add(ins);
                    }
                    else { var2store.Remove(varId); var2load.Remove(varId); }
                    break;
                }
                case OpLoad when ins.Operands.Length == 3:
                {
                    uint varId = RootVar(ins.Operands[2]);
                    if (!IsFunctionVar(varId) || !OnlySupportedRefs(varId)) break;
                    bool direct = def.TryGetValue(ins.Operands[2], out var pd) && pd.Opcode == OpVariable;
                    uint replId = 0;
                    if (direct)
                    {
                        if (var2store.TryGetValue(varId, out var st)) replId = st.Operands[1];
                        else if (var2load.TryGetValue(varId, out var ld)) replId = ld.Operands[1];
                    }
                    else if (var2store.TryGetValue(varId, out var st)) save.Add(st);
                    if (replId != 0) { MapReplace(users, ins.Operands[1], replId); kill.Add(ins); }
                    else if (direct) var2load[varId] = ins;
                    break;
                }
                case OpFunctionCall:
                    var2store.Clear(); var2load.Clear();
                    break;
            }
        }

        if (kill.Count > 0) m.Instrs.RemoveAll(kill.Contains);
    }

    // Aggressive-DCE approximation: drops stores to Function/Private variables that are never loaded, then
    // iteratively removes any result-defining instruction whose result is unused and which has no side
    // effect (dead OpVariable/types/constants/arithmetic/loads/access chains). Matches what spirv-opt's
    // AggressiveDCE strips after mem2reg. Ids are not renumbered.
    // spirv-opt's AggressiveDCE bails entirely (SuccessWithoutChange) when the module uses an extension it
    // does not recognise, the VariablePointers(StorageBuffer) capability, or a non-allowed non-semantic
    // instruction set (aggressive_dead_code_elim_pass.cpp:793). In that case no dead code is removed at all.
    static readonly HashSet<string> AdceExtAllowlist = new()
    {
        "SPV_AMD_shader_explicit_vertex_parameter", "SPV_AMD_shader_trinary_minmax", "SPV_AMD_gcn_shader",
        "SPV_KHR_shader_ballot", "SPV_AMD_shader_ballot", "SPV_AMD_gpu_shader_half_float",
        "SPV_KHR_shader_draw_parameters", "SPV_KHR_subgroup_vote", "SPV_KHR_8bit_storage",
        "SPV_KHR_16bit_storage", "SPV_KHR_device_group", "SPV_KHR_multiview",
        "SPV_NVX_multiview_per_view_attributes", "SPV_NV_viewport_array2", "SPV_NV_stereo_view_rendering",
        "SPV_NV_sample_mask_override_coverage", "SPV_NV_geometry_shader_passthrough",
        "SPV_AMD_texture_gather_bias_lod", "SPV_KHR_storage_buffer_storage_class", "SPV_AMD_gpu_shader_int16",
        "SPV_KHR_post_depth_coverage", "SPV_KHR_shader_atomic_counter_ops", "SPV_EXT_shader_stencil_export",
        "SPV_EXT_shader_viewport_index_layer", "SPV_AMD_shader_image_load_store_lod",
        "SPV_AMD_shader_fragment_mask", "SPV_EXT_fragment_fully_covered", "SPV_AMD_gpu_shader_half_float_fetch",
        "SPV_GOOGLE_decorate_string", "SPV_GOOGLE_hlsl_functionality1", "SPV_GOOGLE_user_type",
        "SPV_NV_shader_subgroup_partitioned", "SPV_EXT_demote_to_helper_invocation",
        "SPV_EXT_descriptor_indexing", "SPV_EXT_descriptor_heap", "SPV_KHR_untyped_pointers",
        "SPV_NV_fragment_shader_barycentric", "SPV_NV_compute_shader_derivatives", "SPV_NV_shader_image_footprint",
        "SPV_NV_shading_rate", "SPV_NV_mesh_shader", "SPV_EXT_mesh_shader", "SPV_NV_ray_tracing",
        "SPV_KHR_ray_tracing", "SPV_KHR_ray_query", "SPV_EXT_fragment_invocation_density",
        "SPV_EXT_physical_storage_buffer", "SPV_KHR_physical_storage_buffer", "SPV_KHR_terminate_invocation",
        "SPV_KHR_shader_clock", "SPV_KHR_subgroup_uniform_control_flow", "SPV_KHR_integer_dot_product",
        "SPV_EXT_shader_image_int64", "SPV_KHR_non_semantic_info", "SPV_KHR_uniform_group_instructions",
        "SPV_KHR_fragment_shader_barycentric", "SPV_KHR_vulkan_memory_model", "SPV_NV_bindless_texture",
        "SPV_EXT_shader_atomic_float_add", "SPV_EXT_fragment_shader_interlock", "SPV_KHR_compute_shader_derivatives",
        "SPV_NV_cooperative_matrix", "SPV_KHR_cooperative_matrix", "SPV_KHR_ray_tracing_position_fetch",
        "SPV_KHR_fragment_shading_rate", "SPV_KHR_quad_control", "SPV_NV_shader_invocation_reorder",
        "SPV_NV_cluster_acceleration_structure", "SPV_NV_linear_swept_spheres", "SPV_KHR_maximal_reconvergence",
        "SPV_NV_push_constant_bank", "SPV_EXT_opacity_micromap", "SPV_EXT_shader_invocation_reorder",
        "SPV_EXT_shader_atomic_float16_add", "SPV_KHR_abort", "SPV_KHR_constant_data",
    };

    static string DecodeSpvString(uint[] ops, int start)
    {
        var bytes = new List<byte>();
        for (int i = start; i < ops.Length; i++)
            for (int b = 0; b < 4; b++) { byte c = (byte)(ops[i] >> (b * 8)); if (c == 0) return System.Text.Encoding.UTF8.GetString(bytes.ToArray()); bytes.Add(c); }
        return System.Text.Encoding.UTF8.GetString(bytes.ToArray());
    }

    static bool AdceEnabled(SpvModule m)
    {
        foreach (var ins in m.Instrs)
        {
            if (ins.Opcode == 10 && !AdceExtAllowlist.Contains(DecodeSpvString(ins.Operands, 0))) return false;   // OpExtension
            if (ins.Opcode == 17 && ins.Operands.Length >= 1 && ins.Operands[0] is 4441 or 4442) return false;    // VariablePointers(StorageBuffer)
            if (ins.Opcode == 11 && ins.Operands.Length >= 2)
            {
                string name = DecodeSpvString(ins.Operands, 1);
                if (name.StartsWith("NonSemantic.") && !name.StartsWith("NonSemantic.Shader.DebugInfo.") && name != "NonSemantic.DebugPrintf") return false;
            }
        }
        return true;
    }

    [ThreadStatic] static IdSet? s_dceUsed, s_dceDefined;
    static void DeadCodeElim(SpvModule m)
    {
        if (!AdceEnabled(m)) return;
        AdceFunctions(m);
        var def = m.DefMap();
        // Only the WorkgroupSize builtin (gl_WorkGroupSize) is force-kept by spirv-opt's ADCE when otherwise
        // unused (aggressive_dead_code_elim_pass.cpp:711); other builtins (e.g. an unread gl_GlobalInvocationID)
        // are removed by ordinary liveness.
        // One pass gathers both the WorkgroupSize-decorated ids and every id used by a non-entry, non-decoration
        // instruction (was two separate O(n) scans).
        var builtinDecorated = Pool.UintSet();
        var usedByNonEntry = (s_dceUsed ??= new()).Begin(m.Header[3]);
        foreach (var ins in m.Instrs)
        {
            if (ins.Opcode == 71) { if (ins.Operands.Length >= 3 && ins.Operands[1] == 11 && ins.Operands[2] == 25) builtinDecorated.Add(ins.Operands[0]); continue; }   // BuiltIn WorkgroupSize
            if (ins.Opcode is 15 or 72 or 332) continue;   // skip OpEntryPoint / names
            ins.Classify(out int ri, out bool[] isId);
            for (int i = 0; i < ins.Operands.Length; i++) if (isId[i] && i != ri) usedByNonEntry.Add(ins.Operands[i]);
        }
        uint RootVar(uint id)
        {
            for (int g = 0; g < 64 && def.TryGetValue(id, out var d); g++)
            {
                if (d.Opcode is OpAccessChain or OpInBoundsAccessChain or OpPtrAccessChain or OpInBoundsPtrAccessChain or OpCopyObject) id = d.Operands[2];
                else break;
            }
            return id;
        }

        // Dead module-scope-variable elimination (spirv-opt ADCE, preserve_interface=false,
        // remove_outputs=false): a global variable that is never referenced (only listed in the entry-point
        // interface) is removed and dropped from that list. Only Output variables are force-kept; unused
        // inputs, uniforms and samplers go. The dead variable's pointer type and decorations are then
        // reclaimed by the iterative removal below.
        var deadGlobals = Pool.UintSet();
        foreach (var ins in m.Instrs)
            if (ins.Opcode == OpVariable && ins.Operands.Length >= 3 && ins.Operands[2] is not (3 or 7)   // not Output, not Function-local
                && !usedByNonEntry.Contains(ins.Operands[1]) && !builtinDecorated.Contains(ins.Operands[1]))
                deadGlobals.Add(ins.Operands[1]);
        if (deadGlobals.Count > 0)
            foreach (var ins in m.Instrs)
                if (ins.Opcode == 15)
                {
                    SpvGrammar.Classify(15, ins.Operands, out int ri2, out bool[] isId2);
                    var kept = new List<uint>();
                    for (int i = 0; i < ins.Operands.Length; i++)
                        if (!(isId2[i] && i != ri2 && deadGlobals.Contains(ins.Operands[i]))) kept.Add(ins.Operands[i]);
                    ins.Operands = kept.ToArray();
                }

        // Iterative removal of unused, side-effect-free, result-defining instructions. GLSL.std.450 ext-insts
        // are pure math and removable when unused (unlike a NonSemantic/debugPrintf ext-inst).
        uint glslExtId = 0;
        foreach (var ins in m.Instrs) if (ins.Opcode == 11 && ins.Operands.Length >= 2 && DecodeSpvString(ins.Operands, 1) == "GLSL.std.450") { glslExtId = ins.Operands[0]; break; }
        // Mark-sweep liveness (O(n)): an instruction is removable if it is a pure, result-defining, non-kept
        // value. Everything else is inherently live (roots); a value is live iff reachable from a root through
        // operand edges. Decorations (71/72/332) never keep a value alive, so they are not roots. This removes
        // exactly the transitively-unused set the old iterative while-loop did, in one pass instead of O(n^2).
        bool Removable(SpvInstr ins)
        {
            uint r = ins.ResultId;
            bool pure = IsPure(ins.Opcode) || (ins.Opcode == OpExtInst && glslExtId != 0 && ins.Operands.Length >= 3 && ins.Operands[2] == glslExtId);
            if (r == 0 || !pure || builtinDecorated.Contains(r)) return false;
            if (ins.Opcode == OpVariable && ins.Operands.Length >= 3 && ins.Operands[2] == 3) return false;   // Output vars are always kept
            return true;
        }
        // `def` (built at the top) is still the id->definer map: only result-less OpStores were removed since.
        int liveMark = SpvInstr.NewStamp();
        var stack = Pool.InstrStack();
        foreach (var ins in m.Instrs) if (ins.Opcode is not (71 or 72 or 332) && !Removable(ins) && ins.Mark != liveMark) { ins.Mark = liveMark; stack.Push(ins); }
        while (stack.Count > 0)
        {
            var ins = stack.Pop();
            ins.Classify(out int ri, out bool[] isId);
            for (int i = 0; i < ins.Operands.Length; i++)
                if (isId[i] && i != ri && def.TryGetValue(ins.Operands[i], out var d) && d.Mark != liveMark) { d.Mark = liveMark; stack.Push(d); }
        }
        m.Instrs.RemoveAll(ins => ins.Mark != liveMark && Removable(ins));

        // A decoration whose target id no longer exists is dead; identical decorations are redundant. Both
        // are stripped (spirv-opt removes a decoration along with the instruction it targeted, and never
        // emits duplicates). Liveness is untouched: an instruction that still exists keeps its decorations.
        var defined = (s_dceDefined ??= new()).Begin(m.Header[3]);
        foreach (var ins in m.Instrs) { uint r = ins.ResultId; if (r != 0) defined.Add(r); }
        var seenDecor = new HashSet<SpvInstr>(SameInstrComparer.Instance);
        m.Instrs.RemoveAll(ins =>
        {
            if (ins.Opcode is not (71 or 72 or 332)) return false;   // OpDecorate / OpMemberDecorate / OpDecorateId
            if (ins.Operands.Length > 0 && !defined.Contains(ins.Operands[0])) return true;
            return !seenDecor.Add(ins);
        });
        Pool.Free(builtinDecorated); Pool.Free(deadGlobals); Pool.Free(stack);
    }

    // Instructions equal by opcode and operand words.
    sealed class SameInstrComparer : IEqualityComparer<SpvInstr>
    {
        public static readonly SameInstrComparer Instance = new();
        public bool Equals(SpvInstr? a, SpvInstr? b) => a!.Opcode == b!.Opcode && a.Operands.AsSpan().SequenceEqual(b.Operands);
        public int GetHashCode(SpvInstr i) { var h = new HashCode(); h.Add(i.Opcode); foreach (uint w in i.Operands) h.Add(w); return h.ToHashCode(); }
    }

    // Result-defining opcodes with no side effect, safe to drop when their result is unused. Anything with a
    // side effect or a structural/module role is excluded and never auto-removed here. OpExtInst is treated
    // as impure so a void-result debugPrintf is never dropped.
    static bool IsPure(int opcode)
    {
        if (opcode is OpStore or OpCopyMemory or OpCopyMemorySized or OpFunctionCall or OpControlBarrier
            or OpMemoryBarrier or OpImageWrite or OpExtInst or OpFunction or OpFunctionEnd or OpLabel
            or 55                                                    // OpFunctionParameter
            or 246 or 247 or 248 or 249 or 250 or 251 or 252 or 253 or 254 or 255   // merges/branches/return/kill/unreachable
            or 218 or 219 or 220 or 221                              // OpEmit/EndPrimitive family
            or 10 or 11 or 14 or 15 or 16 or 17                      // OpExtension/ExtInstImport/MemoryModel/EntryPoint/ExecutionMode/Capability
            or 71 or 72 or 73 or 74 or 75 or 331 or 332)             // decorations, OpExecutionModeId, OpDecorateId
            return false;
        if (opcode is >= 227 and <= 242 or 318 or 6035 or 6036 or 6037) return false;   // OpAtomic* incl. flag test-and-set and float add/min/max
        // Ray-tracing trace/callable/report/ignore/terminate ops have side effects (OpReportIntersection returns a
        // bool but still reports a hit), and the ray-query mutators (Initialize/Terminate/Generate/Confirm/Proceed)
        // advance the query. The ray-query getters (>= 4479) are pure reads and stay removable when unused.
        if (opcode is 4445 or 4446 or 4448 or 4449 or 5334 or 5335 or 5336 or 5337 or 5344) return false;
        if (opcode is >= 4473 and <= 4477) return false;
        return true;
    }
}
