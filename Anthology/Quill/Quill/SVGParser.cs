using Prowl.Vector;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Color = Prowl.Vector.Color;

namespace Prowl.Quill
{
    /// <summary>
    /// Represents an SVG element parsed from an SVG document.
    /// </summary>
    public class SvgElement
    {
        /// <summary>
        /// The type of SVG element (path, circle, rect, etc.).
        /// </summary>
        public TagType tag;

        /// <summary>
        /// The nesting depth of this element in the SVG document hierarchy.
        /// </summary>
        public int depth;

        /// <summary>
        /// Gets the attributes defined on this element.
        /// </summary>
        public Dictionary<string, string> Attributes { get; }

        /// <summary>
        /// Gets the child elements of this element.
        /// </summary>
        public List<SvgElement> Children { get; }

        /// <summary>
        /// The draw commands for path elements.
        /// </summary>
        public DrawCommand[] drawCommands;

        /// <summary>
        /// The stroke color of this element.
        /// </summary>
        public Color32 stroke;

        /// <summary>
        /// The fill color of this element.
        /// </summary>
        public Color32 fill;

        /// <summary>
        /// The type of stroke color (none, currentColor, or specific).
        /// </summary>
        public ColorType strokeType;

        /// <summary>
        /// The type of fill color (none, currentColor, or specific).
        /// </summary>
        public ColorType fillType;

        /// <summary>
        /// The stroke width of this element.
        /// </summary>
        public float strokeWidth;

        /// <summary>
        /// Creates a new SVG element.
        /// </summary>
        public SvgElement()
        {
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            Children = new List<SvgElement>();
        }

        /// <summary>
        /// Returns a string representation of this element.
        /// </summary>
        public override string ToString()
        {
            return $"<{tag} Depth={depth} Attributes='{Attributes.Count}' Children='{Children.Count}'>";
        }

        /// <summary>
        /// Flattens the element hierarchy into a single list.
        /// </summary>
        /// <returns>A list containing this element and all its descendants.</returns>
        public List<SvgElement> Flatten()
        {
            var list = new List<SvgElement>();
            AddChildren(this, list);
            return list;
        }

        void AddChildren(SvgElement element, List<SvgElement> list)
        {
            list.Add(element);
            foreach (var child in element.Children)
                AddChildren(child, list);
        }

        /// <summary>
        /// Parses the element's attributes and initializes stroke and fill properties.
        /// </summary>
        public virtual void Parse()
        {
            var strokeText = ParseString("stroke");
            strokeType = ParseColorType("stroke");
            fillType = ParseColorType("fill");

            if (strokeType == ColorType.specific)
                stroke = ParseColor("stroke");
            if (fillType == ColorType.specific)
                fill = ParseColor("fill");

            strokeWidth = Attributes.ContainsKey("stroke-width") ? ParseFloat("stroke-width") : 1.0f;
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

        protected float ParseFloat(string key)
        {
            if (Attributes.TryGetValue(key, out var value) && float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                return result;
            return 0;
        }

        Color32 ParseColor(string key)
        {
            var color = (Color32)Color.Transparent;
            var attribute = "white";
            if (Attributes.ContainsKey(key))
                attribute = Attributes[key];

            if (attribute.Equals("none", StringComparison.OrdinalIgnoreCase))
                color = (Color32)Color.Transparent;
            else if (attribute.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
                color = (Color32)Color.Transparent; // Placeholder: currentColor requires context (e.g., inherited color)
            else
                color = ColorParser.Parse(attribute);

            return color;
        }

        /// <summary>
        /// Specifies the type of SVG element.
        /// </summary>
        public enum TagType
        {
            /// <summary>Root SVG container element.</summary>
            svg,
            /// <summary>Path element with draw commands.</summary>
            path,
            /// <summary>Circle element.</summary>
            circle,
            /// <summary>Rectangle element.</summary>
            rect,
            /// <summary>Line element.</summary>
            line,
            /// <summary>Polyline element (open path through points).</summary>
            polyline,
            /// <summary>Polygon element (closed path through points).</summary>
            polygon,
            /// <summary>Ellipse element.</summary>
            ellipse,
            /// <summary>Group element for organizing other elements.</summary>
            g,
        }

        /// <summary>
        /// Specifies how a color value is determined.
        /// </summary>
        public enum ColorType
        {
            /// <summary>No color (transparent).</summary>
            none,
            /// <summary>Uses the current inherited color.</summary>
            currentColor,
            /// <summary>A specific color value is defined.</summary>
            specific
        }
    }

    /// <summary>
    /// Represents an SVG rectangle element.
    /// </summary>
    public class SvgRectElement : SvgElement
    {
        /// <summary>The position of the rectangle's top-left corner.</summary>
        public Float2 pos;

        /// <summary>The size of the rectangle.</summary>
        public Float2 size;

        /// <summary>The corner radius for rounded rectangles.</summary>
        public Float2 radius;

        /// <inheritdoc/>
        public override void Parse()
        {
            base.Parse();
            pos.X = ParseFloat("x");
            pos.Y = ParseFloat("y");
            size.X = ParseFloat("width");
            size.Y = ParseFloat("height");
            radius.X = ParseFloat("rx");
            radius.Y = ParseFloat("ry");
        }
    }

    /// <summary>
    /// Represents an SVG circle element.
    /// </summary>
    public class SvgCircleElement : SvgElement
    {
        /// <summary>The X coordinate of the center.</summary>
        public float cx;

        /// <summary>The Y coordinate of the center.</summary>
        public float cy;

        /// <summary>The radius of the circle.</summary>
        public float r;

        /// <inheritdoc/>
        public override void Parse()
        {
            base.Parse();
            cx = ParseFloat("cx");
            cy = ParseFloat("cy");
            r = ParseFloat("r");
        }
    }

    /// <summary>
    /// Represents an SVG ellipse element.
    /// </summary>
    public class SvgEllipseElement : SvgElement
    {
        /// <summary>The X coordinate of the center.</summary>
        public float cx;

        /// <summary>The Y coordinate of the center.</summary>
        public float cy;

        /// <summary>The X-axis radius.</summary>
        public float rx;

        /// <summary>The Y-axis radius.</summary>
        public float ry;

        /// <inheritdoc/>
        public override void Parse()
        {
            base.Parse();
            cx = ParseFloat("cx");
            cy = ParseFloat("cy");
            rx = ParseFloat("rx");
            ry = ParseFloat("ry");
        }
    }

    /// <summary>
    /// Represents an SVG line element.
    /// </summary>
    public class SvgLineElement : SvgElement
    {
        /// <summary>The X coordinate of the start point.</summary>
        public float x1;

        /// <summary>The Y coordinate of the start point.</summary>
        public float y1;

        /// <summary>The X coordinate of the end point.</summary>
        public float x2;

        /// <summary>The Y coordinate of the end point.</summary>
        public float y2;

        /// <inheritdoc/>
        public override void Parse()
        {
            base.Parse();
            x1 = ParseFloat("x1");
            y1 = ParseFloat("y1");
            x2 = ParseFloat("x2");
            y2 = ParseFloat("y2");
        }
    }

    /// <summary>
    /// Represents an SVG polyline element (an open series of connected line segments).
    /// </summary>
    public class SvgPolylineElement : SvgElement
    {
        /// <summary>The points defining the polyline.</summary>
        public Float2[] points = Array.Empty<Float2>();

        /// <inheritdoc/>
        public override void Parse()
        {
            base.Parse();
            if (Attributes.TryGetValue("points", out var pts))
                points = ParsePoints(pts);
        }

        internal static Float2[] ParsePoints(string pts)
        {
            var matches = Regex.Matches(pts, @"-?\d*\.?\d+");
            var list = new List<Float2>();
            for (int i = 0; i + 1 < matches.Count; i += 2)
            {
                var x = float.Parse(matches[i].Value, CultureInfo.InvariantCulture);
                var y = float.Parse(matches[i + 1].Value, CultureInfo.InvariantCulture);
                list.Add(new Float2(x, y));
            }
            return list.ToArray();
        }
    }

    /// <summary>
    /// Represents an SVG polygon element (a closed series of connected line segments).
    /// </summary>
    public class SvgPolygonElement : SvgElement
    {
        /// <summary>The points defining the polygon.</summary>
        public Float2[] points = Array.Empty<Float2>();

        /// <inheritdoc/>
        public override void Parse()
        {
            base.Parse();
            if (Attributes.TryGetValue("points", out var pts))
                points = SvgPolylineElement.ParsePoints(pts);
        }
    }

    /// <summary>
    /// Represents an SVG path element with draw commands.
    /// </summary>
    public class SvgPathElement : SvgElement
    {
        /// <inheritdoc/>
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
                    var param = new List<float>();
                    var matches2 = Regex.Matches(parametersString, @"[+-]?(?:\d+\.?\d*|\.\d+)(?:[eE][+-]?\d+)?");
                    for (int j = 0; j < matches2.Count; j++)
                        for (int k = 0; k < matches2[j].Groups.Count; k++)
                            param.Add(float.Parse(matches2[j].Groups[k].ToString(), CultureInfo.InvariantCulture));

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

    /// <summary>
    /// Represents a single draw command in an SVG path.
    /// </summary>
    public struct DrawCommand
    {
        /// <summary>The type of draw command.</summary>
        public DrawType type;

        /// <summary>Whether coordinates are relative to the current position.</summary>
        public bool relative;

        /// <summary>The parameters for this command.</summary>
        public float[] param;

        /// <summary>
        /// Returns a string representation of this draw command.
        /// </summary>
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

    /// <summary>
    /// Specifies the type of SVG path draw command.
    /// </summary>
    public enum DrawType
    {
        /// <summary>Move to a new position without drawing.</summary>
        MoveTo,
        /// <summary>Draw a line to the specified position.</summary>
        LineTo,
        /// <summary>Draw a vertical line to the specified Y coordinate.</summary>
        VerticalLineTo,
        /// <summary>Draw a horizontal line to the specified X coordinate.</summary>
        HorizontalLineTo,
        /// <summary>Draw a cubic Bezier curve.</summary>
        CubicCurveTo,
        /// <summary>Draw a smooth cubic Bezier curve (control point reflected from previous).</summary>
        SmoothCubicCurveTo,
        /// <summary>Draw a quadratic Bezier curve.</summary>
        QuadraticCurveTo,
        /// <summary>Draw a smooth quadratic Bezier curve (control point reflected from previous).</summary>
        SmoothQuadraticCurveTo,
        /// <summary>Draw an elliptical arc.</summary>
        ArcTo,
        /// <summary>Close the current path by drawing a line to the start.</summary>
        ClosePath
    }

    /// <summary>
    /// Provides methods for parsing SVG documents into SvgElement trees.
    /// </summary>
    public static class SVGParser
    {
        /// <summary>
        /// Parses an SVG document from a file path.
        /// </summary>
        /// <param name="filePath">The path to the SVG file.</param>
        /// <returns>The root SvgElement representing the parsed document.</returns>
        /// <exception cref="FileNotFoundException">Thrown when the file does not exist.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the document is not a valid SVG.</exception>
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
                case SvgElement.TagType.line:
                    svgElement = new SvgLineElement();
                    break;
                case SvgElement.TagType.polyline:
                    svgElement = new SvgPolylineElement();
                    break;
                case SvgElement.TagType.polygon:
                    svgElement = new SvgPolygonElement();
                    break;
                case SvgElement.TagType.ellipse:
                    svgElement = new SvgEllipseElement();
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
