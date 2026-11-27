namespace Prowl.Prism.Optimizer;

public static partial class SpirVOptimizer
{
    // spirv-opt CopyPropagateArrays (copy_prop_arrays.cpp): a Function variable written by a single store of a
    // value read straight out of another memory object (through loads, extracts, copies, and constructs or
    // insert chains that rebuild that object in order) is replaced by an access chain into that object,
    // provided the source is never written and the store dominates every read. Only array variables qualify,
    // or any variable whose source is an Input. Users are rewritten to the source's types (new pointer types
    // go at the end of the types section), and a stored copy is rebuilt member-wise when its type changes.
    static void CopyPropagateArrays(SpvModule m)
    {
        const int OpCompositeConstruct = 80, OpCompositeExtract = 81, OpCompositeInsert = 82, OpCopyLogical = 400;
        const int OpImageTexelPointer = 60, OpTypeVector = 23, OpTypeRuntimeArray = 29, OpTypeImage = 25, OpEntryPoint = 15;
        const uint InputStorage = 1;

        uint nextId = m.Header[3];
        var def = new Dictionary<uint, SpvInstr>();
        var users = new Dictionary<uint, List<SpvInstr>>();
        foreach (var ins in m.Instrs) Track(ins);

        void Track(SpvInstr ins)
        {
            ins.Classify(out int ri, out bool[] isId);
            if (ri >= 0) def[ins.Operands[ri]] = ins;
            for (int k = 0; k < ins.Operands.Length; k++)
                if (isId[k] && k != ri) AddUser(ins.Operands[k], ins);
        }
        void AddUser(uint id, SpvInstr ins)
        {
            if (!users.TryGetValue(id, out var l)) users[id] = l = [];
            int at = l.Count; while (at > 0 && l[at - 1].Serial > ins.Serial) at--;
            if (at > 0 && l[at - 1] == ins) return;
            l.Insert(at, ins);
        }
        List<SpvInstr> Users(uint id) => users.TryGetValue(id, out var l) ? [.. l] : [];
        void SetOperand(SpvInstr ins, int index, uint value)
        {
            uint old = ins.Operands[index];
            ins.Operands[index] = value;
            ins.Classify(out int ri, out bool[] isId);
            bool stillUses = false;
            for (int k = 0; k < ins.Operands.Length; k++) if (isId[k] && k != ri && ins.Operands[k] == old) stillUses = true;
            if (!stillUses && users.TryGetValue(old, out var l)) l.Remove(ins);
            AddUser(value, ins);
        }
        // Every (user, operand index) pair of an id, in user order.
        List<(SpvInstr Use, int Index)> Uses(uint id)
        {
            var r = new List<(SpvInstr, int)>();
            foreach (var u in Users(id))
            {
                u.Classify(out int ri, out bool[] isId);
                for (int k = 0; k < u.Operands.Length; k++) if (isId[k] && k != ri && u.Operands[k] == id) r.Add((u, k));
            }
            return r;
        }

        int glslStd = -1;
        foreach (var ins in m.Instrs)
            if (ins.Opcode == 11 && DecodeSpvString(ins.Operands, 1) == "GLSL.std.450") { glslStd = (int)ins.Operands[0]; break; }
        bool IsInterpolation(SpvInstr i) => i.Opcode == OpExtInst && i.Operands.Length > 4 && i.Operands[2] == (uint)glslStd && i.Operands[3] is 76 or 77 or 78;
        static bool IsDecoration(SpvInstr i) => i.Opcode is 71 or 72 or 73 or 74 or 332 or 5632 or 5633;
        bool IsDebugDeclareOrValue(SpvInstr i) => i.Opcode == OpExtInst && i.Operands.Length > 3 && def.TryGetValue(i.Operands[2], out var set)
            && DecodeSpvString(set.Operands, 1).StartsWith("NonSemantic.Shader.DebugInfo", StringComparison.Ordinal) && i.Operands[3] is 28 or 29;
        bool IsCommonDebug(SpvInstr i) => i.Opcode == OpExtInst && i.Operands.Length > 2 && def.TryGetValue(i.Operands[2], out var set)
            && DecodeSpvString(set.Operands, 1).StartsWith("NonSemantic.Shader.DebugInfo", StringComparison.Ordinal);

        // New types and constants join the end of the types-and-values section.
        int TypesEnd() { int i = m.Instrs.FindIndex(x => x.Opcode == OpFunction); return i < 0 ? m.Instrs.Count : i; }
        uint AddGlobal(SpvInstr g) { m.Instrs.Insert(TypesEnd(), g); Track(g); return g.ResultId; }
        uint FindPointerToType(uint pointee, uint storage)
        {
            foreach (var g in m.Instrs)
            {
                if (g.Opcode == OpFunction) break;
                if (g.Opcode == OpTypePointer && g.Operands[1] == storage && g.Operands[2] == pointee) return g.Operands[0];
            }
            return AddGlobal(new SpvInstr(OpTypePointer, [nextId++, storage, pointee]));
        }
        uint MemberTypeId(uint id, IEnumerable<uint> chain)
        {
            foreach (uint index in chain)
            {
                var t = def[id];
                id = t.Opcode switch
                {
                    OpTypeArray or OpTypeRuntimeArray or OpTypeMatrix or OpTypeVector => t.Operands[1],
                    OpTypeStruct => t.Operands[1 + index],
                    _ => id,
                };
            }
            return id;
        }
        bool ConstU32(uint id, out uint v)
        {
            v = 0;
            if (!def.TryGetValue(id, out var c) || c.Opcode != OpConstant || !def.TryGetValue(c.Operands[0], out var ct) || ct.Opcode != 21) return false;
            v = c.Operands[2];
            return true;
        }
        uint NumberOfMembers(uint type)
        {
            var t = def[type];
            return t.Opcode switch
            {
                OpTypeStruct => (uint)(t.Operands.Length - 1),
                OpTypeArray => ConstU32(t.Operands[2], out uint n) ? n : 0,
                OpTypeVector or OpTypeMatrix => t.Operands[2],
                _ => 0,
            };
        }

        // Dominance between two instructions of the same function: same block by position, else block dominance.
        bool InstDominates(SpvInstr a, SpvInstr b)
        {
            if (a == b) return true;
            int ia = m.Instrs.IndexOf(a), ib = m.Instrs.IndexOf(b);
            int fs = ia; while (fs >= 0 && m.Instrs[fs].Opcode != OpFunction) fs--;
            int fe = fs; while (fe < m.Instrs.Count && m.Instrs[fe].Opcode != OpFunctionEnd) fe++;
            if (fs < 0 || ib < fs || ib > fe) return false;
            uint BlockOf(int i) { while (m.Instrs[i].Opcode != OpLabel) i--; return m.Instrs[i].Operands[0]; }
            uint ba = BlockOf(ia), bb = BlockOf(ib);
            if (ba == bb) return ia < ib;
            var blocks = ParseBlocks(m, fs, fe, out _);
            return Dominates(ba, bb, ComputeIdom(blocks));
        }

        bool HasValidReferencesOnly(SpvInstr ptr, SpvInstr store)
        {
            foreach (var use in Users(ptr.ResultId))
            {
                bool ok;
                if (use.Opcode is OpLoad or OpImageTexelPointer) ok = InstDominates(store, use);
                else if (IsInterpolation(use)) ok = use.Operands[4] == store.Operands[0] && InstDominates(store, use);
                else if (use.Opcode == OpAccessChain) ok = HasValidReferencesOnly(use, store);
                else if (IsDecoration(use) || use.Opcode == 5) ok = true;
                else if (use.Opcode == OpStore) ok = ptr.Opcode == OpVariable && store.Operands[0] == ptr.ResultId;
                else ok = IsDebugDeclareOrValue(use);
                if (!ok) return false;
            }
            return true;
        }

        bool HasNoStores(SpvInstr ptr)
        {
            foreach (var use in Users(ptr.ResultId))
            {
                bool ok = use.Opcode switch
                {
                    OpLoad or OpImageTexelPointer or OpEntryPoint or 5 => true,
                    OpAccessChain => HasNoStores(use),
                    OpStore => false,
                    _ => IsDecoration(use) || IsInterpolation(use) || IsCommonDebug(use),
                };
                if (!ok) return false;
            }
            return true;
        }

        bool IndexEquals((bool IsId, uint Value) entry, uint value) =>
            entry.IsId ? ConstU32(entry.Value, out uint v) && v == value : entry.Value == value;

        MemoryObject? SourceOf(uint result)
        {
            var inst = def[result];
            switch (inst.Opcode)
            {
                case OpLoad:
                {
                    var chain = new List<(bool, uint)>();
                    var cur = def[inst.Operands[2]];
                    while (cur.Opcode == OpAccessChain)
                    {
                        for (int k = cur.Operands.Length - 1; k >= 3; k--) chain.Add((true, cur.Operands[k]));
                        cur = def[cur.Operands[2]];
                    }
                    if (cur.Opcode != OpVariable) return null;
                    chain.Reverse();
                    return new MemoryObject(cur, chain);
                }
                case OpCompositeExtract:
                {
                    var r = SourceOf(inst.Operands[2]);
                    if (r is null) return null;
                    for (int k = 3; k < inst.Operands.Length; k++) r.Chain.Add((false, inst.Operands[k]));
                    return r;
                }
                case OpCompositeConstruct:
                {
                    var mo = SourceOf(inst.Operands[2]);
                    if (mo is null || mo.Chain.Count == 0 || !IndexEquals(mo.Chain[^1], 0)) return null;
                    mo.Chain.RemoveAt(mo.Chain.Count - 1);
                    if (ObjectMembers(mo) != inst.Operands.Length - 2) return null;
                    for (int i = 1; i < inst.Operands.Length - 2; i++)
                    {
                        var member = SourceOf(inst.Operands[2 + i]);
                        if (member is null || member.Chain.Count == 0 || !mo.Contains(member) || !IndexEquals(member.Chain[^1], (uint)i)) return null;
                    }
                    return mo;
                }
                case OpCopyObject or OpCopyLogical:
                    return SourceOf(inst.Operands[2]);
                case OpCompositeInsert:
                {
                    uint count = NumberOfMembers(inst.Operands[0]);
                    if (count == 0 || inst.Operands.Length != 5 || inst.Operands[4] != count - 1) return null;
                    var mo = SourceOf(inst.Operands[2]);
                    if (mo is null || mo.Chain.Count == 0 || !IndexEquals(mo.Chain[^1], count - 1)) return null;
                    mo.Chain.RemoveAt(mo.Chain.Count - 1);
                    var current = def[inst.Operands[3]];
                    for (uint i = count - 1; i > 0; i--)
                    {
                        if (current.Opcode != OpCompositeInsert || current.Operands.Length != 5 || current.Operands[4] != i - 1) return null;
                        var part = SourceOf(current.Operands[2]);
                        if (part is null || part.Chain.Count == 0 || mo.Chain.Count + 1 != part.Chain.Count || !mo.Contains(part)
                            || !IndexEquals(part.Chain[^1], i - 1)) return null;
                        current = def[current.Operands[3]];
                    }
                    return mo;
                }
                default:
                    return null;
            }
        }

        List<uint> AccessIds(MemoryObject mo) => [.. mo.Chain.Select(e => e.IsId ? (ConstU32(e.Value, out uint v) ? v : 0) : e.Value)];
        uint ObjectMembers(MemoryObject mo) => NumberOfMembers(MemberTypeId(def[mo.Var.Operands[0]].Operands[2], AccessIds(mo)));
        uint ObjectPointerType(MemoryObject mo)
        {
            var varPtr = def[mo.Var.Operands[0]];
            return FindPointerToType(MemberTypeId(varPtr.Operands[2], AccessIds(mo)), varPtr.Operands[1]);
        }

        bool CanUpdateUses(SpvInstr original, uint typeId)
        {
            var type = def[typeId];
            if (type.Opcode == OpTypeRuntimeArray) return false;
            if (type.Opcode is not (OpTypeStruct or OpTypeArray or OpTypePointer)) return true;
            foreach (var (use, _) in Uses(original.ResultId))
            {
                if (IsDebugDeclareOrValue(use)) continue;
                bool ok;
                switch (use.Opcode)
                {
                    case OpLoad:
                    {
                        uint newType = type.Operands[2];
                        ok = newType == use.Operands[0] || CanUpdateUses(use, newType);
                        break;
                    }
                    case OpExtInst:
                        ok = IsInterpolation(use);
                        break;
                    case OpAccessChain:
                    {
                        uint pointee = type.Operands[2];
                        var chain = new List<uint>();
                        ok = true;
                        for (int k = 3; k < use.Operands.Length; k++)
                        {
                            if (ConstU32(use.Operands[k], out uint v)) chain.Add(v);
                            else { chain.Add(0); if (def[pointee].Opcode == OpTypeStruct) { ok = false; break; } }
                        }
                        if (!ok) break;
                        uint newPtr = FindPointerToType(MemberTypeId(pointee, chain), type.Operands[1]);
                        ok = newPtr == use.Operands[0] || CanUpdateUses(use, newPtr);
                        break;
                    }
                    case OpCompositeExtract:
                    {
                        uint newType = MemberTypeId(typeId, use.Operands.Skip(3));
                        ok = newType == use.Operands[0] || CanUpdateUses(use, newType);
                        break;
                    }
                    case OpStore or OpImageTexelPointer or 5:
                        ok = true;
                        break;
                    default:
                        ok = IsDecoration(use);
                        break;
                }
                if (!ok) return false;
            }
            return true;
        }

        void InsertBefore(SpvInstr at, SpvInstr ins) { m.Instrs.Insert(m.Instrs.IndexOf(at), ins); Track(ins); }

        // Pass::GenerateCopy: rebuild a value of one aggregate type as another member by member, before |at|.
        uint GenerateCopy(SpvInstr obj, uint newType, SpvInstr at)
        {
            uint origType = obj.Operands[0];
            if (origType == newType) return obj.ResultId;
            var ot = def[origType]; var nt = def[newType];
            if (ot.Opcode != nt.Opcode) return 0;
            var elements = new List<uint>();
            if (ot.Opcode == OpTypeArray)
            {
                if (!ConstU32(ot.Operands[2], out uint length)) return 0;
                for (uint i = 0; i < length; i++)
                {
                    var extract = new SpvInstr(OpCompositeExtract, [ot.Operands[1], nextId++, obj.ResultId, i]);
                    InsertBefore(at, extract);
                    uint id = GenerateCopy(extract, nt.Operands[1], at);
                    if (id == 0) return 0;
                    elements.Add(id);
                }
            }
            else if (ot.Opcode == OpTypeStruct)
            {
                for (int i = 1; i < ot.Operands.Length; i++)
                {
                    var extract = new SpvInstr(OpCompositeExtract, [ot.Operands[i], nextId++, obj.ResultId, (uint)(i - 1)]);
                    InsertBefore(at, extract);
                    uint id = GenerateCopy(extract, nt.Operands[i], at);
                    if (id == 0) return 0;
                    elements.Add(id);
                }
            }
            else return 0;
            var construct = new SpvInstr(OpCompositeConstruct, [newType, nextId++, .. elements]);
            InsertBefore(at, construct);
            return construct.ResultId;
        }

        var worklist = new Queue<SpvInstr>();
        void AddUsesToWorklist(SpvInstr inst)
        {
            foreach (var (use, _) in Uses(inst.ResultId))
                if (use.Opcode == OpStore && def.TryGetValue(use.Operands[0], out var target) && target.Opcode == OpVariable)
                    worklist.Enqueue(target);
        }

        bool UpdateUses(SpvInstr original, SpvInstr newPtr)
        {
            foreach (var (use, index) in Uses(original.ResultId))
            {
                if (IsCommonDebug(use)) { SetOperand(use, index, newPtr.ResultId); continue; }
                switch (use.Opcode)
                {
                    case OpLoad:
                    {
                        SetOperand(use, index, newPtr.ResultId);
                        uint newType = def[newPtr.Operands[0]].Operands[2];
                        if (newType != use.Operands[0])
                        {
                            use.Operands[0] = newType;
                            if (!UpdateUses(use, use)) return false;
                        }
                        AddUsesToWorklist(use);
                        break;
                    }
                    case OpExtInst:
                        if (!IsInterpolation(use)) return false;
                        SetOperand(use, index, newPtr.ResultId);
                        break;
                    case OpAccessChain:
                    {
                        SetOperand(use, index, newPtr.ResultId);
                        var chain = new List<uint>();
                        for (int k = 3; k < use.Operands.Length; k++) chain.Add(ConstU32(use.Operands[k], out uint v) ? v : 0);
                        var ptrType = def[newPtr.Operands[0]];
                        uint newPtrType = FindPointerToType(MemberTypeId(ptrType.Operands[2], chain), ptrType.Operands[1]);
                        if (newPtrType != use.Operands[0])
                        {
                            use.Operands[0] = newPtrType;
                            if (!UpdateUses(use, use)) return false;
                        }
                        break;
                    }
                    case OpCompositeExtract:
                    {
                        SetOperand(use, index, newPtr.ResultId);
                        uint newType = MemberTypeId(newPtr.Operands[0], use.Operands.Skip(3));
                        if (newType != use.Operands[0])
                        {
                            use.Operands[0] = newType;
                            if (!UpdateUses(use, use)) return false;
                        }
                        break;
                    }
                    case OpStore:
                        // The store into the propagated variable stays (ADCE removes it); a stored value is copied
                        // into the stored-to type when it changed.
                        if (index == 1)
                        {
                            uint pointee = def[def[use.Operands[0]].Operands[0]].Operands[2];
                            uint copy = GenerateCopy(original, pointee, use);
                            if (copy == 0) return false;
                            SetOperand(use, 1, copy);
                        }
                        break;
                    case OpDecorate or OpImageTexelPointer:
                        SetOperand(use, index, newPtr.ResultId);
                        break;
                    default:
                        return false;
                }
            }
            return true;
        }

        for (int f = 0; f < m.Instrs.Count; f++)
        {
            if (m.Instrs[f].Opcode != OpFunction) continue;
            int i = f + 1; while (i < m.Instrs.Count && m.Instrs[i].Opcode == OpFunctionParameter) i++;
            if (i < m.Instrs.Count && m.Instrs[i].Opcode == OpLabel)
                for (i++; i < m.Instrs.Count && m.Instrs[i].Opcode == OpVariable; i++) worklist.Enqueue(m.Instrs[i]);
        }

        while (worklist.Count > 0)
        {
            var var = worklist.Dequeue();
            if (!m.Instrs.Contains(var)) continue;
            SpvInstr? store = null;
            foreach (var use in Users(var.ResultId))
                if (use.Opcode == OpStore && use.Operands[0] == var.ResultId)
                {
                    if (store is null) store = use;
                    else { store = null; break; }
                }
            if (store is null || !HasValidReferencesOnly(var, store)) continue;
            var source = SourceOf(store.Operands[1]);
            if (source is null || !HasNoStores(source.Var)) continue;
            var varPointee = def[def[var.Operands[0]].Operands[2]];
            bool arrayLike = varPointee.Opcode is OpTypeArray or OpTypeImage;
            if (!arrayLike && def[source.Var.Operands[0]].Operands[1] != InputStorage) continue;

            uint pointerType = ObjectPointerType(source);
            if (!CanUpdateUses(var, pointerType)) continue;

            // BuildNewAccessChain: the source variable itself, or an access chain into it before the store.
            SpvInstr newPtr;
            if (source.Chain.Count == 0) newPtr = source.Var;
            else
            {
                var ids = new List<uint>();
                foreach (var (isId, value) in source.Chain)
                {
                    if (isId) { ids.Add(value); continue; }
                    uint uintType = 0;
                    foreach (var g in m.Instrs) { if (g.Opcode == OpFunction) break; if (g.Opcode == 21 && g.Operands[1] == 32 && g.Operands[2] == 0) { uintType = g.Operands[0]; break; } }
                    if (uintType == 0) return;
                    uint constant = 0;
                    foreach (var g in m.Instrs) { if (g.Opcode == OpFunction) break; if (g.Opcode == OpConstant && g.Operands[0] == uintType && g.Operands.Length == 3 && g.Operands[2] == value) { constant = g.Operands[1]; break; } }
                    ids.Add(constant != 0 ? constant : AddGlobal(new SpvInstr(OpConstant, [uintType, nextId++, value])));
                }
                newPtr = new SpvInstr(OpAccessChain, [pointerType, nextId++, source.Var.ResultId, .. ids]);
                InsertBefore(store, newPtr);
            }
            // KillNamesAndDecorates on the propagated variable.
            foreach (var use in Users(var.ResultId))
                if (IsDecoration(use) || use.Opcode == 5) m.Instrs.Remove(use);
            if (!UpdateUses(var, newPtr)) break;
        }
        m.Header[3] = nextId;
    }

    sealed class MemoryObject(SpvInstr var, List<(bool IsId, uint Value)> chain)
    {
        public SpvInstr Var = var;
        public List<(bool IsId, uint Value)> Chain = chain;

        public bool Contains(MemoryObject other)
        {
            if (Var != other.Var || Chain.Count > other.Chain.Count) return false;
            for (int i = 0; i < Chain.Count; i++) if (Chain[i] != other.Chain[i]) return false;
            return true;
        }
    }
}
