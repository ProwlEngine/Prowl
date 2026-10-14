using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Prowl.Prism.Optimizer;

/// <summary>
/// The SPIR-V operand grammar, trimmed to what the optimizer needs: for any instruction it can walk the
/// operand words and say which are id references. That lets passes find an instruction's result id and
/// rewrite uses of an id without corrupting the literal operands that happen to share the id's numeric
/// value. Loaded once from the embedded trimmed grammar JSON.
/// </summary>
internal static class SpvGrammar
{
    static readonly Dictionary<int, (string Kind, string Quant)[]> Instrs = new();
    static readonly Dictionary<string, (bool Bit, Dictionary<uint, string[]> Vals)> Enums = new();
    static readonly HashSet<string> IdKinds = new() { "IdResultType", "IdResult", "IdRef", "IdScope", "IdMemorySemantics" };

    // Opcodes whose id-operand layout is a pure function of the operand COUNT (no value-dependent enum masks,
    // wrapped-op or embedded string) can have their classification memoized by (opcode, length). The rest
    // (OpSpecConstantOp, string-bearing debug ops, image/memory ops with id-bearing enum masks) compute fresh.
    static readonly HashSet<int> Cacheable = new();
    static readonly ConcurrentDictionary<long, (int Ri, bool[] IsId)> ClassifyCache = new();

    static SpvGrammar()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("spirv.grammar.min.json")!;
        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        foreach (var ins in root.GetProperty("instructions").EnumerateObject())
        {
            var ops = new List<(string, string)>();
            foreach (var op in ins.Value.EnumerateArray())
                ops.Add((op[0].GetString()!, op[1].GetString()!));
            Instrs[int.Parse(ins.Name)] = ops.ToArray();
        }
        foreach (var en in root.GetProperty("enums").EnumerateObject())
        {
            var vals = new Dictionary<uint, string[]>();
            foreach (var v in en.Value.GetProperty("vals").EnumerateObject())
                vals[uint.Parse(v.Name)] = v.Value.EnumerateArray().Select(p => p.GetString()!).ToArray();
            Enums[en.Name] = (en.Value.GetProperty("bit").GetBoolean(), vals);
        }

        // An enum "carries ids" if any of its values pulls in an id operand (directly, via a Pair kind, or via
        // a nested enum). Computed to a fixpoint since a bit-enum value's param can be another enum.
        var enumHasId = new Dictionary<string, bool>();
        foreach (var name in Enums.Keys) enumHasId[name] = false;
        bool ParamCarriesId(string p) => p.Contains("Id") || (Enums.ContainsKey(p) && enumHasId[p]);
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (var (name, def) in Enums)
                if (!enumHasId[name] && def.Vals.Values.Any(pars => pars.Any(ParamCarriesId)))
                    { enumHasId[name] = true; changed = true; }
        }

        foreach (var (op, defs) in Instrs)
        {
            bool cacheable = op != 52;   // OpSpecConstantOp reclassifies by its wrapped opcode
            foreach (var (kind, _) in defs)
                if (kind == "LiteralString" || (Enums.ContainsKey(kind) && enumHasId[kind])) { cacheable = false; break; }
            if (cacheable) Cacheable.Add(op);
        }
    }

    /// <summary>Marks which operand words are id references (see the compute overload). For value-independent
    /// opcodes the answer depends only on the operand count, so it is memoized and a shared read-only array is
    /// returned; callers must not mutate <paramref name="isId"/>.</summary>
    public static void Classify(int opcode, uint[] operands, out int resultIndex, out bool[] isId)
    {
        if (Cacheable.Contains(opcode))
        {
            var (ri, ids) = ClassifyCache.GetOrAdd(((long)opcode << 20) | (uint)operands.Length, _ =>
            {
                ClassifyCompute(opcode, new uint[operands.Length], out int r, out bool[] b);
                return (r, b);
            });
            resultIndex = ri;
            isId = ids;
            return;
        }
        ClassifyCompute(opcode, operands, out resultIndex, out isId);
    }

    /// <summary>Marks which operand words of an instruction are id references and where the result id sits
    /// (-1 if the instruction defines none). Operand words are those after the opcode word.</summary>
    static void ClassifyCompute(int opcode, uint[] operands, out int resultIndex, out bool[] isId)
    {
        isId = new bool[operands.Length];
        resultIndex = -1;
        if (!Instrs.TryGetValue(opcode, out var defs)) return;

        int p = 0;
        int rIndex = -1;
        var id = isId;
        var ops = operands;

        void Emit(string kind)
        {
            if (p >= ops.Length) return;
            if (kind == "IdResult") { rIndex = p; id[p] = true; p++; return; }
            if (IdKinds.Contains(kind)) { id[p] = true; p++; return; }
            switch (kind)
            {
                // Composite operand kinds (OpPhi / OpSwitch / OpGroupMemberDecorate): each is two words.
                case "PairIdRefIdRef": if (p < ops.Length) id[p] = true; p++; if (p < ops.Length) id[p] = true; p++; return;
                case "PairLiteralIntegerIdRef": p++; if (p < ops.Length) id[p] = true; p++; return;
                case "PairIdRefLiteralInteger": if (p < ops.Length) id[p] = true; p++; p++; return;
                case "LiteralString":
                    while (p < ops.Length)
                    {
                        uint word = ops[p++];
                        if ((word & 0xFF) == 0 || (word & 0xFF00) == 0 || (word & 0xFF0000) == 0 || (word & 0xFF000000) == 0) break;
                    }
                    break;
                case "LiteralContextDependentNumber":
                    p = ops.Length;   // consumes the rest of the instruction
                    break;
                default:
                    if (Enums.TryGetValue(kind, out var en))
                    {
                        uint val = ops[p++];
                        if (en.Bit)
                        {
                            foreach (var kv in en.Vals)
                                if (kv.Key != 0 && (val & kv.Key) != 0)
                                    foreach (var param in kv.Value) Emit(param);
                        }
                        else if (en.Vals.TryGetValue(val, out var pars))
                            foreach (var param in pars) Emit(param);
                    }
                    else p++;   // literal integer / unknown single-word operand
                    break;
            }
        }

        foreach (var (kind, quant) in defs)
        {
            if (quant == "*") { while (p < ops.Length) Emit(kind); }
            else if (quant == "?") { if (p < ops.Length) Emit(kind); }
            else Emit(kind);
            if (p >= ops.Length) break;
        }

        // OpSpecConstantOp (52): the words after the wrapped-opcode literal are that operation's operands
        // (minus its own result-type/result), so classify them with the wrapped opcode's grammar.
        if (opcode == 52 && operands.Length >= 3 && Instrs.TryGetValue((int)operands[2], out var wrapped))
            foreach (var (kind, quant) in wrapped)
            {
                if (kind is "IdResultType" or "IdResult") continue;
                if (quant == "*") { while (p < operands.Length) Emit(kind); }
                else if (quant == "?") { if (p < operands.Length) Emit(kind); }
                else Emit(kind);
                if (p >= operands.Length) break;
            }
        resultIndex = rIndex;
    }
}
