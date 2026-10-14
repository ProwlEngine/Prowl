using System.Collections.Generic;

namespace Prowl.Prism.Optimizer;

/// <summary>Thread-static pools for the short-lived scratch collections passes allocate (id sets, instruction
/// sets, work stacks). Rent one, use it, Free it when done; forgetting to Free just means it is not reused (no
/// correctness impact). Thread-static keeps the parallel test collections independent.</summary>
internal static class Pool
{
    [ThreadStatic] static Stack<HashSet<uint>>? _uintSets;
    [ThreadStatic] static Stack<HashSet<SpvInstr>>? _instrSets;
    [ThreadStatic] static Stack<Stack<SpvInstr>>? _instrStacks;

    public static HashSet<uint> UintSet() { var p = _uintSets ??= new(); var s = p.Count > 0 ? p.Pop() : new(); s.Clear(); return s; }
    public static void Free(HashSet<uint> s) => (_uintSets ??= new()).Push(s);

    public static HashSet<SpvInstr> InstrSet() { var p = _instrSets ??= new(); var s = p.Count > 0 ? p.Pop() : new(); s.Clear(); return s; }
    public static void Free(HashSet<SpvInstr> s) => (_instrSets ??= new()).Push(s);

    public static Stack<SpvInstr> InstrStack() { var p = _instrStacks ??= new(); var s = p.Count > 0 ? p.Pop() : new(); s.Clear(); return s; }
    public static void Free(Stack<SpvInstr> s) => (_instrStacks ??= new()).Push(s);
}

/// <summary>A parsed SPIR-V instruction: its opcode and the operand words that follow the opcode word.</summary>
internal sealed class SpvInstr(int opcode, uint[] operands)
{
    const int Uncomputed = int.MinValue;
    int _opcode = opcode;
    uint[] _operands = operands;
    int _resultIndex = Uncomputed;   // cached; the result-operand index and id-mask are stable under in-place writes
    bool[]? _isId;

    public int Opcode { get => _opcode; set { _opcode = value; _resultIndex = Uncomputed; _isId = null; } }
    public uint[] Operands { get => _operands; set { _operands = value; _resultIndex = Uncomputed; _isId = null; } }

    public int WordCount => 1 + _operands.Length;

    void EnsureClassified()
    {
        if (_isId is null) { SpvGrammar.Classify(_opcode, _operands, out _resultIndex, out _isId); }
    }

    /// <summary>Which operand words are id references, and where the result operand sits, cached on the
    /// instruction. Read constantly (every def/user map, every liveness walk); the grammar lookup is done once
    /// and reused, invalidated only when Opcode/Operands are reassigned (an in-place `Operands[k] = ...` never
    /// changes the operand shape). Callers must not mutate the returned array.</summary>
    public void Classify(out int resultIndex, out bool[] isId) { EnsureClassified(); resultIndex = _resultIndex; isId = _isId!; }

    /// <summary>The id this instruction defines, or 0 if it defines none.</summary>
    public uint ResultId
    {
        get
        {
            if (_resultIndex == Uncomputed) EnsureClassified();
            return _resultIndex >= 0 ? _operands[_resultIndex] : 0u;
        }
    }
}

/// <summary>
/// A SPIR-V module split into its 5-word header and a flat instruction list, so optimizer passes can walk,
/// rewrite and remove instructions and serialize back to a word stream. Ids are never renumbered here;
/// passes remove instructions and rewrite id uses, matching spirv-opt (which does not compact the id space).
/// </summary>
internal sealed class SpvModule
{
    public uint[] Header = new uint[5];
    public List<SpvInstr> Instrs = [];

    public static SpvModule Parse(uint[] words)
    {
        var m = new SpvModule();
        for (int i = 0; i < 5; i++) m.Header[i] = words[i];
        int p = 5;
        while (p < words.Length)
        {
            uint first = words[p];
            int opcode = (int)(first & 0xFFFF);
            int count = (int)(first >> 16);
            if (count == 0) break;
            int operandCount = count - 1;
            var operands = new uint[operandCount];
            for (int k = 0; k < operandCount && p + 1 + k < words.Length; k++) operands[k] = words[p + 1 + k];
            m.Instrs.Add(new SpvInstr(opcode, operands));
            p += count;
        }
        return m;
    }

    public uint[] Serialize()
    {
        var words = new List<uint>(Header.Length + Instrs.Count * 4);
        words.AddRange(Header);
        foreach (var ins in Instrs)
        {
            words.Add((uint)ins.Opcode | ((uint)ins.WordCount << 16));
            words.AddRange(ins.Operands);
        }
        return words.ToArray();
    }

    /// <summary>Rewrites every id-reference use of <paramref name="from"/> to <paramref name="to"/>. Result
    /// ids (definitions) are left alone, and literal operands are never touched.</summary>
    public void ReplaceUses(uint from, uint to)
    {
        foreach (var ins in Instrs)
        {
            // Decorations and names are metadata bound to a specific id; a value replacement must NOT move them
            // onto the replacement (spirv-opt drops the old id's decorations, it does not migrate them).
            if (ins.Opcode is 5 or 6 or 71 or 72 or 73 or 74) continue;   // OpName/OpMemberName/OpDecorate/OpMemberDecorate/OpGroupDecorate/OpGroupMemberDecorate
            ins.Classify(out int resultIndex, out bool[] isId);
            for (int i = 0; i < ins.Operands.Length; i++)
                if (isId[i] && i != resultIndex && ins.Operands[i] == from)
                    ins.Operands[i] = to;
        }
    }

    // Def/user maps are rebuilt ~20x per compile (once per pass) and were a top allocation source (~40 MB on a
    // big module). They are reused from thread-static buffers: a pass builds one at its start and uses it to the
    // end, and passes within a compile run sequentially, so a single reusable instance per thread is safe.
    // (ThreadStatic keeps the parallel test collections independent. A pass that needs two independent maps, or
    // nests a build, would corrupt the shared one -- none do; the corpus suite would catch it byte-for-byte.)
    [ThreadStatic] static Dictionary<uint, SpvInstr>? _defScratch;
    [ThreadStatic] static Dictionary<uint, List<SpvInstr>>? _userScratch;
    [ThreadStatic] static Stack<List<SpvInstr>>? _listPool;
    [ThreadStatic] static Dictionary<uint, HashSet<SpvInstr>>? _userScratchH;
    [ThreadStatic] static Stack<HashSet<SpvInstr>>? _hsetPool;

    /// <summary>UserMapOver with HashSet buckets (O(1) user removal), for passes that maintain the map by
    /// removing individual users. Reused/recycled on the next UserMapOverHash call.</summary>
    public Dictionary<uint, HashSet<SpvInstr>> UserMapOverHash(IEnumerable<SpvInstr> instrs)
    {
        var pool = _hsetPool ??= new();
        var map = _userScratchH ??= new();
        foreach (var h in map.Values) { h.Clear(); pool.Push(h); }
        map.Clear();
        foreach (var ins in instrs)
        {
            ins.Classify(out int ri, out bool[] isId);
            for (int i = 0; i < ins.Operands.Length; i++)
                if (isId[i] && i != ri)
                {
                    if (!map.TryGetValue(ins.Operands[i], out var set)) map[ins.Operands[i]] = set = pool.Count > 0 ? pool.Pop() : new();
                    set.Add(ins);
                }
        }
        return map;
    }

    /// <summary>Map from an id to every instruction that references it (as a non-result id operand). The returned
    /// map and its lists are reused on the next UserMap() call, so a caller must finish with it before then.</summary>
    public Dictionary<uint, List<SpvInstr>> UserMap()
    {
        var pool = _listPool ??= new();
        var map = _userScratch ??= new(Instrs.Count);
        foreach (var l in map.Values) { l.Clear(); pool.Push(l); }   // recycle the previous build's lists
        map.Clear();
        foreach (var ins in Instrs)
        {
            ins.Classify(out int ri, out bool[] isId);
            for (int i = 0; i < ins.Operands.Length; i++)
                if (isId[i] && i != ri)
                {
                    if (!map.TryGetValue(ins.Operands[i], out var list)) map[ins.Operands[i]] = list = pool.Count > 0 ? pool.Pop() : [];
                    list.Add(ins);
                }
        }
        return map;
    }

    /// <summary>Like UserMap but over an explicit instruction sequence (a single function's blocks), reusing the
    /// same pooled dictionary + list pool. The caller may keep adding/removing entries; recycled on the next
    /// UserMap/UserMapOver call, so finish before then.</summary>
    public Dictionary<uint, List<SpvInstr>> UserMapOver(IEnumerable<SpvInstr> instrs)
    {
        var pool = _listPool ??= new();
        var map = _userScratch ??= new();
        foreach (var l in map.Values) { l.Clear(); pool.Push(l); }
        map.Clear();
        foreach (var ins in instrs)
        {
            ins.Classify(out int ri, out bool[] isId);
            for (int i = 0; i < ins.Operands.Length; i++)
                if (isId[i] && i != ri)
                {
                    if (!map.TryGetValue(ins.Operands[i], out var list)) map[ins.Operands[i]] = list = pool.Count > 0 ? pool.Pop() : [];
                    list.Add(ins);
                }
        }
        return map;
    }

    /// <summary>
    /// Like <see cref="DefMap"/> but returns a private map. Use this when the map is held across other
    /// passes: the pooled one is cleared and rebuilt by the next DefMap() call, which would silently
    /// invalidate a long-lived holder.
    /// </summary>
    public Dictionary<uint, SpvInstr> DefMapOwned()
    {
        var map = new Dictionary<uint, SpvInstr>(Instrs.Count);
        foreach (var ins in Instrs)
        {
            uint r = ins.ResultId;
            if (r != 0) map[r] = ins;
        }
        return map;
    }

    /// <summary>Map from each defined id to its defining instruction. Reused on the next DefMap() call.</summary>
    public Dictionary<uint, SpvInstr> DefMap()
    {
        var map = _defScratch ??= new(Instrs.Count);
        map.Clear();
        foreach (var ins in Instrs)
        {
            uint r = ins.ResultId;
            if (r != 0) map[r] = ins;
        }
        return map;
    }
}
