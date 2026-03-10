// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Globalization;
using System.Text;

namespace Prowl.Echo;

/// <summary>
/// JSON file format for EchoObject serialization. Zero external dependencies.
/// Writes clean, standard JSON. Reads both Echo-produced and generic JSON.
/// Note: Some type precision may be lost (e.g., byte/short → int, float → double, ByteArray → base64 string).
/// For exact type preservation, use EchoTextFormat or EchoBinaryFormat.
/// </summary>
public sealed class JsonFileFormat : IFileFormat
{
    public static readonly JsonFileFormat Instance = new();

    /// <summary>
    /// Whether to indent the output for readability. Default true.
    /// </summary>
    public bool Indented { get; set; } = true;

    public void WriteTo(EchoObject tag, Stream stream)
    {
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
        WriteValue(writer, tag, 0);
    }

    public EchoObject ReadFrom(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: true);
        var json = reader.ReadToEnd();
        int index = 0;
        SkipWhitespace(json, ref index);
        if (index >= json.Length)
            return new EchoObject(EchoType.Null, null);
        return ParseValue(json, ref index);
    }

    #region Writing

    private void WriteValue(TextWriter writer, EchoObject tag, int indent)
    {
        switch (tag.TagType)
        {
            case EchoType.Null:
                writer.Write("null");
                break;
            case EchoType.Bool:
                writer.Write(tag.BoolValue ? "true" : "false");
                break;
            case EchoType.Byte:
                writer.Write(tag.ByteValue);
                break;
            case EchoType.sByte:
                writer.Write(tag.sByteValue);
                break;
            case EchoType.Short:
                writer.Write(tag.ShortValue);
                break;
            case EchoType.Int:
                writer.Write(tag.IntValue);
                break;
            case EchoType.Long:
                writer.Write(tag.LongValue);
                break;
            case EchoType.UShort:
                writer.Write(tag.UShortValue);
                break;
            case EchoType.UInt:
                writer.Write(tag.UIntValue);
                break;
            case EchoType.ULong:
                writer.Write(tag.ULongValue);
                break;
            case EchoType.Float:
                WriteFloat(writer, tag.FloatValue);
                break;
            case EchoType.Double:
                WriteDouble(writer, tag.DoubleValue);
                break;
            case EchoType.Decimal:
                writer.Write(tag.DecimalValue.ToString(CultureInfo.InvariantCulture));
                break;
            case EchoType.String:
                WriteJsonString(writer, tag.StringValue);
                break;
            case EchoType.ByteArray:
                WriteJsonString(writer, Convert.ToBase64String(tag.ByteArrayValue));
                break;
            case EchoType.List:
                WriteArray(writer, tag, indent);
                break;
            case EchoType.Compound:
                WriteObject(writer, tag, indent);
                break;
        }
    }

    private static void WriteFloat(TextWriter writer, float value)
    {
        if (float.IsNaN(value)) { writer.Write("\"NaN\""); return; }
        if (float.IsPositiveInfinity(value)) { writer.Write("\"Infinity\""); return; }
        if (float.IsNegativeInfinity(value)) { writer.Write("\"-Infinity\""); return; }
        var s = value.ToString("R", CultureInfo.InvariantCulture);
        writer.Write(s);
        // Ensure it looks like a float (has a decimal point)
        if (s.IndexOf('.') < 0 && s.IndexOf('E') < 0)
            writer.Write(".0");
    }

    private static void WriteDouble(TextWriter writer, double value)
    {
        if (double.IsNaN(value)) { writer.Write("\"NaN\""); return; }
        if (double.IsPositiveInfinity(value)) { writer.Write("\"Infinity\""); return; }
        if (double.IsNegativeInfinity(value)) { writer.Write("\"-Infinity\""); return; }
        var s = value.ToString("R", CultureInfo.InvariantCulture);
        writer.Write(s);
        if (s.IndexOf('.') < 0 && s.IndexOf('E') < 0)
            writer.Write(".0");
    }

    private void WriteArray(TextWriter writer, EchoObject list, int indent)
    {
        var items = list.List;
        if (items.Count == 0)
        {
            writer.Write("[]");
            return;
        }

        writer.Write('[');
        if (Indented) writer.WriteLine();

        for (int i = 0; i < items.Count; i++)
        {
            if (Indented) WriteIndent(writer, indent + 1);
            WriteValue(writer, items[i], indent + 1);
            if (i < items.Count - 1) writer.Write(',');
            if (Indented) writer.WriteLine();
        }

        if (Indented) WriteIndent(writer, indent);
        writer.Write(']');
    }

    private void WriteObject(TextWriter writer, EchoObject compound, int indent)
    {
        var tags = compound.Tags;
        if (tags.Count == 0)
        {
            writer.Write("{}");
            return;
        }

        writer.Write('{');
        if (Indented) writer.WriteLine();

        bool first = true;
        foreach (var kvp in tags)
        {
            if (!first)
            {
                writer.Write(',');
                if (Indented) writer.WriteLine();
            }
            first = false;

            if (Indented) WriteIndent(writer, indent + 1);
            WriteJsonString(writer, kvp.Key);
            writer.Write(':');
            if (Indented) writer.Write(' ');
            WriteValue(writer, kvp.Value, indent + 1);
        }

        if (Indented) writer.WriteLine();
        if (Indented) WriteIndent(writer, indent);
        writer.Write('}');
    }

    private static void WriteIndent(TextWriter writer, int level)
    {
        for (int i = 0; i < level; i++)
            writer.Write("  ");
    }

    private static void WriteJsonString(TextWriter writer, string value)
    {
        writer.Write('"');
        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            switch (c)
            {
                case '"': writer.Write("\\\""); break;
                case '\\': writer.Write("\\\\"); break;
                case '\b': writer.Write("\\b"); break;
                case '\f': writer.Write("\\f"); break;
                case '\n': writer.Write("\\n"); break;
                case '\r': writer.Write("\\r"); break;
                case '\t': writer.Write("\\t"); break;
                default:
                    if (c < ' ')
                    {
                        // Emit surrogate pairs as \uHHHH\uHHHH when in the control-char/escape branch
                        if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                        {
                            writer.Write("\\u");
                            writer.Write(((int)c).ToString("X4"));
                            i++;
                            writer.Write("\\u");
                            writer.Write(((int)value[i]).ToString("X4"));
                        }
                        else
                        {
                            writer.Write("\\u");
                            writer.Write(((int)c).ToString("X4"));
                        }
                    }
                    else if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
                    {
                        // Surrogate pairs above U+001F: write raw (valid UTF-8)
                        writer.Write(c);
                        i++;
                        writer.Write(value[i]);
                    }
                    else
                    {
                        writer.Write(c);
                    }
                    break;
            }
        }
        writer.Write('"');
    }

    #endregion

    #region Reading

    private static void SkipWhitespace(string json, ref int index)
    {
        while (index < json.Length && char.IsWhiteSpace(json[index]))
            index++;
    }

    private static EchoObject ParseValue(string json, ref int index)
    {
        SkipWhitespace(json, ref index);
        if (index >= json.Length)
            throw new InvalidDataException("Unexpected end of JSON");

        char c = json[index];

        if (c == '"') return ParseString(json, ref index);
        if (c == '{') return ParseObject(json, ref index);
        if (c == '[') return ParseArray(json, ref index);
        if (c == 't') return ParseLiteral(json, ref index, "true", new EchoObject(true));
        if (c == 'f') return ParseLiteral(json, ref index, "false", new EchoObject(false));
        if (c == 'n') return ParseLiteral(json, ref index, "null", new EchoObject(EchoType.Null, null));
        if (c == '-' || (c >= '0' && c <= '9')) return ParseNumber(json, ref index);

        throw new InvalidDataException($"Unexpected character '{c}' at position {index}");
    }

    private static EchoObject ParseString(string json, ref int index)
    {
        return new EchoObject(ReadJsonString(json, ref index));
    }

    private static string ReadJsonString(string json, ref int index)
    {
        if (json[index] != '"')
            throw new InvalidDataException($"Expected '\"' at position {index}");
        index++; // skip opening quote

        var sb = new StringBuilder();
        while (index < json.Length)
        {
            char c = json[index++];

            if (c == '"')
                return sb.ToString();

            if (c == '\\')
            {
                if (index >= json.Length)
                    throw new InvalidDataException("Unexpected end of JSON in string escape");

                char esc = json[index++];
                switch (esc)
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'u':
                        if (index + 4 > json.Length)
                            throw new InvalidDataException("Unexpected end of JSON in unicode escape");
                        var hex = json.Substring(index, 4);
                        int codeUnit = int.Parse(hex, NumberStyles.HexNumber);
                        index += 4;
                        // Handle UTF-16 surrogate pairs (RFC 8259 §7)
                        if (codeUnit >= 0xD800 && codeUnit <= 0xDBFF)
                        {
                            if (index + 6 <= json.Length && json[index] == '\\' && json[index + 1] == 'u')
                            {
                                var hex2 = json.Substring(index + 2, 4);
                                int low = int.Parse(hex2, NumberStyles.HexNumber);
                                if (low >= 0xDC00 && low <= 0xDFFF)
                                {
                                    index += 6;
                                    int codePoint = 0x10000 + (codeUnit - 0xD800) * 0x400 + (low - 0xDC00);
                                    sb.Append(char.ConvertFromUtf32(codePoint));
                                    break;
                                }
                            }
                        }
                        sb.Append((char)codeUnit);
                        break;
                    default:
                        sb.Append('\\');
                        sb.Append(esc);
                        break;
                }
            }
            else
            {
                sb.Append(c);
            }
        }

        throw new InvalidDataException("Unterminated string");
    }

    private static EchoObject ParseNumber(string json, ref int index)
    {
        int start = index;
        bool hasDecimal = false;
        bool hasExponent = false;

        if (json[index] == '-') index++;

        // RFC 8259: leading zeros are not allowed (e.g. 007 is invalid)
        if (index < json.Length && json[index] == '0')
        {
            index++;
            if (index < json.Length && json[index] >= '0' && json[index] <= '9')
                throw new InvalidDataException($"Leading zeros are not permitted in JSON numbers at position {start}");
        }

        while (index < json.Length)
        {
            char c = json[index];
            if (c >= '0' && c <= '9')
            {
                index++;
            }
            else if (c == '.' && !hasDecimal)
            {
                hasDecimal = true;
                index++;
            }
            else if ((c == 'e' || c == 'E') && !hasExponent)
            {
                hasExponent = true;
                index++;
                if (index < json.Length && (json[index] == '+' || json[index] == '-'))
                    index++;
            }
            else
            {
                break;
            }
        }

        var numStr = json.Substring(start, index - start);

        if (hasDecimal || hasExponent)
        {
            if (double.TryParse(numStr, NumberStyles.Float | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out double d))
                return new EchoObject(d);
            throw new InvalidDataException($"Invalid number: {numStr}");
        }

        // Integer
        if (int.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
            return new EchoObject(intVal);
        if (long.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longVal))
            return new EchoObject(longVal);
        if (ulong.TryParse(numStr, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong ulongVal))
            return new EchoObject(ulongVal);
        if (double.TryParse(numStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double dbl))
            return new EchoObject(dbl);

        throw new InvalidDataException($"Invalid number: {numStr}");
    }

    private static EchoObject ParseObject(string json, ref int index)
    {
        index++; // skip '{'
        SkipWhitespace(json, ref index);

        var compound = EchoObject.NewCompound();

        if (index < json.Length && json[index] == '}')
        {
            index++;
            return compound;
        }

        while (index < json.Length)
        {
            SkipWhitespace(json, ref index);

            // Read key
            if (json[index] != '"')
                throw new InvalidDataException($"Expected '\"' for object key at position {index}");
            var key = ReadJsonString(json, ref index);

            // Read colon
            SkipWhitespace(json, ref index);
            if (index >= json.Length || json[index] != ':')
                throw new InvalidDataException($"Expected ':' at position {index}");
            index++;

            // Read value
            SkipWhitespace(json, ref index);
            compound.Add(key, ParseValue(json, ref index));

            SkipWhitespace(json, ref index);
            if (index >= json.Length)
                throw new InvalidDataException("Unterminated object");

            if (json[index] == '}')
            {
                index++;
                return compound;
            }

            if (json[index] == ',')
            {
                index++;
                continue;
            }

            throw new InvalidDataException($"Expected ',' or '}}' at position {index}");
        }

        throw new InvalidDataException("Unterminated object");
    }

    private static EchoObject ParseArray(string json, ref int index)
    {
        index++; // skip '['
        SkipWhitespace(json, ref index);

        var list = EchoObject.NewList();

        if (index < json.Length && json[index] == ']')
        {
            index++;
            return list;
        }

        while (index < json.Length)
        {
            SkipWhitespace(json, ref index);
            list.ListAdd(ParseValue(json, ref index));

            SkipWhitespace(json, ref index);
            if (index >= json.Length)
                throw new InvalidDataException("Unterminated array");

            if (json[index] == ']')
            {
                index++;
                return list;
            }

            if (json[index] == ',')
            {
                index++;
                continue;
            }

            throw new InvalidDataException($"Expected ',' or ']' at position {index}");
        }

        throw new InvalidDataException("Unterminated array");
    }

    private static EchoObject ParseLiteral(string json, ref int index, string expected, EchoObject result)
    {
        if (index + expected.Length > json.Length ||
            json.Substring(index, expected.Length) != expected)
            throw new InvalidDataException($"Expected '{expected}' at position {index}");

        index += expected.Length;
        return result;
    }

    #endregion
}
