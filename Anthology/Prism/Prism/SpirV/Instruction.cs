using System.Runtime.CompilerServices;
using System.Text;

namespace Prowl.Prism.SpirV;

/// <summary>
/// A single SPIR-V instruction. Word layout is
/// <c>[wordCount &lt;&lt; 16 | opcode] [resultType?] [resultId?] [operands...]</c>;
/// a zero <see cref="TypeId"/> or <see cref="ResultId"/> means that slot is absent
/// (ids are always &gt;= 1).
/// </summary>
public sealed class Instruction(Op op, uint typeId = 0, uint resultId = 0)
{
    public Op Op { get; private set; } = op;
    public uint TypeId { get; private set; } = typeId;
    public uint ResultId { get; private set; } = resultId;
    public List<uint> Operands { get; } = [];

    // Re-initialize a pooled instruction for reuse across compiles (see InstrPool), clearing operands.
    public Instruction Reset(Op op, uint typeId = 0, uint resultId = 0)
    {
        Op = op; TypeId = typeId; ResultId = resultId; Operands.Clear();
        return this;
    }

    public Instruction Add(uint word) { Operands.Add(word); return this; }
    // Fixed-arity overloads win overload resolution over the params one, so the common 2..4 operand calls
    // do not allocate an array.
    public Instruction Add(uint a, uint b) { Operands.Add(a); Operands.Add(b); return this; }
    public Instruction Add(uint a, uint b, uint c) { Operands.Add(a); Operands.Add(b); Operands.Add(c); return this; }
    public Instruction Add(uint a, uint b, uint c, uint d) { Operands.Add(a); Operands.Add(b); Operands.Add(c); Operands.Add(d); return this; }
    public Instruction Add(params uint[] words) { Operands.AddRange(words); return this; }
    // Reinterpret the enum's 4 bytes as a uint rather than Convert.ToUInt32, which would box it (this is on
    // the hot path: a storage class / decoration / dim operand on nearly every type and variable).
    public Instruction AddEnum<T>(T value) where T : Enum { Operands.Add(Unsafe.As<T, uint>(ref value)); return this; }
    public Instruction AddString(string text)
    {
        // A SPIR-V literal string: UTF-8, null-terminated, packed little-endian into words and zero-padded
        // to a word. Encoded directly into the operand list -- no intermediate byte[] or yield iterator.
        int byteCount = Encoding.UTF8.GetByteCount(text);
        Span<byte> bytes = byteCount <= 1024 ? stackalloc byte[byteCount] : new byte[byteCount];
        Encoding.UTF8.GetBytes(text, bytes);
        int wordCount = byteCount / 4 + 1;   // +1 guarantees at least one trailing null byte
        for (int w = 0; w < wordCount; w++)
        {
            uint word = 0;
            for (int b = 0; b < 4; b++)
            {
                int index = w * 4 + b;
                if (index < byteCount) word |= (uint)bytes[index] << (b * 8);
            }
            Operands.Add(word);
        }
        return this;
    }

    public int WordCount => 1 + (TypeId != 0 ? 1 : 0) + (ResultId != 0 ? 1 : 0) + Operands.Count;

    public void WriteTo(List<uint> words)
    {
        // The word count occupies the high 16 bits of the opcode word, so an instruction longer than that
        // cannot be encoded. Emitting it truncated would produce a module that parses off the rails partway
        // through rather than failing here (a huge constant composite or literal string can reach this).
        int count = WordCount;
        if (count > 0xFFFF)
            throw new Prowl.Prism.Glsl.GlslParseException($"instruction {Op} needs {count} words, exceeding the SPIR-V limit of 65535");
        words.Add(((uint)count << 16) | (uint)Op);
        if (TypeId != 0) words.Add(TypeId);
        if (ResultId != 0) words.Add(ResultId);
        words.AddRange(Operands);
    }

}

/// <summary>
/// A per-thread bump pool of <see cref="Instruction"/> objects, mirroring glslang's TPoolAllocator: a
/// compile reuses the objects (and their operand lists) allocated by the previous compile on this thread,
/// so after warm-up the front-end allocates zero instructions. <see cref="Reset"/> is called at the start
/// of every compile (nothing is individually freed; the used-index simply rewinds). Thread-static, so
/// concurrent compiles on different threads never share.
/// </summary>
public static class InstrPool
{
    [ThreadStatic] static List<Instruction>? _all;
    [ThreadStatic] static int _used;
    [ThreadStatic] static int _bypass;

    /// <summary>
    /// Suspends pooling on this thread while a second, concurrently-live compile runs. The pooled
    /// instructions belong to the compile that claimed them; rewinding or handing them out again would
    /// overwrite instructions that compile still references.
    /// </summary>
    public static void PushBypass() => _bypass++;
    public static void PopBypass() => _bypass--;

    public static void Reset() { if (_bypass == 0) _used = 0; }

    public static Instruction Rent(Op op, uint typeId = 0, uint resultId = 0)
    {
        if (_bypass > 0) return new Instruction(op, typeId, resultId);
        var all = _all ??= [];
        if (_used < all.Count) return all[_used++].Reset(op, typeId, resultId);
        var inst = new Instruction(op, typeId, resultId);
        all.Add(inst);
        _used++;
        return inst;
    }
}

/// <summary>Reads a serialized module back into its instruction stream (opcode + operand words).</summary>
public static class SpirVReader
{
    public static IEnumerable<(Op Op, uint[] Operands)> Read(uint[] words)
    {
        int i = 5;   // skip the 5-word header
        while (i < words.Length)
        {
            uint first = words[i];
            int count = (int)(first >> 16);
            var op = (Op)(first & 0xFFFF);
            yield return (op, words[(i + 1)..(i + count)]);
            i += count;
        }
    }
}
