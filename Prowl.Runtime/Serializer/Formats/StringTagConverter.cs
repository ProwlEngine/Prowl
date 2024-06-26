using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Prowl.Runtime
{
    public static class StringTagConverter
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
            TextMemoryParser parser = new(input.ToCharArray());

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

        private static SerializedProperty ReadTag(TextMemoryParser parser)
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

        private static SerializedProperty ReadCompoundTag(TextMemoryParser parser)
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
                        var name = parser.Token[0] is '"' or '\'' ? ParseQuotedStringValue(parser) : new string(parser.Token);

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

        private static SerializedProperty ReadListTag(TextMemoryParser parser)
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

        private static SerializedProperty ReadArrayTag(TextMemoryParser parser)
        {
            return parser.Token[1] switch {
                'B' => ReadByteArrayTag(parser),
                _ => throw new InvalidDataException($"Invalid array type \"{parser.Token[1]}\" at position {parser.TokenPosition}")
            };
        }

        private static SerializedProperty ReadByteArrayTag(TextMemoryParser parser)
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

        private static SerializedProperty ReadValueTag(TextMemoryParser parser)
        {
            // null
            if (parser.Token.SequenceEqual("NULL")) return new SerializedProperty(PropertyType.Null, null);

            // boolean
            if (parser.Token.SequenceEqual("false")) return new SerializedProperty(false);
            if (parser.Token.SequenceEqual("true")) return new SerializedProperty(true);

            // string
            if (parser.Token[0] is '"' or '\'')
                return new SerializedProperty(ParseQuotedStringValue(parser));

            if (char.IsLetter(parser.Token[0]))
                return new SerializedProperty(new string(parser.Token));

            // number
            if (parser.Token[0] >= '0' && parser.Token[0] <= '9' || parser.Token[0] is '+' or '-' or '.')
                return ReadNumberTag(parser);

            throw new InvalidDataException($"Invalid value \"{parser.Token}\" found while reading a tag at position {parser.TokenPosition}");
        }

        private static SerializedProperty ReadNumberTag(TextMemoryParser parser)
        {
            static T ParsePrimitive<T>(TextMemoryParser parser) where T : unmanaged
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

        private static string ParseQuotedStringValue(TextMemoryParser parser)
        {
            var token = parser.Token;

            var s = new char[token.Length];
            var len = 0;

            var quote = parser.Token[0];
            if (token[^1] != quote)
                throw new InvalidDataException($"Missing ending quote from string \"{token}\" at position {parser.TokenPosition}");

            var original = token;
            token = token[1..^1];

            while (!token.IsEmpty)
            {
                if (token[0] == quote)
                    throw new InvalidDataException($"Unescaped quote character in string \"{original}\" at position {parser.TokenPosition}");

                if (token[0] == '\\')
                {
                    if (token.Length < 2)
                        throw new EndOfStreamException();

                    switch (token[1])
                    {
                        case '\\':
                            s[len++] = '\\';
                            break;
                        case 't':
                            s[len++] = '\t';
                            break;
                        case 'n':
                            s[len++] = '\n';
                            break;
                        case 'r':
                            s[len++] = '\r';
                            break;
                        default:
                            if (token[1] == quote)
                                s[len++] = quote;
                            else
                                throw new InvalidDataException($"Invalid escape sequence in string \"{original}\" at position {parser.TokenPosition}");
                            break;
                    }

                    token = token[2..];
                    continue;
                }

                s[len++] = token[0];
                token = token[1..];
            }

            var result = new string(s[..len]);
            return result;
        }



        public class TextMemoryParser
        {
            public TextMemoryParser(ReadOnlyMemory<char> input)
            {
                Input = input;
                TokenType = TextTokenType.None;
                TokenPosition = 0;
                InputPosition = 0;
                TokenMemory = ReadOnlyMemory<char>.Empty;
            }

            public ReadOnlyMemory<char> TokenMemory { get; private set; }
            public ReadOnlyMemory<char> Input { get; }
            public TextTokenType TokenType { get; private set; }
            public ReadOnlySpan<char> Token => TokenMemory.Span;
            public int TokenPosition { get; private set; }
            public int InputPosition { get; private set; }

            private static bool IsSymbol(char c) => c is '{' or '}' or ',' or ';' or ':' or '[' or ']';

            public bool MoveNext()
            {
                while (InputPosition < Input.Length && char.IsWhiteSpace(Input.Span[InputPosition]))
                    InputPosition++;

                if (InputPosition >= Input.Length)
                {
                    TokenPosition = Input.Length;
                    TokenType = TextTokenType.None;
                    TokenMemory = ReadOnlyMemory<char>.Empty;
                    return false;
                }

                TokenPosition = InputPosition;

                var firstChar = Input.Span[InputPosition];

                switch (firstChar)
                {
                    case '{':
                        TokenType = TextTokenType.BeginCompound;
                        TokenMemory = Input.Slice(TokenPosition, 1);
                        InputPosition++;
                        return true;
                    case '}':
                        TokenType = TextTokenType.EndCompound;
                        TokenMemory = Input.Slice(TokenPosition, 1);
                        InputPosition++;
                        return true;
                    case '[':
                        if (InputPosition + 2 < Input.Length && Input.Span[InputPosition + 2] == ';')
                        {
                            TokenMemory = Input.Slice(TokenPosition, 3);
                            TokenType = TextTokenType.BeginArray;
                            InputPosition += 3;
                            return true;
                        }

                        TokenType = TextTokenType.BeginList;
                        TokenMemory = Input.Slice(TokenPosition, 1);
                        InputPosition++;
                        return true;
                    case ']':
                        TokenType = TextTokenType.EndList;
                        TokenMemory = Input.Slice(TokenPosition, 1);
                        InputPosition++;
                        return true;
                    case ',':
                        TokenType = TextTokenType.Separator;
                        TokenMemory = Input.Slice(TokenPosition, 1);
                        InputPosition++;
                        return true;
                    case ':':
                        TokenType = TextTokenType.NameValueSeparator;
                        TokenMemory = Input.Slice(TokenPosition, 1);
                        InputPosition++;
                        return true;
                    case '"' or '\'':
                    {
                        TokenType = TextTokenType.Value;
                        InputPosition++;
                        while (InputPosition < Input.Length)
                        {
                            if (Input.Span[InputPosition] == firstChar)
                            {
                                InputPosition++;
                                TokenMemory = Input.Slice(TokenPosition, InputPosition - TokenPosition);
                                return true;
                            }

                            if (Input.Span[InputPosition] == '\\')
                            {
                                if (InputPosition + 1 >= Input.Length)
                                    throw new InvalidDataException($"Reached end of input while reading an escape sequence in a quoted string starting at position {TokenPosition}.");

                                InputPosition++;
                            }

                            InputPosition++;
                        }

                        throw new InvalidDataException($"Reached end of input while reading a quoted string starting at position {TokenPosition}.");

                    }
                    default:
                    {
                        TokenType = TextTokenType.Value;
                        InputPosition++;
                        while (InputPosition < Input.Length
                               && !IsSymbol(Input.Span[InputPosition])
                               && !char.IsWhiteSpace(Input.Span[InputPosition])
                               && Input.Span[InputPosition] != '"'
                               && Input.Span[InputPosition] != '\'')
                            InputPosition++;

                        TokenMemory = Input.Slice(TokenPosition, InputPosition - TokenPosition);
                        return true;
                    }
                }
            }




        }
    }
}
