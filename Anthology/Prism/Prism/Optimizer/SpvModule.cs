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
    public static void Free(HashSet<uint> s) { s.Clear(); (_uintSets ??= new()).Push(s); }

    public static HashSet<SpvInstr> InstrSet() { var p = _instrSets ??= new(); var s = p.Count > 0 ? p.Pop() : new(); s.Clear(); return s; }
    public static void Free(HashSet<SpvInstr> s) { s.Clear(); (_instrSets ??= new()).Push(s); }   // cleared now so it pins no instructions

    public static Stack<SpvInstr> InstrStack() { var p = _instrStacks ??= new(); var s = p.Count > 0 ? p.Pop() : new(); s.Clear(); return s; }
    public static void Free(Stack<SpvInstr> s) { s.Clear(); (_instrStacks ??= new()).Push(s); }
}

/// <summary>A parsed SPIR-V instruction: its opcode and the operand words that follow the opcode word.</summary>
internal sealed class SpvInstr(int opcode, uint[] operands)
{
    const int Uncomputed = int.MinValue;
    int _opcode = opcode;
    uint[] _operands = operands;
    int _resultIndex = Uncomputed;   // cached; the result-operand index and id-mask are stable under in-place writes
    bool[]? _isId;

    static long s_nextSerial;
    /// <summary>Creation order (spirv-opt's Instruction::unique_id): def-use user lists are ordered by it. Only
    /// the relative order within one module matters, so a process-wide counter is fine.</summary>
    public long Serial = System.Threading.Interlocked.Increment(ref s_nextSerial);
    // A stored identity hash: the default object hash goes through the object header's slow path, and passes
    // hash instructions constantly (live sets, block maps). A racy counter is fine -- a duplicate hash only
    // costs a probe. Equality stays reference equality.
    static int s_nextHash;
    readonly int _hash = unchecked(++s_nextHash);
    public override int GetHashCode() => _hash;
    public override bool Equals(object? obj) => ReferenceEquals(this, obj);
    /// <summary>Pass scratch: a stamp from <see cref="NewStamp"/> marks membership in a pass-local set, and Tag
    /// holds a pass-local annotation. Stamps are unique process-wide, so a stale mark never matches.</summary>
    public int Mark;
    public object? Tag;
    public int Pos;   // pass scratch: a position a pass assigned itself (only meaningful within that pass)
    static int s_nextStamp;
    public static int NewStamp() => System.Threading.Interlocked.Increment(ref s_nextStamp);
    public static long NextSerial() => System.Threading.Interlocked.Increment(ref s_nextSerial);

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
    [ThreadStatic] static DefTable? _defScratch;
    [ThreadStatic] static DefTable? _defFolder;
    // Passes also add lists of their own to the maps, and every list is offered back to the pool on the next
    // build, so the pool is capped (count and per-list size) or it would grow by every pass's extras forever.
    internal const int MaxPooled = 1 << 15, MaxPooledItems = 16;
    [ThreadStatic] static UserTable? _userScratch;
    [ThreadStatic] static Dictionary<uint, HashSet<SpvInstr>>? _userScratchH;
    [ThreadStatic] static Stack<HashSet<SpvInstr>>? _hsetPool;

    /// <summary>UserMapOver with HashSet buckets (O(1) user removal), for passes that maintain the map by
    /// removing individual users. Reused/recycled on the next UserMapOverHash call.</summary>
    public Dictionary<uint, HashSet<SpvInstr>> UserMapOverHash(IEnumerable<SpvInstr> instrs)
    {
        var pool = _hsetPool ??= new();
        var map = _userScratchH ??= new();
        foreach (var h in map.Values) if (pool.Count < MaxPooled && h.Count <= MaxPooledItems) { h.Clear(); pool.Push(h); }
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

    /// <summary>Map from an id to every instruction that references it (as a non-result id operand), in
    /// instruction order. The table and its lists are reused on the next UserMap()/UserMapOver() call, so a
    /// caller must finish with it before then.</summary>
    public UserTable UserMap() => UserMapRange(0, Instrs.Count);

    /// <summary>Like UserMap but over an explicit instruction sequence (a single function's blocks). The caller
    /// may keep adding/removing entries; recycled on the next UserMap/UserMapOver call.</summary>
    /// <summary>UserMapOver for the instructions at [from, to).</summary>
    public UserTable UserMapRange(int from, int to)
    {
        var map = _userScratch ??= new();
        map.Reset(Header[3]);
        for (int k = from; k < to; k++)
        {
            var ins = Instrs[k];
            ins.Classify(out int ri, out bool[] isId);
            var ops = ins.Operands;
            for (int i = 0; i < ops.Length; i++)
                if (isId[i] && i != ri) map.GetOrAdd(ops[i]).Add(ins);
        }
        return map;
    }

    public UserTable UserMapOver(IEnumerable<SpvInstr> instrs)
    {
        var map = _userScratch ??= new();
        map.Reset(Header[3]);
        foreach (var ins in instrs)
        {
            ins.Classify(out int ri, out bool[] isId);
            var ops = ins.Operands;
            for (int i = 0; i < ops.Length; i++)
                if (isId[i] && i != ri) map.GetOrAdd(ops[i]).Add(ins);
        }
        return map;
    }

    // One spare instruction list per thread: a pass that rebuilds the module into a new list takes the spare,
    // and the list it replaces becomes the next spare (module-sized lists are large-object-heap sized).
    [ThreadStatic] static List<SpvInstr>? _spareList;

    /// <summary>An empty list to rebuild the instruction stream into (pass it to <see cref="SetInstrs"/>).</summary>
    public static List<SpvInstr> RentInstrList(int capacity)
    {
        var l = _spareList; _spareList = null;
        if (l is null) return new List<SpvInstr>(capacity);
        if (l.Capacity < capacity) l.Capacity = capacity;
        return l;
    }

    /// <summary>Hand back a rented list that did not become the module's.</summary>
    public static void ReturnInstrList(List<SpvInstr> l) { l.Clear(); _spareList = l; }

    /// <summary>Install a rebuilt instruction list; the old one is cleared and kept as the spare.</summary>
    public void SetInstrs(List<SpvInstr> list)
    {
        var old = Instrs;
        Instrs = list;
        if (!ReferenceEquals(old, list)) { old.Clear(); _spareList = old; }
    }

    // Function start indices, checked on every lookup (a hit is the OpFunction for that id by definition) and
    // rebuilt on a miss, so passes that rewrite functions never need to invalidate it.
    Dictionary<uint, int>? _fnStart;
    /// <summary>Index of the OpFunction defining <paramref name="fnId"/>, or -1.</summary>
    public int FindFunction(uint fnId)
    {
        if (_fnStart is not null && _fnStart.TryGetValue(fnId, out int i) && i < Instrs.Count && Instrs[i].Opcode == 54 && Instrs[i].Operands[1] == fnId) return i;
        (_fnStart ??= new()).Clear();
        for (int k = 0; k < Instrs.Count; k++) if (Instrs[k].Opcode == 54) _fnStart[Instrs[k].Operands[1]] = k;
        return _fnStart.TryGetValue(fnId, out i) ? i : -1;
    }

    /// <summary>A second pooled def table for the constant folder, which holds its own for a whole pass while
    /// the pass itself uses DefMap(). Reused by the next Folder.</summary>
    public DefTable DefMapForFolder() => FillDefs(_defFolder ??= new());

    /// <summary>Map from each defined id to its defining instruction. Reused on the next DefMap() call.</summary>
    public DefTable DefMap() => FillDefs(_defScratch ??= new());

    DefTable FillDefs(DefTable map)
    {
        map.Reset(Header[3]);
        foreach (var ins in Instrs)
        {
            uint r = ins.ResultId;
            if (r != 0) map[r] = ins;
        }
        return map;
    }
}

/// <summary>Id -> defining instruction as a dense array indexed by id (ids are below the module bound), with
/// the Dictionary members the passes use. Lookups are an index, not a hash probe.</summary>
internal sealed class DefTable
{
    const uint DenseLimit = 1u << 24;   // larger ids (the folder's high-bit placeholders) go to a side map
    SpvInstr?[] _a = new SpvInstr?[256];
    int _hi;   // one past the highest slot ever written since the last Reset
    Dictionary<uint, SpvInstr>? _far;

    public void Reset(uint bound)
    {
        if (_a.Length < bound && bound <= DenseLimit) _a = new SpvInstr?[Math.Max((int)bound, _a.Length * 2)];
        else System.Array.Clear(_a, 0, _hi);
        _hi = 0;
        _far?.Clear();
    }

    public bool TryGetValue(uint id, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out SpvInstr value)
    {
        if (id < (uint)_a.Length) value = _a[id];
        else if (_far is null || !_far.TryGetValue(id, out value)) value = null;
        return value is not null;
    }

    public SpvInstr? GetValueOrDefault(uint id) => TryGetValue(id, out var d) ? d : null;
    public bool ContainsKey(uint id) => TryGetValue(id, out _);
    public void Remove(uint id) { if (id < (uint)_a.Length) _a[id] = null; else _far?.Remove(id); }
    /// <summary>The defining instructions, dense ids in id order, then any placeholder ids.</summary>
    public IEnumerable<SpvInstr> Values
    {
        get
        {
            for (int i = 0; i < _hi; i++) if (_a[i] is { } d) yield return d;
            if (_far is not null) foreach (var d in _far.Values) yield return d;
        }
    }

    public SpvInstr this[uint id]
    {
        get => TryGetValue(id, out var d) ? d : throw new KeyNotFoundException($"id {id}");
        set
        {
            if (id >= (uint)_a.Length)
            {
                if (id >= DenseLimit) { (_far ??= new())[id] = value; return; }
                System.Array.Resize(ref _a, Math.Max((int)id + 1, _a.Length * 2));
            }
            _a[id] = value;
            if (id >= _hi) _hi = (int)id + 1;
        }
    }
}

/// <summary>A read-only id -> users index over an instruction range, stored flat (compressed rows): users of
/// an id are a contiguous slice, in instruction order. Two linear passes, no hashing, buffers reused. For
/// passes that only read users; the next Build on this instance invalidates earlier slices.</summary>
internal sealed class UserIndex
{
    int[] _start = new int[256];   // id -> slice start (after the fill pass); _start[id + 1] is its end
    SpvInstr[] _users = new SpvInstr[1024];
    int _ids;                       // ids covered: [0, _ids)

    public void Build(List<SpvInstr> instrs, int from, int to, uint bound)
    {
        int n = (int)bound + 1;
        // A hostile bound can be huge; fall back to the ids actually used.
        if (bound > 1u << 24) { n = 0; for (int i = from; i < to; i++) foreach (uint w in instrs[i].Operands) if (w < 1u << 24 && w + 1 > n) n = (int)w + 1; n++; }
        if (_start.Length < n + 1) _start = new int[Math.Max(n + 1, _start.Length * 2)];
        else System.Array.Clear(_start, 0, n + 1);
        _ids = n;
        int total = 0;
        for (int i = from; i < to; i++)
        {
            var ins = instrs[i];
            ins.Classify(out int ri, out bool[] isId);
            var ops = ins.Operands;
            for (int k = 0; k < ops.Length; k++)
                if (isId[k] && k != ri && ops[k] < (uint)n) { _start[ops[k] + 1]++; total++; }
        }
        for (int id = 1; id <= n; id++) _start[id] += _start[id - 1];
        if (_users.Length < total) _users = new SpvInstr[Math.Max(total, _users.Length * 2)];
        // Fill using _start[id] as a cursor; afterwards _start[id] has moved to the old _start[id + 1].
        for (int i = from; i < to; i++)
        {
            var ins = instrs[i];
            ins.Classify(out int ri, out bool[] isId);
            var ops = ins.Operands;
            for (int k = 0; k < ops.Length; k++)
                if (isId[k] && k != ri && ops[k] < (uint)n) _users[_start[ops[k]]++] = ins;
        }
        for (int id = n; id > 0; id--) _start[id] = _start[id - 1];
        _start[0] = 0;
    }

    public ReadOnlySpan<SpvInstr> this[uint id] => id < (uint)_ids ? _users.AsSpan(_start[id], _start[id + 1] - _start[id]) : default;
}

/// <summary>A set of ids as a stamped array: starting a new set is O(1) (bump the stamp), membership is an
/// index. Ids at or past the dense limit spill to a hash set.</summary>
internal sealed class IdSet
{
    const uint DenseLimit = 1u << 24;
    int[] _mark = new int[256];
    int _stamp;
    HashSet<uint>? _far;

    public IdSet Begin(uint bound)
    {
        if (bound <= DenseLimit && _mark.Length < bound) _mark = new int[Math.Max((int)bound, _mark.Length * 2)];
        if (++_stamp == int.MaxValue) { System.Array.Clear(_mark); _stamp = 1; }
        _far?.Clear();
        return this;
    }

    public bool Add(uint id)
    {
        if (id < (uint)_mark.Length) { if (_mark[id] == _stamp) return false; _mark[id] = _stamp; return true; }
        if (id >= DenseLimit) return (_far ??= new()).Add(id);
        System.Array.Resize(ref _mark, Math.Max((int)id + 1, _mark.Length * 2));
        _mark[id] = _stamp; return true;
    }

    public bool Contains(uint id) => id < (uint)_mark.Length ? _mark[id] == _stamp : _far is not null && _far.Contains(id);
}

/// <summary>Id -> users list as a dense array indexed by id, with the Dictionary members the passes use. Lists
/// are recycled through a capped pool across builds.</summary>
internal sealed class UserTable
{
    const uint DenseLimit = 1u << 24;
    List<SpvInstr>?[] _a = new List<SpvInstr>?[256];
    int _hi;
    Dictionary<uint, List<SpvInstr>>? _far;
    readonly Stack<List<SpvInstr>> _pool = new();

    public void Reset(uint bound)
    {
        for (int i = 0; i < _hi; i++)
            if (_a[i] is { } l)
            {
                if (_pool.Count < SpvModule.MaxPooled && l.Capacity <= SpvModule.MaxPooledItems) { l.Clear(); _pool.Push(l); }
                _a[i] = null;
            }
        _hi = 0;
        _far?.Clear();
        if (_a.Length < bound && bound <= DenseLimit) _a = new List<SpvInstr>?[Math.Max((int)bound, _a.Length * 2)];
    }

    public List<SpvInstr> GetOrAdd(uint id)
    {
        if (id < (uint)_a.Length && _a[id] is { } l) return l;
        var n = _pool.Count > 0 ? _pool.Pop() : new List<SpvInstr>();
        this[id] = n;
        return n;
    }

    public bool TryGetValue(uint id, [System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out List<SpvInstr> value)
    {
        if (id < (uint)_a.Length) value = _a[id];
        else if (_far is null || !_far.TryGetValue(id, out value)) value = null;
        return value is not null;
    }

    public List<SpvInstr>? GetValueOrDefault(uint id) => TryGetValue(id, out var l) ? l : null;

    public IEnumerable<List<SpvInstr>> Values
    {
        get
        {
            for (int i = 0; i < _hi; i++) if (_a[i] is { } l) yield return l;
            if (_far is not null) foreach (var l in _far.Values) yield return l;
        }
    }

    public List<SpvInstr> this[uint id]
    {
        get => TryGetValue(id, out var l) ? l : throw new KeyNotFoundException($"id {id}");
        set
        {
            if (id >= (uint)_a.Length)
            {
                if (id >= DenseLimit) { (_far ??= new())[id] = value; return; }
                System.Array.Resize(ref _a, Math.Max((int)id + 1, _a.Length * 2));
            }
            _a[id] = value;
            if (id >= _hi) _hi = (int)id + 1;
        }
    }
}
