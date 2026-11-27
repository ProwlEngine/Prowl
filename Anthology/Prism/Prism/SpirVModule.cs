using System.Buffers.Binary;

namespace Prowl.Prism;

/// <summary>
/// An in-memory SPIR-V module: the 32-bit word stream plus parsed header fields.
/// This is the output of the compiler and the unit of comparison against the
/// reference corpus. The header parsing here is fully implemented; it is what the
/// reference tests exercise today while code generation is still being built.
/// </summary>
public sealed class SpirVModule
{
    /// <summary>The SPIR-V magic number, first word of every valid module.</summary>
    public const uint MagicNumber = 0x07230203;

    readonly uint[] _words;

    public SpirVModule(uint[] words)
    {
        ArgumentNullException.ThrowIfNull(words);
        if (words.Length < 5)
            throw new ArgumentException("A SPIR-V module has at least a 5-word header.", nameof(words));
        if (words[0] != MagicNumber)
            throw new ArgumentException(
                $"Not a SPIR-V module: first word 0x{words[0]:X8} is not the magic number 0x{MagicNumber:X8}.",
                nameof(words));
        _words = words;
    }

    /// <summary>The raw 32-bit word stream, including the header.</summary>
    public ReadOnlySpan<uint> Words => _words;

    /// <summary>Word count of the module (header + instructions).</summary>
    public int WordCount => _words.Length;

    /// <summary>Encoded version word (byte layout 0x00MMmm00).</summary>
    public uint VersionWord => _words[1];

    /// <summary>SPIR-V major version, e.g. 1 for SPIR-V 1.x.</summary>
    public int VersionMajor => (int)((_words[1] >> 16) & 0xFF);

    /// <summary>SPIR-V minor version, e.g. 3 for SPIR-V 1.3.</summary>
    public int VersionMinor => (int)((_words[1] >> 8) & 0xFF);

    /// <summary>Generator magic number identifying the tool that produced the module.</summary>
    public uint GeneratorMagic => _words[2];

    /// <summary>The id bound: all result ids in the module are strictly less than this.</summary>
    public uint Bound => _words[3];

    /// <summary>Reserved instruction schema word (currently always 0).</summary>
    public uint Schema => _words[4];

    /// <summary>Copies the module to a little-endian byte array (SPIR-V is little-endian on disk).</summary>
    public byte[] ToBytes()
    {
        var bytes = new byte[_words.Length * sizeof(uint)];
        for (int i = 0; i < _words.Length; i++)
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(i * sizeof(uint)), _words[i]);
        return bytes;
    }

    /// <summary>Parses a module from a little-endian byte buffer.</summary>
    public static SpirVModule FromBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length % sizeof(uint) != 0)
            throw new ArgumentException("SPIR-V byte length must be a multiple of 4.", nameof(bytes));

        var words = new uint[bytes.Length / sizeof(uint)];

        // A valid module starts with the magic number; its byte order tells us the
        // endianness the buffer was written in so we can decode either way.
        bool bigEndian = words.Length > 0
            && BinaryPrimitives.ReadUInt32LittleEndian(bytes) != MagicNumber
            && BinaryPrimitives.ReadUInt32BigEndian(bytes) == MagicNumber;

        for (int i = 0; i < words.Length; i++)
        {
            var word = bytes.Slice(i * sizeof(uint), sizeof(uint));
            words[i] = bigEndian
                ? BinaryPrimitives.ReadUInt32BigEndian(word)
                : BinaryPrimitives.ReadUInt32LittleEndian(word);
        }
        return new SpirVModule(words);
    }

    /// <summary>Loads a <c>.spv</c> file from disk.</summary>
    public static SpirVModule Load(string path) => FromBytes(File.ReadAllBytes(path));

    /// <summary>Writes the module to a <c>.spv</c> file on disk.</summary>
    public void Save(string path) => File.WriteAllBytes(path, ToBytes());

    public override string ToString() =>
        $"SpirVModule(v{VersionMajor}.{VersionMinor}, bound={Bound}, {WordCount} words)";
}
