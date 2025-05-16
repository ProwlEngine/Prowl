using System;
using System.Collections.Generic;
using System.Xml;
using System.IO;
using System.Text.RegularExpressions;
using System.Globalization;
using System.Text;
using System.Drawing;

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
            stroke = ParseColor("stroke");
            fill = ParseColor("fill");
        }

        string? ParseString(string key)
        {
            if (Attributes.ContainsKey(key))
                return Attributes[key];
            return null;
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
            g,
        }
    }

    public class SvgRectElement : SvgElement
    {
        public override void Parse()
        {
            base.Parse();
        }
    }

    public class SvgCircleElement : SvgElement
    {
        public override void Parse()
        {
            base.Parse();
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

            var pathData = Attributes["d"];
            if (string.IsNullOrEmpty(pathData))
                throw new InvalidDataException();

            //sample input
            //m8.293 16.293l1.414 1.414L12 15.414l2.293 2.293l1.414-1.414L13.414 14l2.293-2.293l-1.414-1.414L12 12.586l-2.293-2.293l-1.414 1.414L10.586 14z

            //sample output
            //m8.293 16.293
            //l1.414 1.414
            //L12 15.414
            //l2.293 2.293
            //l1.414-1.414
            //L13.414 14
            //l2.293-2.293
            //l-1.414-1.414
            //L12 12.586
            //l-2.293-2.293
            //l-1.414 1.414
            //L10.586 14
            //z

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
                    case 't': drawCommand.type = DrawType.BezierCurveTo; break;
                    case 'c': drawCommand.type = DrawType.BezierCurveTo; break;
                    case 's': drawCommand.type = DrawType.BezierCurveTo; break;
                    case 'z': drawCommand.type = DrawType.ClosePath; break;
                }

                Console.WriteLine($"{command} {parametersString}");

                if (!string.IsNullOrEmpty(parametersString))
                {
                    var param = new List<double>();
                    var matches2 = Regex.Matches(parametersString, @"[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?");
                    for (int j = 0; j < matches2.Count; j++)
                        for (int k = 0; k < matches2[j].Groups.Count; k++)
                            param.Add(double.Parse(matches2[j].Groups[k].ToString()));

                    drawCommand.param = param.ToArray();
                }
                Console.WriteLine(drawCommand.ToString());
                drawCommands[i] = drawCommand;
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
            sb.Append($"{type} relative:{relative} parameters:");
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
        BezierCurveTo,
        SmoothBezierCurveTo,
        QuadraticCurveTo,
        SmoothQuadraticCurveTo,
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
            SvgElement svgElement;// = new SvgElement();

            var tag = Enum.Parse<SvgElement.TagType>(xmlElement.Name);
            switch (tag)
            {
                case SvgElement.TagType.path:
                    svgElement = new SvgPathElement();
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
                    svgElement.Children.Add(ParseXmlElement((XmlElement)childNode, depth + 1));

            svgElement.Parse();

            return svgElement;
        }
    }
}
