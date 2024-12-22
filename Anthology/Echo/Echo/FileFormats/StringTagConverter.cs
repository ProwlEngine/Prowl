// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Globalization;

namespace Prowl.Echo;

internal static class StringTagConverter
{
    // Writing:

    public static void WriteToFile(EchoObject tag, FileInfo file)
    {
        File.WriteAllText(file.FullName, Write(tag));
    }

    public static string Write(EchoObject prop)
    {
        using var writer = new StringWriter();
        Serialize(prop, writer, 0);
        return writer.ToString();
    }

    public static EchoObject ReadFromFile(FileInfo file)
    {
        return Read(File.ReadAllText(file.FullName));
    }

    public static EchoObject Read(string input)
    {
        StringTagTokenizer parser = new(input.ToCharArray());

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

    private static void Serialize(EchoObject prop, TextWriter writer, int indentLevel)
    {
        var indent = new string(' ', indentLevel * 2);

        switch (prop.TagType)
        {
            case EchoType.Null:
                writer.Write("NULL");
                break;
            case EchoType.Byte:
                writer.Write(prop.ByteValue);
                writer.Write('B');
                break;
            case EchoType.sByte:
                writer.Write(prop.sByteValue);
                writer.Write('N');
                break;
            case EchoType.Short:
                writer.Write(prop.ShortValue);
                writer.Write('S');
                break;
            case EchoType.Int:
                writer.Write(prop.IntValue);
                break;
            case EchoType.Long:
                writer.Write(prop.LongValue);
                writer.Write('L');
                break;
            case EchoType.UShort:
                writer.Write(prop.UShortValue);
                writer.Write('V');
                break;
            case EchoType.UInt:
                writer.Write(prop.UIntValue);
                writer.Write('U');
                break;
            case EchoType.ULong:
                writer.Write(prop.ULongValue);
                writer.Write('C');
                break;
            case EchoType.Float:
                writer.Write(prop.FloatValue.ToString(CultureInfo.InvariantCulture));
                writer.Write('F');
                break;
            case EchoType.Double:
                writer.Write(prop.DoubleValue.ToString(CultureInfo.InvariantCulture));
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
                writer.WriteLine("[");
                var list = (List<EchoObject>)prop.Value!;
                for (int i = 0; i < list.Count; i++)
                {
                    writer.Write(indent);
                    writer.Write("  ");
                    Serialize(list[i], writer, indentLevel + 1);
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
                WriteCompound(writer, (Dictionary<string, EchoObject>)prop.Value!, indentLevel);
                break;
        }
    }

    private static void WriteString(TextWriter writer, string value)
    {
        writer.Write('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    writer.Write("\\\"");
                    break;
                case '\\':
                    writer.Write("\\\\");
                    break;
                case '\n':
                    writer.Write("\\n");
                    break;
                case '\r':
                    writer.Write("\\r");
                    break;
                case '\t':
                    writer.Write("\\t");
                    break;
                default:
                    writer.Write(c);
                    break;
            }
        }
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
        var indent = new string(' ', indentLevel * 2);

        writer.WriteLine("{");

        // Write "$id" and "$type" keys first, if they exist
        if (dict.ContainsKey("$id"))
        {
            WriteCompoundElement("$id", writer, dict, indentLevel, indent);
            writer.Write(",");
            writer.WriteLine();
        }

        if (dict.ContainsKey("$type"))
        {
            WriteCompoundElement("$type", writer, dict, indentLevel, indent);
            writer.Write(",");
            writer.WriteLine();
        }

        if (dict.ContainsKey("$dependencies"))
        {
            WriteCompoundElement("$dependencies", writer, dict, indentLevel, indent);
            writer.Write(",");
            writer.WriteLine();
        }

        // Write the remaining key-value pairs
        var skipNextComma = true;
        foreach (var kvp in dict)
        {
            if (kvp.Key == "$id" || kvp.Key == "$type" || kvp.Key == "$dependencies")
                continue;

            if (!skipNextComma)
            {
                skipNextComma = false;
                writer.Write(",");
                writer.WriteLine();
            }
            skipNextComma = false;

            WriteCompoundElement(kvp.Key, writer, dict, indentLevel, indent);
        }

        writer.WriteLine();
        writer.Write(indent);
        writer.Write("}");
    }

    private static void WriteCompoundElement(string key, TextWriter writer, Dictionary<string, EchoObject> dict, int indentLevel, string indent)
    {
        writer.Write(indent);
        writer.Write("  ");
        WriteString(writer, key);
        writer.Write(": ");
        Serialize(dict[key], writer, indentLevel + 1);
    }

    // Reading:

    public enum TextTokenType
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

    private static EchoObject ReadTag(StringTagTokenizer parser)
    {
        return parser.TokenType switch
        {
            TextTokenType.BeginCompound => ReadCompoundTag(parser),
            TextTokenType.BeginList => ReadListTag(parser),
            TextTokenType.BeginArray => ReadArrayTag(parser),
            TextTokenType.Value => ReadValueTag(parser),
            _ => throw new InvalidDataException(
                $"Invalid token \"{parser.Token}\" found while reading a property at position {parser.TokenPosition}")
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
                        throw new InvalidDataException($"Invalid token \"{parser.Token}\" found while reading a compound property at position {parser.TokenPosition}");

                    if (!parser.MoveNext())
                        throw new InvalidDataException($"End of input reached while reading a compound property starting at position {startPosition}");

                    var value = ReadTag(parser);

                    dict.Add(name, value);

                    continue;
                default:
                    throw new InvalidDataException($"Invalid token \"{parser.Token}\" found while reading a compound property at position {parser.TokenPosition}");
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
                    throw new InvalidDataException($"Invalid token \"{parser.Token}\" found while reading a byte array at position {parser.TokenPosition}");
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

        if (char.IsLetter(parser.Token[0]))
            return new EchoObject(new string(parser.Token));

        // number
        if (parser.Token[0] >= '0' && parser.Token[0] <= '9' || parser.Token[0] is '+' or '-' or '.')
            return ReadNumberTag(parser);

        throw new InvalidDataException($"Invalid value \"{parser.Token}\" found while reading a tag at position {parser.TokenPosition}");
    }

    private static EchoObject ReadNumberTag(StringTagTokenizer parser)
    {
        static T ParsePrimitive<T>(StringTagTokenizer parser) where T : unmanaged
            => (T)Convert.ChangeType(new string(parser.Token[..^1]), typeof(T));

        return parser.Token[^1] switch
        {
            'B' => new EchoObject(ParsePrimitive<byte>(parser)),
            'N' => new EchoObject(ParsePrimitive<sbyte>(parser)),
            'S' => new EchoObject(ParsePrimitive<short>(parser)),
            'I' => new EchoObject(ParsePrimitive<int>(parser)),
            'L' => new EchoObject(ParsePrimitive<long>(parser)),
            'V' => new EchoObject(ParsePrimitive<ushort>(parser)),
            'U' => new EchoObject(ParsePrimitive<uint>(parser)),
            'C' => new EchoObject(ParsePrimitive<ulong>(parser)),
            'F' => new EchoObject(ParsePrimitive<float>(parser)),
            'D' => new EchoObject(ParsePrimitive<double>(parser)),
            'M' => new EchoObject(ParsePrimitive<decimal>(parser)),
            >= '0' and <= '9' => new EchoObject((int)Convert.ChangeType(new string(parser.Token), typeof(int))),
            _ => throw new InvalidDataException($"Invalid number type indicator found while reading a number \"{parser.Token}\" at position {parser.TokenPosition}")
        };
    }

    public class StringTagTokenizer
    {
        private readonly Tokenizer<TextTokenType> _tokenizer;

        public StringTagTokenizer(ReadOnlyMemory<char> input)
        {
            var symbolHandlers = new Dictionary<char, Func<TextTokenType>>
            {
                {'{', () => HandleSingleCharToken(TextTokenType.BeginCompound)},
                {'}', () => HandleSingleCharToken(TextTokenType.EndCompound)},
                {'[', () => HandleOpenBracket()},
                {']', () => HandleSingleCharToken(TextTokenType.EndList)},
                {',', () => HandleSingleCharToken(TextTokenType.Separator)},
                {':', () => HandleSingleCharToken(TextTokenType.NameValueSeparator)}
            };

            _tokenizer = new Tokenizer<TextTokenType>(
                input,
                symbolHandlers,
                c => c is '{' or '}' or ',' or ';' or ':' or '[' or ']',
                TextTokenType.Value,
                TextTokenType.None
            );
        }

        private TextTokenType HandleSingleCharToken(TextTokenType tokenType)
        {
            _tokenizer.TokenMemory = _tokenizer.Input.Slice(_tokenizer.TokenPosition, 1);
            _tokenizer.IncrementInputPosition();
            return tokenType;
        }

        private TextTokenType HandleOpenBracket()
        {
            if (_tokenizer.InputPosition + 2 < _tokenizer.Input.Length &&
                _tokenizer.Input.Span[_tokenizer.InputPosition + 2] == ';')
            {
                _tokenizer.TokenMemory = _tokenizer.Input.Slice(_tokenizer.TokenPosition, 3);
                _tokenizer.IncrementInputPosition(3);
                return TextTokenType.BeginArray;
            }

            return HandleSingleCharToken(TextTokenType.BeginList);
        }

        public bool MoveNext() => _tokenizer.MoveNext();

        public string ParseQuotedStringValue() => _tokenizer.ParseQuotedStringValue();

        public TextTokenType TokenType => _tokenizer.TokenType;
        public ReadOnlySpan<char> Token => _tokenizer.Token;
        public int TokenPosition => _tokenizer.TokenPosition;
        public int InputPosition => _tokenizer.InputPosition;
    }


}
