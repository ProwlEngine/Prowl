using System;
using System.Collections.Generic;
using System.Linq;

namespace Prowl.Prism.Optimizer;

/// <summary>
/// A faithful port of spirv-opt's shared folding engine: the ConstantManager (constants.cpp), the
/// InstructionFolder dispatch (fold.cpp), and const_folding_rules.cpp. CCP and the Simplification pass both go
/// through this, so the constants it materializes get their ids in exactly spirv-opt's order -- byte parity by
/// construction, instead of two separate approximations (the old inline CCP folder and SimplifyFolds).
///
/// A "constant" is identified by its defining instruction (OpConstant / OpConstantTrue|False / OpConstantNull /
/// OpConstantComposite). Constants are created lazily and deduped by value, and appended to the types/constants
/// section (before the first OpFunction) with a fresh id from Header[3] -- matching ConstantManager::
/// GetDefiningInstruction -> FindDeclaredConstant / BuildInstructionAndAddToModule.
/// </summary>
internal sealed class Folder
{
    // Opcodes used here.
    const int OpTypeInt = 21, OpTypeFloat = 22, OpTypeVector = 23, OpTypeBool = 20,
        OpConstantTrue = 41, OpConstantFalse = 42, OpConstant = 43, OpConstantComposite = 44, OpConstantNull = 46,
        OpFunction = 54, OpExtInst = 12, OpExtInstImport = 11;

    readonly SpvModule _m;
    readonly DefTable _def;         // id -> defining instruction (kept in sync as constants are added)
    readonly List<SpvInstr> _newConsts = [];          // constants created this run, spliced in at the end
    readonly List<SpvInstr> _pendingBody = [];        // body instrs a rule created, to splice before the current inst
    readonly Dictionary<string, uint> _scalarPool = [];   // "type|word0,word1,.." -> id  (OpConstant)
    readonly Dictionary<uint, uint> _truePool = [], _falsePool = [], _nullPool = [];   // type -> id
    readonly Dictionary<string, uint> _compositePool = [];   // "type:c0,c1,.." -> id
    uint _nextId;
    uint _glslExt;                                    // the GLSL.std.450 ext-inst import id, or 0

    public Folder(SpvModule m)
    {
        _m = m;
        _def = m.DefMapForFolder();   // held for this Folder's lifetime, so it must not be the pass's DefMap() scratch
        _nextId = m.Header[3];
        foreach (var ins in m.Instrs)
        {
            switch (ins.Opcode)
            {
                case OpExtInstImport when Str(ins) == "GLSL.std.450": _glslExt = ins.Operands[0]; break;
                case OpConstant when ins.Operands.Length >= 2: _scalarPool[ScalarKey(ins.Operands[0], ins.Operands[2..])] = ins.Operands[1]; break;
                case OpConstantTrue: _truePool[ins.Operands[0]] = ins.Operands[1]; break;
                case OpConstantFalse: _falsePool[ins.Operands[0]] = ins.Operands[1]; break;
                case OpConstantNull: _nullPool[ins.Operands[0]] = ins.Operands[1]; break;
                case OpConstantComposite when ins.Operands.Length >= 2: _compositePool[CompKey(ins.Operands[0], ins.Operands[2..])] = ins.Operands[1]; break;
                case 17 when ins.Operands.Length >= 1 && ins.Operands[0] is >= 4464 and <= 4468: _fpFoldBlocked = true; break;   // float-control caps
                case 71 when ins.Operands.Length >= 2 && ins.Operands[1] == 42: _noContract.Add(ins.Operands[0]); break;         // OpDecorate NoContraction
            }
        }
        RegisterRules();
        RegisterFoldRules();
    }

    /// <summary>Commit created constants into the module and write back the id counter. Call once when done.</summary>
    public void Commit()
    {
        if (_newConsts.Count > 0) { int ff = _m.Instrs.FindIndex(x => x.Opcode == OpFunction); _m.Instrs.InsertRange(ff < 0 ? _m.Instrs.Count : ff, _newConsts); }
        _m.Header[3] = _nextId;
    }

    static string Str(SpvInstr ins)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 1; i < ins.Operands.Length; i++) { uint w = ins.Operands[i]; for (int b = 0; b < 4; b++) { char c = (char)((w >> (b * 8)) & 0xFF); if (c == 0) return sb.ToString(); sb.Append(c); } }
        return sb.ToString();
    }

    static string ScalarKey(uint type, uint[] words) => type + "|" + string.Join(',', words);
    static string CompKey(uint type, uint[] comps) => type + ":" + string.Join(',', comps);

    // ---- ConstantManager: value inspection ----

    public bool IsConst(uint id) => _def.TryGetValue(id, out var d) && d.Opcode is OpConstant or OpConstantTrue or OpConstantFalse or OpConstantNull or OpConstantComposite;
    /// <summary>Whether <paramref name="id"/> is a boolean constant (incl. ones created this run), and its value.</summary>
    public bool AsBool(uint id, out bool v) => BoolVal(id, out v);
    SpvInstr? Def(uint id) => _def.TryGetValue(id, out var d) ? d : null;
    public uint TypeOf(uint id) => _def.TryGetValue(id, out var d) && d.Operands.Length > 0 ? d.Operands[0] : 0;

    bool IsFloatType(uint t) => Def(t) is { Opcode: OpTypeFloat };
    bool IsIntType(uint t) => Def(t) is { Opcode: OpTypeInt };
    int TypeWidth(uint t) => Def(t) is { Opcode: OpTypeFloat or OpTypeInt } d && d.Operands.Length >= 2 ? (int)d.Operands[1] : 0;
    bool IntSigned(uint t) => Def(t) is { Opcode: OpTypeInt } d && d.Operands.Length >= 3 && d.Operands[2] == 1;

    // A 32-bit scalar constant's raw word (null -> 0). Returns false if not a 32-bit scalar/null constant.
    bool ScalarWord(uint id, out uint word)
    {
        word = 0;
        var d = Def(id);
        if (d is null) return false;
        if (d.Opcode == OpConstantNull) return true;
        if (d.Opcode == OpConstant && d.Operands.Length == 3) { word = d.Operands[2]; return true; }
        if (d.Opcode is OpConstantTrue) { word = 1; return true; }
        if (d.Opcode is OpConstantFalse) { word = 0; return true; }
        return false;
    }

    bool BoolVal(uint id, out bool v)
    {
        v = false; var d = Def(id);
        if (d is { Opcode: OpConstantTrue }) { v = true; return true; }
        if (d is { Opcode: OpConstantFalse or OpConstantNull }) { v = false; return true; }
        return false;
    }

    bool IsZeroScalar(uint id) { var d = Def(id); if (d is { Opcode: OpConstantNull }) return true; if (d is { Opcode: OpConstant } && d.Operands.Length == 3) return d.Operands[2] == 0; if (d is { Opcode: OpConstantFalse }) return true; return false; }

    // ---- ConstantManager: creation (deduped, TakeNextId at types section) ----

    public uint ScalarConst(uint type, uint word)
    {
        string k = ScalarKey(type, [word]);
        if (_scalarPool.TryGetValue(k, out uint id)) return id;
        id = _nextId++; var c = new SpvInstr(OpConstant, [type, id, word]); _newConsts.Add(c); _def[id] = c; _scalarPool[k] = id; return id;
    }
    /// <summary>First word of an integer constant (0 for a null one), as CCP reads a switch selector.</summary>
    public bool SwitchSelectorWord(uint id, out uint word)
    {
        word = 0;
        var d = Def(id);
        if (d is { Opcode: OpConstantNull }) return true;
        if (d is { Opcode: OpConstant } && Def(d.Operands[0]) is { Opcode: OpTypeInt }) { word = d.Operands[2]; return true; }
        return false;
    }
    public uint BoolConst(uint type, bool v)
    {
        var pool = v ? _truePool : _falsePool;
        if (pool.TryGetValue(type, out uint id)) return id;
        id = _nextId++; var c = new SpvInstr(v ? OpConstantTrue : OpConstantFalse, [type, id]); _newConsts.Add(c); _def[id] = c; pool[type] = id; return id;
    }
    public uint NullConst(uint type)
    {
        if (_nullPool.TryGetValue(type, out uint id)) return id;
        id = _nextId++; var c = new SpvInstr(OpConstantNull, [type, id]); _newConsts.Add(c); _def[id] = c; _nullPool[type] = id; return id;
    }
    // A null component of a null vector (Constant::GetVectorComponents) is only a Constant object in spirv-opt;
    // it gets an id only if it ends up in the module. These placeholder ids (high bit set, never a real id) stand
    // in for it until Materialize turns them into a real OpConstantNull.
    const uint VirtualNullBit = 0x80000000;
    uint VirtualNull(uint type)
    {
        if (_nullPool.TryGetValue(type, out uint id)) return id;
        id = VirtualNullBit | type;
        if (!_def.ContainsKey(id)) _def[id] = new SpvInstr(OpConstantNull, [type, id]);
        return id;
    }
    uint Materialize(uint id) => (id & VirtualNullBit) != 0 ? NullConst(id & ~VirtualNullBit) : id;

    uint CompositeConst(uint type, uint[] comps)
    {
        comps = comps.Select(Materialize).ToArray();
        string k = CompKey(type, comps);
        if (_compositePool.TryGetValue(k, out uint id)) return id;
        id = _nextId++; var c = new SpvInstr(OpConstantComposite, [type, id, .. comps]); _newConsts.Add(c); _def[id] = c; _compositePool[k] = id; return id;
    }

    // Make a scalar OR vector constant from result-type + one word per (broadcast) component; used to wrap the
    // result of FoldScalars/OperateWords back into a typed constant (fold.cpp GetConstant({result_val})).
    uint TypedScalar(uint type, uint word)
    {
        if (Def(type) is { Opcode: OpTypeBool }) return BoolConst(type, word != 0);
        if (TypeWidth(type) == 64)
            return Scalar64(type, word, word == 0xFFFFFFFF ? 0xFFFFFFFFu : 0u);
        return ScalarConst(type, word);
    }

    // ================= FoldInstructionToConstant (fold.cpp) =================

    /// <summary>Fold an instruction to a constant, mapping operand ids through <paramref name="map"/> (CCP's
    /// lattice). Returns the constant id, or 0 if not foldable. Faithful to InstructionFolder::
    /// FoldInstructionToConstant: const-folding rules first (in registered order), then FoldScalars, then
    /// FoldIntegerOpToConstant.</summary>
    public uint FoldToConstant(SpvInstr inst, Func<uint, uint> map)
    {
        if (!IsFoldableByFoldScalar(inst) && !IsFoldableByFoldVector(inst) && !HasFoldingRule(inst)) return 0;

        // Collect (possibly-null) constant operand ids, mapped. The list is reused across calls (no rule
        // re-enters this method or retains the list -- each copies what it needs), which matters because
        // CCP and Simplification call this for nearly every instruction in the module.
        inst.Classify(out int ri, out bool[] isId);
        var ops = _opsScratch;
        ops.Clear();
        bool missing = false;
        for (int i = 0; i < inst.Operands.Length; i++)
        {
            if (i == ri) continue;                                   // result id
            if (inst.Opcode != OpExtInst && i == 0 && isId[0] && Def(inst.Operands[0]) is { Opcode: >= 19 and <= 39 }) continue;   // result type (any OpType*)
            if (!isId[i]) { ops.Add(0); continue; }                  // literal (e.g. shuffle indices) - handled by rules
            uint mid = map(inst.Operands[i]);
            ops.Add(mid);
            if (!IsConst(mid)) missing = true;
        }

        // 1) Constant folding rules (in order). First non-zero wins.
        foreach (var rule in RulesFor(inst))
        {
            uint r = rule(inst, ops);
            if (r != 0) return Materialize(r);
        }

        // 2) FoldScalars (scalar arithmetic on 32-bit words) then FoldIntegerOpToConstant (partial constants).
        // Scalar/vector arithmetic runs in 32 bits, so narrow (8/16-bit) results are normalized back to
        // their width here (wrap + sign/zero-extend); without that U16 0x8000<<1 folds to 0x10000.
        if (IsFoldableByFoldScalar(inst))
        {
            if (!missing && FoldScalars(inst, ops, map, out uint rv)) return NormalizeFolded(inst.Operands[0], rv);
            if (FoldIntegerOpToConstant(inst, ops, out uint rv2)) return TypedScalar(inst.Operands[0], rv2);
        }
        else if (IsFoldableByFoldVector(inst))
        {
            if (!missing && FoldVectors(inst, ops, out uint[] words))
            {
                var t = inst.Operands[0]; var vt = Def(t)!; uint elem = vt.Operands[1];
                var comps = new uint[words.Length];
                for (int i = 0; i < words.Length; i++) { comps[i] = NormalizeFolded(elem, words[i]); if (comps[i] == 0) return 0; }
                return CompositeConst(t, comps);
            }
        }
        return 0;
    }

    uint NormalizeFolded(uint type, uint word)
    {
        if (Def(type) is { Opcode: OpTypeBool }) return BoolConst(type, word != 0);
        if (Def(type) is { Opcode: OpTypeInt }) return GenerateIntegerConstant(type, word);
        return ScalarConst(type, word);
    }

    // fold.cpp IsFoldableOpcode -- the ops FoldScalars/OperateWords handle.
    static bool IsFoldableOpcode(int op) => op is
        199 or 197 or 198 or 128 or 170 or 132 or 171 or 130 or 167 or 164 or 168 or 165 or 166 or 200 or
        135 or 169 or 173 or 175 or 196 or 195 or 194 or 177 or 179 or 139 or 126 or 138 or 114 or 113 or 134 or
        172 or 174 or 176 or 178 or 137;
    // (OpBitwiseAnd199 Or197 Xor198 IAdd128 IEqual170 IMul132 INotEqual171 ISub130 LogicalAnd167 LogicalEqual164
    //  LogicalNot168 LogicalNotEqual165 LogicalOr166 Not200 SDiv135 Select169 SGreaterThan173 SGreaterThanEqual175
    //  ShiftLeftLogical196 ShiftRightArithmetic195 ShiftRightLogical194 SLessThan177 SLessThanEqual179 SMod139
    //  SNegate126 SRem138 SConvert114 UConvert113 UDiv134 UGreaterThan172 UGreaterThanEqual174
    //  ULessThan176 ULessThanEqual178 UMod137)

    bool IsScalarNumeric(uint type) => Def(type) is { Opcode: OpTypeInt or OpTypeBool } || (Def(type) is { Opcode: OpTypeFloat });
    bool IsFoldableByFoldScalar(SpvInstr inst) => IsFoldableOpcode(inst.Opcode) && inst.Operands.Length > 0 && Def(inst.Operands[0]) is { Opcode: OpTypeInt or OpTypeBool };
    bool IsFoldableByFoldVector(SpvInstr inst) => IsFoldableOpcode(inst.Opcode) && inst.Operands.Length > 0 && Def(inst.Operands[0]) is { Opcode: OpTypeVector } vt && Def(vt.Operands[1]) is { Opcode: OpTypeInt or OpTypeBool };

    // FoldScalars: OperateWords on the raw 32-bit words of the operands. Returns false if any operand isn't a
    // 32-bit scalar/null constant. Shifts only fold at 32/64-bit width (spirv-opt leaves narrow shifts alone).
    bool FoldScalars(SpvInstr inst, List<uint> ops, Func<uint, uint> map, out uint result)
    {
        result = 0;
        if (inst.Opcode is 194 or 195 or 196 && TypeWidth(inst.Operands[0]) is not (32 or 64)) return false;
        var words = new uint[ops.Count];
        for (int i = 0; i < ops.Count; i++) { if (!ScalarWord(ops[i], out words[i])) return false; }
        return OperateWords(inst.Opcode, words, out result);
    }

    bool FoldVectors(SpvInstr inst, List<uint> ops, out uint[] result)
    {
        var vt = Def(inst.Operands[0])!; int dims = (int)vt.Operands[2];
        result = new uint[dims];
        if (inst.Opcode is 194 or 195 or 196 && TypeWidth(vt.Operands[1]) is not (32 or 64)) return false;
        // each operand must be a vector constant (composite) or null.
        var comps = new uint[ops.Count][];
        for (int i = 0; i < ops.Count; i++)
        {
            var d = Def(ops[i]);
            if (d is { Opcode: OpConstantNull }) { comps[i] = null!; continue; }
            if (d is { Opcode: OpConstantComposite }) { comps[i] = d.Operands[2..]; continue; }
            return false;
        }
        for (int dd = 0; dd < dims; dd++)
        {
            var w = new uint[ops.Count];
            for (int i = 0; i < ops.Count; i++) { if (comps[i] is null) w[i] = 0; else if (!ScalarWord(comps[i][dd], out w[i])) return false; }
            if (!OperateWords(inst.Opcode, w, out result[dd])) return false;
        }
        return true;
    }

    // fold.cpp OperateWords / Unary / Binary / TernaryOperate -- exact.
    bool OperateWords(int opcode, uint[] w, out uint result)
    {
        result = 0;
        switch (w.Length)
        {
            case 1: return UnaryOperate(opcode, w[0], out result);
            case 2: return BinaryOperate(opcode, w[0], w[1], out result);
            case 3: if (opcode == 169) { result = ToBool(w[0]) ? w[1] : w[2]; return true; } return false;   // OpSelect
            default: return false;
        }
    }
    static bool ToBool(uint x) => x != 0;
    bool UnaryOperate(int op, uint a, out uint r)
    {
        r = 0;
        switch (op)
        {
            case 126: r = (int)a == int.MinValue ? a : unchecked((uint)(-(int)a)); return true;   // OpSNegate
            case 200: r = ~a; return true;                                                        // OpNot
            case 168: r = ToBool(a) ? 0u : 1u; return true;                                        // OpLogicalNot
            default: return false;
        }
    }
    bool BinaryOperate(int op, uint a, uint b, out uint r)
    {
        r = 0;
        switch (op)
        {
            case 194: r = b >= 32 ? 0 : a >> (int)b; return true;                                          // ShiftRightLogical
            case 195: if (b > 32) { r = 0; } else if (b == 32) { r = (int)a >= 0 ? 0u : unchecked((uint)-1); } else r = unchecked((uint)((int)a >> (int)b)); return true;   // ShiftRightArithmetic
            case 196: r = b >= 32 ? 0 : a << (int)b; return true;                                          // ShiftLeftLogical
            case 197: r = a | b; return true;                                                              // BitwiseOr
            case 199: r = a & b; return true;                                                              // BitwiseAnd
            case 198: r = a ^ b; return true;                                                              // BitwiseXor
            case 164: r = ToBool(a) == ToBool(b) ? 1u : 0u; return true;                                   // LogicalEqual
            case 165: r = ToBool(a) != ToBool(b) ? 1u : 0u; return true;                                   // LogicalNotEqual
            case 166: r = ToBool(a) || ToBool(b) ? 1u : 0u; return true;                                   // LogicalOr
            case 167: r = ToBool(a) && ToBool(b) ? 1u : 0u; return true;                                   // LogicalAnd
            case 170: r = a == b ? 1u : 0u; return true;                                                   // IEqual
            case 171: r = a != b ? 1u : 0u; return true;                                                   // INotEqual
            case 176: r = a < b ? 1u : 0u; return true;                                                    // ULessThan
            case 177: r = (int)a < (int)b ? 1u : 0u; return true;                                          // SLessThan
            case 172: r = a > b ? 1u : 0u; return true;                                                    // UGreaterThan
            case 173: r = (int)a > (int)b ? 1u : 0u; return true;                                          // SGreaterThan
            case 178: r = a <= b ? 1u : 0u; return true;                                                   // ULessThanEqual
            case 179: r = (int)a <= (int)b ? 1u : 0u; return true;                                         // SLessThanEqual
            case 174: r = a >= b ? 1u : 0u; return true;                                                   // UGreaterThanEqual
            case 175: r = (int)a >= (int)b ? 1u : 0u; return true;                                         // SGreaterThanEqual
            case 128: r = a + b; return true;                                                              // IAdd  (also handled by rule; harmless)
            case 130: r = a - b; return true;                                                              // ISub
            case 132: r = a * b; return true;                                                              // IMul
            default: return false;
        }
    }

    // fold.cpp FoldIntegerOpToConstant -> FoldBinaryIntegerOpToConstant + FoldBinaryBooleanOpToConstant.
    bool FoldIntegerOpToConstant(SpvInstr inst, List<uint> ops, out uint result)
    {
        result = 0;
        if (ops.Count != 2) return false;
        return FoldBinaryIntegerOpToConstant(inst, ops, out result) || FoldBinaryBooleanOpToConstant(inst.Opcode, ops, out result);
    }
    bool IntConstU(uint id, out uint v) { v = 0; var d = Def(id); if (d is { Opcode: OpConstant } && d.Operands.Length == 3) { v = d.Operands[2]; return true; } if (d is { Opcode: OpConstantNull }) return true; return false; }
    bool FoldBinaryIntegerOpToConstant(SpvInstr inst, List<uint> ops, out uint result)
    {
        result = 0;
        int op = inst.Opcode;
        bool c0 = IntConstU(ops[0], out uint v0), c1 = IntConstU(ops[1], out uint v1);
        switch (op)
        {
            case 132: case 134: case 135: case 138: case 139: case 137:   // IMul / UDiv / SDiv / SRem / SMod / UMod : *,/ by 0 -> 0
                if ((c0 && v0 == 0) || (c1 && v1 == 0)) { result = 0; return true; } break;
            case 194: case 196:                                            // shifts by >= bit-width -> 0 (32/64-bit only)
                {
                    int width = TypeWidth(inst.Operands[0]);
                    if (width != 32 && width != 64) break;
                    if (c1 && v1 >= (uint)width) { result = 0; return true; }
                    break;
                }
            case 197:                                                      // BitwiseOr with all-ones -> all-ones
                if ((c0 && v0 == 0xFFFFFFFF) || (c1 && v1 == 0xFFFFFFFF)) { result = 0xFFFFFFFF; return true; } break;
            case 199:                                                      // BitwiseAnd with 0 -> 0
                if ((c0 && v0 == 0) || (c1 && v1 == 0)) { result = 0; return true; } break;
            case 176: if ((c0 && v0 == 0xFFFFFFFF) || (c1 && v1 == 0)) { result = 0; return true; } break;                     // ULessThan
            case 177: if ((c0 && (int)v0 == int.MaxValue) || (c1 && (int)v1 == int.MinValue)) { result = 0; return true; } break;   // SLessThan
            case 172: if ((c0 && v0 == 0) || (c1 && v1 == 0xFFFFFFFF)) { result = 0; return true; } break;                     // UGreaterThan
            case 173: if ((c0 && (int)v0 == int.MinValue) || (c1 && (int)v1 == int.MaxValue)) { result = 0; return true; } break;
            case 178: if ((c0 && v0 == 0) || (c1 && v1 == 0xFFFFFFFF)) { result = 1; return true; } break;
            case 179: if ((c0 && (int)v0 == int.MinValue) || (c1 && (int)v1 == int.MaxValue)) { result = 1; return true; } break;
            case 174: if ((c0 && v0 == 0xFFFFFFFF) || (c1 && v1 == 0)) { result = 1; return true; } break;
            case 175: if ((c0 && (int)v0 == int.MaxValue) || (c1 && (int)v1 == int.MinValue)) { result = 1; return true; } break;
        }
        return false;
    }
    bool FoldBinaryBooleanOpToConstant(int op, List<uint> ops, out uint result)
    {
        result = 0;
        bool c0 = BoolVal(ops[0], out bool v0), c1 = BoolVal(ops[1], out bool v1);
        switch (op)
        {
            case 166: if ((c0 && v0) || (c1 && v1)) { result = 1; return true; } break;   // LogicalOr, one true -> true
            case 167: if ((c0 && !v0) || (c1 && !v1)) { result = 0; return true; } break; // LogicalAnd, one false -> false
        }
        return false;
    }

    // ================= const_folding_rules dispatch =================
    // Each rule takes (inst, mapped-operand-ids) and returns a constant id or 0. Registered in spirv-opt's order.

    delegate uint Rule(SpvInstr inst, List<uint> ops);

    bool HasFoldingRule(SpvInstr inst) => RulesFor(inst).Count > 0;
    /// <summary>Instruction::IsFoldable: FoldInstructionToConstant can act on it at all.</summary>
    public bool IsFoldable(SpvInstr inst) => IsFoldableByFoldScalar(inst) || IsFoldableByFoldVector(inst) || HasFoldingRule(inst);

    IReadOnlyList<Rule> RulesFor(SpvInstr inst)
    {
        if (inst.Opcode == OpExtInst)
        {
            if (_glslExt == 0 || inst.Operands.Length < 4 || inst.Operands[2] != _glslExt) return [];
            return _extRules.TryGetValue(inst.Operands[3], out var er) ? er : [];
        }
        return _rules.TryGetValue(inst.Opcode, out var r) ? r : [];
    }

    readonly List<uint> _opsScratch = [];   // reused operand buffer for FoldToConstant
    readonly Dictionary<int, List<Rule>> _rules = [];
    readonly Dictionary<uint, List<Rule>> _extRules = [];

    void Add(int op, params Rule[] rs) { if (!_rules.TryGetValue(op, out var l)) _rules[op] = l = []; l.AddRange(rs); }
    void AddExt(uint op, params Rule[] rs) { if (!_extRules.TryGetValue(op, out var l)) _extRules[op] = l = []; l.AddRange(rs); }

    // ================= FoldInstruction (fold.cpp) : the Simplification path =================
    // In-place simplification rules (folding_rules.cpp). A rule mutates `inst` and returns true if it changed it.
    // The result is either a constant (via FoldToConstant -> OpCopyObject) or an in-place rewrite.

    public delegate bool FoldRule(SpvInstr inst);
    readonly Dictionary<int, List<FoldRule>> _foldRules = [];
    readonly Dictionary<uint, List<FoldRule>> _foldExtRules = [];
    void AddF(int op, params FoldRule[] rs) { if (!_foldRules.TryGetValue(op, out var l)) _foldRules[op] = l = []; l.AddRange(rs); }
    void AddFExt(uint op, params FoldRule[] rs) { if (!_foldExtRules.TryGetValue(op, out var l)) _foldExtRules[op] = l = []; l.AddRange(rs); }

    IReadOnlyList<FoldRule> FoldRulesFor(SpvInstr inst)
    {
        if (inst.Opcode == OpExtInst)
        {
            if (_glslExt == 0 || inst.Operands.Length < 4 || inst.Operands[2] != _glslExt) return [];
            return _foldExtRules.TryGetValue(inst.Operands[3], out var er) ? er : [];
        }
        return _foldRules.TryGetValue(inst.Opcode, out var r) ? r : [];
    }

    const int OpCopyObject = 83, OpNop = 0;
    const int OpUndef = 1, OpVectorShuffle = 79, OpCompositeConstruct = 80, OpCompositeExtract = 81, OpCompositeInsert = 82,
        OpBitcast = 124, OpPhi = 245, OpDot = 148,
        OpTypeArray = 28, OpTypeMatrix = 24, OpTypeStruct = 30,
        OpFAdd = 129, OpFSub = 131, OpFMul = 133, OpFDiv = 136, OpFMod = 141, OpFNegate = 127,
        OpIAdd = 128, OpISub = 130, OpIMul = 132, OpUDiv = 134, OpSDiv = 135, OpUMod = 137, OpSRem = 138, OpSMod = 139,
        OpBitwiseOr = 197, OpBitwiseXor = 198, OpBitwiseAnd = 199, OpShiftRightLogical = 194, OpShiftRightArithmetic = 195, OpShiftLeftLogical = 196,
        OpSelect = 169, OpLogicalAnd = 167, OpLogicalOr = 166, OpLogicalNot = 168, OpLogicalEqual = 164, OpLogicalNotEqual = 165;

    /// <summary>Fold an instruction in place to a fixpoint (fold.cpp FoldInstruction). Returns true if changed.
    /// If it folds to a constant, the instruction becomes OpCopyObject of that constant (the Simplification
    /// driver then propagates and kills it).</summary>
    public bool FoldInstruction(SpvInstr inst)
    {
        bool modified = false;
        while (inst.Opcode != OpCopyObject && FoldInstructionInternal(inst)) modified = true;
        return modified;
    }

    bool FoldInstructionInternal(SpvInstr inst)
    {
        uint folded = FoldToConstant(inst, id => id);
        if (folded != 0)
        {
            uint rt = inst.Operands[0], rid = inst.ResultId;
            inst.Opcode = OpCopyObject; inst.Operands = [rt, rid, folded];
            _def[rid] = inst;
            return true;
        }
        foreach (var rule in FoldRulesFor(inst)) if (rule(inst)) return true;
        return false;
    }

    // Rewrite `inst` in place to OpCopyObject of `srcId` (an existing value). Used by simplification rules that
    // reduce an instruction to one of its operands (x+0 -> x). No new id.
    bool ToCopy(SpvInstr inst, uint srcId) { inst.Opcode = OpCopyObject; inst.Operands = [inst.Operands[0], inst.ResultId, srcId]; return true; }

    // Constant helpers for folding_rules (return existing-or-created constant ids, or 0/false).
    public bool AsFloat32(uint id, out float v) { v = 0; if (!ScalarWord(id, out uint w)) return false; if (Def(TypeOf(id)) is not { Opcode: OpTypeFloat }) return false; if (TypeWidth(TypeOf(id)) != 32) return false; v = F32(w); return true; }
    bool IsFloatOrFloatVec(uint type) => Def(type) is { Opcode: OpTypeFloat } || (Def(type) is { Opcode: OpTypeVector } vt && Def(vt.Operands[1]) is { Opcode: OpTypeFloat });

    // spirv-opt IsFloatingPointFoldingAllowed: folding an FP op is allowed unless the module declares a
    // FloatControls execution mode that would make it observable, or the instruction is NoContraction. We
    // approximate with the two signals our corpus uses (matches SimplifyFolds): NoContraction decoration and
    // the relevant capabilities. Populated in the ctor.
    bool _fpFoldBlocked;
    readonly HashSet<uint> _noContract = [];
    bool FpAllowed(SpvInstr inst) => !_fpFoldBlocked && !_noContract.Contains(inst.ResultId);

    static float F32(uint w) => BitConverter.UInt32BitsToSingle(w);
    static uint W32(float f) => BitConverter.SingleToUInt32Bits(f);
    static double F64(uint lo, uint hi) => BitConverter.UInt64BitsToDouble(lo | ((ulong)hi << 32));
    static float F16(uint w) => (float)BitConverter.UInt16BitsToHalf((ushort)(w & 0xFFFF));

    // FoldFPBinaryOp(FOLD_FPARITH_OP(op)) for scalar/vector. Only f32/f64 element width. Creates constants.
    uint FpBinary(SpvInstr inst, List<uint> ops, Func<uint, uint, float> f32, Func<double, double, double> f64)
    {
        if (!FpAllowed(inst)) return 0;
        uint type = inst.Operands[0];
        uint a = inst.Opcode == OpExtInst ? ops[1] : ops[0], b = inst.Opcode == OpExtInst ? ops[2] : ops[1];
        return FpBinaryTyped(type, a, b, f32, f64);
    }
    uint FpBinaryTyped(uint type, uint a, uint b, Func<uint, uint, float> f32, Func<double, double, double> f64)
    {
        var td = Def(type)!;
        if (td.Opcode == OpTypeVector)
        {
            uint elem = td.Operands[1]; int n = (int)td.Operands[2];
            var ca = Comps(a, n); var cb = Comps(b, n); if (ca is null || cb is null) return 0;
            var res = new uint[n];
            for (int i = 0; i < n; i++) { uint r = FpBinaryTyped(elem, ca[i], cb[i], f32, f64); if (r == 0) return 0; res[i] = r; }
            return CompositeConst(type, res);
        }
        int width = TypeWidth(type);
        if (width == 32) { if (!ScalarWord(a, out uint aw) || !ScalarWord(b, out uint bw)) return 0; return ScalarConst(type, W32(f32(aw, bw))); }
        if (width == 64) { var da = Words64(a); var db = Words64(b); if (da is null || db is null) return 0; ulong bits = BitConverter.DoubleToUInt64Bits(f64(F64(da[0], da[1]), F64(db[0], db[1]))); return Scalar64(type, (uint)bits, (uint)(bits >> 32)); }
        return 0;
    }
    uint[]? Comps(uint id, int n) { var d = Def(id); if (d is { Opcode: OpConstantNull }) return Enumerable.Range(0, n).Select(_ => VirtualNull(Def(TypeOf(id))!.Operands[1])).ToArray(); if (d is { Opcode: OpConstantComposite }) return d.Operands[2..]; return null; }
    uint[]? Words64(uint id) { var d = Def(id); if (d is { Opcode: OpConstant } && d.Operands.Length == 4) return [d.Operands[2], d.Operands[3]]; if (d is { Opcode: OpConstantNull }) return [0, 0]; return null; }
    uint Scalar64(uint type, uint lo, uint hi) { string k = ScalarKey(type, [lo, hi]); if (_scalarPool.TryGetValue(k, out uint id)) return id; id = _nextId++; var c = new SpvInstr(OpConstant, [type, id, lo, hi]); _newConsts.Add(c); _def[id] = c; _scalarPool[k] = id; return id; }

    // FoldFPBinaryOp(FOLD_FPCMP_OP): FP comparison -> bool constant (CompareFloatingPoint), f32/f64, scalar or
    // component-wise vector. ordered=true for Ord.
    uint FpCompare(SpvInstr inst, List<uint> ops, Func<double, double, bool> cmp, bool ordered)
    {
        if (!FpAllowed(inst)) return 0;
        if (!IsConst(ops[0]) || !IsConst(ops[1])) return 0;
        uint bt = inst.Operands[0];
        var td = Def(bt)!;
        if (td.Opcode == OpTypeVector)
        {
            int n = (int)td.Operands[2];
            var ca = Comps(ops[0], n); var cb = Comps(ops[1], n); if (ca is null || cb is null) return 0;
            var res = new uint[n];
            for (int i = 0; i < n; i++) { uint r = FpCompareScalar(td.Operands[1], ca[i], cb[i], cmp, ordered); if (r == 0) return 0; res[i] = r; }
            return CompositeConst(bt, res);
        }
        return FpCompareScalar(bt, ops[0], ops[1], cmp, ordered);
    }
    uint FpCompareScalar(uint bt, uint a, uint b, Func<double, double, bool> cmp, bool ordered)
    {
        double fa, fb;
        int width = TypeWidth(TypeOf(a));
        if (width == 32) { if (!ScalarWord(a, out uint aw) || !ScalarWord(b, out uint bw)) return 0; fa = F32(aw); fb = F32(bw); }
        else if (width == 64) { var da = Words64(a); var db = Words64(b); if (da is null || db is null) return 0; fa = F64(da[0], da[1]); fb = F64(db[0], db[1]); }
        else return 0;
        bool un = double.IsNaN(fa) || double.IsNaN(fb);
        bool res = ordered ? (!un && cmp(fa, fb)) : (un || cmp(fa, fb));
        return BoolConst(bt, res);
    }

    // FoldFClampFeedingCompare: comparing FClamp(x, min, max) against a constant outside (or on the edge of) the
    // clamp range has a known result.
    uint FClampFeedingCompare(SpvInstr inst, List<uint> ops)
    {
        if (!FpAllowed(inst)) return 0;
        int op = inst.Opcode;
        bool c0 = IsConst(ops[0]), c1 = IsConst(ops[1]);
        var operand = Def(c0 ? ops[1] : ops[0]);
        if (operand is null) return 0;
        uint ot = TypeOf(operand.ResultId);
        if (Def(ot) is not { Opcode: OpTypeFloat }) return 0;
        int w = TypeWidth(ot); if (w != 32 && w != 64) return 0;
        if (operand.Opcode != OpExtInst || operand.Operands.Length < 7 || operand.Operands[3] != 43) return 0;   // GLSLstd450FClamp
        if (!c0 && !c1) return 0;
        uint minId = operand.Operands[5], maxId = operand.Operands[6];
        bool hasMin = IsConst(minId), hasMax = IsConst(maxId);
        double V(uint id) => FloatOf(id, TypeWidth(TypeOf(id)));
        bool found = false, result = false;
        bool lessKind = op is 184 or 185 or 188 or 189;   // FOrd/FUnord LessThan(Equal)
        if (op is 184 or 185 or 190 or 191)   // <, >=
        {
            if (c0)
            {
                if (hasMin && V(ops[0]) < V(minId)) { found = true; result = lessKind; }
                if (hasMax && V(ops[0]) >= V(maxId)) { found = true; result = !lessKind; }
            }
            if (c1)
            {
                if (hasMax && V(maxId) < V(ops[1])) { found = true; result = lessKind; }
                if (hasMin && V(minId) >= V(ops[1])) { found = true; result = !lessKind; }
            }
        }
        else if (op is 186 or 187 or 188 or 189)   // >, <=
        {
            if (c0)
            {
                if (hasMin && V(ops[0]) <= V(minId)) { found = true; result = lessKind; }
                if (hasMax && V(ops[0]) > V(maxId)) { found = true; result = !lessKind; }
            }
            if (c1)
            {
                if (hasMax && V(maxId) <= V(ops[1])) { found = true; result = lessKind; }
                if (hasMin && V(minId) > V(ops[1])) { found = true; result = !lessKind; }
            }
        }
        return found ? BoolConst(inst.Operands[0], result) : 0;
    }

    // FoldUnaryOp(FoldScalarSConvert / FoldScalarUConvert): integer width conversion of a constant.
    uint IntConvert(SpvInstr inst, List<uint> ops, bool signed)
    {
        if (!IsConst(ops[0])) return 0;
        uint type = inst.Operands[0]; var td = Def(type)!;
        if (td.Opcode == OpTypeVector)
        {
            int n = (int)td.Operands[2]; var c = Comps(ops[0], n); if (c is null) return 0;
            var res = new uint[n];
            for (int i = 0; i < n; i++) { uint r = IntConvertScalar(td.Operands[1], c[i], signed); if (r == 0) return 0; res[i] = r; }
            return CompositeConst(type, res);
        }
        return IntConvertScalar(type, ops[0], signed);
    }
    uint IntConvertScalar(uint type, uint id, bool signed)
    {
        var d = Def(id); int srcWidth = TypeWidth(TypeOf(id));
        ulong v;
        if (d is { Opcode: OpConstantNull }) v = 0;
        else if (d is not { Opcode: OpConstant }) return 0;
        else if (srcWidth <= 32) v = signed ? (ulong)(long)(int)d.Operands[2] : d.Operands[2];
        else v = d.Operands[2] | ((ulong)d.Operands[3] << 32);
        if (!signed && srcWidth < 64) v &= (1UL << srcWidth) - 1;
        return GenerateIntegerConstant(type, v);
    }
    // ConstantManager::GenerateIntegerConstant: 64-bit -> two words; narrower -> sign/zero-extended single word.
    uint GenerateIntegerConstant(uint type, ulong v)
    {
        var td = Def(type);
        if (td is not { Opcode: OpTypeInt }) return 0;
        int width = (int)td.Operands[1];
        if (width == 64) return Scalar64(type, (uint)v, (uint)(v >> 32));
        if (width < 64)
        {
            if (td.Operands[2] == 1 && (v >> (width - 1) & 1) != 0) v |= ~0UL << width;
            else v &= (1UL << width) - 1;
        }
        return ScalarConst(type, (uint)v);
    }

    // FoldFNegate / FoldSNegate.
    uint FNegate(SpvInstr inst, List<uint> ops)
    {
        if (!FpAllowed(inst)) return 0;
        uint type = inst.Operands[0]; var td = Def(type)!;
        if (td.Opcode == OpTypeVector) { uint elem = td.Operands[1]; int n = (int)td.Operands[2]; var c = Comps(ops[0], n); if (c is null) return 0; var res = new uint[n]; for (int i = 0; i < n; i++) { uint r = FNegateScalar(elem, c[i]); if (r == 0) return 0; res[i] = r; } return CompositeConst(type, res); }
        return FNegateScalar(type, ops[0]);
    }
    uint FNegateScalar(uint type, uint id) { int w = TypeWidth(type); if (w == 32) { if (!ScalarWord(id, out uint x)) return 0; return ScalarConst(type, x ^ 0x80000000u); } if (w == 64) { var d = Words64(id); if (d is null) return 0; return Scalar64(type, d[0], d[1] ^ 0x80000000u); } return 0; }
    // FoldUnaryOp(NegateIntConst): any width, the operand read sign-extended; vectors component-wise.
    uint SNegate(SpvInstr inst, List<uint> ops)
    {
        uint type = inst.Operands[0]; var td = Def(type)!;
        if (td.Opcode == OpTypeVector)
        {
            int n = (int)td.Operands[2]; var c = Comps(ops[0], n); if (c is null) return 0;
            var res = new uint[n];
            for (int i = 0; i < n; i++) { uint r = SNegateScalar(td.Operands[1], c[i]); if (r == 0) return 0; res[i] = r; }
            return CompositeConst(type, res);
        }
        return SNegateScalar(type, ops[0]);
    }
    uint SNegateScalar(uint type, uint id)
    {
        var d = Def(id);
        if (d is { Opcode: OpConstantNull }) return id;
        if (d is not { Opcode: OpConstant }) return 0;
        long v = TypeWidth(TypeOf(id)) <= 32 ? (int)d.Operands[2] : (long)(d.Operands[2] | ((ulong)d.Operands[3] << 32));
        return GenerateIntegerConstant(type, unchecked((ulong)-v));
    }

    // FoldIToF / FoldFToI (32-bit int only). FP-folding-guarded.
    // FoldFPUnaryOp(FoldIToFOp): 32-bit integer -> float, signedness from the operand's integer type; scalar or
    // component-wise vector.
    uint IToF(SpvInstr inst, List<uint> ops)
    {
        if (!FpAllowed(inst) || !IsConst(ops[0])) return 0;
        return FoldFpUnaryPer(inst, ops[0], (elem, id) =>
        {
            var it = Def(TypeOf(id));
            if (it is not { Opcode: OpTypeInt } || it.Operands[1] != 32) return 0u;
            if (!ScalarWord(id, out uint x)) return 0u;
            double val = it.Operands[2] == 1 ? (int)x : x;
            int w = TypeWidth(elem);
            if (w == 32) return ScalarConst(elem, W32((float)val));
            if (w == 64) { ulong bits = BitConverter.DoubleToUInt64Bits(val); return Scalar64(elem, (uint)bits, (uint)(bits >> 32)); }
            return 0u;
        });
    }
    // FoldFPUnaryOp(FoldFToIOp): float -> 32-bit integer, signedness from the result's integer type.
    uint FToI(SpvInstr inst, List<uint> ops)
    {
        if (!FpAllowed(inst) || !IsConst(ops[0])) return 0;
        return FoldFpUnaryPer(inst, ops[0], (elem, id) =>
        {
            var it = Def(elem);
            if (it is not { Opcode: OpTypeInt } || it.Operands[1] != 32) return 0u;
            int sw = TypeWidth(TypeOf(id));
            double val;
            if (sw == 32) { if (!ScalarWord(id, out uint x)) return 0u; val = F32(x); }
            else if (sw == 64) { var d = Words64(id); if (d is null) return 0u; val = F64(d[0], d[1]); }
            else return 0u;
            uint r = it.Operands[2] == 1 ? unchecked((uint)(int)val) : (uint)val;
            return ScalarConst(elem, r);
        });
    }

    // FoldOpDotWithConstants (148): a constant all-zero (or null) operand makes the result 0 whatever the other
    // operand is; otherwise both must be constant and the dot product accumulates in the result's own width,
    // one rounded multiply and add per component.
    uint FoldDot(SpvInstr inst, List<uint> ops)
    {
        if (!FpAllowed(inst)) return 0;
        uint type = inst.Operands[0]; int w = TypeWidth(type);
        bool ZeroVector(uint id)
        {
            var d = Def(id);
            if (d is { Opcode: OpConstantNull }) return true;
            if (d is not { Opcode: OpConstantComposite }) return false;
            foreach (uint c in d.Operands[2..])
            {
                var cd = Def(c);
                if (cd is { Opcode: OpConstantNull }) continue;
                if (cd is not { Opcode: OpConstant } || cd.Operands[2..].Any(x => x != 0)) return false;
            }
            return true;
        }
        for (int i = 0; i < 2; i++)
            if (IsConst(ops[i]) && ZeroVector(ops[i]))
                return w == 32 ? ScalarConst(type, 0) : w == 64 ? Scalar64(type, 0, 0) : 0;
        if (!IsConst(ops[0]) || !IsConst(ops[1])) return 0;
        var vt = Def(TypeOf(ops[0]));
        if (vt is not { Opcode: 23 }) return 0;   // OpTypeVector
        int n = (int)vt.Operands[2];
        var a = Comps(ops[0], n); var b = Comps(ops[1], n);
        if (a is null || b is null) return 0;
        if (w == 32)
        {
            float r = 0f;
            for (int i = 0; i < n; i++)
            {
                if (!ScalarWord(a[i], out uint x) || !ScalarWord(b[i], out uint y)) return 0;
                float p = F32(x) * F32(y);
                r += p;
            }
            return ScalarConst(type, W32(r));
        }
        if (w == 64)
        {
            double r = 0.0;
            for (int i = 0; i < n; i++)
            {
                var x = Words64(a[i]); var y = Words64(b[i]);
                if (x is null || y is null) return 0;
                double p = F64(x[0], x[1]) * F64(y[0], y[1]);
                r += p;
            }
            ulong bits = BitConverter.DoubleToUInt64Bits(r);
            return Scalar64(type, (uint)bits, (uint)(bits >> 32));
        }
        return 0;
    }

    // FoldVectorTimesMatrix (144) / FoldMatrixTimesVector (145): constant float products, accumulated per
    // result component in the element width. A zero operand gives the zero vector.
    uint FoldVectorMatrix(SpvInstr inst, List<uint> ops, bool matrixFirst)
    {
        uint type = inst.Operands[0];
        if (!FpAllowed(inst)) return 0;
        if (!IsConst(ops[0]) || !IsConst(ops[1])) return 0;
        var vt = Def(type)!; uint elem = vt.Operands[1]; int n = (int)vt.Operands[2]; int w = TypeWidth(elem);
        if (w != 32 && w != 64) return 0;
        uint Elem(double v) { if (w == 32) return ScalarConst(elem, W32((float)v)); ulong b = BitConverter.DoubleToUInt64Bits(v); return Scalar64(elem, (uint)b, (uint)(b >> 32)); }
        if (IsZeroConst(ops[0]) || IsZeroConst(ops[1]))
        {
            var zero = new uint[n];
            for (int i = 0; i < n; i++) zero[i] = w == 32 ? ScalarConst(elem, 0) : Scalar64(elem, 0, 0);
            return CompositeConst(type, zero);
        }
        uint mat = matrixFirst ? ops[0] : ops[1], vec = matrixFirst ? ops[1] : ops[0];
        var md = Def(mat);
        if (md is not { Opcode: OpConstantComposite }) return 0;
        uint[] cols = md.Operands[2..];
        var colType = Def(TypeOf(mat))!; uint colVecType = colType.Operands[1]; int rows = (int)Def(colVecType)!.Operands[2];
        var v = Comps(vec, matrixFirst ? cols.Length : rows);
        if (v is null) return 0;
        double Scalar(uint id) { if (w == 32) { ScalarWord(id, out uint x); return F32(x); } var d = Words64(id)!; return F64(d[0], d[1]); }
        uint[]? Col(uint c) => Def(c) is { Opcode: OpConstantNull } ? null : Comps(c, rows);
        var res = new uint[n];
        for (int i = 0; i < n; i++)
        {
            if (w == 32)
            {
                float r = 0f;
                if (matrixFirst) { for (int j = 0; j < cols.Length; j++) { var c = Col(cols[j]); if (c is null) continue; float p = (float)Scalar(c[i]) * (float)Scalar(v[j]); r += p; } }
                else { var c = Col(cols[i]); if (c is not null) for (int j = 0; j < c.Length; j++) { float p = (float)Scalar(v[j]) * (float)Scalar(c[j]); r += p; } }
                res[i] = Elem(r);
            }
            else
            {
                double r = 0.0;
                if (matrixFirst) { for (int j = 0; j < cols.Length; j++) { var c = Col(cols[j]); if (c is null) continue; r += Scalar(c[i]) * Scalar(v[j]); } }
                else { var c = Col(cols[i]); if (c is not null) for (int j = 0; j < c.Length; j++) r += Scalar(v[j]) * Scalar(c[j]); }
                res[i] = Elem(r);
            }
        }
        return CompositeConst(type, res);
    }

    // FoldVectorTimesScalar (142): vec * scalar. zero handling per source.
    uint VectorTimesScalar(SpvInstr inst, List<uint> ops)
    {
        if (!FpAllowed(inst)) return 0;
        uint type = inst.Operands[0]; var td = Def(type)!; uint elem = td.Operands[1]; int n = (int)td.Operands[2];
        uint vec = ops[0], scl = ops[1];
        if (IsZeroVec(vec)) return vec;
        if (IsZeroScalar(scl)) return NullConst(type);
        var c = Comps(vec, n); if (c is null) return 0;
        int width = TypeWidth(elem);
        var res = new uint[n];
        for (int i = 0; i < n; i++)
        {
            if (width == 32) { if (!ScalarWord(c[i], out uint cw) || !ScalarWord(scl, out uint sw)) return 0; res[i] = ScalarConst(elem, W32(F32(cw) * F32(sw))); }
            else if (width == 64) { var dc = Words64(c[i]); var ds = Words64(scl); if (dc is null || ds is null) return 0; ulong bits = BitConverter.DoubleToUInt64Bits(F64(dc[0], dc[1]) * F64(ds[0], ds[1])); res[i] = Scalar64(elem, (uint)bits, (uint)(bits >> 32)); }
            else return 0;
        }
        return CompositeConst(type, res);
    }
    bool IsZeroVec(uint id) { var d = Def(id); if (d is { Opcode: OpConstantNull }) return true; if (d is { Opcode: OpConstantComposite }) return d.Operands[2..].All(IsZeroScalar); return false; }

    // FoldCompositeWithConstants: an OpCompositeConstruct whose inputs are all constants folds to an
    // OpConstantComposite. The operand ids are already the declared component constants, so they become the
    // composite's constituents directly.
    uint FoldCompositeWithConstants(SpvInstr inst, List<uint> ops)
    {
        foreach (uint id in ops) if (!IsConst(id)) return 0;
        return CompositeConst(inst.Operands[0], ops.ToArray());
    }

    // FoldExtractWithConstants: OpCompositeExtract from a constant composite -> the indexed constant element.
    uint FoldExtractWithConstants(SpvInstr inst, List<uint> ops)
    {
        uint cId = ops[0];
        if (!IsConst(cId)) return 0;
        var c = Def(cId);
        for (int i = 1; i < NumIn(inst); i++)
        {
            uint elementIndex = InOp(inst, i);
            if (c is { Opcode: OpConstantNull }) return NullConst(inst.Operands[0]);
            if (c is not { Opcode: OpConstantComposite }) return 0;
            var components = c.Operands[2..];
            if (elementIndex >= components.Length) return 0;
            cId = components[(int)elementIndex];
            c = Def(cId);
        }
        return cId;
    }

    // FoldInsertWithConstants: OpCompositeInsert of a constant object into a constant composite -> a new
    // constant composite. Walks down the index hierarchy rebuilding the deepest level with the object, then
    // back up rebuilding each parent, materializing a constant per level (const_folding_rules
    // FoldInsertWithConstants) -- matching spirv-opt's per-level id allocation exactly.
    uint FoldInsertWithConstants(SpvInstr inst, List<uint> ops)
    {
        uint objectId = ops[0], compositeId = ops[1];
        if (!IsConst(objectId) || !IsConst(compositeId)) return 0;
        int nIdx = inst.Operands.Length - 4;
        if (nIdx < 1) return 0;

        var chain = new List<(uint Type, uint[] Comps)>();
        uint curId = compositeId, curType = 0; uint[]? comps = null;
        for (int i = 0; i < nIdx; i++)
        {
            curType = TypeOf(curId);
            comps = InsertComponentIds(curId, curType);
            if (comps is null) return 0;
            uint index = inst.Operands[4 + i];
            if (index >= comps.Length) return 0;
            if (i != nIdx - 1) chain.Add((curType, comps));
            curId = comps[(int)index];
        }
        uint finalIndex = inst.Operands[^1];
        var ids = new uint[comps!.Length];
        for (int k = 0; k < comps.Length; k++) ids[k] = k == finalIndex ? objectId : comps[k];
        // Every level below the top goes through BuildInstructionAndAddToModule, which always declares a new
        // constant -- a duplicate when an equal one exists, in which case the parent still refers to the first.
        uint newConst = chain.Count > 0 ? CompositeConstForceNew(curType, ids) : CompositeConst(curType, ids);
        for (int i = chain.Count - 1; i >= 0; i--)
        {
            var (ptype, pcomps) = chain[i];
            uint idx = inst.Operands[4 + i];
            var pids = new uint[pcomps.Length];
            for (int k = 0; k < pcomps.Length; k++) pids[k] = k == idx ? newConst : pcomps[k];
            newConst = i > 0 ? CompositeConstForceNew(ptype, pids) : CompositeConst(ptype, pids);
        }
        return newConst;
    }
    uint CompositeConstForceNew(uint type, uint[] comps)
    {
        comps = comps.Select(Materialize).ToArray();
        string k = CompKey(type, comps);
        uint id = _nextId++; var c = new SpvInstr(OpConstantComposite, [type, id, .. comps]); _newConsts.Add(c); _def[id] = c;
        return _compositePool.TryAdd(k, id) ? id : _compositePool[k];
    }
    uint[]? InsertComponentIds(uint id, uint typeId)
    {
        var d = Def(id);
        if (d is { Opcode: OpConstantComposite }) return d.Operands[2..];
        if (d is { Opcode: OpConstantNull } && Def(typeId) is { Opcode: OpTypeVector } t)
        {
            int n = (int)t.Operands[2]; uint e = t.Operands[1]; var r = new uint[n];
            for (int k = 0; k < n; k++) r[k] = VirtualNull(e);
            return r;
        }
        return null;
    }

    // FoldVectorShuffleWithConstants: shuffle of two constant vectors -> a constant vector.
    uint FoldVectorShuffleWithConstants(SpvInstr inst, List<uint> ops)
    {
        uint c1 = ops[0], c2 = ops[1];
        if (!IsConst(c1) || !IsConst(c2)) return 0;
        var c1c = Comps(c1, (int)Def(TypeOf(c1))!.Operands[2]);
        var c2c = Comps(c2, (int)Def(TypeOf(c2))!.Operands[2]);
        if (c1c is null || c2c is null) return 0;
        var ids = new List<uint>();
        for (int i = 2; i < NumIn(inst); i++)
        {
            uint index = InOp(inst, i);
            if (index == 0xffffffff) return 0;
            ids.Add(index < c1c.Length ? c1c[(int)index] : c2c[(int)index - c1c.Length]);
        }
        return CompositeConst(inst.Operands[0], ids.ToArray());
    }

    // FoldTranspose: OpTranspose of a constant matrix -> the transposed constant matrix. result[row][col] =
    // input[col][row] (const_folding_rules FoldTranspose/TransposeMatrix).
    uint FoldTranspose(SpvInstr inst, List<uint> ops)
    {
        uint mId = ops[0];
        if (!IsConst(mId) || !FpAllowed(inst)) return 0;
        uint resultType = inst.Operands[0];
        if (Def(mId) is { Opcode: OpConstantNull }) return NullConst(resultType);
        if (Def(mId) is not { Opcode: OpConstantComposite } mat) return 0;

        uint[] columns = mat.Operands[2..];
        if (columns.Length == 0 || Def(TypeOf(columns[0])) is not { Opcode: OpTypeVector } col0) return 0;
        int rows = (int)col0.Operands[2];

        var rowElems = new List<uint>[rows];
        for (int r = 0; r < rows; r++) rowElems[r] = [];
        foreach (uint colId in columns)
        {
            var comps = Comps(colId, rows);
            if (comps is null) return 0;
            for (int r = 0; r < rows; r++) rowElems[r].Add(comps[r]);
        }

        uint resultColVec = Def(resultType)!.Operands[1];
        var resultColumns = new uint[rows];
        for (int c = 0; c < rows; c++) resultColumns[c] = CompositeConst(resultColVec, rowElems[c].ToArray());
        return CompositeConst(resultType, resultColumns);
    }

    // ---- GLSL.std.450 const folds (ext_rules) ----
    // For an OpExtInst, the collected ops are [resultType, set, 0(instr), arg0, arg1, ...], so arg i = ops[3+i].
    static uint ExtArg(List<uint> ops, int i) => ops[3 + i];

    uint IntWord(uint id) { ScalarWord(id, out uint w); return w; }
    ulong Int64Of(uint id) { var d = Words64(id); return d is null ? 0UL : d[0] | ((ulong)d[1] << 32); }
    double FloatOf(uint id, int w) { if (w == 16) { ScalarWord(id, out uint x); return F16(x); } if (w == 32) { ScalarWord(id, out uint x); return F32(x); } var d = Words64(id); return d is null ? 0 : F64(d[0], d[1]); }
    static bool NegZeroLess(double a, double b) { if (a == 0.0 && b == 0.0 && double.IsNegative(a) && !double.IsNegative(b)) return true; return a < b; }

    // FoldMin/FoldMax scalar rule: returns whichever operand id is smaller/larger (no new constant for scalars).
    uint MinScalar(uint elem, uint aId, uint bId, bool max)
    {
        var d = Def(elem)!;
        if (d.Opcode == OpTypeInt)
        {
            int w = TypeWidth(elem); bool sg = IntSigned(elem);
            bool aLess;
            if (w <= 32) { uint va = IntWord(aId), vb = IntWord(bId); aLess = sg ? (int)va < (int)vb : va < vb; }
            else { ulong la = Int64Of(aId), lb = Int64Of(bId); aLess = sg ? (long)la < (long)lb : la < lb; }
            return (max ? !aLess && aId != bId : aLess) ? aId : bId;
        }
        if (d.Opcode == OpTypeFloat)
        {
            int w = TypeWidth(elem);
            // spirv-opt does not fold 16-bit min/max/clamp (leaves the ExtInst); folding here
            // would diverge from the reference and, with the old FloatOf, miscompiled.
            if (w != 32 && w != 64) return 0;
            double va = FloatOf(aId, w), vb = FloatOf(bId, w);
            return (max ? NegZeroLess(vb, va) : NegZeroLess(va, vb)) ? aId : bId;
        }
        return 0;
    }
    uint FoldExtBinaryPer(SpvInstr inst, uint aId, uint bId, Func<uint, uint, uint, uint> scalar)
    {
        var td = Def(inst.Operands[0])!;
        if (td.Opcode == OpTypeVector)
        {
            int n = (int)td.Operands[2]; uint elem = td.Operands[1];
            var ac = Comps(aId, n); var bc = Comps(bId, n); if (ac is null || bc is null) return 0;
            var ids = new uint[n]; for (int i = 0; i < n; i++) { ids[i] = scalar(elem, ac[i], bc[i]); if (ids[i] == 0) return 0; }
            return CompositeConst(inst.Operands[0], ids);
        }
        return scalar(inst.Operands[0], aId, bId);
    }
    uint FoldMinMax(SpvInstr inst, List<uint> ops, bool max)
    {
        if (!FpAllowed(inst)) return 0;
        uint a = ExtArg(ops, 0), b = ExtArg(ops, 1);
        if (!IsConst(a) || !IsConst(b)) return 0;
        return FoldExtBinaryPer(inst, a, b, (e, x, y) => MinScalar(e, x, y, max));
    }
    // The clamps fold through FoldFPBinaryOp(FoldMax/FoldMin), whose vector intermediate is an unmaterialized
    // constant: only the final result becomes an instruction, so the intermediate stays component-wise here.
    uint[]? Parts(SpvInstr inst, uint id)
    {
        var td = Def(inst.Operands[0])!;
        return td.Opcode == OpTypeVector ? Comps(id, (int)td.Operands[2]) : [id];
    }
    uint[]? MinMaxParts(SpvInstr inst, uint[]? a, uint[]? b, bool max)
    {
        if (a is null || b is null) return null;
        var td = Def(inst.Operands[0])!;
        uint elem = td.Opcode == OpTypeVector ? td.Operands[1] : inst.Operands[0];
        var r = new uint[a.Length];
        for (int i = 0; i < a.Length; i++) { r[i] = MinScalar(elem, a[i], b[i], max); if (r[i] == 0) return null; }
        return r;
    }
    uint FromParts(SpvInstr inst, uint[] parts) =>
        Def(inst.Operands[0])!.Opcode == OpTypeVector ? CompositeConst(inst.Operands[0], parts) : parts[0];
    // Whether a folded intermediate is the same constant as an operand (the constant manager's pointer identity):
    // a null vector constant is never equal to a composite built from components.
    bool SameConstant(SpvInstr inst, uint[] parts, uint id) =>
        Def(inst.Operands[0])!.Opcode == OpTypeVector
            ? Def(id) is { Opcode: OpConstantComposite } d && d.Operands[2..].AsSpan().SequenceEqual(parts)
            : parts[0] == id;
    uint FoldClamp1(SpvInstr inst, List<uint> ops)
    {
        if (!FpAllowed(inst)) return 0;
        uint x = ExtArg(ops, 0), mn = ExtArg(ops, 1), mx = ExtArg(ops, 2);
        if (!IsConst(x) || !IsConst(mn) || !IsConst(mx)) return 0;
        var temp = MinMaxParts(inst, Parts(inst, x), Parts(inst, mn), max: true);
        var result = MinMaxParts(inst, temp, Parts(inst, mx), max: false);
        return result is null ? 0 : FromParts(inst, result);
    }
    uint FoldClamp2(SpvInstr inst, List<uint> ops)   // x <= min_val -> min_val
    {
        uint x = ExtArg(ops, 0), mn = ExtArg(ops, 1);
        if (!IsConst(x) || !IsConst(mn)) return 0;
        var temp = MinMaxParts(inst, Parts(inst, x), Parts(inst, mn), max: true);
        return temp is not null && SameConstant(inst, temp, mn) ? mn : 0;
    }
    uint FoldClamp3(SpvInstr inst, List<uint> ops)   // x >= max_val -> max_val
    {
        uint x = ExtArg(ops, 0), mx = ExtArg(ops, 2);
        if (!IsConst(x) || !IsConst(mx)) return 0;
        var temp = MinMaxParts(inst, Parts(inst, x), Parts(inst, mx), max: false);
        return temp is not null && SameConstant(inst, temp, mx) ? mx : 0;
    }
    uint FoldFpUnaryPer(SpvInstr inst, uint aId, Func<uint, uint, uint> scalar)
    {
        var td = Def(inst.Operands[0])!;
        if (td.Opcode == OpTypeVector)
        {
            int n = (int)td.Operands[2]; uint elem = td.Operands[1];
            var ac = Comps(aId, n); if (ac is null) return 0;
            var ids = new uint[n]; for (int i = 0; i < n; i++) { ids[i] = scalar(elem, ac[i]); if (ids[i] == 0) return 0; }
            return CompositeConst(inst.Operands[0], ids);
        }
        return scalar(inst.Operands[0], aId);
    }
    uint FoldTranscendental(SpvInstr inst, List<uint> ops, Func<double, double> fp)
    {
        if (!FpAllowed(inst)) return 0;
        uint a = ExtArg(ops, 0);
        if (!IsConst(a)) return 0;
        return FoldFpUnaryPer(inst, a, (elem, id) =>
        {
            int w = TypeWidth(elem);
            if (w == 32) { if (!ScalarWord(id, out uint x)) return 0u; return ScalarConst(elem, W32((float)fp(F32(x)))); }
            if (w == 64) { var d = Words64(id); if (d is null) return 0u; ulong bits = BitConverter.DoubleToUInt64Bits(fp(F64(d[0], d[1]))); return Scalar64(elem, (uint)bits, (uint)(bits >> 32)); }
            return 0u;
        });
    }

    // FoldFMix: x * (1 - a) + y * a on constants, each step a rounded FOLD_FPARITH_OP. Scalar intermediates are
    // never declared; for vectors the scalar 1.0 and every per-component intermediate is (FoldFPBinaryOp builds
    // each vector result from declared component constants).
    uint FoldFMix(SpvInstr inst, List<uint> ops)
    {
        if (!FpAllowed(inst)) return 0;
        uint x = ExtArg(ops, 0), y = ExtArg(ops, 1), a = ExtArg(ops, 2);
        if (!IsConst(x) || !IsConst(y) || !IsConst(a)) return 0;
        uint type = TypeOf(x);
        var td = Def(type)!;
        uint elem = td.Opcode == OpTypeVector ? td.Operands[1] : type;
        int w = TypeWidth(elem);
        if (w != 32 && w != 64) return 0;
        if (td.Opcode != OpTypeVector)
        {
            if (w == 32)
            {
                float fx = (float)FloatOf(x, 32), fy = (float)FloatOf(y, 32), fa = (float)FloatOf(a, 32);
                float t1 = 1.0f - fa, t2 = fx * t1, t3 = fy * fa, r = t2 + t3;
                return ScalarConst(inst.Operands[0], W32(r));
            }
            double dx = FloatOf(x, 64), dy = FloatOf(y, 64), da = FloatOf(a, 64);
            double d = dx * (1.0 - da) + dy * da;
            ulong bits = BitConverter.DoubleToUInt64Bits(d);
            return Scalar64(inst.Operands[0], (uint)bits, (uint)(bits >> 32));
        }
        int n = (int)td.Operands[2];
        uint one = w == 32 ? ScalarConst(elem, W32(1.0f)) : Scalar64(elem, 0, 0x3FF00000u);
        var oneV = Enumerable.Repeat(one, n).ToArray();
        var xc = Comps(x, n); var yc = Comps(y, n); var ac = Comps(a, n);
        if (xc is null || yc is null || ac is null) return 0;
        uint[]? Step(uint[] p, uint[] q, Func<uint, uint, float> f32, Func<double, double, double> f64)
        {
            var r = new uint[n];
            for (int i = 0; i < n; i++) { r[i] = FpBinaryTyped(elem, p[i], q[i], f32, f64); if (r[i] == 0) return null; }
            return r;
        }
        var t1v = Step(oneV, ac, (p, q) => F32(p) - F32(q), (p, q) => p - q); if (t1v is null) return 0;
        var t2v = Step(xc, t1v, (p, q) => F32(p) * F32(q), (p, q) => p * q); if (t2v is null) return 0;
        var t3v = Step(yc, ac, (p, q) => F32(p) * F32(q), (p, q) => p * q); if (t3v is null) return 0;
        var rv = Step(t2v, t3v, (p, q) => F32(p) + F32(q), (p, q) => p + q); if (rv is null) return 0;
        return CompositeConst(inst.Operands[0], rv);
    }

    // FoldFPBinaryOp(FoldFTranscendentalBinary(fp)): atan2/pow on constants, f32 computed via double.
    uint FoldTranscendental2(SpvInstr inst, List<uint> ops, Func<double, double, double> fp)
    {
        if (!FpAllowed(inst)) return 0;
        uint a = ExtArg(ops, 0), b = ExtArg(ops, 1);
        if (!IsConst(a) || !IsConst(b)) return 0;
        return FoldExtBinaryPer(inst, a, b, (elem, x, y) =>
        {
            int w = TypeWidth(elem);
            if (w == 32) { if (!ScalarWord(x, out uint xa) || !ScalarWord(y, out uint ya)) return 0u; return ScalarConst(elem, W32((float)fp(F32(xa), F32(ya)))); }
            if (w == 64) { var da = Words64(x); var db = Words64(y); if (da is null || db is null) return 0u; ulong bits = BitConverter.DoubleToUInt64Bits(fp(F64(da[0], da[1]), F64(db[0], db[1]))); return Scalar64(elem, (uint)bits, (uint)(bits >> 32)); }
            return 0u;
        });
    }

    // A scalar/vector constant whose every component is `dv` (float) or `iv` (int).
    uint UniformConst(uint type, double dv, ulong iv)
    {
        var td = Def(type)!;
        if (td.Opcode == OpTypeVector) { uint elem = td.Operands[1]; int n = (int)td.Operands[2]; uint s = UniformConst(elem, dv, iv); if (s == 0) return 0; return CompositeConst(type, Enumerable.Repeat(s, n).ToArray()); }
        int w = TypeWidth(type);
        if (IsFloatType(type)) { if (w == 32) return ScalarConst(type, W32((float)dv)); if (w == 64) { ulong b = BitConverter.DoubleToUInt64Bits(dv); return Scalar64(type, (uint)b, (uint)(b >> 32)); } return 0; }
        if (IsIntType(type)) return w <= 32 ? ScalarConst(type, (uint)iv) : Scalar64(type, (uint)iv, (uint)(iv >> 32));
        return 0;
    }
    // FoldRedundantSub: x - x -> 0.
    uint FoldRedundantSub(SpvInstr inst, List<uint> ops)
    {
        if (ops[0] != ops[1]) return 0;
        if (inst.Opcode == OpFSub && !FpAllowed(inst)) return 0;
        return NullConst(inst.Operands[0]);
    }
    // FoldRedundantDiv: x/x -> 1, (-x)/x or x/(-x) -> -1 (SDiv/FDiv only).
    uint FoldRedundantDiv(SpvInstr inst, List<uint> ops)
    {
        if (IsConst(ops[0]) || IsConst(ops[1])) return 0;
        bool uf = inst.Opcode == OpFDiv;
        if (uf && !FpAllowed(inst)) return 0;
        uint type = inst.Operands[0];
        if (ops[0] == ops[1]) return UniformConst(type, 1.0, 1);
        if (inst.Opcode == OpUDiv) return 0;
        var lhs = Def(ops[0]);
        if (lhs is { Opcode: 126 or 127 } && InOp(lhs, 0) == ops[1] && (!uf || FpAllowed(lhs))) return UniformConst(type, -1.0, ulong.MaxValue);
        var rhs = Def(ops[1]);
        if (rhs is { Opcode: 126 or 127 } && InOp(rhs, 0) == ops[0] && (!uf || FpAllowed(rhs))) return UniformConst(type, -1.0, ulong.MaxValue);
        return 0;
    }

    // FoldInvariantSelect: both values equal or both zero -> value 1.
    uint InvariantSelect(SpvInstr inst, List<uint> ops)
    {
        if (ops.Count < 3) return 0;
        if (IsConst(ops[1]) && IsConst(ops[2]) && (ops[1] == ops[2] || (IsZeroScalar(ops[1]) && IsZeroScalar(ops[2])))) return ops[1];
        return 0;
    }

    // ================= folding_rules.cpp : in-place simplification rules =================
    // Faithful ports. Each mutates `inst` and returns true if it changed it. In-place rules reuse existing
    // ids (no id allocation), so their output is byte-identical whether spirv-opt or we apply them.

    // In-operand indexing (GetSingleWordInOperand / NumInOperands / SetInOperands), matching spirv-opt: the
    // operand words after the result-type (if any) and result-id (if any).
    int InStart(SpvInstr inst) { inst.Classify(out int ri, out _); return ri >= 0 ? ri + 1 : 0; }
    uint InOp(SpvInstr inst, int i) => inst.Operands[InStart(inst) + i];
    int NumIn(SpvInstr inst) => inst.Operands.Length - InStart(inst);
    void SetIn(SpvInstr inst, params uint[] words) { int s = InStart(inst); inst.Operands = [.. inst.Operands[..s], .. words]; }
    void SetOp(SpvInstr inst, int op, params uint[] words) { int s = InStart(inst); var head = inst.Operands[..s]; inst.Opcode = op; inst.Operands = [.. head, .. words]; }

    // The def of `id` if it is a constant (mirrors spirv-opt's constants[i]); 0 if not.
    uint ConstOrZero(uint id) => IsConst(id) ? id : 0;
    // ConstInput(constants): constants[0] if it is const, else constants[1] (may be 0). NonConstInput: operand[c?1:0].
    uint ConstInputId(SpvInstr inst) { uint a = InOp(inst, 0); if (IsConst(a)) return a; uint b = InOp(inst, 1); return IsConst(b) ? b : 0; }
    uint NonConstInputId(SpvInstr inst) => IsConst(InOp(inst, 0)) ? InOp(inst, 1) : InOp(inst, 0);

    enum FKind { Unknown, Zero, One }
    FKind FloatConstKind(uint id)
    {
        var d = Def(id);
        if (d is null) return FKind.Unknown;
        if (d.Opcode == OpConstantNull) return FKind.Zero;
        uint type = d.Operands[0];
        if (Def(type) is { Opcode: OpTypeVector })
        {
            if (d.Opcode != OpConstantComposite || d.Operands.Length <= 2) return FKind.Unknown;
            var comps = d.Operands[2..];
            FKind k = FloatConstKind(comps[0]);
            for (int i = 1; i < comps.Length; i++) if (FloatConstKind(comps[i]) != k) return FKind.Unknown;
            return k;
        }
        if (Def(type) is not { Opcode: OpTypeFloat }) return FKind.Unknown;
        int width = TypeWidth(type);
        if (width == 16) { if (!ScalarWord(id, out uint x)) return FKind.Unknown; float f = F16(x); return f == 0f ? FKind.Zero : f == 1f ? FKind.One : FKind.Unknown; }
        if (width == 32) { if (!ScalarWord(id, out uint x)) return FKind.Unknown; float f = F32(x); return f == 0f ? FKind.Zero : f == 1f ? FKind.One : FKind.Unknown; }
        if (width == 64) { var w = Words64(id); if (w is null) return FKind.Unknown; double v = F64(w[0], w[1]); return v == 0.0 ? FKind.Zero : v == 1.0 ? FKind.One : FKind.Unknown; }
        return FKind.Unknown;
    }

    // GetBoolConstantKind: the uniform boolean value of a bool/null/composite constant, or null.
    bool? BoolConstKind(uint id)
    {
        var d = Def(id);
        if (d is null) return null;
        if (d.Opcode == OpConstantComposite)
        {
            if (d.Operands.Length <= 2) return null;
            var comps = d.Operands[2..];
            var first = BoolConstKind(comps[0]);
            if (first is null) return null;
            for (int i = 1; i < comps.Length; i++) if (BoolConstKind(comps[i]) != first) return null;
            return first;
        }
        if (d.Opcode == OpConstantNull) return false;
        if (d.Opcode == 41) return true;    // OpConstantTrue
        if (d.Opcode == 42) return false;   // OpConstantFalse
        return null;
    }

    // Constant::IsZero (bit pattern all-zero for scalar, all-zero components for composite, null -> zero).
    bool IsZeroConst(uint id)
    {
        var d = Def(id);
        if (d is null) return false;
        if (d.Opcode == OpConstantNull || d.Opcode == 42) return true;
        if (d.Opcode == 41) return false;
        if (d.Opcode == OpConstant) { for (int i = 2; i < d.Operands.Length; i++) if (d.Operands[i] != 0) return false; return d.Operands.Length > 2; }
        if (d.Opcode == OpConstantComposite) { for (int i = 2; i < d.Operands.Length; i++) if (!IsZeroConst(d.Operands[i])) return false; return d.Operands.Length > 2; }
        return false;
    }

    // IsAllInt1: every element sign-extends to 1.
    bool IsAllInt1(uint id)
    {
        var d = Def(id);
        if (d is null) return false;
        if (d.Opcode == OpConstantComposite) { for (int i = 2; i < d.Operands.Length; i++) if (!IsAllInt1(d.Operands[i])) return false; return d.Operands.Length > 2; }
        if (d.Opcode == OpConstant && IsIntType(d.Operands[0]))
        {
            if (d.Operands.Length == 3) return d.Operands[2] == 1;
            if (d.Operands.Length == 4) return d.Operands[2] == 1 && d.Operands[3] == 0;
        }
        return false;
    }

    // ElementWidth: unwrap a vector to its scalar element's bit width.
    int ElementWidth(uint type)
    {
        var d = Def(type);
        if (d is { Opcode: OpTypeVector }) return ElementWidth(d.Operands[1]);
        if (d is { Opcode: OpTypeFloat or OpTypeInt } && d.Operands.Length >= 2) return (int)d.Operands[1];
        return 0;
    }
    bool HasFloatingPoint(uint type)
    {
        var d = Def(type);
        if (d is { Opcode: OpTypeFloat }) return true;
        if (d is { Opcode: OpTypeVector }) return HasFloatingPoint(d.Operands[1]);
        return false;
    }

    // NumUses of a value, supplied by the Simplification driver (its maintained user map). null (CCP) -> treat
    // as ">1" so single-use-guarded rules conservatively decline.
    public Func<uint, int>? UseCountFn;
    int NumUses(uint id) => UseCountFn?.Invoke(id) ?? 2;

    // ---- Arithmetic identity / redundancy (in-place) ----

    bool RedundantFAdd(SpvInstr inst)
    {
        if (!FpAllowed(inst)) return false;
        var k0 = FloatConstKind(InOp(inst, 0)); var k1 = FloatConstKind(InOp(inst, 1));
        if (k0 == FKind.Zero || k1 == FKind.Zero) { SetOp(inst, OpCopyObject, InOp(inst, k0 == FKind.Zero ? 1 : 0)); return true; }
        return false;
    }
    bool RedundantFSub(SpvInstr inst)
    {
        if (!FpAllowed(inst)) return false;
        var k0 = FloatConstKind(InOp(inst, 0)); var k1 = FloatConstKind(InOp(inst, 1));
        if (k0 == FKind.Zero) { SetOp(inst, OpFNegate, InOp(inst, 1)); return true; }
        if (k1 == FKind.Zero) { SetOp(inst, OpCopyObject, InOp(inst, 0)); return true; }
        return false;
    }
    bool RedundantFMul(SpvInstr inst)
    {
        if (!FpAllowed(inst)) return false;
        var k0 = FloatConstKind(InOp(inst, 0)); var k1 = FloatConstKind(InOp(inst, 1));
        if (k0 == FKind.Zero || k1 == FKind.Zero) { SetOp(inst, OpCopyObject, InOp(inst, k0 == FKind.Zero ? 0 : 1)); return true; }
        if (k0 == FKind.One || k1 == FKind.One) { SetOp(inst, OpCopyObject, InOp(inst, k0 == FKind.One ? 1 : 0)); return true; }
        return false;
    }
    bool RedundantFDiv(SpvInstr inst)
    {
        if (!FpAllowed(inst)) return false;
        var k0 = FloatConstKind(InOp(inst, 0)); var k1 = FloatConstKind(InOp(inst, 1));
        if (k0 == FKind.Zero || k1 == FKind.One) { SetOp(inst, OpCopyObject, InOp(inst, 0)); return true; }
        return false;
    }
    bool RedundantFMod(SpvInstr inst)
    {
        if (!FpAllowed(inst)) return false;
        if (FloatConstKind(InOp(inst, 0)) == FKind.Zero) { SetOp(inst, OpCopyObject, InOp(inst, 0)); return true; }
        return false;
    }
    bool IntMultipleBy1(SpvInstr inst)
    {
        for (int i = 0; i < 2; i++)
        {
            uint c = InOp(inst, i);
            if (Def(c) is not { Opcode: OpConstant } cd || !IsIntType(cd.Operands[0])) continue;
            int width = ElementWidth(cd.Operands[0]);
            if (width != 32 && width != 64) return false;
            bool isOne = width == 32 ? cd.Operands.Length == 3 && cd.Operands[2] == 1 : cd.Operands.Length == 4 && cd.Operands[2] == 1 && cd.Operands[3] == 0;
            if (isOne) { SetOp(inst, OpCopyObject, InOp(inst, 1 - i)); return true; }
        }
        return false;
    }
    // RedundantBinaryOpWithZeroOperand(arg, foldToArg): if operand `arg` is a zero constant, fold to operand `foldToArg`.
    bool RedundantBinaryZero(SpvInstr inst, int arg, int foldToArg)
    {
        uint c = InOp(inst, arg);
        if (!IsConst(c) || !IsZeroConst(c)) return false;
        uint operand = InOp(inst, foldToArg);
        SetOp(inst, inst.Operands[0] == TypeOf(c) ? OpCopyObject : OpBitcast, operand);
        return true;
    }
    bool RedundantSUDiv(SpvInstr inst)
    {
        uint c = InOp(inst, 1);
        if (!IsConst(c) || !IsAllInt1(c)) return false;
        SetOp(inst, inst.Operands[0] == TypeOf(c) ? OpCopyObject : OpBitcast, InOp(inst, 0));
        return true;
    }
    bool RedundantSUMod(SpvInstr inst)
    {
        uint c = InOp(inst, 1);
        if (!IsConst(c) || !IsAllInt1(c)) return false;
        SetOp(inst, OpCopyObject, NullConst(inst.Operands[0]));
        return true;
    }

    // ---- Select / Phi ----

    bool RedundantSelect(SpvInstr inst)
    {
        uint trueId = InOp(inst, 1), falseId = InOp(inst, 2);
        if (trueId == falseId) { SetOp(inst, OpCopyObject, trueId); return true; }
        uint condId = InOp(inst, 0);
        if (!IsConst(condId)) return false;
        var ct = Def(TypeOf(condId));
        if (ct is { Opcode: OpTypeBool })
        {
            bool selFalse = Def(condId) is { Opcode: OpConstantNull or 42 };
            SetOp(inst, OpCopyObject, selFalse ? falseId : trueId);
            return true;
        }
        if (ct is not { Opcode: OpTypeVector }) return false;
        var cd = Def(condId)!;
        if (cd.Opcode == OpConstantNull) { SetOp(inst, OpCopyObject, falseId); return true; }
        if (cd.Opcode != OpConstantComposite) return false;
        var comps = cd.Operands[2..]; uint size = (uint)comps.Length;
        var ops = new List<uint> { trueId, falseId };
        for (uint i = 0; i < size; i++)
        {
            bool isFalse = Def(comps[(int)i]) is { Opcode: OpConstantNull or 42 };
            ops.Add(isFalse ? i + size : i);
        }
        SetOp(inst, OpVectorShuffle, ops.ToArray());
        return true;
    }
    bool FoldConstantBooleanSelect(SpvInstr inst)
    {
        uint c1 = InOp(inst, 1), c2 = InOp(inst, 2);
        if (!IsConst(c1) || !IsConst(c2)) return false;
        uint condId = InOp(inst, 0);
        if (inst.Operands[0] != TypeOf(condId)) return false;
        var ut = BoolConstKind(c1); var uf = BoolConstKind(c2);
        if (ut is null || uf is null) return false;
        if (ut == true && uf == false) { SetOp(inst, OpCopyObject, condId); return true; }
        if (ut == false && uf == true) { SetOp(inst, OpLogicalNot, condId); return true; }
        return false;
    }
    bool RedundantPhi(SpvInstr inst)
    {
        uint incoming = 0;
        for (int i = 0; i < NumIn(inst); i += 2)
        {
            uint opId = InOp(inst, i);
            if (opId == inst.ResultId) continue;
            if (incoming == 0) incoming = opId;
            else if (opId != incoming) return false;
        }
        if (incoming == 0) return false;
        SetOp(inst, OpCopyObject, incoming);
        return true;
    }

    // ---- Logical ----

    bool RedundantLogicalAnd(SpvInstr inst)
    {
        if (BoolConstKind(ConstInputId(inst)) == true) { SetOp(inst, OpCopyObject, NonConstInputId(inst)); return true; }
        return false;
    }
    bool RedundantLogicalOr(SpvInstr inst)
    {
        if (BoolConstKind(ConstInputId(inst)) == false) { SetOp(inst, OpCopyObject, NonConstInputId(inst)); return true; }
        return false;
    }
    bool RedundantLogicalNot(SpvInstr inst)
    {
        if (Def(InOp(inst, 0)) is { Opcode: OpLogicalNot } child) { SetOp(inst, OpCopyObject, InOp(child, 0)); return true; }
        return false;
    }
    bool RedundantLogicalEqual(SpvInstr inst)
    {
        uint constId = ConstInputId(inst);
        if (constId == 0) return false;
        if (inst.Operands[0] != TypeOf(InOp(inst, 0))) return false;
        var uk = BoolConstKind(constId);
        if (uk is null) return false;
        bool directCopy = inst.Opcode == OpLogicalEqual ? uk.Value : !uk.Value;
        SetOp(inst, directCopy ? OpCopyObject : OpLogicalNot, NonConstInputId(inst));
        return true;
    }
    bool FoldLogicalNotComparison(SpvInstr inst)
    {
        var child = Def(InOp(inst, 0));
        if (child is null || NumUses(child.ResultId) > 1) return false;
        int no = child.Opcode switch
        {
            170 => 171, 171 => 170, 164 => 165, 165 => 164,
            180 => 183, 182 => 181, 181 => 182, 183 => 180,
            172 => 178, 178 => 172, 173 => 179, 179 => 173,
            186 => 189, 188 => 187, 187 => 188, 189 => 186,
            176 => 174, 174 => 176, 177 => 175, 175 => 177,
            184 => 191, 190 => 185, 185 => 190, 191 => 184,
            _ => 0
        };
        if (no == 0) return false;
        SetOp(inst, no, InOp(child, 0), InOp(child, 1));
        return true;
    }

    // ---- Bitcast ----

    bool RedundantBitcast(SpvInstr inst)
    {
        uint childId = InOp(inst, 0);
        if (Def(childId) is not { } child) return false;
        if (inst.Operands[0] == TypeOf(childId)) { SetOp(inst, OpCopyObject, childId); return true; }
        if (child.Opcode != OpBitcast) return false;
        uint grand = InOp(child, 0);
        if (TypeOf(grand) == inst.Operands[0]) { SetOp(inst, OpCopyObject, grand); return true; }
        SetIn(inst, grand);
        return true;
    }

    // ---- Composite extract / construct ----

    bool HaveSameIndexesExceptForLast(SpvInstr a, SpvInstr b)
    {
        if (NumIn(a) != NumIn(b)) return false;
        int first = a.Opcode == OpCompositeInsert ? 2 : 1;
        for (int i = first; i < NumIn(a) - 1; i++) if (InOp(a, i) != InOp(b, i)) return false;
        return true;
    }
    // Walks index words inst.Operands[start..end) from `typeId`, returning the accessed element's type id (0 if invalid).
    uint GetElementType(uint typeId, SpvInstr inst, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            var t = Def(typeId);
            if (t is null) return 0;
            switch (t.Opcode)
            {
                case OpTypeArray or OpTypeMatrix or OpTypeVector: typeId = t.Operands[1]; break;
                case OpTypeStruct: typeId = t.Operands[1 + (int)inst.Operands[i]]; break;
                default: return 0;
            }
        }
        return typeId;
    }
    uint NumElemsContributed(SpvInstr inst, int index)
    {
        if (Def(inst.Operands[0]) is not { Opcode: OpTypeVector }) return 1;
        if (Def(TypeOf(InOp(inst, index))) is { Opcode: OpTypeVector } td) return td.Operands[2];
        return 1;
    }
    List<uint> ExtractOperandsForElement(SpvInstr inst, uint resultIndex)
    {
        var result = new List<uint>();
        if (Def(inst.Operands[0]) is not { Opcode: OpTypeVector })
        {
            if (resultIndex < NumIn(inst)) result.Add(InOp(inst, (int)resultIndex));
            return result;
        }
        uint total = 0;
        for (int idx = 0; idx < NumIn(inst); idx++)
        {
            uint ec = NumElemsContributed(inst, idx);
            total += ec;
            if (resultIndex < total)
            {
                uint id = InOp(inst, idx);
                result.Add(id);
                if (Def(TypeOf(id)) is { Opcode: OpTypeVector }) result.Add(resultIndex - (total - ec));
                return result;
            }
        }
        return result;
    }
    bool CompositeConstructFeedingExtract(SpvInstr inst)
    {
        if (NumIn(inst) <= 1) return false;
        var cinst = Def(InOp(inst, 0));
        if (cinst is not { Opcode: OpCompositeConstruct }) return false;
        uint indexIntoResult = InOp(inst, 1);
        var operands = ExtractOperandsForElement(cinst, indexIntoResult);
        if (operands.Count == 0) return false;
        for (int i = 2; i < NumIn(inst); i++) operands.Add(InOp(inst, i));
        if (operands.Count == 1) SetOp(inst, OpCopyObject, operands.ToArray());
        else SetIn(inst, operands.ToArray());
        return true;
    }
    bool CompositeExtractFeedingConstruct(SpvInstr inst)
    {
        if (NumIn(inst) == 0) return false;
        uint originalId = 0;
        SpvInstr? firstElem = null;
        for (int i = 0; i < NumIn(inst); i++)
        {
            var elem = Def(InOp(inst, i));
            if (elem is null) return false;
            firstElem ??= elem;
            if (elem.Opcode != OpCompositeExtract) return false;
            if (!HaveSameIndexesExceptForLast(elem, firstElem)) return false;
            if (elem.Operands[^1] != (uint)i) return false;
            if (i == 0) originalId = InOp(elem, 0);
            else if (originalId != InOp(elem, 0)) return false;
        }
        var originalInst = Def(originalId);
        if (originalInst is null) return false;
        uint originalTypeId = GetElementType(originalInst.Operands[0], firstElem!, 3, firstElem!.Operands.Length - 1);
        if (inst.Operands[0] != originalTypeId) return false;
        if (NumIn(firstElem) == 2) { SetOp(inst, OpCopyObject, originalId); return true; }
        SetOp(inst, OpCompositeExtract, firstElem.Operands[2..(firstElem.Operands.Length - 1)]);
        return true;
    }
    bool InsertFeedingExtract(SpvInstr inst)
    {
        var cinst = Def(InOp(inst, 0));
        if (cinst is not { Opcode: OpCompositeInsert }) return false;
        int i;
        for (i = 1; i < NumIn(inst); i++)
        {
            if (i + 1 >= NumIn(cinst)) break;
            if (InOp(inst, i) != InOp(cinst, i + 1)) break;
        }
        if (i == NumIn(inst) && i + 1 == NumIn(cinst)) { SetOp(inst, OpCopyObject, InOp(cinst, 0)); return true; }
        if (i == NumIn(inst)) return false;
        if (i + 1 == NumIn(cinst))
        {
            var operands = new List<uint> { InOp(cinst, 0) };
            for (; i < NumIn(inst); i++) operands.Add(InOp(inst, i));
            SetIn(inst, operands.ToArray());
            return true;
        }
        var ops = new List<uint> { InOp(cinst, 1) };
        for (i = 1; i < NumIn(inst); i++) ops.Add(InOp(inst, i));
        SetIn(inst, ops.ToArray());
        return true;
    }
    bool VectorShuffleFeedingExtract(SpvInstr inst)
    {
        var cinst = Def(InOp(inst, 0));
        if (cinst is not { Opcode: OpVectorShuffle }) return false;
        var firstInput = Def(InOp(cinst, 0));
        if (firstInput is null || Def(firstInput.Operands[0]) is not { Opcode: OpTypeVector } fit) return false;
        uint firstSize = fit.Operands[2];
        uint newIndex = InOp(cinst, 2 + (int)InOp(inst, 1));
        if (newIndex == 0xffffffff) { SetOp(inst, OpUndef); return true; }
        uint newVector;
        if (newIndex < firstSize) newVector = InOp(cinst, 0);
        else { newVector = InOp(cinst, 1); newIndex -= firstSize; }
        int s = InStart(inst);
        inst.Operands[s] = newVector; inst.Operands[s + 1] = newIndex;
        return true;
    }
    bool DotProductDoingExtract(SpvInstr inst)
    {
        if (!FpAllowed(inst)) return false;
        for (int i = 0; i < 2; i++)
        {
            uint cid = InOp(inst, i);
            if (!IsConst(cid)) continue;
            if (Def(TypeOf(cid)) is not { Opcode: OpTypeVector } vd) return false;
            if (Def(vd.Operands[1]) is not { Opcode: OpTypeFloat } ed) return false;
            int ew = (int)ed.Operands[1];
            if (ew != 32 && ew != 64) return false;
            var comps = Comps(cid, (int)vd.Operands[2]);
            if (comps is null) return false;
            uint componentWithOne = 0xffffffff; bool allOthersZero = true;
            for (uint j = 0; j < comps.Length; j++)
            {
                double value;
                if (ew == 32) { if (!ScalarWord(comps[j], out uint w)) return false; value = F32(w); }
                else { var w = Words64(comps[j]); if (w is null) return false; value = F64(w[0], w[1]); }
                if (value == 0.0) continue;
                if (value == 1.0) { if (componentWithOne == 0xffffffff) componentWithOne = j; else { componentWithOne = 0xffffffff; break; } }
                else { allOthersZero = false; break; }
            }
            if (!allOthersZero || componentWithOne == 0xffffffff) continue;
            SetOp(inst, OpCompositeExtract, InOp(inst, 1 - i), componentWithOne);
            return true;
        }
        return false;
    }

    // ---- Arithmetic Merge folding_rules (constant-allocating) ----

    static bool IsValidF(float f) => !float.IsNaN(f) && !float.IsInfinity(f) && !float.IsSubnormal(f);
    static bool IsValidD(double d) => !double.IsNaN(d) && !double.IsInfinity(d) && !double.IsSubnormal(d);

    // NegateConstant: -c as a new (deduped) constant id.
    uint NegateConst(uint id)
    {
        uint type = TypeOf(id); var td = Def(type)!;
        if (td.Opcode == OpTypeVector)
        {
            var d = Def(id)!;
            if (d.Opcode == OpConstantNull) return id;
            uint elem = td.Operands[1];
            var ids = d.Operands[2..].Select(c => NegateScalarConst(elem, c)).ToArray();
            return CompositeConst(type, ids);
        }
        return NegateScalarConst(type, id);
    }
    uint NegateScalarConst(uint type, uint id)
    {
        int w = TypeWidth(type);
        if (IsFloatType(type))
        {
            if (w == 32) { ScalarWord(id, out uint x); return ScalarConst(type, W32(F32(x) * -1.0f)); }
            var d = Words64(id)!; ulong b = BitConverter.DoubleToUInt64Bits(F64(d[0], d[1]) * -1.0); return Scalar64(type, (uint)b, (uint)(b >> 32));
        }
        if (w <= 32) { ScalarWord(id, out uint x); return ScalarConst(type, unchecked(0u - x)); }
        ulong v = Int64Of(id); ulong nv = unchecked(0UL - v); return Scalar64(type, (uint)nv, (uint)(nv >> 32));
    }

    // Body instructions a folding rule materialized (ReassociateNestedAddSub's inner add/sub), to be spliced in by
    // the driver before the instruction being folded. Drained once per FoldInstruction call.
    public IReadOnlyList<SpvInstr> DrainPendingBody() { if (_pendingBody.Count == 0) return []; var r = _pendingBody.ToList(); _pendingBody.Clear(); return r; }
    // Where the pending body goes when a rule builds it somewhere other than before the folded instruction
    // (LoadFeedingExtract builds before the load it replaces). Cleared by the driver after splicing.
    public SpvInstr? PendingAnchor;

    // InstructionBuilder::AddBinaryOp: a fresh binary op with a new id, queued for insertion before the fold site.
    uint AddBinaryOp(uint type, int op, uint a, uint b)
    {
        uint id = _nextId++;
        var ins = new SpvInstr(op, [type, id, a, b]);
        _def[id] = ins; _pendingBody.Add(ins);
        return id;
    }

    // ReassociateCommutiveOp (folding_rules.cpp, the bitwise ops): A op (b op C) -> b op (A op C), A and C
    // constants, 32-bit only.
    bool ReassociateCommutiveBitwise(SpvInstr inst)
    {
        if (ElementWidth(inst.Operands[0]) != 32) return false;
        uint a0 = InOp(inst, 0), a1 = InOp(inst, 1);
        bool c0 = IsConst(a0);
        uint const1 = c0 ? a0 : IsConst(a1) ? a1 : 0;
        if (const1 == 0) return false;
        var other = Def(c0 ? a1 : a0);
        if (other is null || other.Opcode != inst.Opcode) return false;
        uint o0 = InOp(other, 0), o1 = InOp(other, 1);
        bool oc0 = IsConst(o0);
        uint const2 = oc0 ? o0 : IsConst(o1) ? o1 : 0;
        if (const2 == 0) return false;
        uint nonConst = oc0 ? o1 : o0;
        uint merged = PerformOp(inst.Opcode, const1, const2);
        if (merged == 0) return false;
        SetOp(inst, inst.Opcode, nonConst, merged);
        return true;
    }

    // ReassociateNestedGenericInt (folding_rules.cpp): (a op C0) op (b op C1) -> (C0 op C1) op (a op b) for
    // IMul/Or/Xor/And.
    bool ReassociateNestedGenericInt(SpvInstr inst)
    {
        uint a0 = InOp(inst, 0), a1 = InOp(inst, 1);
        if (IsConst(a0) || IsConst(a1)) return false;
        uint type = inst.Operands[0];
        int width = ElementWidth(type); if (width != 32 && width != 64) return false;
        var lhs = Def(a0); var rhs = Def(a1);
        if (lhs is null || rhs is null || lhs.Opcode != inst.Opcode || rhs.Opcode != inst.Opcode) return false;
        bool l0 = IsConst(InOp(lhs, 0)); uint lc = l0 ? InOp(lhs, 0) : IsConst(InOp(lhs, 1)) ? InOp(lhs, 1) : 0;
        if (lc == 0) return false;
        bool r0 = IsConst(InOp(rhs, 0)); uint rc = r0 ? InOp(rhs, 0) : IsConst(InOp(rhs, 1)) ? InOp(rhs, 1) : 0;
        if (rc == 0) return false;
        uint merged = PerformOp(inst.Opcode, lc, rc);
        if (merged == 0) return false;
        uint newRhs = AddBinaryOp(type, inst.Opcode, l0 ? InOp(lhs, 1) : InOp(lhs, 0), r0 ? InOp(rhs, 1) : InOp(rhs, 0));
        SetOp(inst, inst.Opcode, merged, newRhs);
        return true;
    }

    // ReassociateNestedMulDivFloat (folding_rules.cpp): FMul/FDiv of two FMul/FDiv that each have a constant
    // operand -> mergedC * (x op y) (or mergedC / (x * y)), e.g. (a * C0) * (b * C1) -> (C0 * C1) * (a * b).
    bool ReassociateNestedMulDivFloat(SpvInstr inst)
    {
        uint a0 = InOp(inst, 0), a1 = InOp(inst, 1);
        if (IsConst(a0) || IsConst(a1)) return false;
        uint type = inst.Operands[0];
        int width = ElementWidth(type); if (width != 32 && width != 64) return false;
        if (!FpAllowed(inst)) return false;
        var lhs = Def(a0); var rhs = Def(a1);
        if (lhs is null || rhs is null) return false;
        bool lhsMul = lhs.Opcode == OpFMul, lhsDiv = lhs.Opcode == OpFDiv, rhsMul = rhs.Opcode == OpFMul, rhsDiv = rhs.Opcode == OpFDiv;
        if (!(lhsMul || lhsDiv) || !(rhsMul || rhsDiv)) return false;
        if (!FpAllowed(lhs) || !FpAllowed(rhs)) return false;
        bool l0 = IsConst(InOp(lhs, 0)), l1 = IsConst(InOp(lhs, 1));
        if (!l0 && !l1) return false;
        bool r0 = IsConst(InOp(rhs, 0)), r1 = IsConst(InOp(rhs, 1));
        if (!r0 && !r1) return false;
        uint lhsConst = l0 ? InOp(lhs, 0) : InOp(lhs, 1);
        uint rhsConst = r0 ? InOp(rhs, 0) : InOp(rhs, 1);
        bool constLhsRcp = !l0 && lhsDiv, constRhsRcp = !r0 && rhsDiv;
        uint nonConstLhs = l0 ? InOp(lhs, 1) : InOp(lhs, 0);
        bool nonConstLhsRcp = l0 && lhsDiv;
        uint nonConstRhs = r0 ? InOp(rhs, 1) : InOp(rhs, 0);
        bool nonConstRhsRcp = r0 && rhsDiv;
        if (inst.Opcode == OpFDiv) { constRhsRcp = !constRhsRcp; nonConstRhsRcp = !nonConstRhsRcp; }
        // Reciprocal() only handles a scalar float constant.
        if (constLhsRcp) { lhsConst = Def(TypeOf(lhsConst)) is { Opcode: OpTypeFloat } ? ReciprocalScalar(TypeOf(lhsConst), lhsConst) : 0; if (lhsConst == 0) return false; }
        if (constRhsRcp) { rhsConst = Def(TypeOf(rhsConst)) is { Opcode: OpTypeFloat } ? ReciprocalScalar(TypeOf(rhsConst), rhsConst) : 0; if (rhsConst == 0) return false; }
        uint merged = PerformOp(OpFMul, lhsConst, rhsConst);
        if (merged == 0) return false;
        int op; uint newRhs;
        if (!nonConstLhsRcp && !nonConstRhsRcp) { newRhs = AddBinaryOp(type, OpFMul, nonConstLhs, nonConstRhs); op = OpFMul; }
        else if (nonConstLhsRcp && !nonConstRhsRcp) { newRhs = AddBinaryOp(type, OpFDiv, nonConstRhs, nonConstLhs); op = OpFMul; }
        else if (!nonConstLhsRcp && nonConstRhsRcp) { newRhs = AddBinaryOp(type, OpFDiv, nonConstLhs, nonConstRhs); op = OpFMul; }
        else { newRhs = AddBinaryOp(type, OpFMul, nonConstLhs, nonConstRhs); op = OpFDiv; }
        SetOp(inst, op, merged, newRhs);
        return true;
    }

    // RedundantFMix (folding_rules.cpp): mix(x, y, 0) -> x, mix(x, y, 1) -> y.
    bool RedundantFMix(SpvInstr inst)
    {
        if (!FpAllowed(inst) || inst.Operands.Length < 7) return false;
        var k = IsConst(inst.Operands[6]) ? FloatConstKind(inst.Operands[6]) : FKind.Unknown;
        if (k == FKind.Unknown) return false;
        return ToCopy(inst, k == FKind.Zero ? inst.Operands[4] : inst.Operands[5]);
    }

    // StoringUndef (folding_rules.cpp): a non-volatile OpStore of OpUndef becomes OpNop.
    bool StoringUndef(SpvInstr inst)
    {
        if (inst.Operands.Length >= 3 && (inst.Operands[2] & 1) != 0) return false;   // MemoryAccess Volatile
        if (Def(inst.Operands[1]) is not { Opcode: OpUndef }) return false;
        inst.Opcode = OpNop; inst.Operands = [];
        return true;
    }

    // FactorAddSubMuls (folding_rules.cpp): (a * b) +/- (a * c) -> a * (b +/- c), any operand order, when both
    // muls have a single use.
    bool FactorAddSubMuls(SpvInstr inst)
    {
        uint type = inst.Operands[0];
        if (HasFloatingPoint(type) && !FpAllowed(inst)) return false;
        uint a0 = InOp(inst, 0), a1 = InOp(inst, 1);
        var m0 = Def(a0); var m1 = Def(a1);
        if (m0 is not { Opcode: OpFMul or OpIMul } || m1 is not { Opcode: OpFMul or OpIMul }) return false;
        // NumUses counts the folded instruction once per operand id; the same mul on both sides is 2 uses.
        if (a0 == a1 || NumUses(a0) > 1 || NumUses(a1) > 1) return false;
        if (m0.Opcode == OpFMul && (!FpAllowed(m0) || !FpAllowed(m1))) return false;
        for (int i = 0; i < 2; i++)
            for (int j = 0; j < 2; j++)
            {
                if (InOp(m0, i) != InOp(m1, j)) continue;
                uint inner = AddBinaryOp(type, inst.Opcode, InOp(m0, 1 - i), InOp(m1, 1 - j));
                bool isFloat = inst.Opcode is OpFAdd or OpFSub;
                SetOp(inst, isFloat ? OpFMul : OpIMul, InOp(m0, i), inner);
                return true;
            }
        return false;
    }

    // ReassociateNestedAddSub (folding_rules.cpp): (C1 +/- x) +/- (C2 +/- y) -> mergedC +/- (x +/- y).
    bool ReassociateNestedAddSub(SpvInstr inst)
    {
        if (IsConst(InOp(inst, 0)) || IsConst(InOp(inst, 1))) return false;
        uint type = inst.Operands[0];
        int width = ElementWidth(type); if (width != 32 && width != 64) return false;
        bool usesFloat = HasFloatingPoint(type);
        if (usesFloat && !FpAllowed(inst)) return false;

        var lhs = Def(InOp(inst, 0)); var rhs = Def(InOp(inst, 1));
        if (lhs is null || rhs is null) return false;
        int addOp = usesFloat ? OpFAdd : OpIAdd;
        int subOp = usesFloat ? OpFSub : OpISub;
        bool lhsIsAdd = lhs.Opcode == addOp, lhsIsSub = lhs.Opcode == subOp;
        bool rhsIsAdd = rhs.Opcode == addOp, rhsIsSub = rhs.Opcode == subOp;
        if (!(lhsIsAdd || lhsIsSub) || !(rhsIsAdd || rhsIsSub)) return false;
        if (usesFloat && (!FpAllowed(lhs) || !FpAllowed(rhs))) return false;

        bool lhs0c = IsConst(InOp(lhs, 0)), lhs1c = IsConst(InOp(lhs, 1));
        if (!lhs0c && !lhs1c) return false;
        bool rhs0c = IsConst(InOp(rhs, 0)), rhs1c = IsConst(InOp(rhs, 1));
        if (!rhs0c && !rhs1c) return false;

        uint lhsConst = lhs0c ? InOp(lhs, 0) : InOp(lhs, 1);
        uint rhsConst = rhs0c ? InOp(rhs, 0) : InOp(rhs, 1);
        bool constLhsNeg = !lhs0c && lhsIsSub;
        bool constRhsNeg = !rhs0c && rhsIsSub;
        uint nonConstLhs = lhs0c ? InOp(lhs, 1) : InOp(lhs, 0);
        bool nonConstLhsNeg = lhs0c && lhsIsSub;
        uint nonConstRhs = rhs0c ? InOp(rhs, 1) : InOp(rhs, 0);
        bool nonConstRhsNeg = rhs0c && rhsIsSub;

        if (inst.Opcode == OpFSub || inst.Opcode == OpISub) { constRhsNeg = !constRhsNeg; nonConstRhsNeg = !nonConstRhsNeg; }
        if (constLhsNeg) { lhsConst = NegateConst(lhsConst); if (lhsConst == 0) return false; }
        if (constRhsNeg) { rhsConst = NegateConst(rhsConst); if (rhsConst == 0) return false; }

        uint merged = PerformOp(addOp, lhsConst, rhsConst); if (merged == 0) return false;

        int op; uint newRhs;
        if (!nonConstLhsNeg && !nonConstRhsNeg) { newRhs = AddBinaryOp(type, addOp, nonConstLhs, nonConstRhs); op = addOp; }
        else if (nonConstLhsNeg && !nonConstRhsNeg) { newRhs = AddBinaryOp(type, subOp, nonConstRhs, nonConstLhs); op = addOp; }
        else if (!nonConstLhsNeg && nonConstRhsNeg) { newRhs = AddBinaryOp(type, subOp, nonConstLhs, nonConstRhs); op = addOp; }
        else { newRhs = AddBinaryOp(type, addOp, nonConstLhs, nonConstRhs); op = subOp; }
        if (newRhs == 0) return false;
        SetOp(inst, op, merged, newRhs);
        return true;
    }

    // PerformOperation: input1 op input2 as a new constant id (scalar or per-component). 0 if invalid.
    uint PerformOp(int opcode, uint aId, uint bId)
    {
        uint type = TypeOf(aId); var td = Def(type)!;
        if (td.Opcode == OpTypeVector)
        {
            uint elem = td.Operands[1]; int n = (int)td.Operands[2];
            var ac = Comps(aId, n); var bc = Comps(bId, n); if (ac is null || bc is null) return 0;
            var ids = new uint[n];
            for (int i = 0; i < n; i++) { ids[i] = PerformScalar(opcode, elem, ac[i], bc[i]); if (ids[i] == 0) return 0; }
            return CompositeConst(type, ids);
        }
        return PerformScalar(opcode, type, aId, bId);
    }
    uint PerformScalar(int opcode, uint type, uint aId, uint bId)
    {
        if (IsFloatType(type))
        {
            int w = TypeWidth(type);
            if (opcode == OpFDiv && IsZeroConst(bId)) return 0;
            if (w == 32)
            {
                ScalarWord(aId, out uint xa); ScalarWord(bId, out uint xb); float a = F32(xa), b = F32(xb);
                float r = opcode switch { OpFMul => a * b, OpFDiv => a / b, OpFAdd => a + b, OpFSub => a - b, _ => float.NaN };
                return IsValidF(r) ? ScalarConst(type, W32(r)) : 0;
            }
            var da = Words64(aId)!; var db = Words64(bId)!; double a2 = F64(da[0], da[1]), b2 = F64(db[0], db[1]);
            double r2 = opcode switch { OpFMul => a2 * b2, OpFDiv => a2 / b2, OpFAdd => a2 + b2, OpFSub => a2 - b2, _ => double.NaN };
            if (!IsValidD(r2)) return 0; ulong bits = BitConverter.DoubleToUInt64Bits(r2); return Scalar64(type, (uint)bits, (uint)(bits >> 32));
        }
        int iw = TypeWidth(type);
        if (iw <= 32)
        {
            ScalarWord(aId, out uint xa); ScalarWord(bId, out uint xb);
            uint r = opcode switch { OpIMul => unchecked(xa * xb), OpIAdd => unchecked(xa + xb), OpISub => unchecked(xa - xb), OpBitwiseXor => xa ^ xb, OpBitwiseOr => xa | xb, OpBitwiseAnd => xa & xb, _ => 0 };
            return ScalarConst(type, r);
        }
        ulong la = Int64Of(aId), lb = Int64Of(bId);
        ulong rl = opcode switch { OpIMul => unchecked(la * lb), OpIAdd => unchecked(la + lb), OpISub => unchecked(la - lb), OpBitwiseXor => la ^ lb, OpBitwiseOr => la | lb, OpBitwiseAnd => la & lb, _ => 0 };
        return Scalar64(type, (uint)rl, (uint)(rl >> 32));
    }

    bool IsFoldableNegation(SpvInstr? inst) => inst is not null && (inst.Opcode == 126 || (inst.Opcode == OpFNegate && FpAllowed(inst)));
    // The constant operand of a binary op (op0 if const, else op1), or 0.
    uint OpConstId(SpvInstr op) { uint a = InOp(op, 0); if (IsConst(a)) return a; uint b = InOp(op, 1); return IsConst(b) ? b : 0; }
    bool OpZeroIsVar(SpvInstr op) => !IsConst(InOp(op, 0));

    bool MergeNegateArithmetic(SpvInstr inst)
    {
        uint type = inst.Operands[0];
        if (HasFloatingPoint(type) && !FpAllowed(inst)) return false;
        var op = Def(InOp(inst, 0)); if (op is null) return false;
        if (HasFloatingPoint(type) && !FpAllowed(op)) return false;
        if (op.Opcode == inst.Opcode) { SetOp(inst, OpCopyObject, InOp(op, 0)); return true; }
        return false;
    }
    bool MergeNegateMulDiv(SpvInstr inst)
    {
        uint type = inst.Operands[0];
        if (HasFloatingPoint(type) && !FpAllowed(inst)) return false;
        var op = Def(InOp(inst, 0)); if (op is null) return false;
        if (HasFloatingPoint(type) && !FpAllowed(op)) return false;
        int w = ElementWidth(type); if (w != 32 && w != 64) return false;
        int oc = op.Opcode;
        if (oc != OpFMul && oc != OpFDiv && oc != OpIMul && oc != OpSDiv) return false;
        uint c = OpConstId(op); if (c == 0) return false;
        bool zeroIsVar = OpZeroIsVar(op);
        uint negId = NegateConst(c);
        uint nonConst = zeroIsVar ? InOp(op, 0) : InOp(op, 1);
        if (oc is OpFDiv or OpSDiv or OpUDiv) { uint op0 = zeroIsVar ? nonConst : negId, op1 = zeroIsVar ? negId : nonConst; SetOp(inst, oc, op0, op1); }
        else SetOp(inst, oc, nonConst, negId);
        return true;
    }
    bool MergeNegateAddSub(SpvInstr inst)
    {
        uint type = inst.Operands[0]; bool uf = HasFloatingPoint(type);
        if (uf && !FpAllowed(inst)) return false;
        var op = Def(InOp(inst, 0)); if (op is null) return false;
        if (uf && !FpAllowed(op)) return false;
        int w = ElementWidth(type); if (w != 32 && w != 64) return false;
        int oc = op.Opcode;
        if (oc != OpFAdd && oc != OpFSub && oc != OpIAdd && oc != OpISub) return false;
        uint c = OpConstId(op); if (c == 0) return false;
        bool zeroIsVar = OpZeroIsVar(op);
        bool isAdd = oc == OpFAdd || oc == OpIAdd;
        bool swap = !isAdd || zeroIsVar;
        uint constId = isAdd ? NegateConst(c) : zeroIsVar ? InOp(op, 1) : InOp(op, 0);
        uint op0 = zeroIsVar ? InOp(op, 0) : constId;
        uint op1 = zeroIsVar ? constId : InOp(op, 1);
        if (swap) (op0, op1) = (op1, op0);
        SetOp(inst, uf ? OpFSub : OpISub, op0, op1);
        return true;
    }
    bool MergeMulMul(SpvInstr inst)
    {
        uint type = inst.Operands[0];
        if (HasFloatingPoint(type) && !FpAllowed(inst)) return false;
        int w = ElementWidth(type); if (w != 32 && w != 64) return false;
        uint c1 = ConstInputId(inst); if (c1 == 0) return false;
        var other = Def(NonConstInputId(inst)); if (other is null) return false;
        if (HasFloatingPoint(type) && !FpAllowed(other)) return false;
        if (other.Opcode != inst.Opcode) return false;
        uint c2 = OpConstId(other); if (c2 == 0) return false;
        uint merged = PerformOp(inst.Opcode, c1, c2); if (merged == 0) return false;
        uint nonConst = OpZeroIsVar(other) ? InOp(other, 0) : InOp(other, 1);
        SetIn(inst, nonConst, merged);
        return true;
    }
    bool MergeMulNegate(SpvInstr inst)
    {
        uint type = inst.Operands[0]; bool uf = HasFloatingPoint(type);
        if (uf && !FpAllowed(inst)) return false;
        int w = ElementWidth(type); if (w != 32 && w != 64) return false;
        uint c = ConstInputId(inst); if (c == 0) return false;
        var other = Def(NonConstInputId(inst)); if (other is null) return false;
        if (uf && !FpAllowed(other)) return false;
        if (other.Opcode is 126 or OpFNegate) { SetIn(inst, InOp(other, 0), NegateConst(c)); return true; }
        return false;
    }
    bool MergeDivMulDoubleNeg(SpvInstr inst)
    {
        uint type = inst.Operands[0];
        if (HasFloatingPoint(type) && !FpAllowed(inst)) return false;
        var lhs = Def(InOp(inst, 0)); var rhs = Def(InOp(inst, 1));
        if (IsFoldableNegation(lhs) && IsFoldableNegation(rhs)) { SetIn(inst, InOp(lhs!, 0), InOp(rhs!, 0)); return true; }
        return false;
    }
    bool MergeAddNegate(SpvInstr inst)
    {
        uint type = inst.Operands[0]; bool uf = HasFloatingPoint(type);
        if (uf && !FpAllowed(inst)) return false;
        var lhs = Def(InOp(inst, 0)); var rhs = Def(InOp(inst, 1));
        if (IsFoldableNegation(lhs)) { SetOp(inst, uf ? OpFSub : OpISub, InOp(inst, 1), InOp(lhs!, 0)); return true; }
        if (IsFoldableNegation(rhs)) { SetOp(inst, uf ? OpFSub : OpISub, InOp(inst, 0), InOp(rhs!, 0)); return true; }
        return false;
    }
    bool MergeSubNegate(SpvInstr inst)
    {
        uint type = inst.Operands[0]; bool uf = HasFloatingPoint(type);
        if (uf && !FpAllowed(inst)) return false;
        var lhs = Def(InOp(inst, 0)); var rhs = Def(InOp(inst, 1));
        if (IsFoldableNegation(rhs)) { SetOp(inst, uf ? OpFAdd : OpIAdd, InOp(inst, 0), InOp(rhs!, 0)); return true; }
        int w = ElementWidth(type); if (w != 32 && w != 64) return false;
        uint c1 = InOp(inst, 1);
        if (IsConst(c1) && IsFoldableNegation(lhs)) { SetIn(inst, NegateConst(c1), InOp(lhs!, 0)); return true; }
        return false;
    }

    bool HasZeroConst(uint id) { var d = Def(id); if (d is null) return false; if (d.Opcode == OpConstantNull) return true; if (d.Opcode == OpConstantComposite) return d.Operands[2..].Any(HasZeroConst); return IsZeroConst(id); }

    bool MergeMulDiv(SpvInstr inst)
    {
        uint type = inst.Operands[0];
        if (!FpAllowed(inst)) return false;
        int w = ElementWidth(type); if (w != 32 && w != 64) return false;
        for (int i = 0; i < 2; i++)
        {
            var op = Def(InOp(inst, i));
            if (op is { Opcode: OpFDiv } && InOp(op, 1) == InOp(inst, 1 - i)) { SetOp(inst, OpCopyObject, InOp(op, 0)); return true; }
        }
        uint c1 = ConstInputId(inst); if (c1 == 0) return false;
        var other = Def(NonConstInputId(inst)); if (other is null) return false;
        if (!FpAllowed(other) || other.Opcode != OpFDiv) return false;
        uint c2 = OpConstId(other); if (c2 == 0 || HasZeroConst(c2)) return false;
        bool otherFirstIsVar = OpZeroIsVar(other);
        uint merged = PerformOp(otherFirstIsVar ? OpFDiv : OpFMul, c1, c2); if (merged == 0) return false;
        uint nonConst = otherFirstIsVar ? InOp(other, 0) : InOp(other, 1);
        if (otherFirstIsVar) SetOp(inst, OpFMul, nonConst, merged); else SetOp(inst, OpFDiv, merged, nonConst);
        return true;
    }
    bool MergeAddAdd(SpvInstr inst)
    {
        uint type = inst.Operands[0]; bool uf = HasFloatingPoint(type);
        if (uf && !FpAllowed(inst)) return false;
        int w = ElementWidth(type); if (w != 32 && w != 64) return false;
        uint c1 = ConstInputId(inst); if (c1 == 0) return false;
        var other = Def(NonConstInputId(inst)); if (other is null) return false;
        if (uf && !FpAllowed(other)) return false;
        if (other.Opcode != OpFAdd && other.Opcode != OpIAdd) return false;
        uint c2 = OpConstId(other); if (c2 == 0) return false;
        uint nonConst = OpZeroIsVar(other) ? InOp(other, 0) : InOp(other, 1);
        uint merged = PerformOp(inst.Opcode, c1, c2); if (merged == 0) return false;
        SetIn(inst, nonConst, merged);
        return true;
    }
    bool MergeAddSub(SpvInstr inst)
    {
        uint type = inst.Operands[0]; bool uf = HasFloatingPoint(type);
        if (uf && !FpAllowed(inst)) return false;
        int w = ElementWidth(type); if (w != 32 && w != 64) return false;
        uint c1 = ConstInputId(inst); if (c1 == 0) return false;
        var other = Def(NonConstInputId(inst)); if (other is null) return false;
        if (uf && !FpAllowed(other)) return false;
        if (other.Opcode != OpFSub && other.Opcode != OpISub) return false;
        uint c2 = OpConstId(other); if (c2 == 0) return false;
        bool firstIsVar = OpZeroIsVar(other);
        int op = inst.Opcode; uint op1, op2;
        if (firstIsVar) { op1 = InOp(other, 0); op2 = PerformOp(other.Opcode, c1, c2); }
        else { op1 = PerformOp(inst.Opcode, c1, c2); op2 = InOp(other, 1); op = other.Opcode; }
        if (op1 == 0 || op2 == 0) return false;
        SetOp(inst, op, op1, op2);
        return true;
    }
    bool MergeSubAdd(SpvInstr inst)
    {
        uint type = inst.Operands[0]; bool uf = HasFloatingPoint(type);
        if (uf && !FpAllowed(inst)) return false;
        int w = ElementWidth(type); if (w != 32 && w != 64) return false;
        uint c1 = ConstInputId(inst); if (c1 == 0) return false;
        var other = Def(NonConstInputId(inst)); if (other is null) return false;
        if (uf && !FpAllowed(other)) return false;
        if (other.Opcode != OpFAdd && other.Opcode != OpIAdd) return false;
        uint c2 = OpConstId(other); if (c2 == 0) return false;
        uint nonConst = OpZeroIsVar(other) ? InOp(other, 0) : InOp(other, 1);
        bool instC0Null = !IsConst(InOp(inst, 0));
        uint a = c1, b = c2; if (instC0Null) (a, b) = (b, a);
        uint merged = PerformOp(inst.Opcode, a, b);
        int op = inst.Opcode; uint op1, op2;
        if (instC0Null) { op1 = nonConst; op2 = merged; op = other.Opcode; } else { op1 = merged; op2 = nonConst; }
        if (op1 == 0 || op2 == 0) return false;
        SetOp(inst, op, op1, op2);
        return true;
    }
    bool MergeSubSub(SpvInstr inst)
    {
        uint type = inst.Operands[0]; bool uf = HasFloatingPoint(type);
        if (uf && !FpAllowed(inst)) return false;
        int w = ElementWidth(type); if (w != 32 && w != 64) return false;
        uint c1 = ConstInputId(inst); if (c1 == 0) return false;
        var other = Def(NonConstInputId(inst)); if (other is null) return false;
        if (uf && !FpAllowed(other)) return false;
        if (other.Opcode != OpFSub && other.Opcode != OpISub) return false;
        uint c2 = OpConstId(other); if (c2 == 0) return false;
        uint nonConst = OpZeroIsVar(other) ? InOp(other, 0) : InOp(other, 1);
        bool otherC0Null = !IsConst(InOp(other, 0));
        bool instC0Null = !IsConst(InOp(inst, 0));
        int mergeOp = inst.Opcode; uint a = c1, b = c2;
        if (otherC0Null) mergeOp = uf ? OpFAdd : OpIAdd;
        else if (instC0Null) (a, b) = (b, a);
        uint merged = PerformOp(mergeOp, a, b); if (merged == 0) return false;
        int op = inst.Opcode;
        if (!instC0Null && !otherC0Null) op = uf ? OpFAdd : OpIAdd;
        uint op1, op2;
        if (instC0Null ^ otherC0Null) { op1 = merged; op2 = nonConst; } else { op1 = nonConst; op2 = merged; }
        SetOp(inst, op, op1, op2);
        return true;
    }
    bool MergeGenericAddendSub(uint addend, uint sub, SpvInstr inst)
    {
        var subInst = Def(sub);
        if (subInst is null || (subInst.Opcode != OpFSub && subInst.Opcode != OpISub)) return false;
        if (subInst.Opcode == OpFSub && !FpAllowed(subInst)) return false;
        if (addend != InOp(subInst, 1)) return false;
        SetOp(inst, OpCopyObject, InOp(subInst, 0));
        return true;
    }
    bool MergeGenericAddSub(SpvInstr inst)
    {
        uint type = inst.Operands[0];
        if (HasFloatingPoint(type) && !FpAllowed(inst)) return false;
        int w = ElementWidth(type); if (w != 32 && w != 64) return false;
        uint a0 = InOp(inst, 0), a1 = InOp(inst, 1);
        return MergeGenericAddendSub(a0, a1, inst) || MergeGenericAddendSub(a1, a0, inst);
    }
    bool MergeDivDiv(SpvInstr inst)
    {
        if (!FpAllowed(inst)) return false;
        int w = ElementWidth(inst.Operands[0]); if (w != 32 && w != 64) return false;
        uint c1 = ConstInputId(inst); if (c1 == 0 || HasZeroConst(c1)) return false;
        var other = Def(NonConstInputId(inst)); if (other is null || !FpAllowed(other)) return false;
        bool firstIsVar = !IsConst(InOp(inst, 0));
        if (other.Opcode != OpFDiv) return false;
        uint c2 = OpConstId(other); if (c2 == 0 || HasZeroConst(c2)) return false;
        bool otherFirstVar = OpZeroIsVar(other);
        int mergeOp = otherFirstVar ? OpFMul : OpFDiv;
        uint a = c1, b = c2; if (firstIsVar) (a, b) = (b, a);
        uint merged = PerformOp(mergeOp, a, b); if (merged == 0) return false;
        uint nonConst = otherFirstVar ? InOp(other, 0) : InOp(other, 1);
        int op = !firstIsVar && !otherFirstVar ? OpFMul : OpFDiv;
        uint op1 = merged, op2 = nonConst; if (firstIsVar && otherFirstVar) (op1, op2) = (op2, op1);
        SetOp(inst, op, op1, op2);
        return true;
    }
    bool MergeDivMul(SpvInstr inst)
    {
        if (!FpAllowed(inst)) return false;
        int w = ElementWidth(inst.Operands[0]); if (w != 32 && w != 64) return false;
        var op0 = Def(InOp(inst, 0));
        if (op0 is { Opcode: OpFMul })
            for (int i = 0; i < 2; i++)
                if (InOp(op0, i) == InOp(inst, 1)) { SetOp(inst, OpCopyObject, InOp(op0, 1 - i)); return true; }
        uint c1 = ConstInputId(inst); if (c1 == 0 || HasZeroConst(c1)) return false;
        var other = Def(NonConstInputId(inst)); if (other is null || !FpAllowed(other)) return false;
        bool firstIsVar = !IsConst(InOp(inst, 0));
        if (other.Opcode != OpFMul) return false;
        uint c2 = OpConstId(other); if (c2 == 0) return false;
        bool otherFirstVar = OpZeroIsVar(other);
        uint a = c1, b = c2; if (firstIsVar) (a, b) = (b, a);
        uint merged = PerformOp(OpFDiv, a, b); if (merged == 0) return false;
        uint nonConst = otherFirstVar ? InOp(other, 0) : InOp(other, 1);
        uint op1 = merged, op2 = nonConst; if (firstIsVar) (op1, op2) = (op2, op1);
        if (firstIsVar) SetOp(inst, other.Opcode, op1, op2); else SetIn(inst, op1, op2);
        return true;
    }
    bool MergeDivNegate(SpvInstr inst)
    {
        if (!FpAllowed(inst)) return false;
        uint c1 = ConstInputId(inst); if (c1 == 0) return false;
        var other = Def(NonConstInputId(inst)); if (other is null || !FpAllowed(other)) return false;
        bool firstIsVar = !IsConst(InOp(inst, 0));
        if (other.Opcode != OpFNegate) return false;
        uint neg = NegateConst(c1);
        if (firstIsVar) SetIn(inst, InOp(other, 0), neg); else SetIn(inst, neg, InOp(other, 0));
        return true;
    }

    // ReciprocalFDiv: x / c (float constant divisor) -> x * (1/c), materializing the reciprocal constant.
    // Registered before the div-merge rules so, like spirv-opt, a constant divisor is reciprocated (creating
    // that constant) rather than combined in one step -- the id-allocation order the oracle produces.
    bool ReciprocalFDiv(SpvInstr inst)
    {
        if (!FpAllowed(inst)) return false;
        uint type = inst.Operands[0];
        int w = ElementWidth(type); if (w != 32 && w != 64) return false;
        uint divisor = InOp(inst, 1);
        if (!IsConst(divisor)) return false;
        uint recip;
        if (Def(type) is { Opcode: OpTypeVector } vt)
        {
            uint elem = vt.Operands[1]; int n = (int)vt.Operands[2];
            var comps = Comps(divisor, n); if (comps is null) return false;
            var ids = new uint[n];
            for (int i = 0; i < n; i++) { ids[i] = ReciprocalScalar(elem, comps[i]); if (ids[i] == 0) return false; }
            recip = CompositeConst(type, ids);
        }
        else { recip = ReciprocalScalar(type, divisor); if (recip == 0) return false; }
        SetOp(inst, OpFMul, InOp(inst, 0), recip);
        return true;
    }
    uint ReciprocalScalar(uint type, uint constId)
    {
        if (Def(constId) is not { Opcode: OpConstant }) return 0;
        if (ElementWidth(type) == 32)
        {
            if (!ScalarWord(constId, out uint cw)) return 0;
            float val = F32(cw); if (val == 0f) return 0;
            float r = 1f / val; uint bits = W32(r);
            if (float.IsNaN(r) || float.IsInfinity(r) || ((bits & 0x7F800000) == 0 && (bits & 0x007FFFFF) != 0)) return 0;
            return ScalarConst(type, bits);
        }
        var words = Words64(constId); if (words is null) return 0;
        double dv = F64(words[0], words[1]); if (dv == 0d) return 0;
        double dr = 1d / dv; ulong db = System.BitConverter.DoubleToUInt64Bits(dr);
        if (double.IsNaN(dr) || double.IsInfinity(dr) || ((db & 0x7FF0000000000000UL) == 0 && (db & 0x000FFFFFFFFFFFFFUL) != 0)) return 0;
        return Scalar64(type, (uint)db, (uint)(db >> 32));
    }

    // BitCastScalarOrVector: OpBitcast of a numeric constant -> a constant of the target type with the same words.
    int TypeWordCount(uint type)
    {
        var d = Def(type);
        if (d is { Opcode: OpTypeVector }) return (int)d.Operands[2] * TypeWordCount(d.Operands[1]);
        if (d is { Opcode: OpTypeInt or OpTypeFloat }) return TypeWidth(type) == 64 ? 2 : 1;
        if (d is { Opcode: OpTypeBool }) return 1;
        return 0;
    }
    List<uint>? NumericWords(uint id)
    {
        var d = Def(id);
        if (d is null) return null;
        if (d.Opcode == OpConstantNull) { int t = TypeWordCount(TypeOf(id)); return t == 0 ? null : Enumerable.Repeat(0u, t).ToList(); }
        if (d.Opcode == OpConstant) return d.Operands[2..].ToList();
        if (d.Opcode == OpConstantComposite) { var w = new List<uint>(); for (int i = 2; i < d.Operands.Length; i++) { var s = NumericWords(d.Operands[i]); if (s is null) return null; w.AddRange(s); } return w; }
        return null;
    }
    uint ScalarFromWords(uint type, List<uint> w)
    {
        if (Def(type) is { Opcode: OpTypeBool }) return BoolConst(type, w[0] != 0);
        if (TypeWidth(type) == 64 && w.Count == 2) return Scalar64(type, w[0], w[1]);
        if (w.Count != 1) return 0;
        // Narrow-integer bitcast: the word holds the full 32-bit pattern but only the low
        // `width` bits are the value; re-extend per the target's signedness (e.g. U16 0xFFFF
        // -> I16 -1 is 0xFFFFFFFF, not 0x0000FFFF).
        if (Def(type) is { Opcode: OpTypeInt } td && td.Operands.Length >= 3
            && (int)td.Operands[1] is int width && width is > 0 and < 32)
        {
            uint masked = w[0] & (width == 32 ? 0xFFFFFFFFu : ((1u << width) - 1));
            if (td.Operands[2] == 1 && (masked >> (width - 1) & 1) != 0)
                masked |= ~0u << width;
            return ScalarConst(type, masked);
        }
        return ScalarConst(type, w[0]);
    }
    uint WordsToConstant(List<uint> words, uint type)
    {
        var d = Def(type)!;
        if (d.Opcode == OpTypeVector)
        {
            uint elem = d.Operands[1]; int n = (int)d.Operands[2]; int ew = TypeWordCount(elem);
            if (ew == 0 || words.Count != n * ew) return 0;
            var ids = new uint[n];
            for (int i = 0; i < n; i++) { ids[i] = ScalarFromWords(elem, words.GetRange(i * ew, ew)); if (ids[i] == 0) return 0; }
            return CompositeConst(type, ids);
        }
        return ScalarFromWords(type, words);
    }
    bool BitCastScalarOrVector(SpvInstr inst)
    {
        uint srcId = InOp(inst, 0);
        if (!IsConst(srcId)) return false;
        uint type = inst.Operands[0];
        if (HasFloatingPoint(type) && !FpAllowed(inst)) return false;
        var words = NumericWords(srcId);
        if (words is null || words.Count == 0) return false;
        uint newId = WordsToConstant(words, type);
        if (newId == 0) return false;
        SetOp(inst, OpCopyObject, newId);
        return true;
    }

    // BitReverseScalarOrVector: reverse each 32-bit word of a constant integer scalar/vector.
    bool BitReverseScalarOrVector(SpvInstr inst)
    {
        uint srcId = InOp(inst, 0);
        if (!IsConst(srcId)) return false;
        var words = NumericWords(srcId);
        if (words is null || words.Count == 0) return false;
        for (int i = 0; i < words.Count; i++)
        {
            uint w = words[i];
            w = ((w & 0x55555555) << 1) | ((w >> 1) & 0x55555555);
            w = ((w & 0x33333333) << 2) | ((w >> 2) & 0x33333333);
            w = ((w & 0x0F0F0F0F) << 4) | ((w >> 4) & 0x0F0F0F0F);
            w = ((w & 0x00FF00FF) << 8) | ((w >> 8) & 0x00FF00FF);
            words[i] = (w << 16) | (w >> 16);
        }
        uint newId = WordsToConstant(words, inst.Operands[0]);
        if (newId == 0) return false;
        SetOp(inst, OpCopyObject, newId);
        return true;
    }

    // InstructionBuilder: a fresh instruction with a new result id, queued for insertion before the fold site.
    uint AddInstr(int op, uint type, params uint[] rest)
    {
        uint id = _nextId++;
        var ins = new SpvInstr(op, [type, id, .. rest]);
        _def[id] = ins; _pendingBody.Add(ins);
        return id;
    }

    // CompositeInsertToCompositeConstruct: a chain of inserts that together writes every element of the
    // container becomes a single OpCompositeConstruct (the innermost container when the inserts are nested).
    bool CompositeInsertToCompositeConstruct(SpvInstr inst)
    {
        int numIn = inst.Operands.Length - 2;
        if (numIn < 3) return false;
        bool SameIndexesExceptLast(SpvInstr a, SpvInstr b)
        {
            if (a.Operands.Length != b.Operands.Length) return false;
            for (int i = 4; i < a.Operands.Length - 1; i++) if (a.Operands[i] != b.Operands[i]) return false;
            return true;
        }
        var inserted = new SortedDictionary<uint, uint>();
        for (var cur = inst; cur is { Opcode: OpCompositeInsert }; cur = Def(cur.Operands[3]))
        {
            if (cur.Operands.Length - 2 > numIn && !inserted.ContainsKey(cur.Operands[2 + numIn - 1])) return false;
            if (SameIndexesExceptLast(inst, cur)) inserted.TryAdd(cur.Operands[^1], cur.Operands[2]);
        }
        // The container the last index selects into: walk the type through every index but the last.
        uint container = inst.Operands[0];
        for (int i = 4; i < inst.Operands.Length - 1; i++)
        {
            var t = Def(container);
            if (t is { Opcode: OpTypeArray or OpTypeMatrix or 23 }) container = t.Operands[1];
            else if (t is { Opcode: OpTypeStruct }) container = t.Operands[1 + inst.Operands[i]];
            else return false;
        }
        var ct = Def(container);
        uint size = ct switch
        {
            { Opcode: 23 or OpTypeMatrix } => ct.Operands[2],
            { Opcode: OpTypeStruct } => (uint)(ct.Operands.Length - 1),
            { Opcode: OpTypeArray } => Def(ct.Operands[2]) is { Opcode: OpConstant } len ? len.Operands[2] : uint.MaxValue,
            _ => 0,
        };
        if (inserted.Count == 0 || inserted.Count != size || inserted.Keys.Max() >= size) return false;
        uint construct = AddInstr(OpCompositeConstruct, container, [.. inserted.Values]);
        if (numIn == 3) SetOp(inst, OpCopyObject, construct);
        else { inst.Operands = [.. inst.Operands[..2], construct, .. inst.Operands[3..^1]]; }
        return true;
    }

    uint FindOrAddGlobal(Func<SpvInstr, bool> match, Func<uint, SpvInstr> make)
    {
        foreach (var d in _def.Values) if (match(d)) return d.ResultId;   // types are unique, so the order is moot
        uint id = _nextId++; var g = make(id); _newConsts.Add(g); _def[id] = g; return id;
    }
    uint UIntType32() => FindOrAddGlobal(d => d.Opcode == 21 && d.Operands.Length == 3 && d.Operands[1] == 32 && d.Operands[2] == 0, id => new SpvInstr(21, [id, 32, 0]));
    uint PointerType(uint pointee, uint storage) => FindOrAddGlobal(d => d.Opcode == 32 && d.Operands[1] == storage && d.Operands[2] == pointee, id => new SpvInstr(32, [id, storage, pointee]));

    // GetElementType: the type an extract's literal indexes select inside `type`, or 0.
    uint ElementTypeAt(uint type, IEnumerable<uint> indexes)
    {
        foreach (uint idx in indexes)
        {
            var t = Def(type);
            if (t is { Opcode: OpTypeArray or OpTypeMatrix or 23 }) type = t.Operands[1];
            else if (t is { Opcode: OpTypeStruct } && 1 + idx < t.Operands.Length) type = t.Operands[1 + idx];
            else return 0;
        }
        return type;
    }

    // CopyLogicalFeedingExtract: extract from the OpCopyLogical's source instead, copying (logically, when the
    // element types still differ) only the element.
    bool CopyLogicalFeedingExtract(SpvInstr inst)
    {
        var copy = Def(inst.Operands[2]);
        if (copy is not { Opcode: 400 }) return false;
        uint original = copy.Operands[2];
        uint[] indexes = inst.Operands[3..];
        uint elemType = ElementTypeAt(TypeOf(original), indexes);
        if (elemType == 0) return false;
        uint extract = AddInstr(OpCompositeExtract, elemType, [original, .. indexes]);
        SetOp(inst, elemType == inst.Operands[0] ? OpCopyObject : 400, extract);
        return true;
    }

    // CompositeConstructFeedingCopyLogical: a logical copy of a just-built composite becomes a construct of the
    // result type, logically copying only the constituents whose types differ.
    bool CompositeConstructFeedingCopyLogical(SpvInstr inst)
    {
        var src = Def(inst.Operands[2]);
        if (src is not { Opcode: OpCompositeConstruct }) return false;
        var dst = Def(inst.Operands[0]);
        uint[] parts = src.Operands[2..];
        uint[] expected;
        if (dst is { Opcode: OpTypeStruct }) { if (dst.Operands.Length - 1 != parts.Length) return false; expected = dst.Operands[1..]; }
        else if (dst is { Opcode: OpTypeArray }) expected = [.. Enumerable.Repeat(dst.Operands[1], parts.Length)];
        else return false;
        var operands = new uint[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            operands[i] = TypeOf(parts[i]) == expected[i] ? parts[i] : AddInstr(400, expected[i], parts[i]);
        SetOp(inst, OpCompositeConstruct, operands);
        return true;
    }

    // FMixFeedingExtract: an element of mix(x, y, a) whose `a` element folds to 0 or 1 is that element of x or y.
    bool FMixFeedingExtract(SpvInstr inst)
    {
        var mix = Def(inst.Operands[2]);
        if (mix is not { Opcode: OpExtInst } || _glslExt == 0 || mix.Operands.Length < 7 || mix.Operands[2] != _glslExt || mix.Operands[3] != 46) return false;
        // Fold a scratch copy of the extract that reads `a` instead (its result id is the extract's own).
        uint rid = inst.ResultId;
        var saved = _def.GetValueOrDefault(rid);
        var probe = new SpvInstr(OpCompositeExtract, [inst.Operands[0], rid, mix.Operands[6], .. inst.Operands[3..]]);
        FoldInstruction(probe);
        if (saved is not null) _def[rid] = saved; else _def.Remove(rid);
        if (probe.Opcode != OpCopyObject) return false;
        uint aConst = probe.Operands[2];
        if (Def(aConst) is not { Opcode: OpConstant or OpConstantNull }) return false;
        int width = ElementWidth(inst.Operands[0]);
        if (width != 32 && width != 64) return false;
        double v = FloatOf(aConst, TypeWidth(TypeOf(aConst)));
        bool useX;
        if (v == 0.0) useX = true; else if (v == 1.0) useX = false; else return false;
        inst.Operands[2] = useX ? mix.Operands[4] : mix.Operands[5];
        return true;
    }

    // LoadFeedingExtract: an element extracted from a whole-struct/array load of non-Function/Private memory is
    // loaded directly through an access chain (built before the original load, which DCE then drops).
    bool LoadFeedingExtract(SpvInstr inst)
    {
        var load = Def(inst.Operands[2]);
        if (load is not { Opcode: 61 }) return false;
        if (Def(load.Operands[0]) is not { Opcode: OpTypeStruct or OpTypeArray }) return false;
        uint mask = load.Operands.Length > 3 ? load.Operands[3] : 0;
        if ((mask & 0x1) != 0) return false;                                   // Volatile
        if ((mask & 0x2) != 0) return false;                                   // Aligned: needs the element's byte offset
        uint ptr = load.Operands[2];
        if (Def(TypeOf(ptr)) is not { Opcode: 32 } ptrType) return false;
        uint storage = ptrType.Operands[1];
        if (storage is 7 or 6) return false;                                   // Function / Private: LocalAccessChainConvert's
        var idx = new List<uint>();
        for (int i = 3; i < inst.Operands.Length; i++) idx.Add(ScalarConst(UIntType32(), inst.Operands[i]));
        uint elemPtr = PointerType(inst.Operands[0], storage);
        uint chain = AddInstr(65, elemPtr, [ptr, .. idx]);
        uint newLoad = AddInstr(61, inst.Operands[0], [chain, .. load.Operands[3..]]);
        PendingAnchor = load;
        SetOp(inst, OpCopyObject, newLoad);
        return true;
    }

    static readonly int[] MergeBinaryOpSelectOps =
    [
        164, 165, 167, 166, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187,
        188, 189, 190, 191, OpIAdd, OpFAdd, OpISub, OpFSub, OpIMul, OpFMul, OpUDiv, OpSDiv, OpFDiv, 142,
        OpShiftRightLogical, OpShiftRightArithmetic, OpShiftLeftLogical, OpBitwiseXor, OpBitwiseOr, OpBitwiseAnd,
    ];

    // MergeBinaryOpSelect: (c ? C0 : C1) op C2 -> c ? (C0 op C2) : (C1 op C2) (either operand order). The two new
    // binary ops are built before the instruction and folded on the spot; the folded copies stay behind.
    bool MergeBinaryOpSelect(SpvInstr inst, int op)
    {
        uint a = InOp(inst, 0), b = InOp(inst, 1);
        bool c0 = IsConst(a), c1 = IsConst(b);
        if (!c0 && !c1) return false;
        var sel = Def(c0 ? b : a);
        if (sel is not { Opcode: OpSelect }) return false;
        uint cond = InOp(sel, 0), tv = InOp(sel, 1), fv = InOp(sel, 2);
        if (!IsConst(tv) || !IsConst(fv)) return false;
        // Before SPIR-V 1.4 an OpSelect with a vector result needs a vector condition.
        if (Def(inst.Operands[0]) is { Opcode: 23 } && Def(TypeOf(cond)) is not { Opcode: 23 } && _m.Header[1] < 0x00010400) return false;
        uint type = inst.Operands[0];
        uint lhs = c0 ? AddBinaryOp(type, op, a, tv) : AddBinaryOp(type, op, tv, b);
        uint rhs = c0 ? AddBinaryOp(type, op, a, fv) : AddBinaryOp(type, op, fv, b);
        uint Settle(uint id)
        {
            var d = Def(id)!;
            if (FoldInstruction(d))
                while (d.Opcode == OpCopyObject) { id = d.Operands[2]; d = Def(id)!; }
            return id;
        }
        lhs = Settle(lhs); rhs = Settle(rhs);
        SetOp(inst, OpSelect, cond, lhs, rhs);
        return true;
    }

    // VectorShuffleFeedingShuffle: a shuffle reading through another shuffle reads that shuffle's source
    // directly when every component it takes comes from one side of it (an OpConstantNull stands in when none
    // does). Kept as spirv-opt writes it, including how it re-bases second-operand indices.
    bool VectorShuffleFeedingShuffle(SpvInstr inst)
    {
        const uint Undef = 0xFFFFFFFF;
        var ops = inst.Operands;   // [type, result, v0, v1, comps...]
        var feeding = Def(ops[2])!;
        uint op0Length = Def(TypeOf(ops[2]))!.Operands[2];
        bool feederIsOp0 = true;
        if (feeding.Opcode != OpVectorShuffle) { feeding = Def(ops[3])!; feederIsOp0 = false; }
        if (feeding.Opcode != OpVectorShuffle) return false;
        uint feederOp0Length = Def(TypeOf(feeding.Operands[2]))!.Operands[2];
        uint newFeeder = 0;
        var comps = new List<uint>();
        for (int k = 4; k < ops.Length; k++)
        {
            uint ci = ops[k];
            if (ci != Undef && feederIsOp0 == (ci < op0Length))
            {
                if (ci >= op0Length) ci -= op0Length;
                ci = feeding.Operands[4 + (int)ci];
                if (ci < feederOp0Length)
                {
                    if (newFeeder == 0) newFeeder = feeding.Operands[2];
                    else if (newFeeder != feeding.Operands[2]) return false;
                }
                else if (ci != Undef)
                {
                    if (newFeeder == 0) newFeeder = feeding.Operands[3];
                    else if (newFeeder != feeding.Operands[3]) return false;
                    ci -= feederOp0Length;
                }
                if (!feederIsOp0 && ci != Undef) ci += op0Length;
            }
            comps.Add(ci);
        }
        if (newFeeder == 0) newFeeder = NullConst(feeding.Operands[0]);
        uint v0, v1;
        if (feederIsOp0)
        {
            uint newOp0Size = Def(TypeOf(newFeeder))!.Operands[2];
            int adjustment = (int)op0Length - (int)newOp0Size;
            if (adjustment != 0)
                for (int i = 2; i < comps.Count + 2; i++)
                {
                    // spirv-opt reads the instruction's in-operand i here (the second vector for i == 1 is skipped).
                    uint operand = ops[2 + i];
                    if (operand >= op0Length && operand != Undef) comps[i - 2] = (uint)(comps[i - 2] - adjustment);
                }
            v0 = newFeeder; v1 = ops[3];
        }
        else { v0 = ops[2]; v1 = newFeeder; }
        inst.Operands = [ops[0], ops[1], v0, v1, .. comps];
        return true;
    }

    void RegisterFoldRules()
    {
        AddF(OpBitwiseOr, i => RedundantBinaryZero(i, 1, 0), i => RedundantBinaryZero(i, 0, 1));
        AddF(OpBitwiseXor, i => RedundantBinaryZero(i, 1, 0), i => RedundantBinaryZero(i, 0, 1));
        AddF(OpShiftRightLogical, i => RedundantBinaryZero(i, 1, 0), i => RedundantBinaryZero(i, 0, 0));
        AddF(OpShiftRightArithmetic, i => RedundantBinaryZero(i, 1, 0), i => RedundantBinaryZero(i, 0, 0));
        AddF(OpShiftLeftLogical, i => RedundantBinaryZero(i, 1, 0), i => RedundantBinaryZero(i, 0, 0));
        AddF(OpIAdd, i => RedundantBinaryZero(i, 1, 0), i => RedundantBinaryZero(i, 0, 1));
        AddF(OpISub, i => RedundantBinaryZero(i, 1, 0));
        AddF(OpSDiv, i => RedundantBinaryZero(i, 0, 0));
        AddF(OpUDiv, i => RedundantBinaryZero(i, 0, 0));
        AddF(OpSMod, i => RedundantBinaryZero(i, 0, 0));
        AddF(OpUMod, i => RedundantBinaryZero(i, 0, 0));
        AddF(62, StoringUndef);   // OpStore
        AddFExt(46, RedundantFMix);   // GLSLstd450FMix
        foreach (int op in new[] { OpBitwiseOr, OpBitwiseXor, OpBitwiseAnd }) AddF(op, ReassociateCommutiveBitwise);
        foreach (int op in new[] { OpIMul, OpBitwiseOr, OpBitwiseXor, OpBitwiseAnd }) AddF(op, ReassociateNestedGenericInt);
        // MergeBinaryOpSelect sits with the generic rules, ahead of every opcode-specific one.
        foreach (int op in MergeBinaryOpSelectOps) { int o = op; AddF(o, i => MergeBinaryOpSelect(i, o)); }
        AddF(OpSDiv, RedundantSUDiv); AddF(OpUDiv, RedundantSUDiv);
        AddF(OpSMod, RedundantSUMod); AddF(OpUMod, RedundantSUMod);
        AddF(OpIMul, IntMultipleBy1);
        AddF(OpFAdd, RedundantFAdd);
        AddF(OpFSub, RedundantFSub);
        AddF(OpFMul, RedundantFMul);
        AddF(OpFDiv, RedundantFDiv);
        AddF(OpFMod, RedundantFMod);
        AddF(OpBitcast, BitCastScalarOrVector, RedundantBitcast);
        AddF(204, BitReverseScalarOrVector);   // OpBitReverse
        AddF(OpPhi, RedundantPhi);
        AddF(OpSelect, RedundantSelect, FoldConstantBooleanSelect);
        AddF(OpLogicalAnd, RedundantLogicalAnd);
        AddF(OpLogicalOr, RedundantLogicalOr);
        AddF(OpLogicalNot, RedundantLogicalNot, FoldLogicalNotComparison);
        AddF(OpLogicalEqual, RedundantLogicalEqual);
        AddF(OpLogicalNotEqual, RedundantLogicalEqual);
        AddF(OpCompositeConstruct, CompositeExtractFeedingConstruct);
        AddF(400, CompositeConstructFeedingCopyLogical);   // OpCopyLogical
        AddF(OpCompositeExtract, InsertFeedingExtract, CompositeConstructFeedingExtract, VectorShuffleFeedingExtract, FMixFeedingExtract, CopyLogicalFeedingExtract, LoadFeedingExtract);
        AddF(OpCompositeInsert, CompositeInsertToCompositeConstruct);
        AddF(OpDot, DotProductDoingExtract);
        AddF(OpVectorShuffle, VectorShuffleFeedingShuffle);

        // Arithmetic Merge rules (appended after the redundancy/identity rules already registered above).
        AddF(127, MergeNegateArithmetic, MergeNegateAddSub, MergeNegateMulDiv);   // OpFNegate
        AddF(126, MergeNegateArithmetic, MergeNegateMulDiv, MergeNegateAddSub);   // OpSNegate (MulDiv before AddSub)
        AddF(OpFMul, MergeMulMul, MergeMulDiv, MergeMulNegate, MergeDivMulDoubleNeg, ReassociateNestedMulDivFloat);
        AddF(OpIMul, MergeMulMul, MergeMulNegate, MergeDivMulDoubleNeg);
        AddF(OpFAdd, MergeAddNegate, MergeAddAdd, MergeAddSub, MergeGenericAddSub, ReassociateNestedAddSub, FactorAddSubMuls);
        AddF(OpIAdd, MergeAddNegate, MergeAddAdd, MergeAddSub, MergeGenericAddSub, ReassociateNestedAddSub, FactorAddSubMuls);
        AddF(OpFSub, MergeSubNegate, MergeSubAdd, MergeSubSub, ReassociateNestedAddSub, FactorAddSubMuls);
        AddF(OpISub, MergeSubNegate, MergeSubAdd, MergeSubSub, ReassociateNestedAddSub, FactorAddSubMuls);
        AddF(OpFDiv, ReciprocalFDiv, MergeDivDiv, MergeDivMul, MergeDivNegate, MergeDivMulDoubleNeg, ReassociateNestedMulDivFloat);
        AddF(OpSDiv, MergeDivMulDoubleNeg);
        AddF(142, MergeDivMulDoubleNeg);   // OpVectorTimesScalar
    }

    // FoldBinaryOp(FoldBinaryIntegerOperation<Signed|Unsigned>(op)): constant integer arithmetic at the type's
    // width, component-wise for vectors, with x / 0 and x % 0 folding to 0.
    uint IntBinary(SpvInstr inst, List<uint> ops, bool signed, Func<ulong, ulong, ulong> op)
    {
        if (!IsConst(ops[0]) || !IsConst(ops[1])) return 0;
        uint type = inst.Operands[0];
        var td = Def(type)!;
        if (td.Opcode == 23)
        {
            uint elem = td.Operands[1]; int n = (int)td.Operands[2];
            var a = Comps(ops[0], n); var b = Comps(ops[1], n);
            if (a is null || b is null) return 0;
            var res = new uint[n];
            for (int i = 0; i < n; i++) { uint r = IntScalar(elem, a[i], b[i], signed, op); if (r == 0) return 0; res[i] = r; }
            return CompositeConst(type, res);
        }
        return IntScalar(type, ops[0], ops[1], signed, op);
    }
    uint IntScalar(uint type, uint a, uint b, bool signed, Func<ulong, ulong, ulong> op)
    {
        var td = Def(type);
        if (td is not { Opcode: 21 }) return 0;
        int width = (int)td.Operands[1];
        ulong Value(uint id)
        {
            var d = Def(id);
            if (d is { Opcode: OpConstantNull }) return 0;
            ulong raw = d!.Operands.Length >= 4 ? d.Operands[2] | ((ulong)d.Operands[3] << 32) : d.Operands[2];
            if (width < 64) raw &= (1UL << width) - 1;
            if (signed && width < 64 && (raw >> (width - 1) & 1) != 0) raw |= ~0UL << width;
            return raw;
        }
        if (Def(a) is not { Opcode: OpConstant or OpConstantNull } || Def(b) is not { Opcode: OpConstant or OpConstantNull }) return 0;
        ulong r = op(Value(a), Value(b));
        if (width == 64) return Scalar64(type, (uint)r, (uint)(r >> 32));
        r &= width == 32 ? 0xFFFFFFFFUL : (1UL << width) - 1;
        // A narrow signed constant's word carries its sign extension (SPIR-V literal encoding).
        if (td.Operands[2] == 1 && width < 32 && (r >> (width - 1) & 1) != 0) r |= 0xFFFFFFFFUL << width;
        return ScalarConst(type, (uint)r);
    }

    void RegisterRules()
    {
        Add(OpIAdd, (i, o) => IntBinary(i, o, false, (a, b) => a + b));
        Add(OpISub, (i, o) => IntBinary(i, o, false, (a, b) => a - b));
        Add(OpIMul, (i, o) => IntBinary(i, o, false, (a, b) => a * b));
        Add(OpUDiv, (i, o) => IntBinary(i, o, false, (a, b) => b != 0 ? a / b : 0));
        Add(OpSDiv, (i, o) => IntBinary(i, o, true, (a, b) => b != 0 && !(a == 0x8000000000000000UL && b == ulong.MaxValue) ? (ulong)((long)a / (long)b) : b != 0 ? a : 0));
        Add(OpUMod, (i, o) => IntBinary(i, o, false, (a, b) => b != 0 ? a % b : 0));
        Add(OpSRem, (i, o) => IntBinary(i, o, true, (a, b) => b != 0 && b != ulong.MaxValue ? (ulong)((long)a % (long)b) : 0));
        Add(OpSMod, (i, o) => IntBinary(i, o, true, (a, b) =>
        {
            if (b == 0) return 0;
            // No -1 early-out: like spirv-opt, an exact multiple with a negative divisor adjusts
            // to -1 (e.g. SMod(-32768, -1) folds to -1, matching the reference bit-for-bit).
            long sa = (long)a, sb = (long)b, r = sa % sb;
            if ((sb < 0) != (r < 0)) r += sb;
            return (ulong)r;
        }));
        Add(111, IToF); Add(112, IToF);                                  // OpConvertSToF / UToF
        Add(110, FToI); Add(109, FToI);                                  // OpConvertFToS / FToU
        Add(129, (i, o) => FpBinary(i, o, (a, b) => F32(a) + F32(b), (a, b) => a + b));   // OpFAdd
        Add(131, (i, o) => FpBinary(i, o, (a, b) => F32(a) - F32(b), (a, b) => a - b));   // OpFSub
        Add(133, (i, o) => FpBinary(i, o, (a, b) => F32(a) * F32(b), (a, b) => a * b));   // OpFMul
        Add(136, (i, o) => FpBinary(i, o, (a, b) => F32(a) / F32(b), (a, b) => a / b));   // OpFDiv
        Add(131, FoldRedundantSub); Add(130, FoldRedundantSub);          // OpFSub / OpISub : x-x -> 0
        Add(114, (i, o) => IntConvert(i, o, true));    // OpSConvert
        Add(113, (i, o) => IntConvert(i, o, false));   // OpUConvert
        Add(OpFDiv, FoldRedundantDiv); Add(OpSDiv, FoldRedundantDiv); Add(OpUDiv, FoldRedundantDiv);   // OpFDiv/SDiv/UDiv : x/x -> 1
        Add(169, InvariantSelect);                                       // OpSelect
        Add(180, (i, o) => FpCompare(i, o, (a, b) => a == b, true));     // OpFOrdEqual
        Add(181, (i, o) => FpCompare(i, o, (a, b) => a == b, false));    // OpFUnordEqual
        Add(182, (i, o) => FpCompare(i, o, (a, b) => a != b, true));     // OpFOrdNotEqual
        Add(183, (i, o) => FpCompare(i, o, (a, b) => a != b, false));    // OpFUnordNotEqual
        Add(184, (i, o) => FpCompare(i, o, (a, b) => a < b, true));      // OpFOrdLessThan
        Add(184, FClampFeedingCompare);
        Add(185, (i, o) => FpCompare(i, o, (a, b) => a < b, false));     // OpFUnordLessThan
        Add(185, FClampFeedingCompare);
        Add(186, (i, o) => FpCompare(i, o, (a, b) => a > b, true));      // OpFOrdGreaterThan
        Add(186, FClampFeedingCompare);
        Add(187, (i, o) => FpCompare(i, o, (a, b) => a > b, false));     // OpFUnordGreaterThan
        Add(187, FClampFeedingCompare);
        Add(188, (i, o) => FpCompare(i, o, (a, b) => a <= b, true));     // OpFOrdLessThanEqual
        Add(188, FClampFeedingCompare);
        Add(189, (i, o) => FpCompare(i, o, (a, b) => a <= b, false));    // OpFUnordLessThanEqual
        Add(189, FClampFeedingCompare);
        Add(190, (i, o) => FpCompare(i, o, (a, b) => a >= b, true));     // OpFOrdGreaterThanEqual
        Add(190, FClampFeedingCompare);
        Add(191, (i, o) => FpCompare(i, o, (a, b) => a >= b, false));    // OpFUnordGreaterThanEqual
        Add(191, FClampFeedingCompare);
        Add(127, FNegate);                                               // OpFNegate
        Add(126, SNegate);                                               // OpSNegate
        Add(142, VectorTimesScalar);                                     // OpVectorTimesScalar
        Add(OpCompositeConstruct, FoldCompositeWithConstants);            // 80
        Add(OpCompositeExtract, FoldExtractWithConstants);               // 81
        Add(OpCompositeInsert, FoldInsertWithConstants);                 // 82
        Add(OpVectorShuffle, FoldVectorShuffleWithConstants);            // 79
        Add(84, FoldTranspose);                                          // OpTranspose
        Add(OpDot, FoldDot);                                             // 148
        Add(144, (i, o) => FoldVectorMatrix(i, o, matrixFirst: false));  // OpVectorTimesMatrix
        Add(145, (i, o) => FoldVectorMatrix(i, o, matrixFirst: true));   // OpMatrixTimesVector

        // GLSL.std.450 min/max/clamp/transcendental const folds (ext_rules).
        AddExt(37, (i, o) => FoldMinMax(i, o, false)); AddExt(38, (i, o) => FoldMinMax(i, o, false)); AddExt(39, (i, o) => FoldMinMax(i, o, false));   // FMin/UMin/SMin
        AddExt(40, (i, o) => FoldMinMax(i, o, true)); AddExt(41, (i, o) => FoldMinMax(i, o, true)); AddExt(42, (i, o) => FoldMinMax(i, o, true));      // FMax/UMax/SMax
        AddExt(43, FoldClamp1, FoldClamp2, FoldClamp3); AddExt(44, FoldClamp1, FoldClamp2, FoldClamp3); AddExt(45, FoldClamp1, FoldClamp2, FoldClamp3); // FClamp/UClamp/SClamp
        AddExt(13, (i, o) => FoldTranscendental(i, o, Math.Sin)); AddExt(14, (i, o) => FoldTranscendental(i, o, Math.Cos)); AddExt(15, (i, o) => FoldTranscendental(i, o, Math.Tan));
        AddExt(16, (i, o) => FoldTranscendental(i, o, Math.Asin)); AddExt(17, (i, o) => FoldTranscendental(i, o, Math.Acos)); AddExt(18, (i, o) => FoldTranscendental(i, o, Math.Atan));
        AddExt(27, (i, o) => FoldTranscendental(i, o, Math.Exp)); AddExt(28, (i, o) => FoldTranscendental(i, o, Math.Log)); AddExt(31, (i, o) => FoldTranscendental(i, o, Math.Sqrt));
        AddExt(46, FoldFMix);   // FMix
        AddExt(29, (i, o) => FoldTranscendental(i, o, double.Exp2)); AddExt(30, (i, o) => FoldTranscendental(i, o, Math.Log2));   // Exp2 / Log2
        AddExt(25, (i, o) => FoldTranscendental2(i, o, Math.Atan2)); AddExt(26, (i, o) => FoldTranscendental2(i, o, Math.Pow));    // Atan2 / Pow
    }
}
