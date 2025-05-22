using System;
using System.Collections.Generic;
using System.Xml;
using System.IO;
using System.Text.RegularExpressions;
using System.Text;
using System.Drawing;
using Prowl.Vector;

namespace Prowl.Quill
{
    public class SvgElement
    {
        public TagType tag;
        public int depth;
        public Dictionary<string, string> Attributes { get; }
        public List<SvgElement> Children { get; }
        public DrawCommand[] drawCommands;

        public Color stroke;
        public Color fill;
        public ColorType strokeType;
        public ColorType fillType;
        public double strokeWidth;

        public SvgElement()
        {
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Children = new List<SvgElement>();
        }

        public override string ToString()
        {
            return $"<{tag} Depth={depth} Attributes='{Attributes.Count}' Children='{Children.Count}'>";
        }

        public List<SvgElement> Flatten()
        {
            var list = new List<SvgElement>();
            AddChildren(this, list);
            return list;
        }

        void AddChildren(SvgElement element, List<SvgElement> list)
        {
            list.Add(element);
            foreach (var child in Children)
                child.AddChildren(child, list);
        }

        public virtual void Parse()
        {
            var strokeText = ParseString("stroke");
            strokeType = ParseColorType("stroke");
            fillType = ParseColorType("fill");

            if (strokeType == ColorType.specific)
                stroke = ParseColor("stroke");
            if (fillType == ColorType.specific)
                fill = ParseColor("fill");

            strokeWidth = ParseDouble("stroke-width");
        }

        string? ParseString(string key)
        {
            if (Attributes.ContainsKey(key))
                return Attributes[key];
            return null;
        }

        ColorType ParseColorType(string key)
        {
            if (Attributes.ContainsKey(key))
            {
                var value = Attributes[key];
                if (Enum.TryParse<ColorType>(value, true, out var result))
                    return result;
                return ColorType.specific;
            }
            return ColorType.none;
        }

        protected double ParseDouble(string key)
        {
            if (Attributes.ContainsKey(key))
                return double.Parse(Attributes[key]);
            return 0;
        }

        Color ParseColor(string key)
        {
            var color = Color.Transparent;
            var attribute = "white";
            if (Attributes.ContainsKey(key))
                attribute = Attributes[key];

            if (attribute.Equals("none", StringComparison.OrdinalIgnoreCase))
                color = Color.Transparent;
            else if (attribute.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
                color = Color.Transparent; // Placeholder: currentColor requires context (e.g., inherited color)
            else
                color = ColorParser.Parse(attribute);

            return color;
        }

        public enum TagType
        {
            svg,
            path,
            circle,
            rect,
            g,
        }

        public enum ColorType
        {
            none,
            currentColor,
            specific
        }
    }

    public class SvgRectElement : SvgElement
    {
        public Vector2 pos;
        public Vector2 size;
        public Vector2 radius;

        public override void Parse()
        {
            base.Parse();
            pos.x = ParseDouble("x");
            pos.y = ParseDouble("y");
            size.x = ParseDouble("width");
            size.y = ParseDouble("height");
            radius.x = ParseDouble("rx");
            radius.y = ParseDouble("ry");
        }
    }

    public class SvgCircleElement : SvgElement
    {
        public double cx, cy, r;

        public override void Parse()
        {
            base.Parse();
            cx = ParseDouble("cx");
            cy = ParseDouble("cy");
            r = ParseDouble("r");
        }
    }

    public class SvgEllipseElement : SvgElement
    {
        public override void Parse()
        {
            base.Parse();
        }
    }

    public class SvgLineElement : SvgElement
    {
        public override void Parse()
        {
            base.Parse();
        }
    }

    public class SvgPolylineElement : SvgElement
    {
        public override void Parse()
        {
            base.Parse();
        }
    }

    public class SvgPolygonElement : SvgElement
    {
        public override void Parse()
        {
            base.Parse();
        }
    }

    public class SvgPathElement : SvgElement
    {
        public override void Parse()
        {
            base.Parse();

            //if (!Attributes.ContainsKey("d"))
            //    return;

            var pathData = Attributes["d"];
            if (string.IsNullOrEmpty(pathData))
                throw new InvalidDataException();

            var matches = Regex.Matches(pathData, @"([A-Za-z])([-0-9.,\s]*)");
            drawCommands = new DrawCommand[matches.Count];
            for (int i = 0; i < matches.Count; i++)
            {
                var match = matches[i];
                var drawCommand = new DrawCommand();
                var commandSegment = match.Groups[1].Value + match.Groups[2].Value.Trim();
                var parametersString = commandSegment.Length > 1 ? commandSegment.Substring(1).Trim() : "";
                var command = commandSegment[0];

                drawCommand.relative = char.IsLower(command);

                switch (char.ToLower(command))
                {
                    case 'm': drawCommand.type = DrawType.MoveTo; break;
                    case 'l': drawCommand.type = DrawType.LineTo; break;
                    case 'h': drawCommand.type = DrawType.HorizontalLineTo; break;
                    case 'v': drawCommand.type = DrawType.VerticalLineTo; break;
                    case 'q': drawCommand.type = DrawType.QuadraticCurveTo; break;
                    case 't': drawCommand.type = DrawType.SmoothQuadraticCurveTo; break;
                    case 'c': drawCommand.type = DrawType.CubicCurveTo; break;
                    case 's': drawCommand.type = DrawType.SmoothCubicCurveTo; break;
                    case 'a': drawCommand.type = DrawType.ArcTo; break;
                    case 'z': drawCommand.type = DrawType.ClosePath; break;
                }

                //Console.WriteLine($"{command} {parametersString}");

                if (!string.IsNullOrEmpty(parametersString))
                {
                    var param = new List<double>();
                    var matches2 = Regex.Matches(parametersString, @"[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?");
                    for (int j = 0; j < matches2.Count; j++)
                        for (int k = 0; k < matches2[j].Groups.Count; k++)
                            param.Add(double.Parse(matches2[j].Groups[k].ToString()));

                    drawCommand.param = param.ToArray();
                }
                //Console.WriteLine(drawCommand.ToString());
                drawCommands[i] = drawCommand;
                //if (!ValidateParameterCount(drawCommand))
                //{
                //    Console.WriteLine(pathData);
                //    Console.WriteLine($"{match.Groups[0].Value}=>{drawCommand}");
                //}
            }
        }

        bool ValidateParameterCount(DrawCommand command)
        {
            //Console.WriteLine(command.param?.Length);
            switch (command.type)
            {
                case DrawType.MoveTo: return command.param.Length == 2;
                case DrawType.LineTo: return command.param.Length == 2;
                case DrawType.HorizontalLineTo: return command.param.Length == 1;
                case DrawType.VerticalLineTo: return command.param.Length == 1;
                case DrawType.QuadraticCurveTo: return command.param.Length == 4;
                case DrawType.SmoothQuadraticCurveTo: return command.param.Length == 2;
                case DrawType.CubicCurveTo: return command.param.Length == 6;
                case DrawType.SmoothCubicCurveTo: return command.param.Length == 4;
                case DrawType.ArcTo: return command.param.Length == 7;
                case DrawType.ClosePath: return command.param == null;
                default: return true;
            }
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"<{tag} Depth={depth} Attributes='{Attributes.Count}' Children='{Children.Count}'>");
            foreach (var command in drawCommands)
                sb.AppendLine(command.ToString());
            return sb.ToString();
        }
    }

    public struct DrawCommand
    {
        public DrawType type;
        public bool relative;
        public double[] param;

        public override string ToString()
        {
            var sb = new StringBuilder();
            var relativeString = relative ? " relative" : "";
            sb.Append($"{type}{relativeString}:");
            if (param != null)
                foreach (var para in param)
                    sb.Append($"{para} ");

            return sb.ToString();
        }
    }

    public enum DrawType
    {
        MoveTo,
        LineTo,
        VerticalLineTo,
        HorizontalLineTo,
        CubicCurveTo,
        SmoothCubicCurveTo,
        QuadraticCurveTo,
        SmoothQuadraticCurveTo,
        ArcTo,
        ClosePath
    }

    public static class SVGParser
    {
        public static SvgElement ParseSVGDocument(string filePath)
        {
            if (!File.Exists(filePath))
                throw new FileNotFoundException("SVG file not found.", filePath);

            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.Load(filePath);

            if (xmlDoc.DocumentElement != null && xmlDoc.DocumentElement.Name.Equals("svg", StringComparison.OrdinalIgnoreCase))
                return ParseXmlElement(xmlDoc.DocumentElement, 0);
            else
                throw new InvalidOperationException("Invalid SVG document: Missing root <svg> element.");
        }

        private static SvgElement ParseXmlElement(XmlElement xmlElement, int depth)
        {
            SvgElement svgElement;

            var supported = Enum.TryParse<SvgElement.TagType>(xmlElement.Name, out var result);
            if (!supported)
                return null;

            var tag = Enum.Parse<SvgElement.TagType>(xmlElement.Name, true);
            switch (tag)
            {
                case SvgElement.TagType.path:
                    svgElement = new SvgPathElement();
                    break;
                case SvgElement.TagType.circle:
                    svgElement = new SvgCircleElement();
                    break;
                case SvgElement.TagType.rect:
                    svgElement = new SvgRectElement();
                    break;
                default:
                    svgElement = new SvgElement();
                    break;
            }
            svgElement.depth = depth;
            svgElement.tag = tag;

            foreach (XmlAttribute attribute in xmlElement.Attributes)
                svgElement.Attributes[attribute.Name] = attribute.Value;

            foreach (XmlNode childNode in xmlElement.ChildNodes)
                if (childNode.NodeType == XmlNodeType.Element)
                {
                    var child = ParseXmlElement((XmlElement)childNode, depth + 1);
                    if (child != null)
                        svgElement.Children.Add(child);
                }

            svgElement.Parse();

            return svgElement;
        }
    }
}
