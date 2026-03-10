// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Globalization;
using System.Text;

namespace Prowl.Echo;

/// <summary>
/// XML file format for EchoObject serialization. Zero external dependencies.
/// Uses type attributes to preserve all EchoObject types during roundtrip.
/// Also supports reading generic XML without type attributes (types are inferred).
/// </summary>
public sealed class XmlFileFormat : IFileFormat
{
    public static readonly XmlFileFormat Instance = new();

    public void WriteTo(EchoObject tag, Stream stream)
    {
        using var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, leaveOpen: true);
        writer.WriteLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>");
        WriteElement(writer, tag, "echo", 0);
    }

    public EchoObject ReadFrom(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: true);
        var xml = reader.ReadToEnd();
        int index = 0;

        // Skip XML declaration if present
        SkipWhitespace(xml, ref index);
        if (index < xml.Length && xml[index] == '<' && index + 1 < xml.Length && xml[index + 1] == '?')
        {
            int end = xml.IndexOf("?>", index);
            if (end >= 0) index = end + 2;
        }

        SkipWhitespace(xml, ref index);
        if (index >= xml.Length)
            return new EchoObject(EchoType.Null, null);

        return ReadElementFull(xml, ref index).Value;
    }

    #region Writing

    private static void WriteElement(TextWriter writer, EchoObject tag, string name, int indent)
    {
        var sanitized = SanitizeElementName(name);
        WriteIndent(writer, indent);
        writer.Write('<');
        writer.Write(sanitized);

        // Store original key if sanitized
        if (sanitized != name)
        {
            writer.Write(" key=\"");
            WriteAttrValue(writer, name);
            writer.Write('"');
        }

        switch (tag.TagType)
        {
            case EchoType.Null:
                writer.Write(" type=\"null\"");
                writer.WriteLine(" />");
                break;

            case EchoType.Bool:
                writer.Write(" type=\"bool\">");
                writer.Write(tag.BoolValue ? "true" : "false");
                writer.Write("</");
                writer.Write(sanitized);
                writer.WriteLine('>');
                break;

            case EchoType.Byte:
                WriteSimpleElement(writer, sanitized, "byte", tag.ByteValue.ToString(CultureInfo.InvariantCulture));
                break;
            case EchoType.sByte:
                WriteSimpleElement(writer, sanitized, "sbyte", tag.sByteValue.ToString(CultureInfo.InvariantCulture));
                break;
            case EchoType.Short:
                WriteSimpleElement(writer, sanitized, "short", tag.ShortValue.ToString(CultureInfo.InvariantCulture));
                break;
            case EchoType.Int:
                WriteSimpleElement(writer, sanitized, "int", tag.IntValue.ToString(CultureInfo.InvariantCulture));
                break;
            case EchoType.Long:
                WriteSimpleElement(writer, sanitized, "long", tag.LongValue.ToString(CultureInfo.InvariantCulture));
                break;
            case EchoType.UShort:
                WriteSimpleElement(writer, sanitized, "ushort", tag.UShortValue.ToString(CultureInfo.InvariantCulture));
                break;
            case EchoType.UInt:
                WriteSimpleElement(writer, sanitized, "uint", tag.UIntValue.ToString(CultureInfo.InvariantCulture));
                break;
            case EchoType.ULong:
                WriteSimpleElement(writer, sanitized, "ulong", tag.ULongValue.ToString(CultureInfo.InvariantCulture));
                break;
            case EchoType.Float:
                WriteSimpleElement(writer, sanitized, "float", tag.FloatValue.ToString(CultureInfo.InvariantCulture));
                break;
            case EchoType.Double:
                WriteSimpleElement(writer, sanitized, "double", tag.DoubleValue.ToString(CultureInfo.InvariantCulture));
                break;
            case EchoType.Decimal:
                WriteSimpleElement(writer, sanitized, "decimal", tag.DecimalValue.ToString(CultureInfo.InvariantCulture));
                break;

            case EchoType.String:
                writer.Write(" type=\"string\">");
                WriteTextContent(writer, tag.StringValue);
                writer.Write("</");
                writer.Write(sanitized);
                writer.WriteLine('>');
                break;

            case EchoType.ByteArray:
                WriteSimpleElement(writer, sanitized, "bytearray", Convert.ToBase64String(tag.ByteArrayValue));
                break;

            case EchoType.List:
                writer.Write(" type=\"list\">");
                writer.WriteLine();
                foreach (var item in tag.List)
                    WriteElement(writer, item, "item", indent + 1);
                WriteIndent(writer, indent);
                writer.Write("</");
                writer.Write(sanitized);
                writer.WriteLine('>');
                break;

            case EchoType.Compound:
                writer.Write(" type=\"compound\">");
                writer.WriteLine();
                foreach (var kvp in tag.Tags)
                    WriteElement(writer, kvp.Value, kvp.Key, indent + 1);
                WriteIndent(writer, indent);
                writer.Write("</");
                writer.Write(sanitized);
                writer.WriteLine('>');
                break;
        }
    }

    private static void WriteSimpleElement(TextWriter writer, string sanitized, string type, string value)
    {
        writer.Write(" type=\"");
        writer.Write(type);
        writer.Write("\">");
        writer.Write(value);
        writer.Write("</");
        writer.Write(sanitized);
        writer.WriteLine('>');
    }

    private static void WriteIndent(TextWriter writer, int level)
    {
        for (int i = 0; i < level; i++)
            writer.Write("  ");
    }

    private static bool IsForbiddenXmlChar(char c) =>
        c is (>= '\u0001' and <= '\u0008') or '\u000B' or '\u000C' or (>= '\u000E' and <= '\u001F');

    private static void WriteTextContent(TextWriter writer, string value)
    {
        foreach (var c in value)
        {
            if (IsForbiddenXmlChar(c)) continue; // strip forbidden XML 1.0 control chars
            switch (c)
            {
                case '&': writer.Write("&amp;"); break;
                case '<': writer.Write("&lt;"); break;
                case '>': writer.Write("&gt;"); break;
                case '\r': writer.Write("&#xD;"); break;
                default: writer.Write(c); break;
            }
        }
    }

    private static void WriteAttrValue(TextWriter writer, string value)
    {
        foreach (var c in value)
        {
            if (IsForbiddenXmlChar(c)) continue; // strip forbidden XML 1.0 control chars
            switch (c)
            {
                case '&': writer.Write("&amp;"); break;
                case '<': writer.Write("&lt;"); break;
                case '>': writer.Write("&gt;"); break;
                case '"': writer.Write("&quot;"); break;
                case '\r': writer.Write("&#xD;"); break;
                case '\n': writer.Write("&#xA;"); break;
                case '\t': writer.Write("&#x9;"); break;
                default: writer.Write(c); break;
            }
        }
    }

    #endregion

    #region Reading

    private static void SkipWhitespace(string xml, ref int index)
    {
        while (index < xml.Length && char.IsWhiteSpace(xml[index]))
            index++;
    }

    private struct ParsedElement
    {
        public string Name;
        public Dictionary<string, string> Attrs;
        public EchoObject Value;
    }

    private static ParsedElement ReadElementFull(string xml, ref int index)
    {
        SkipWhitespace(xml, ref index);

        if (index >= xml.Length || xml[index] != '<')
            throw new InvalidDataException($"Expected '<' at position {index}");
        index++;

        var elemName = ReadName(xml, ref index);
        var attrs = ReadAttributes(xml, ref index);

        SkipWhitespace(xml, ref index);

        // Self-closing
        if (index < xml.Length && xml[index] == '/')
        {
            index++;
            if (index >= xml.Length || xml[index] != '>')
                throw new InvalidDataException($"Expected '>' at position {index}");
            index++;

            return new ParsedElement
            {
                Name = elemName,
                Attrs = attrs,
                Value = ConvertFromAttributes(attrs, "", elemName)
            };
        }

        if (index >= xml.Length || xml[index] != '>')
            throw new InvalidDataException($"Expected '>' at position {index}");
        index++;

        var children = new List<ParsedElement>();
        var textContent = new StringBuilder();

        while (index < xml.Length)
        {
            // Capture whitespace rather than discarding it — it may be part of text content
            int wsStart = index;
            SkipWhitespace(xml, ref index);
            if (index >= xml.Length) break;

            if (xml[index] == '<')
            {
                if (index + 1 < xml.Length && xml[index + 1] == '/')
                {
                    index += 2;
                    ReadName(xml, ref index); // skip closing tag name
                    SkipWhitespace(xml, ref index);
                    if (index < xml.Length && xml[index] == '>') index++;

                    if (children.Count > 0)
                    {
                        return new ParsedElement
                        {
                            Name = elemName,
                            Attrs = attrs,
                            Value = ConvertFromChildElements(attrs, children)
                        };
                    }

                    return new ParsedElement
                    {
                        Name = elemName,
                        Attrs = attrs,
                        Value = ConvertFromAttributes(attrs, textContent.ToString(), elemName)
                    };
                }

                // Skip comments
                if (index + 3 < xml.Length && xml[index + 1] == '!' && xml[index + 2] == '-' && xml[index + 3] == '-')
                {
                    int endComment = xml.IndexOf("-->", index + 4);
                    if (endComment < 0) throw new InvalidDataException("Unterminated comment");
                    index = endComment + 3;
                    continue;
                }

                // CDATA
                if (index + 8 < xml.Length && xml.Substring(index, 9) == "<![CDATA[")
                {
                    int endCdata = xml.IndexOf("]]>", index + 9);
                    if (endCdata < 0) throw new InvalidDataException("Unterminated CDATA");
                    textContent.Append(xml, index + 9, endCdata - index - 9);
                    index = endCdata + 3;
                    continue;
                }

                children.Add(ReadElementFull(xml, ref index));
            }
            else
            {
                // The whitespace we skipped is actually part of the text content
                if (wsStart < index)
                    textContent.Append(xml, wsStart, index - wsStart);
                textContent.Append(ReadTextContent(xml, ref index));
            }
        }

        throw new InvalidDataException($"Unterminated element '{elemName}'");
    }

    private static string ReadName(string xml, ref int index)
    {
        SkipWhitespace(xml, ref index);
        int start = index;
        while (index < xml.Length && !char.IsWhiteSpace(xml[index]) && xml[index] != '>' && xml[index] != '/' && xml[index] != '=')
            index++;
        if (index == start)
            throw new InvalidDataException($"Expected element/attribute name at position {index}");
        return xml.Substring(start, index - start);
    }

    private static Dictionary<string, string> ReadAttributes(string xml, ref int index)
    {
        var attrs = new Dictionary<string, string>();

        while (index < xml.Length)
        {
            SkipWhitespace(xml, ref index);
            if (index >= xml.Length || xml[index] == '>' || xml[index] == '/')
                break;

            var attrName = ReadName(xml, ref index);
            SkipWhitespace(xml, ref index);

            if (index >= xml.Length || xml[index] != '=')
                throw new InvalidDataException($"Expected '=' after attribute name at position {index}");
            index++;

            SkipWhitespace(xml, ref index);
            if (index >= xml.Length || (xml[index] != '"' && xml[index] != '\''))
                throw new InvalidDataException($"Expected '\"' or \"'\" for attribute value at position {index}");
            char quote = xml[index];
            index++;

            var sb = new StringBuilder();
            while (index < xml.Length && xml[index] != quote)
            {
                if (xml[index] == '&')
                    sb.Append(ReadEntityRef(xml, ref index));
                else
                    sb.Append(xml[index++]);
            }

            if (index >= xml.Length)
                throw new InvalidDataException("Unterminated attribute value");
            index++; // skip closing quote

            attrs[attrName] = sb.ToString();
        }

        return attrs;
    }

    private static string ReadTextContent(string xml, ref int index)
    {
        var sb = new StringBuilder();
        while (index < xml.Length && xml[index] != '<')
        {
            if (xml[index] == '&')
                sb.Append(ReadEntityRef(xml, ref index));
            else
                sb.Append(xml[index++]);
        }
        return sb.ToString();
    }

    private static char ReadEntityRef(string xml, ref int index)
    {
        index++; // skip '&'
        int start = index;
        while (index < xml.Length && xml[index] != ';')
            index++;
        if (index >= xml.Length)
            throw new InvalidDataException("Unterminated entity reference");
        var entity = xml.Substring(start, index - start);
        index++; // skip ';'

        return entity switch
        {
            "amp" => '&',
            "lt" => '<',
            "gt" => '>',
            "quot" => '"',
            "apos" => '\'',
            _ when entity.StartsWith('#') => entity.Length > 1 && (entity[1] == 'x' || entity[1] == 'X')
                ? (char)int.Parse(entity.Substring(2), System.Globalization.NumberStyles.HexNumber)
                : (char)int.Parse(entity.Substring(1)),
            _ => '?'
        };
    }

    private static EchoObject ConvertFromAttributes(Dictionary<string, string> attrs, string textContent, string elemName)
    {
        attrs.TryGetValue("type", out var typeAttr);

        if (typeAttr != null)
        {
            return typeAttr switch
            {
                "null" => new EchoObject(EchoType.Null, null),
                "bool" => new EchoObject(bool.Parse(textContent)),
                "byte" => new EchoObject(byte.Parse(textContent, CultureInfo.InvariantCulture)),
                "sbyte" => new EchoObject(sbyte.Parse(textContent, CultureInfo.InvariantCulture)),
                "short" => new EchoObject(short.Parse(textContent, CultureInfo.InvariantCulture)),
                "int" => new EchoObject(int.Parse(textContent, CultureInfo.InvariantCulture)),
                "long" => new EchoObject(long.Parse(textContent, CultureInfo.InvariantCulture)),
                "ushort" => new EchoObject(ushort.Parse(textContent, CultureInfo.InvariantCulture)),
                "uint" => new EchoObject(uint.Parse(textContent, CultureInfo.InvariantCulture)),
                "ulong" => new EchoObject(ulong.Parse(textContent, CultureInfo.InvariantCulture)),
                "float" => new EchoObject(float.Parse(textContent, CultureInfo.InvariantCulture)),
                "double" => new EchoObject(double.Parse(textContent, CultureInfo.InvariantCulture)),
                "decimal" => new EchoObject(decimal.Parse(textContent, CultureInfo.InvariantCulture)),
                "string" => new EchoObject(textContent),
                "bytearray" => new EchoObject(Convert.FromBase64String(textContent)),
                "list" => EchoObject.NewList(),     // children handled separately
                "compound" => EchoObject.NewCompound(), // children handled separately
                _ => InferScalar(textContent)
            };
        }

        // No type attribute: infer from text content
        return InferScalar(textContent);
    }

    private static EchoObject ConvertFromChildElements(Dictionary<string, string> attrs, List<ParsedElement> children)
    {
        attrs.TryGetValue("type", out var typeAttr);

        if (typeAttr == "list")
        {
            var list = EchoObject.NewList();
            foreach (var child in children)
                list.ListAdd(child.Value);
            return list;
        }

        if (typeAttr == "compound")
        {
            var compound = EchoObject.NewCompound();
            foreach (var child in children)
            {
                var key = child.Attrs.TryGetValue("key", out var k) ? k : child.Name;
                compound.Add(key, child.Value);
            }
            return compound;
        }

        // No type: infer. If all children are "item", treat as list.
        if (children.All(c => c.Name == "item"))
        {
            var list = EchoObject.NewList();
            foreach (var child in children)
                list.ListAdd(child.Value);
            return list;
        }

        // Otherwise compound
        var comp = EchoObject.NewCompound();
        foreach (var child in children)
        {
            var key = child.Attrs.TryGetValue("key", out var k) ? k : child.Name;
            comp.Add(key, child.Value);
        }
        return comp;
    }

    private static EchoObject InferScalar(string value)
    {
        if (string.IsNullOrEmpty(value))
            return new EchoObject(EchoType.Null, null);

        if (value is "true" or "false")
            return new EchoObject(bool.Parse(value));

        if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int intVal))
            return new EchoObject(intVal);

        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long longVal))
            return new EchoObject(longVal);

        if (double.TryParse(value, NumberStyles.Float | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out double doubleVal))
            return new EchoObject(doubleVal);

        return new EchoObject(value);
    }

    #endregion

    #region Helpers

    private static string SanitizeElementName(string name)
    {
        if (string.IsNullOrEmpty(name))
            return "_empty";

        var sb = new StringBuilder(name.Length);

        if (!char.IsLetter(name[0]) && name[0] != '_')
            sb.Append('_');

        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c) || c is '_' or '-' or '.')
                sb.Append(c);
            else
                sb.Append('_');
        }

        return sb.ToString();
    }

    #endregion
}
