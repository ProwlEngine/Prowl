// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Globalization;
using System.Text;

namespace Prowl.Echo;

/// <summary>
/// YAML file format for EchoObject serialization. Zero external dependencies.
/// Writes clean, human-readable YAML. Reads both Echo-produced and generic YAML.
/// Supports mappings, sequences, scalars (strings, numbers, booleans, null), and !!binary.
/// Note: Some type precision may be lost (e.g., byte/short → int, float → double).
/// For exact type preservation, use EchoTextFormat or EchoBinaryFormat.
/// </summary>
public sealed class YamlFileFormat : IFileFormat
{
    public static readonly YamlFileFormat Instance = new();

    public void WriteTo(EchoObject tag, Stream stream)
    {
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
        WriteNode(writer, tag, 0, false);
    }

    public EchoObject ReadFrom(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: true);
        var lines = ReadLines(reader);
        int index = 0;
        // Skip leading document markers (---)
        while (index < lines.Count && (lines[index].Content == "---" || lines[index].Content.Length == 0))
            index++;
        if (index >= lines.Count)
            return new EchoObject(EchoType.Null, null);
        return ParseNode(lines, ref index, -1);
    }

    #region Writing

    private static void WriteNode(TextWriter writer, EchoObject tag, int indent, bool inlineKey)
    {
        switch (tag.TagType)
        {
            case EchoType.Compound:
                WriteMapping(writer, tag, indent, inlineKey);
                break;
            case EchoType.List:
                WriteSequence(writer, tag, indent, inlineKey);
                break;
            default:
                WriteScalar(writer, tag);
                break;
        }
    }

    private static void WriteMapping(TextWriter writer, EchoObject compound, int indent, bool inlineKey)
    {
        var tags = compound.Tags;
        if (tags.Count == 0)
        {
            writer.Write("{}");
            return;
        }

        bool first = true;
        foreach (var kvp in tags)
        {
            // Write indent for all entries, except the first entry when inline (after "- ")
            if (!(first && inlineKey))
                writer.Write(new string(' ', indent));
            first = false;

            writer.Write(NeedsQuoting(kvp.Key) ? QuoteString(kvp.Key) : kvp.Key);
            writer.Write(':');

            if (kvp.Value.TagType == EchoType.Compound || kvp.Value.TagType == EchoType.List)
            {
                if ((kvp.Value.TagType == EchoType.Compound && kvp.Value.Count == 0) ||
                    (kvp.Value.TagType == EchoType.List && kvp.Value.Count == 0))
                {
                    writer.Write(' ');
                    WriteNode(writer, kvp.Value, indent + 2, false);
                    writer.WriteLine();
                }
                else
                {
                    writer.WriteLine();
                    WriteNode(writer, kvp.Value, indent + 2, false);
                }
            }
            else
            {
                writer.Write(' ');
                WriteNode(writer, kvp.Value, indent + 2, false);
                writer.WriteLine();
            }
        }
    }

    private static void WriteSequence(TextWriter writer, EchoObject list, int indent, bool inlineKey)
    {
        var items = list.List;
        if (items.Count == 0)
        {
            writer.Write("[]");
            return;
        }

        foreach (var item in items)
        {
            writer.Write(new string(' ', indent));
            writer.Write("- ");

            if (item.TagType == EchoType.Compound && item.Count > 0)
            {
                // Write first key on same line as dash, rest indented
                WriteNode(writer, item, indent + 2, true);
            }
            else if (item.TagType == EchoType.List && item.Count > 0)
            {
                writer.WriteLine();
                WriteNode(writer, item, indent + 2, false);
            }
            else
            {
                WriteNode(writer, item, indent + 2, false);
                writer.WriteLine();
            }
        }
    }

    private static void WriteScalar(TextWriter writer, EchoObject tag)
    {
        switch (tag.TagType)
        {
            case EchoType.Null:
                writer.Write("null");
                break;
            case EchoType.Bool:
                writer.Write(tag.BoolValue ? "true" : "false");
                break;
            case EchoType.String:
                writer.Write(QuoteString(tag.StringValue));
                break;
            case EchoType.ByteArray:
                writer.Write("!!binary ");
                writer.Write(QuoteString(Convert.ToBase64String(tag.ByteArrayValue)));
                break;
            case EchoType.Float:
                {
                    float f = tag.FloatValue;
                    if (float.IsNaN(f)) writer.Write(".nan");
                    else if (float.IsPositiveInfinity(f)) writer.Write(".inf");
                    else if (float.IsNegativeInfinity(f)) writer.Write("-.inf");
                    else writer.Write(f.ToString(CultureInfo.InvariantCulture));
                }
                break;
            case EchoType.Double:
                {
                    double d = tag.DoubleValue;
                    if (double.IsNaN(d)) writer.Write(".nan");
                    else if (double.IsPositiveInfinity(d)) writer.Write(".inf");
                    else if (double.IsNegativeInfinity(d)) writer.Write("-.inf");
                    else writer.Write(d.ToString(CultureInfo.InvariantCulture));
                }
                break;
            case EchoType.Decimal:
                writer.Write(tag.DecimalValue.ToString(CultureInfo.InvariantCulture));
                break;
            default:
                // All integer types
                writer.Write(tag.StringValue);
                break;
        }
    }

    private static bool NeedsQuoting(string value)
    {
        if (string.IsNullOrEmpty(value)) return true;

        // Keys that could be misinterpreted
        if (value is "null" or "true" or "false" or "~" or
            "Null" or "True" or "False" or
            "NULL" or "TRUE" or "FALSE" or
            "yes" or "Yes" or "YES" or
            "no" or "No" or "NO" or
            "on" or "On" or "ON" or
            "off" or "Off" or "OFF") return true;

        foreach (var c in value)
        {
            if (c is ':' or '#' or '[' or ']' or '{' or '}' or ',' or '&' or '*' or
                '?' or '|' or '-' or '<' or '>' or '=' or '!' or '%' or '@' or
                '`' or '"' or '\'' or '\\' or '\n' or '\r' or '\t')
                return true;
        }

        // If it looks like a number, quote it
        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out _))
            return true;

        return false;
    }

    private static string QuoteString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        sb.Append('"');
        return sb.ToString();
    }

    #endregion

    #region Reading

    private struct YamlLine
    {
        public int Indent;
        public string Content;
    }

    private static List<YamlLine> ReadLines(TextReader reader)
    {
        var lines = new List<YamlLine>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            // Skip comments and document end markers
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#') || trimmed == "...")
                continue;

            int indent = 0;
            while (indent < line.Length && line[indent] == ' ')
                indent++;

            lines.Add(new YamlLine { Indent = indent, Content = trimmed });
        }
        return lines;
    }

    private static EchoObject ParseNode(List<YamlLine> lines, ref int index, int parentIndent)
    {
        if (index >= lines.Count)
            return new EchoObject(EchoType.Null, null);

        var line = lines[index];

        // Empty content
        if (line.Content.Length == 0)
        {
            index++;
            return new EchoObject(EchoType.Null, null);
        }

        // Sequence item
        if (line.Content.StartsWith("- ") || line.Content == "-")
            return ParseSequence(lines, ref index, line.Indent);

        // Check if it's a mapping (contains an unquoted colon)
        if (IsMappingLine(line.Content))
            return ParseMapping(lines, ref index, line.Indent);

        // Inline flow sequence
        if (line.Content.StartsWith('['))
            return ParseFlowSequence(lines, ref index);

        // Inline flow mapping
        if (line.Content.StartsWith('{'))
            return ParseFlowMapping(lines, ref index);

        // Scalar
        index++;
        return ParseScalarValue(line.Content);
    }

    private static EchoObject ParseMapping(List<YamlLine> lines, ref int index, int mappingIndent)
    {
        var compound = EchoObject.NewCompound();

        while (index < lines.Count)
        {
            var line = lines[index];
            if (line.Content.Length == 0) { index++; continue; }
            if (line.Indent < mappingIndent) break;
            if (line.Indent > mappingIndent) break; // shouldn't happen at this level

            if (!IsMappingLine(line.Content))
                break;

            var (key, rest) = SplitMappingLine(line.Content);
            index++;

            if (rest.Length > 0)
            {
                // Value on the same line
                if (rest.StartsWith('['))
                {
                    // Rewind: we need to parse the flow sequence from the rest
                    compound.Add(key, ParseScalarOrFlow(rest));
                }
                else if (rest.StartsWith('{'))
                {
                    compound.Add(key, ParseScalarOrFlow(rest));
                }
                else
                {
                    compound.Add(key, ParseScalarValue(rest));
                }
            }
            else
            {
                // Value is on subsequent indented lines
                if (index < lines.Count && lines[index].Content.Length > 0 && lines[index].Indent > mappingIndent)
                {
                    compound.Add(key, ParseNode(lines, ref index, mappingIndent));
                }
                else
                {
                    compound.Add(key, new EchoObject(EchoType.Null, null));
                }
            }
        }

        return compound;
    }

    private static EchoObject ParseSequence(List<YamlLine> lines, ref int index, int seqIndent)
    {
        var list = EchoObject.NewList();

        while (index < lines.Count)
        {
            var line = lines[index];
            if (line.Content.Length == 0) { index++; continue; }
            if (line.Indent < seqIndent) break;
            if (line.Indent > seqIndent) break;

            if (!line.Content.StartsWith("- ") && line.Content != "-")
                break;

            // Strip the "- " prefix
            var itemContent = line.Content.Length > 2 ? line.Content.Substring(2) : "";
            index++;

            if (itemContent.Length == 0)
            {
                // Item value is on subsequent lines
                if (index < lines.Count && lines[index].Indent > seqIndent)
                {
                    list.ListAdd(ParseNode(lines, ref index, seqIndent));
                }
                else
                {
                    list.ListAdd(new EchoObject(EchoType.Null, null));
                }
            }
            else if (IsMappingLine(itemContent))
            {
                // Inline mapping starting on the dash line
                // We need to reconstruct this as a mapping with potential continuation
                var (key, rest) = SplitMappingLine(itemContent);
                var compound = EchoObject.NewCompound();

                if (rest.Length > 0)
                    compound.Add(key, ParseScalarOrFlow(rest));
                else if (index < lines.Count && lines[index].Indent > seqIndent)
                    compound.Add(key, ParseNode(lines, ref index, seqIndent + 2));
                else
                    compound.Add(key, new EchoObject(EchoType.Null, null));

                // Continue reading mapping entries at indent level seqIndent + 2
                while (index < lines.Count && lines[index].Indent == seqIndent + 2 &&
                       lines[index].Content.Length > 0 && IsMappingLine(lines[index].Content))
                {
                    var subLine = lines[index];
                    var (subKey, subRest) = SplitMappingLine(subLine.Content);
                    index++;

                    if (subRest.Length > 0)
                    {
                        compound.Add(subKey, ParseScalarOrFlow(subRest));
                    }
                    else if (index < lines.Count && lines[index].Indent > seqIndent + 2)
                    {
                        compound.Add(subKey, ParseNode(lines, ref index, seqIndent + 2));
                    }
                    else
                    {
                        compound.Add(subKey, new EchoObject(EchoType.Null, null));
                    }
                }

                list.ListAdd(compound);
            }
            else if (itemContent.StartsWith('[') || itemContent.StartsWith('{'))
            {
                list.ListAdd(ParseScalarOrFlow(itemContent));
            }
            else
            {
                list.ListAdd(ParseScalarValue(itemContent));
            }
        }

        return list;
    }

    private static EchoObject ParseScalarOrFlow(string content)
    {
        content = content.Trim();
        if (content.StartsWith('['))
            return ParseFlowSequenceFromString(content);
        if (content.StartsWith('{'))
            return ParseFlowMappingFromString(content);
        return ParseScalarValue(content);
    }

    private static EchoObject ParseFlowSequence(List<YamlLine> lines, ref int index)
    {
        var result = ParseFlowSequenceFromString(lines[index].Content);
        index++;
        return result;
    }

    private static EchoObject ParseFlowMapping(List<YamlLine> lines, ref int index)
    {
        var result = ParseFlowMappingFromString(lines[index].Content);
        index++;
        return result;
    }

    private static EchoObject ParseFlowSequenceFromString(string input)
    {
        var list = EchoObject.NewList();
        // Strip [ and ]
        input = input.Trim();
        if (input.StartsWith('[')) input = input.Substring(1);
        if (input.EndsWith(']')) input = input.Substring(0, input.Length - 1);
        input = input.Trim();

        if (input.Length == 0)
            return list;

        foreach (var item in SplitFlowItems(input))
        {
            var trimmed = item.Trim();
            if (trimmed.Length > 0)
                list.ListAdd(ParseScalarOrFlow(trimmed));
        }

        return list;
    }

    private static EchoObject ParseFlowMappingFromString(string input)
    {
        var compound = EchoObject.NewCompound();
        input = input.Trim();
        if (input.StartsWith('{')) input = input.Substring(1);
        if (input.EndsWith('}')) input = input.Substring(0, input.Length - 1);
        input = input.Trim();

        if (input.Length == 0)
            return compound;

        foreach (var item in SplitFlowItems(input))
        {
            var trimmed = item.Trim();
            if (trimmed.Length == 0) continue;

            int colonIdx = FindUnquotedColon(trimmed);
            if (colonIdx < 0) continue;

            var key = trimmed.Substring(0, colonIdx).Trim();
            var val = trimmed.Substring(colonIdx + 1).Trim();

            key = UnquoteString(key);
            compound.Add(key, ParseScalarOrFlow(val));
        }

        return compound;
    }

    /// <summary>
    /// Split flow-style items by commas, respecting nested brackets and quotes.
    /// </summary>
    private static List<string> SplitFlowItems(string input)
    {
        var items = new List<string>();
        int depth = 0;
        bool inQuote = false;
        char quoteChar = '"';
        int start = 0;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (inQuote)
            {
                if (c == '\\' && i + 1 < input.Length) { i++; continue; }
                if (c == quoteChar) inQuote = false;
                continue;
            }

            if (c is '"' or '\'') { inQuote = true; quoteChar = c; continue; }
            if (c is '[' or '{') { depth++; continue; }
            if (c is ']' or '}') { depth--; continue; }

            if (c == ',' && depth == 0)
            {
                items.Add(input.Substring(start, i - start));
                start = i + 1;
            }
        }

        if (start < input.Length)
            items.Add(input.Substring(start));

        return items;
    }

    private static EchoObject ParseScalarValue(string value)
    {
        value = value.Trim();

        if (value.Length == 0)
            return new EchoObject(EchoType.Null, null);

        // Handle !!binary tag
        if (value.StartsWith("!!binary ") || value.StartsWith("!!binary\t"))
        {
            var b64 = UnquoteString(value.Substring(9).Trim());
            return new EchoObject(Convert.FromBase64String(b64));
        }

        // Quoted strings are always strings
        if ((value.StartsWith('"') && value.EndsWith('"')) ||
            (value.StartsWith('\'') && value.EndsWith('\'')))
            return new EchoObject(UnquoteString(value));

        // Null
        if (value is "null" or "Null" or "NULL" or "~")
            return new EchoObject(EchoType.Null, null);

        // Booleans
        if (value is "true" or "True" or "TRUE")
            return new EchoObject(true);
        if (value is "false" or "False" or "FALSE")
            return new EchoObject(false);

        // YAML 1.2 special float values
        if (value is ".nan" or ".NaN" or ".NAN")
            return new EchoObject(double.NaN);
        if (value is ".inf" or ".Inf" or ".INF")
            return new EchoObject(double.PositiveInfinity);
        if (value is "-.inf" or "-.Inf" or "-.INF")
            return new EchoObject(double.NegativeInfinity);

        // Numbers
        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
            return new EchoObject(intVal);

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longVal))
            return new EchoObject(longVal);

        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out double doubleVal))
            return new EchoObject(doubleVal);

        // Plain unquoted string
        return new EchoObject(value);
    }

    private static bool IsMappingLine(string content)
    {
        int colonIdx = FindUnquotedColon(content);
        if (colonIdx <= 0) return false;
        // The colon must be followed by a space, end of line, or be at the end
        if (colonIdx + 1 < content.Length && content[colonIdx + 1] != ' ')
            return false;
        return true;
    }

    private static (string key, string value) SplitMappingLine(string content)
    {
        int colonIdx = FindUnquotedColon(content);
        var key = content.Substring(0, colonIdx).Trim();
        var val = colonIdx + 1 < content.Length ? content.Substring(colonIdx + 1).Trim() : "";
        key = UnquoteString(key);
        return (key, val);
    }

    private static int FindUnquotedColon(string content)
    {
        bool inQuote = false;
        char quoteChar = '"';

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];

            if (inQuote)
            {
                if (c == '\\' && i + 1 < content.Length) { i++; continue; }
                if (c == quoteChar) inQuote = false;
                continue;
            }

            if (c is '"' or '\'') { inQuote = true; quoteChar = c; continue; }

            if (c == ':' && (i + 1 >= content.Length || content[i + 1] == ' '))
                return i;
        }

        return -1;
    }

    private static string UnquoteString(string value)
    {
        value = value.Trim();
        if (value.Length < 2) return value;

        if (value[0] == '"' && value[^1] == '"')
        {
            value = value.Substring(1, value.Length - 2);
            return UnescapeString(value);
        }

        if (value[0] == '\'' && value[^1] == '\'')
        {
            value = value.Substring(1, value.Length - 2);
            return value.Replace("''", "'"); // YAML single-quote escaping
        }

        return value;
    }

    private static string UnescapeString(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                i++;
                switch (value[i])
                {
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case '0': sb.Append('\0'); break;
                    case 'a': sb.Append('\u0007'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'e': sb.Append('\u001B'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'v': sb.Append('\v'); break;
                    case 'N': sb.Append('\u0085'); break;
                    case '_': sb.Append('\u00A0'); break;
                    case 'L': sb.Append('\u2028'); break;
                    case 'P': sb.Append('\u2029'); break;
                    case 'x':
                        if (i + 2 < value.Length)
                        {
                            sb.Append((char)Convert.ToInt32(value.Substring(i + 1, 2), 16));
                            i += 2;
                        }
                        else { sb.Append('\\'); sb.Append('x'); }
                        break;
                    case 'u':
                        if (i + 4 < value.Length)
                        {
                            sb.Append((char)Convert.ToInt32(value.Substring(i + 1, 4), 16));
                            i += 4;
                        }
                        else { sb.Append('\\'); sb.Append('u'); }
                        break;
                    case 'U':
                        if (i + 8 < value.Length)
                        {
                            int codepoint = Convert.ToInt32(value.Substring(i + 1, 8), 16);
                            sb.Append(char.ConvertFromUtf32(codepoint));
                            i += 8;
                        }
                        else { sb.Append('\\'); sb.Append('U'); }
                        break;
                    default: sb.Append('\\'); sb.Append(value[i]); break;
                }
            }
            else
            {
                sb.Append(value[i]);
            }
        }
        return sb.ToString();
    }

    #endregion
}
