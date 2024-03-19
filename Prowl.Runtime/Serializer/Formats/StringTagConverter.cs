using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Prowl.Runtime
{
    public static class StringTagConverter
    {
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
            TextNbtMemoryParser parser = new(input.ToCharArray());

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
                    writer.Write(prop.FloatValue);
                    writer.Write('F');
                    break;
                case PropertyType.Double:
                    writer.Write(prop.DoubleValue);
                    writer.Write('D');
                    break;
                case PropertyType.Decimal:
                    writer.Write(prop.DecimalValue);
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
            for (int i = 0; i < value.Length; i++)
                writer.Write(value[i]);
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

            // Write the remaining key-value pairs
            var skipNextComma = true;
            foreach (var kvp in dict)
            {
                if (kvp.Key == "$id" || kvp.Key == "$type")
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





        public enum TextNbtTokenType
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

        private static SerializedProperty ReadTag(TextNbtMemoryParser parser)
        {
            return parser.TokenType switch {
                TextNbtTokenType.BeginCompound => ReadCompoundTag(parser),
                TextNbtTokenType.BeginList => ReadListTag(parser),
                TextNbtTokenType.BeginArray => ReadArrayTag(parser),
                TextNbtTokenType.Value => ReadValueTag(parser),
                _ => throw new InvalidDataException(
                    $"Invalid token \"{parser.Token}\" found while reading a property at position {parser.TokenPosition}")
            };
        }

        private static SerializedProperty ReadCompoundTag(TextNbtMemoryParser parser)
        {
            var startPosition = parser.TokenPosition;

            var dict = new Dictionary<string, SerializedProperty>();
            while (parser.MoveNext())
            {
                switch (parser.TokenType)
                {
                    case TextNbtTokenType.EndCompound:
                        return new SerializedProperty(PropertyType.Compound, dict);
                    case TextNbtTokenType.Separator:
                        continue;
                    case TextNbtTokenType.Value:
                        var name = parser.Token[0] is '"' or '\'' ? ParseQuotedStringValue(parser) : new string(parser.Token);

                        if (!parser.MoveNext())
                            throw new InvalidDataException($"End of input reached while reading a compound property starting at position {startPosition}");

                        if (parser.TokenType != TextNbtTokenType.NameValueSeparator)
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

        private static SerializedProperty ReadListTag(TextNbtMemoryParser parser)
        {
            var startPosition = parser.TokenPosition;

            var items = new List<SerializedProperty>();

            while (parser.MoveNext())
            {
                switch (parser.TokenType)
                {
                    case TextNbtTokenType.EndList:
                        return new SerializedProperty(PropertyType.List, items);
                    case TextNbtTokenType.Separator:
                        continue;
                }

                var pos = parser.TokenPosition;
                var tag = ReadTag(parser);

                items.Add(tag);
            }

            throw new InvalidDataException($"End of input reached while reading a list property starting at position {startPosition}");
        }

        private static SerializedProperty ReadArrayTag(TextNbtMemoryParser parser)
        {
            return parser.Token[1] switch {
                'B' => ReadByteArrayTag(parser),
                _ => throw new InvalidDataException($"Invalid array type \"{parser.Token[1]}\" at position {parser.TokenPosition}")
            };
        }

        private static SerializedProperty ReadByteArrayTag(TextNbtMemoryParser parser)
        {
            var startPosition = parser.TokenPosition;

            byte[] arr = null;
            while (parser.MoveNext())
            {
                switch (parser.TokenType)
                {
                    case TextNbtTokenType.EndList:
                        return new SerializedProperty(arr!);
                    case TextNbtTokenType.Separator:
                        continue;
                    case TextNbtTokenType.Value:
                        arr = new byte[parser.Token.Length];
                        for (int i = 0; i < arr.Length; i++)
                            arr[i] = byte.Parse(parser.Token[i].ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture);
                        continue;
                    default:
                        throw new InvalidDataException($"Invalid token \"{parser.Token}\" found while reading a byte array at position {parser.TokenPosition}");
                }
            }

            throw new InvalidDataException($"End of input reached while reading a byte array starting at position {startPosition}");
        }

        private static SerializedProperty ReadValueTag(TextNbtMemoryParser parser)
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

        private static SerializedProperty ReadNumberTag(TextNbtMemoryParser parser)
        {
            return parser.Token[^1] switch {
                'B' => new SerializedProperty(ParseByteValue(parser)),
                'N' => new SerializedProperty(ParseSByteValue(parser)),
                'S' => new SerializedProperty(ParseShortValue(parser)),
                'I' => new SerializedProperty(ParseIntValue(parser)),
                'L' => new SerializedProperty(ParseLongValue(parser)),
                'V' => new SerializedProperty(ParseUShortValue(parser)),
                'U' => new SerializedProperty(ParseUIntValue(parser)),
                'C' => new SerializedProperty(ParseULongValue(parser)),
                'F' => new SerializedProperty(ParseFloatValue(parser)),
                'D' => new SerializedProperty(ParseDoubleValue(parser)),
                'M' => new SerializedProperty(ParseDecimalValue(parser)),
                >= '0' and <= '9' => new SerializedProperty(ParseIntValue(parser)),
                _ => throw new InvalidDataException($"Invalid number type indicator found while reading a number \"{parser.Token}\" at position {parser.TokenPosition}")
            };
        }

        private static byte ParseByteValue(TextNbtMemoryParser parser)
        {
            if (parser.Token[^1] == 'B' && byte.TryParse(parser.Token[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                return v;

            throw new InvalidDataException($"Error parsing byte value \"{parser.Token}\" at position {parser.TokenPosition}");
        }

        private static sbyte ParseSByteValue(TextNbtMemoryParser parser)
        {
            if (parser.Token[^1] == 'N' && sbyte.TryParse(parser.Token[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                return v;

            throw new InvalidDataException($"Error parsing sbyte value \"{parser.Token}\" at position {parser.TokenPosition}");
        }

        private static short ParseShortValue(TextNbtMemoryParser parser)
        {
            if (parser.Token[^1] == 'S' && short.TryParse(parser.Token[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                return v;

            throw new InvalidDataException($"Error parsing short value \"{parser.Token}\" at position {parser.TokenPosition}");
        }

        private static int ParseIntValue(TextNbtMemoryParser parser)
        {
            if (int.TryParse(parser.Token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                return v;

            throw new InvalidDataException($"Error parsing int value \"{parser.Token}\" at position {parser.TokenPosition}");
        }

        private static long ParseLongValue(TextNbtMemoryParser parser)
        {
            if (parser.Token[^1] == 'L' && long.TryParse(parser.Token[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                return v;

            throw new InvalidDataException($"Error parsing long value \"{parser.Token}\" at position {parser.TokenPosition}");
        }

        private static ushort ParseUShortValue(TextNbtMemoryParser parser)
        {
            if (parser.Token[^1] == 'V' && ushort.TryParse(parser.Token[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                return v;

            throw new InvalidDataException($"Error parsing ushort value \"{parser.Token}\" at position {parser.TokenPosition}");
        }

        private static uint ParseUIntValue(TextNbtMemoryParser parser)
        {
            if (parser.Token[^1] == 'U' && uint.TryParse(parser.Token[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                return v;

            throw new InvalidDataException($"Error parsing uint value \"{parser.Token}\" at position {parser.TokenPosition}");
        }

        private static ulong ParseULongValue(TextNbtMemoryParser parser)
        {
            if (parser.Token[^1] == 'C' && ulong.TryParse(parser.Token[..^1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
                return v;

            throw new InvalidDataException($"Error parsing ulong value \"{parser.Token}\" at position {parser.TokenPosition}");
        }

        private static float ParseFloatValue(TextNbtMemoryParser parser)
        {
            if (parser.Token[^1] == 'F' && float.TryParse(parser.Token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return v;

            throw new InvalidDataException($"Error parsing float value \"{parser.Token}\" at position {parser.TokenPosition}");
        }

        private static double ParseDoubleValue(TextNbtMemoryParser parser)
        {
            if (parser.Token[^1] == 'D' && double.TryParse(parser.Token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return v;

            throw new InvalidDataException($"Error parsing double value \"{parser.Token}\" at position {parser.TokenPosition}");
        }

        private static decimal ParseDecimalValue(TextNbtMemoryParser parser)
        {
            if (parser.Token[^1] == 'M' && decimal.TryParse(parser.Token[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return v;

            throw new InvalidDataException($"Error parsing decimal value \"{parser.Token}\" at position {parser.TokenPosition}");
        }

        private static string ParseQuotedStringValue(TextNbtMemoryParser parser)
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



        public class TextNbtMemoryParser
        {
            public TextNbtMemoryParser(ReadOnlyMemory<char> input)
            {
                Input = input;
                TokenType = TextNbtTokenType.None;
                TokenPosition = 0;
                InputPosition = 0;
                TokenMemory = ReadOnlyMemory<char>.Empty;
            }

            public ReadOnlyMemory<char> TokenMemory { get; private set; }
            public ReadOnlyMemory<char> Input { get; }
            public TextNbtTokenType TokenType { get; private set; }
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
                    TokenType = TextNbtTokenType.None;
                    TokenMemory = ReadOnlyMemory<char>.Empty;
                    return false;
                }

                TokenPosition = InputPosition;

                var firstChar = Input.Span[InputPosition];

                switch (firstChar)
                {
                    case '{':
                        TokenType = TextNbtTokenType.BeginCompound;
                        TokenMemory = Input.Slice(TokenPosition, 1);
                        InputPosition++;
                        return true;
                    case '}':
                        TokenType = TextNbtTokenType.EndCompound;
                        TokenMemory = Input.Slice(TokenPosition, 1);
                        InputPosition++;
                        return true;
                    case '[':
                        if (InputPosition + 2 < Input.Length && Input.Span[InputPosition + 2] == ';')
                        {
                            TokenMemory = Input.Slice(TokenPosition, 3);
                            TokenType = TextNbtTokenType.BeginArray;
                            InputPosition += 3;
                            return true;
                        }

                        TokenType = TextNbtTokenType.BeginList;
                        TokenMemory = Input.Slice(TokenPosition, 1);
                        InputPosition++;
                        return true;
                    case ']':
                        TokenType = TextNbtTokenType.EndList;
                        TokenMemory = Input.Slice(TokenPosition, 1);
                        InputPosition++;
                        return true;
                    case ',':
                        TokenType = TextNbtTokenType.Separator;
                        TokenMemory = Input.Slice(TokenPosition, 1);
                        InputPosition++;
                        return true;
                    case ':':
                        TokenType = TextNbtTokenType.NameValueSeparator;
                        TokenMemory = Input.Slice(TokenPosition, 1);
                        InputPosition++;
                        return true;
                    case '"' or '\'':
                    {
                        TokenType = TextNbtTokenType.Value;
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
                        TokenType = TextNbtTokenType.Value;
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
