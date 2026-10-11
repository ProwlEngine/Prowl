// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Globalization;
using System.Text;

namespace Prowl.Echo;

/// <summary>
/// IFileFormat wrapper around the Echo text format.
/// Preserves all EchoObject types exactly during roundtrip.
/// </summary>
public sealed class EchoTextFormat : IFileFormat
{
    public static readonly EchoTextFormat Instance = new();

    public void WriteTo(EchoObject tag, Stream stream)
    {
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
        writer.Write(Write(tag));
    }

    public EchoObject ReadFrom(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: true);
        return Read(reader.ReadToEnd());
    }

    // Public static methods (used by EchoObject.cs and other callers)

    public static void WriteToFile(EchoObject tag, FileInfo file)
    {
        File.WriteAllText(file.FullName, Write(tag));
    }

    public static string Write(EchoObject prop)
    {
        using var writer = new StringWriter();
        WriteTag(prop, writer, 0);
        return writer.ToString();
    }

    public static EchoObject ReadFromFile(FileInfo file)
    {
        return Read(File.ReadAllText(file.FullName));
    }

    public static EchoObject Read(string input)
    {
        StringTagTokenizer parser = new(input);

        if (!parser.MoveNext())
            throw new InvalidDataException("Empty input");

        try
        {
            return ReadTag(parser);
        }
        catch (Exception e)
        {
            e.Data[nameof(parser.TokenPosition)] = parser.TokenPosition;
            throw;
        }
    }

    // Writing:

    private static readonly string[] SpecialKeys = { "$id", "$type", "$dependencies" };
    private static readonly string[] Indents = new string[64];

    private static string Indent(int level)
    {
        if (level >= Indents.Length)
            return new string(' ', level * 2);
        return Indents[level] ??= new string(' ', level * 2);
    }

    private static void WriteTag(EchoObject prop, TextWriter writer, int indentLevel)
    {
        switch (prop.TagType)
        {
            case EchoType.Null:
                writer.Write("NULL");
                break;
            case EchoType.Byte:
                WriteNumber(writer, (long)prop.ByteValue, writer.FormatProvider);
                writer.Write('B');
                break;
            case EchoType.sByte:
                WriteNumber(writer, (long)prop.sByteValue, writer.FormatProvider);
                writer.Write('N');
                break;
            case EchoType.Short:
                WriteNumber(writer, (long)prop.ShortValue, writer.FormatProvider);
                writer.Write('S');
                break;
            case EchoType.Int:
                WriteNumber(writer, (long)prop.IntValue, writer.FormatProvider);
                break;
            case EchoType.Long:
                WriteNumber(writer, prop.LongValue, writer.FormatProvider);
                writer.Write('L');
                break;
            case EchoType.UShort:
                WriteNumber(writer, (long)prop.UShortValue, writer.FormatProvider);
                writer.Write('V');
                break;
            case EchoType.UInt:
                WriteNumber(writer, (ulong)prop.UIntValue, writer.FormatProvider);
                writer.Write('U');
                break;
            case EchoType.ULong:
                WriteNumber(writer, prop.ULongValue, writer.FormatProvider);
                writer.Write('C');
                break;
            case EchoType.Float:
                Span<char> floatBuffer = stackalloc char[32];
                prop.FloatValue.TryFormat(floatBuffer, out int floatLength, default, CultureInfo.InvariantCulture);
                writer.Write(floatBuffer.Slice(0, floatLength));
                writer.Write('F');
                break;
            case EchoType.Double:
                Span<char> doubleBuffer = stackalloc char[32];
                prop.DoubleValue.TryFormat(doubleBuffer, out int doubleLength, default, CultureInfo.InvariantCulture);
                writer.Write(doubleBuffer.Slice(0, doubleLength));
                writer.Write('D');
                break;
            case EchoType.Decimal:
                writer.Write(prop.DecimalValue.ToString(CultureInfo.InvariantCulture));
                writer.Write('M');
                break;
            case EchoType.String:
                WriteString(writer, prop.StringValue);
                break;
            case EchoType.ByteArray:
                WriteByteArray(writer, prop.ByteArrayValue);
                break;
            case EchoType.Bool:
                writer.Write(prop.BoolValue ? "true" : "false");
                break;
            case EchoType.List:
                string indent = Indent(indentLevel);
                writer.WriteLine("[");
                var list = prop.List;
                for (int i = 0; i < list.Count; i++)
                {
                    writer.Write(indent);
                    writer.Write("  ");
                    WriteTag(list[i], writer, indentLevel + 1);
                    if (i < list.Count - 1)
                    {
                        writer.Write(",");
                        writer.WriteLine();
                    }
                }
                writer.WriteLine();
                writer.Write(indent);
                writer.Write("]");
                break;
            case EchoType.Compound:
                WriteCompound(writer, prop.Tags, indentLevel);
                break;
        }
    }

    // Matches TextWriter.Write(long), which formats with the writer's own provider.
    private static void WriteNumber(TextWriter writer, long value, IFormatProvider provider)
    {
        Span<char> buffer = stackalloc char[24];
        value.TryFormat(buffer, out int length, default, provider);
        writer.Write(buffer.Slice(0, length));
    }

    private static void WriteNumber(TextWriter writer, ulong value, IFormatProvider provider)
    {
        Span<char> buffer = stackalloc char[24];
        value.TryFormat(buffer, out int length, default, provider);
        writer.Write(buffer.Slice(0, length));
    }

    private static void WriteString(TextWriter writer, string value)
    {
        writer.Write('"');
        int start = 0;
        for (int i = 0; i < value.Length; i++)
        {
            string? escape = value[i] switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                _ => null
            };
            if (escape == null)
                continue;

            writer.Write(value.AsSpan(start, i - start));
            writer.Write(escape);
            start = i + 1;
        }
        writer.Write(value.AsSpan(start));
        writer.Write('"');
    }

    private static void WriteByteArray(TextWriter writer, byte[] value)
    {
        writer.Write("[B;");
        writer.Write(Convert.ToBase64String(value));
        writer.Write(']');
    }

    private static void WriteCompound(TextWriter writer, Dictionary<string, EchoObject> dict, int indentLevel)
    {
        string indent = Indent(indentLevel);

        writer.WriteLine("{");

        bool wroteAny = false;

        // Write "$id", "$type", "$dependencies" keys first, if they exist
        foreach (var key in SpecialKeys)
        {
            if (!dict.TryGetValue(key, out var special))
                continue;

            if (wroteAny)
            {
                writer.Write(",");
                writer.WriteLine();
            }
            WriteCompoundElement(key, special, writer, indentLevel, indent);
            wroteAny = true;
        }

        // Write the remaining key-value pairs
        foreach (var kvp in dict)
        {
            if (kvp.Key == "$id" || kvp.Key == "$type" || kvp.Key == "$dependencies")
                continue;

            if (wroteAny)
            {
                writer.Write(",");
                writer.WriteLine();
            }
            WriteCompoundElement(kvp.Key, kvp.Value, writer, indentLevel, indent);
            wroteAny = true;
        }

        writer.WriteLine();
        writer.Write(indent);
        writer.Write("}");
    }

    private static void WriteCompoundElement(string key, EchoObject value, TextWriter writer, int indentLevel, string indent)
    {
        writer.Write(indent);
        writer.Write("  ");
        WriteString(writer, key);
        writer.Write(": ");
        WriteTag(value, writer, indentLevel + 1);
    }

    // Reading:

    private static EchoObject ReadTag(StringTagTokenizer parser)
    {
        return parser.TokenType switch
        {
            TextTokenType.BeginCompound => ReadCompoundTag(parser),
            TextTokenType.BeginList => ReadListTag(parser),
            TextTokenType.BeginArray => ReadArrayTag(parser),
            TextTokenType.Value => ReadValueTag(parser),
            _ => throw new InvalidDataException(
                $"Invalid token \"{parser.Token.ToString()}\" found while reading a property at position {parser.TokenPosition}")
        };
    }

    private static EchoObject ReadCompoundTag(StringTagTokenizer parser)
    {
        var startPosition = parser.TokenPosition;

        var dict = new Dictionary<string, EchoObject>();
        while (parser.MoveNext())
        {
            switch (parser.TokenType)
            {
                case TextTokenType.EndCompound:
                    return new EchoObject(EchoType.Compound, dict);
                case TextTokenType.Separator:
                    continue;
                case TextTokenType.Value:
                    var name = parser.Token[0] is '"' or '\'' ? parser.ParseQuotedStringValue() : new string(parser.Token);

                    if (!parser.MoveNext())
                        throw new InvalidDataException($"End of input reached while reading a compound property starting at position {startPosition}");

                    if (parser.TokenType != TextTokenType.NameValueSeparator)
                        throw new InvalidDataException($"Invalid token \"{parser.Token.ToString()}\" found while reading a compound property at position {parser.TokenPosition}");

                    if (!parser.MoveNext())
                        throw new InvalidDataException($"End of input reached while reading a compound property starting at position {startPosition}");

                    var value = ReadTag(parser);

                    dict.Add(name, value);

                    continue;
                default:
                    throw new InvalidDataException($"Invalid token \"{parser.Token.ToString()}\" found while reading a compound property at position {parser.TokenPosition}");
            }
        }

        throw new InvalidDataException($"End of input reached while reading a compound property starting at position {startPosition}");
    }

    private static EchoObject ReadListTag(StringTagTokenizer parser)
    {
        var startPosition = parser.TokenPosition;

        var items = new List<EchoObject>();

        while (parser.MoveNext())
        {
            switch (parser.TokenType)
            {
                case TextTokenType.EndList:
                    return new EchoObject(EchoType.List, items);
                case TextTokenType.Separator:
                    continue;
            }

            var tag = ReadTag(parser);

            items.Add(tag);
        }

        throw new InvalidDataException($"End of input reached while reading a list property starting at position {startPosition}");
    }

    private static EchoObject ReadArrayTag(StringTagTokenizer parser)
    {
        return parser.Token[1] switch
        {
            'B' => ReadByteArrayTag(parser),
            _ => throw new InvalidDataException($"Invalid array type \"{parser.Token[1]}\" at position {parser.TokenPosition}")
        };
    }

    private static EchoObject ReadByteArrayTag(StringTagTokenizer parser)
    {
        var startPosition = parser.TokenPosition;

        byte[] arr = null;
        while (parser.MoveNext())
        {
            switch (parser.TokenType)
            {
                case TextTokenType.EndList:
                    return new EchoObject(arr!);
                case TextTokenType.Separator:
                    continue;
                case TextTokenType.Value:
                    arr = Convert.FromBase64String(parser.Token.ToString());
                    continue;
                default:
                    throw new InvalidDataException($"Invalid token \"{parser.Token.ToString()}\" found while reading a byte array at position {parser.TokenPosition}");
            }
        }

        throw new InvalidDataException($"End of input reached while reading a byte array starting at position {startPosition}");
    }

    private static EchoObject ReadValueTag(StringTagTokenizer parser)
    {
        // null
        if (parser.Token.SequenceEqual("NULL")) return new EchoObject(EchoType.Null, null);

        // boolean
        if (parser.Token.SequenceEqual("false")) return new EchoObject(false);
        if (parser.Token.SequenceEqual("true")) return new EchoObject(true);

        // string
        if (parser.Token[0] is '"' or '\'')
            return new EchoObject(parser.ParseQuotedStringValue());

        // NaN/Infinity are written as a letter literal plus an F/D suffix; route them to the number path
        // before the letter-first string fallback would misread them as a string.
        if (parser.Token[^1] is 'F' or 'D')
        {
            var body = parser.Token[..^1];
            if (body.SequenceEqual("NaN") || body.SequenceEqual("Infinity") || body.SequenceEqual("-Infinity"))
                return ReadNumberTag(parser);
        }

        if (char.IsLetter(parser.Token[0]))
            return new EchoObject(new string(parser.Token));

        // number
        if (parser.Token[0] >= '0' && parser.Token[0] <= '9' || parser.Token[0] is '+' or '-' or '.')
            return ReadNumberTag(parser);

        throw new InvalidDataException($"Invalid value \"{parser.Token.ToString()}\" found while reading a tag at position {parser.TokenPosition}");
    }

    // Same number styles Convert.ToInt32(string, provider) and friends use.
    private static EchoObject ReadNumberTag(StringTagTokenizer parser)
    {
        const NumberStyles Integer = NumberStyles.Integer;
        const NumberStyles Float = NumberStyles.Float | NumberStyles.AllowThousands;
        var invariant = CultureInfo.InvariantCulture;
        var token = parser.Token;
        var body = token[..^1];

        return token[^1] switch
        {
            'B' => new EchoObject(byte.Parse(body, Integer, invariant)),
            'N' => new EchoObject(sbyte.Parse(body, Integer, invariant)),
            'S' => new EchoObject(short.Parse(body, Integer, invariant)),
            'I' => new EchoObject(int.Parse(body, Integer, invariant)),
            'L' => new EchoObject(long.Parse(body, Integer, invariant)),
            'V' => new EchoObject(ushort.Parse(body, Integer, invariant)),
            'U' => new EchoObject(uint.Parse(body, Integer, invariant)),
            'C' => new EchoObject(ulong.Parse(body, Integer, invariant)),
            'F' => new EchoObject(float.Parse(body, Float, invariant)),
            'D' => new EchoObject(double.Parse(body, Float, invariant)),
            'M' => new EchoObject(decimal.Parse(body, NumberStyles.Number, invariant)),
            >= '0' and <= '9' => new EchoObject(int.Parse(token, Integer, invariant)),
            _ => throw new InvalidDataException($"Invalid number type indicator found while reading a number \"{parser.Token.ToString()}\" at position {parser.TokenPosition}")
        };
    }

    // Nested types

    internal enum TextTokenType
    {
        None,
        BeginCompound,
        EndCompound,
        BeginList,
        BeginArray,
        EndList,
        Separator,
        NameValueSeparator,
        Value
    }

    internal sealed class StringTagTokenizer
    {
        private readonly string _input;
        private int _tokenLength;

        public StringTagTokenizer(string input) => _input = input;

        public TextTokenType TokenType { get; private set; }
        public ReadOnlySpan<char> Token => _input.AsSpan(TokenPosition, _tokenLength);
        public int TokenPosition { get; private set; }
        public int InputPosition { get; private set; }

        public bool MoveNext()
        {
            string input = _input;
            int pos = InputPosition;
            while (pos < input.Length && char.IsWhiteSpace(input[pos]))
                pos++;

            if (pos >= input.Length)
            {
                InputPosition = pos;
                TokenPosition = input.Length;
                TokenType = TextTokenType.None;
                _tokenLength = 0;
                return false;
            }

            TokenPosition = pos;
            char first = input[pos];
            switch (first)
            {
                case '{': return Single(TextTokenType.BeginCompound);
                case '}': return Single(TextTokenType.EndCompound);
                case ']': return Single(TextTokenType.EndList);
                case ',': return Single(TextTokenType.Separator);
                case ':': return Single(TextTokenType.NameValueSeparator);
                case '[':
                    if (pos + 2 < input.Length && input[pos + 2] == ';')
                        return Emit(TextTokenType.BeginArray, pos + 3);
                    return Single(TextTokenType.BeginList);
                case '"':
                case '\'':
                    return ReadQuoted(first);
            }

            pos++;
            while (pos < input.Length && !IsDelimiter(input[pos]))
                pos++;
            return Emit(TextTokenType.Value, pos);
        }

        private static bool IsDelimiter(char c) =>
            c is '{' or '}' or ',' or ';' or ':' or '[' or ']' or '"' or '\'' || char.IsWhiteSpace(c);

        private bool Single(TextTokenType type) => Emit(type, TokenPosition + 1);

        private bool Emit(TextTokenType type, int end)
        {
            TokenType = type;
            _tokenLength = end - TokenPosition;
            InputPosition = end;
            return true;
        }

        private bool ReadQuoted(char quote)
        {
            string input = _input;
            int pos = TokenPosition + 1;
            while (pos < input.Length)
            {
                char c = input[pos];
                if (c == quote)
                    return Emit(TextTokenType.Value, pos + 1);

                if (c == '\\')
                {
                    if (pos + 1 >= input.Length)
                        throw new InvalidDataException($"Reached end of input while reading an escape sequence in a quoted string starting at position {TokenPosition}.");
                    pos++;
                }
                pos++;
            }

            throw new InvalidDataException($"Reached end of input while reading a quoted string starting at position {TokenPosition}.");
        }

        public string ParseQuotedStringValue()
        {
            var token = Token;
            var quote = token[0];
            if (token[^1] != quote)
                throw new InvalidDataException($"Missing ending quote from string \"{token.ToString()}\" at position {TokenPosition}");

            var inner = token[1..^1];
            if (inner.IndexOf('\\') < 0 && inner.IndexOf(quote) < 0)
                return inner.ToString();

            var s = new char[inner.Length];
            var len = 0;
            var rest = inner;
            while (!rest.IsEmpty)
            {
                if (rest[0] == quote)
                    throw new InvalidDataException($"Unescaped quote character in string \"{token.ToString()}\" at position {TokenPosition}");

                if (rest[0] == '\\')
                {
                    if (rest.Length < 2)
                        throw new EndOfStreamException();

                    switch (rest[1])
                    {
                        case '\\': s[len++] = '\\'; break;
                        case 't': s[len++] = '\t'; break;
                        case 'n': s[len++] = '\n'; break;
                        case 'r': s[len++] = '\r'; break;
                        default:
                            if (rest[1] == quote)
                                s[len++] = quote;
                            else
                                throw new InvalidDataException($"Invalid escape sequence in string \"{token.ToString()}\" at position {TokenPosition}");
                            break;
                    }

                    rest = rest[2..];
                    continue;
                }

                s[len++] = rest[0];
                rest = rest[1..];
            }

            return new string(s, 0, len);
        }
    }
}
