using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Prowl.Runtime
{
    public static partial class StringTagConverter
    {
        // Writing:

        public static void WriteToFile(SerializedProperty tag, FileInfo file)
        {
            string json = Write(tag);
            File.WriteAllText(file.FullName, json);
        }

        public static string Write(SerializedProperty prop)
        {
            using var writer = new StringWriter();
            Serialize(prop, writer, 0);
            return writer.ToString();
        }

        public static SerializedProperty ReadFromFile(FileInfo file)
        {
            string json = File.ReadAllText(file.FullName);
            return Read(json);
        }

        public static SerializedProperty Read(string input)
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

        private static void Serialize(SerializedProperty prop, TextWriter writer, int indentLevel)
        {
            var indent = new string(' ', indentLevel * 2);

            switch (prop.TagType)
            {
                case PropertyType.Null:
                    writer.Write("NULL");
                    break;
                case PropertyType.Byte:
                    writer.Write(prop.ByteValue);
                    writer.Write('B');
                    break;
                case PropertyType.sByte:
                    writer.Write(prop.sByteValue);
                    writer.Write('N');
                    break;
                case PropertyType.Short:
                    writer.Write(prop.ShortValue);
                    writer.Write('S');
                    break;
                case PropertyType.Int:
                    writer.Write(prop.IntValue);
                    break;
                case PropertyType.Long:
                    writer.Write(prop.LongValue);
                    writer.Write('L');
                    break;
                case PropertyType.UShort:
                    writer.Write(prop.UShortValue);
                    writer.Write('V');
                    break;
                case PropertyType.UInt:
                    writer.Write(prop.UIntValue);
                    writer.Write('U');
                    break;
                case PropertyType.ULong:
                    writer.Write(prop.ULongValue);
                    writer.Write('C');
                    break;
                case PropertyType.Float:
                    writer.Write(prop.FloatValue.ToString(CultureInfo.InvariantCulture));
                    writer.Write('F');
                    break;
                case PropertyType.Double:
                    writer.Write(prop.DoubleValue.ToString(CultureInfo.InvariantCulture));
                    writer.Write('D');
                    break;
                case PropertyType.Decimal:
                    writer.Write(prop.DecimalValue.ToString(CultureInfo.InvariantCulture));
                    writer.Write('M');
                    break;
                case PropertyType.String:
                    WriteString(writer, prop.StringValue);
                    break;
                case PropertyType.ByteArray:
                    WriteByteArray(writer, prop.ByteArrayValue);
                    break;
                case PropertyType.Bool:
                    writer.Write(prop.BoolValue ? "true" : "false");
                    break;
                case PropertyType.List:
                    writer.WriteLine("[");
                    var list = (List<SerializedProperty>)prop.Value!;
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
                case PropertyType.Compound:
                    WriteCompound(writer, (Dictionary<string, SerializedProperty>)prop.Value!, indentLevel);
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

        private static void WriteCompound(TextWriter writer, Dictionary<string, SerializedProperty> dict, int indentLevel)
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

        private static void WriteCompoundElement(string key, TextWriter writer, Dictionary<string, SerializedProperty> dict, int indentLevel, string indent)
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

        private static SerializedProperty ReadTag(StringTagTokenizer parser)
        {
            return parser.TokenType switch {
                TextTokenType.BeginCompound => ReadCompoundTag(parser),
                TextTokenType.BeginList => ReadListTag(parser),
                TextTokenType.BeginArray => ReadArrayTag(parser),
                TextTokenType.Value => ReadValueTag(parser),
                _ => throw new InvalidDataException(
                    $"Invalid token \"{parser.Token}\" found while reading a property at position {parser.TokenPosition}")
            };
        }

        private static SerializedProperty ReadCompoundTag(StringTagTokenizer parser)
        {
            var startPosition = parser.TokenPosition;

            var dict = new Dictionary<string, SerializedProperty>();
            while (parser.MoveNext())
            {
                switch (parser.TokenType)
                {
                    case TextTokenType.EndCompound:
                        return new SerializedProperty(PropertyType.Compound, dict);
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

        private static SerializedProperty ReadListTag(StringTagTokenizer parser)
        {
            var startPosition = parser.TokenPosition;

            var items = new List<SerializedProperty>();

            while (parser.MoveNext())
            {
                switch (parser.TokenType)
                {
                    case TextTokenType.EndList:
                        return new SerializedProperty(PropertyType.List, items);
                    case TextTokenType.Separator:
                        continue;
                }

                var tag = ReadTag(parser);

                items.Add(tag);
            }

            throw new InvalidDataException($"End of input reached while reading a list property starting at position {startPosition}");
        }

        private static SerializedProperty ReadArrayTag(StringTagTokenizer parser)
        {
            return parser.Token[1] switch {
                'B' => ReadByteArrayTag(parser),
                _ => throw new InvalidDataException($"Invalid array type \"{parser.Token[1]}\" at position {parser.TokenPosition}")
            };
        }

        private static SerializedProperty ReadByteArrayTag(StringTagTokenizer parser)
        {
            var startPosition = parser.TokenPosition;

            byte[] arr = null;
            while (parser.MoveNext())
            {
                switch (parser.TokenType)
                {
                    case TextTokenType.EndList:
                        return new SerializedProperty(arr!);
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

        private static SerializedProperty ReadValueTag(StringTagTokenizer parser)
        {
            // null
            if (parser.Token.SequenceEqual("NULL")) return new SerializedProperty(PropertyType.Null, null);

            // boolean
            if (parser.Token.SequenceEqual("false")) return new SerializedProperty(false);
            if (parser.Token.SequenceEqual("true")) return new SerializedProperty(true);

            // string
            if (parser.Token[0] is '"' or '\'')
                return new SerializedProperty(parser.ParseQuotedStringValue());

            if (char.IsLetter(parser.Token[0]))
                return new SerializedProperty(new string(parser.Token));

            // number
            if (parser.Token[0] >= '0' && parser.Token[0] <= '9' || parser.Token[0] is '+' or '-' or '.')
                return ReadNumberTag(parser);

            throw new InvalidDataException($"Invalid value \"{parser.Token}\" found while reading a tag at position {parser.TokenPosition}");
        }

        private static SerializedProperty ReadNumberTag(StringTagTokenizer parser)
        {
            static T ParsePrimitive<T>(StringTagTokenizer parser) where T : unmanaged
                => (T)Convert.ChangeType(new string(parser.Token[..^1]), typeof(T));

            return parser.Token[^1] switch {
                'B' => new SerializedProperty(ParsePrimitive<byte>(parser)),
                'N' => new SerializedProperty(ParsePrimitive<sbyte>(parser)),
                'S' => new SerializedProperty(ParsePrimitive<short>(parser)),
                'I' => new SerializedProperty(ParsePrimitive<int>(parser)),
                'L' => new SerializedProperty(ParsePrimitive<long>(parser)),
                'V' => new SerializedProperty(ParsePrimitive<ushort>(parser)),
                'U' => new SerializedProperty(ParsePrimitive<uint>(parser)),
                'C' => new SerializedProperty(ParsePrimitive<ulong>(parser)),
                'F' => new SerializedProperty(ParsePrimitive<float>(parser)),
                'D' => new SerializedProperty(ParsePrimitive<double>(parser)),
                'M' => new SerializedProperty(ParsePrimitive<decimal>(parser)),
                >= '0' and <= '9' => new SerializedProperty((int)Convert.ChangeType(new string(parser.Token), typeof(int))),
                _ => throw new InvalidDataException($"Invalid number type indicator found while reading a number \"{parser.Token}\" at position {parser.TokenPosition}")
            };
        }

        public class StringTagTokenizer
        {
            private readonly Utils.Tokenizer<TextTokenType> _tokenizer;

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

                _tokenizer = new Utils.Tokenizer<TextTokenType>(
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
                _tokenizer.InputPosition++;
                return tokenType;
            }

            private TextTokenType HandleOpenBracket()
            {
                if (_tokenizer.InputPosition + 2 < _tokenizer.Input.Length &&
                    _tokenizer.Input.Span[_tokenizer.InputPosition + 1] == ';' &&
                    _tokenizer.Input.Span[_tokenizer.InputPosition + 2] == ']')
                {
                    _tokenizer.TokenMemory = _tokenizer.Input.Slice(_tokenizer.TokenPosition, 3);
                    _tokenizer.InputPosition += 3;
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
}
