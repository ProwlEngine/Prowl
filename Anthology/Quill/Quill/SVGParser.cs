using Prowl.Vector;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using Color = Prowl.Vector.Color;

namespace Prowl.Quill
{
    /// <summary>
    /// The resolved paint state inherited from a parent element (fill, stroke, stroke-width and
    /// accumulated opacity). Passed down the element tree so groups propagate their style to children.
    /// </summary>
    public struct SvgPaintContext
    {
        public SvgElement.ColorType fillType;
        public Color32 fill;
        public SvgElement.ColorType strokeType;
        public Color32 stroke;
        public float strokeWidth;
        public float opacity;

        /// <summary>The default paint at the root of a document: no fill, no stroke, unit stroke width.</summary>
        public static SvgPaintContext Root => new SvgPaintContext
        {
            fillType = SvgElement.ColorType.none,
            strokeType = SvgElement.ColorType.none,
            strokeWidth = 1f,
            opacity = 1f
        };
    }

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
        /// The stroke color of this element, with opacity already applied.
        /// </summary>
        public Color32 stroke;

        /// <summary>
        /// The fill color of this element, with opacity already applied.
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
        /// The accumulated opacity of this element (its own opacity times all ancestor opacities).
        /// </summary>
        public float opacity = 1f;

        // Parsed inline "style" declarations, which take precedence over presentation attributes.
        private Dictionary<string, string> _style;

        // Fill/stroke before opacity is applied, propagated to children so opacity isn't compounded twice.
        private Color32 _rawFill;
        private Color32 _rawStroke;

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
        /// Resolves this element's paint (fill, stroke, stroke-width, opacity), inheriting any value
        /// not specified on the element itself from the supplied parent context.
        /// </summary>
        public virtual void Parse(SvgPaintContext inherited)
        {
            if (Attributes.TryGetValue("style", out var styleText))
                _style = ParseStyle(styleText);

            string fillProp = GetProperty("fill");
            if (fillProp == null)
            {
                fillType = inherited.fillType;
                _rawFill = inherited.fill;
            }
            else
            {
                fillType = ResolveColorType(fillProp);
                _rawFill = fillType == ColorType.specific ? ColorParser.Parse(fillProp.Trim()) : default;
            }

            string strokeProp = GetProperty("stroke");
            if (strokeProp == null)
            {
                strokeType = inherited.strokeType;
                _rawStroke = inherited.stroke;
            }
            else
            {
                strokeType = ResolveColorType(strokeProp);
                _rawStroke = strokeType == ColorType.specific ? ColorParser.Parse(strokeProp.Trim()) : default;
            }

            string widthProp = GetProperty("stroke-width");
            strokeWidth = widthProp != null && TryParseLeadingNumber(widthProp, out var sw) ? sw : inherited.strokeWidth;

            // Element opacity accumulates down the tree; fill/stroke-opacity modulate only their own channel.
            opacity = inherited.opacity * ReadNormalized("opacity", 1f);
            float fillAlpha = opacity * ReadNormalized("fill-opacity", 1f);
            float strokeAlpha = opacity * ReadNormalized("stroke-opacity", 1f);

            fill = fillType == ColorType.specific ? WithAlpha(_rawFill, fillAlpha) : _rawFill;
            stroke = strokeType == ColorType.specific ? WithAlpha(_rawStroke, strokeAlpha) : _rawStroke;
        }

        /// <summary>
        /// The paint context handed to this element's children (raw colors plus accumulated opacity).
        /// </summary>
        public SvgPaintContext GetPaintContext() => new SvgPaintContext
        {
            fillType = fillType,
            fill = _rawFill,
            strokeType = strokeType,
            stroke = _rawStroke,
            strokeWidth = strokeWidth,
            opacity = opacity
        };

        /// <summary>Gets a presentation property, preferring an inline style declaration over an attribute.</summary>
        protected string GetProperty(string key)
        {
            if (_style != null && _style.TryGetValue(key, out var v))
                return v;
            if (Attributes.TryGetValue(key, out var a))
                return a;
            return null;
        }

        /// <summary>Whether a presentation property is present as either an inline style or an attribute.</summary>
        protected bool HasProperty(string key)
            => (_style != null && _style.ContainsKey(key)) || Attributes.ContainsKey(key);

        /// <summary>Reads a numeric property, tolerating unit suffixes (e.g. "2px"), returning 0 if absent.</summary>
        protected float ParseFloat(string key)
        {
            var v = GetProperty(key);
            if (v != null && TryParseLeadingNumber(v, out var result))
                return result;
            return 0;
        }

        // Reads an opacity-like property in the 0..1 range, accepting a raw number or a percentage.
        private float ReadNormalized(string key, float fallback)
        {
            var v = GetProperty(key);
            if (v == null)
                return fallback;
            v = v.Trim();
            bool percent = v.EndsWith("%", StringComparison.Ordinal);
            if (!TryParseLeadingNumber(v, out var num))
                return fallback;
            if (percent)
                num /= 100f;
            return num < 0f ? 0f : (num > 1f ? 1f : num);
        }

        private static Dictionary<string, string> ParseStyle(string style)
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var declaration in style.Split(';'))
            {
                int colon = declaration.IndexOf(':');
                if (colon <= 0)
                    continue;
                var key = declaration.Substring(0, colon).Trim();
                var value = declaration.Substring(colon + 1).Trim();
                if (key.Length > 0 && value.Length > 0)
                    dict[key] = value;
            }
            return dict;
        }

        private static ColorType ResolveColorType(string value)
        {
            value = value.Trim();
            if (value.Length == 0 || value.Equals("none", StringComparison.OrdinalIgnoreCase) || value.Equals("transparent", StringComparison.OrdinalIgnoreCase))
                return ColorType.none;
            if (value.Equals("currentColor", StringComparison.OrdinalIgnoreCase))
                return ColorType.currentColor;
            if (value.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
                return ColorType.none; // gradients and patterns are not supported
            return ColorType.specific;
        }

        private static Color32 WithAlpha(Color32 color, float multiplier)
        {
            if (multiplier >= 1f)
                return color;
            if (multiplier < 0f)
                multiplier = 0f;
            int a = (int)(color.A * multiplier + 0.5f);
            color.A = (byte)(a > 255 ? 255 : a);
            return color;
        }

        private static bool TryParseLeadingNumber(string s, out float value)
        {
            var scanner = new PathScanner(s.AsSpan());
            return scanner.TryReadNumber(out value);
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
        public override void Parse(SvgPaintContext inherited)
        {
            base.Parse(inherited);
            pos.X = ParseFloat("x");
            pos.Y = ParseFloat("y");
            size.X = ParseFloat("width");
            size.Y = ParseFloat("height");

            // Per SVG, a missing rx/ry defaults to the other axis; both missing means square corners.
            bool hasRx = HasProperty("rx");
            bool hasRy = HasProperty("ry");
            if (!hasRx && !hasRy)
            {
                radius = Float2.Zero;
            }
            else
            {
                float rx = hasRx ? ParseFloat("rx") : ParseFloat("ry");
                float ry = hasRy ? ParseFloat("ry") : ParseFloat("rx");
                radius = new Float2(rx, ry);
            }
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
        public override void Parse(SvgPaintContext inherited)
        {
            base.Parse(inherited);
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
        public override void Parse(SvgPaintContext inherited)
        {
            base.Parse(inherited);
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
        public override void Parse(SvgPaintContext inherited)
        {
            base.Parse(inherited);
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
        public override void Parse(SvgPaintContext inherited)
        {
            base.Parse(inherited);
            var pts = GetProperty("points");
            if (pts != null)
                points = ParsePoints(pts);
        }

        internal static Float2[] ParsePoints(string pts)
        {
            var scanner = new PathScanner(pts.AsSpan());
            var list = new List<Float2>();
            while (scanner.TryReadNumber(out var x) && scanner.TryReadNumber(out var y))
                list.Add(new Float2(x, y));
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
        public override void Parse(SvgPaintContext inherited)
        {
            base.Parse(inherited);
            var pts = GetProperty("points");
            if (pts != null)
                points = SvgPolylineElement.ParsePoints(pts);
        }
    }

    /// <summary>
    /// Represents an SVG path element with draw commands.
    /// </summary>
    public class SvgPathElement : SvgElement
    {
        /// <inheritdoc/>
        public override void Parse(SvgPaintContext inherited)
        {
            base.Parse(inherited);

            if (!Attributes.TryGetValue("d", out var pathData) || string.IsNullOrEmpty(pathData))
            {
                drawCommands = Array.Empty<DrawCommand>();
                return;
            }

            drawCommands = ParsePathData(pathData);
        }

        /// <summary>
        /// Parses an SVG path "d" string into draw commands in a single pass. Handles scientific
        /// notation, sign-delimited and comma/space-delimited numbers, packed arc flags, and implicit
        /// command repetition (e.g. "M0 0 1 1 2 2" becomes a move followed by two line-tos).
        /// </summary>
        private static DrawCommand[] ParsePathData(string d)
        {
            var commands = new List<DrawCommand>();
            var scanner = new PathScanner(d.AsSpan());
            char command = '\0';

            while (true)
            {
                scanner.SkipSeparators();
                if (scanner.AtEnd)
                    break;

                if (scanner.TryReadCommand(out var c))
                    command = c;
                else if (command == '\0')
                    break; // a number before any command; malformed, stop

                bool relative = char.IsLower(command);
                switch (char.ToLowerInvariant(command))
                {
                    case 'm':
                        if (!scanner.TryReadPoint(out var mx, out var my)) return commands.ToArray();
                        commands.Add(Command(DrawType.MoveTo, relative, mx, my));
                        command = relative ? 'l' : 'L'; // implicit pairs after a move are line-tos
                        break;
                    case 'l':
                        if (!scanner.TryReadPoint(out var lx, out var ly)) return commands.ToArray();
                        commands.Add(Command(DrawType.LineTo, relative, lx, ly));
                        break;
                    case 'h':
                        if (!scanner.TryReadNumber(out var hx)) return commands.ToArray();
                        commands.Add(Command(DrawType.HorizontalLineTo, relative, hx));
                        break;
                    case 'v':
                        if (!scanner.TryReadNumber(out var vy)) return commands.ToArray();
                        commands.Add(Command(DrawType.VerticalLineTo, relative, vy));
                        break;
                    case 'c':
                        if (!scanner.TryReadNumbers(6, out var cp)) return commands.ToArray();
                        commands.Add(Command(DrawType.CubicCurveTo, relative, cp));
                        break;
                    case 's':
                        if (!scanner.TryReadNumbers(4, out var sp)) return commands.ToArray();
                        commands.Add(Command(DrawType.SmoothCubicCurveTo, relative, sp));
                        break;
                    case 'q':
                        if (!scanner.TryReadNumbers(4, out var qp)) return commands.ToArray();
                        commands.Add(Command(DrawType.QuadraticCurveTo, relative, qp));
                        break;
                    case 't':
                        if (!scanner.TryReadPoint(out var tx, out var ty)) return commands.ToArray();
                        commands.Add(Command(DrawType.SmoothQuadraticCurveTo, relative, tx, ty));
                        break;
                    case 'a':
                        if (!scanner.TryReadArc(out var ap)) return commands.ToArray();
                        commands.Add(Command(DrawType.ArcTo, relative, ap));
                        break;
                    case 'z':
                        commands.Add(new DrawCommand { type = DrawType.ClosePath, relative = relative });
                        command = '\0'; // nothing may implicitly follow a close
                        break;
                    default:
                        return commands.ToArray();
                }
            }

            return commands.ToArray();
        }

        private static DrawCommand Command(DrawType type, bool relative, params float[] param)
            => new DrawCommand { type = type, relative = relative, param = param };

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
    /// A forward-only scanner over SVG numeric data (path "d" strings and point lists). Reads numbers
    /// per the SVG grammar without allocating, so it correctly handles exponents, sign-delimited values
    /// and packed arc flags.
    /// </summary>
    internal ref struct PathScanner
    {
        private readonly ReadOnlySpan<char> _s;
        private int _pos;

        public PathScanner(ReadOnlySpan<char> s)
        {
            _s = s;
            _pos = 0;
        }

        public bool AtEnd => _pos >= _s.Length;

        public void SkipSeparators()
        {
            while (_pos < _s.Length)
            {
                char c = _s[_pos];
                if (c == ' ' || c == ',' || c == '\t' || c == '\n' || c == '\r' || c == '\f')
                    _pos++;
                else
                    break;
            }
        }

        public bool TryReadCommand(out char command)
        {
            if (_pos < _s.Length && IsCommand(_s[_pos]))
            {
                command = _s[_pos];
                _pos++;
                return true;
            }
            command = '\0';
            return false;
        }

        public bool TryReadNumber(out float value)
        {
            SkipSeparators();
            int start = _pos;

            if (_pos < _s.Length && (_s[_pos] == '+' || _s[_pos] == '-'))
                _pos++;

            bool hasDigits = false;
            while (_pos < _s.Length && IsDigit(_s[_pos])) { _pos++; hasDigits = true; }
            if (_pos < _s.Length && _s[_pos] == '.')
            {
                _pos++;
                while (_pos < _s.Length && IsDigit(_s[_pos])) { _pos++; hasDigits = true; }
            }

            if (!hasDigits)
            {
                _pos = start;
                value = 0;
                return false;
            }

            if (_pos < _s.Length && (_s[_pos] == 'e' || _s[_pos] == 'E'))
            {
                int mark = _pos;
                _pos++;
                if (_pos < _s.Length && (_s[_pos] == '+' || _s[_pos] == '-'))
                    _pos++;
                if (_pos < _s.Length && IsDigit(_s[_pos]))
                    while (_pos < _s.Length && IsDigit(_s[_pos])) _pos++;
                else
                    _pos = mark; // not a valid exponent, leave it for the next token
            }

            value = float.Parse(_s.Slice(start, _pos - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            return true;
        }

        // Arc flags are single '0'/'1' characters and may be packed together with no separator.
        public bool TryReadFlag(out float value)
        {
            SkipSeparators();
            if (_pos < _s.Length && (_s[_pos] == '0' || _s[_pos] == '1'))
            {
                value = _s[_pos] - '0';
                _pos++;
                return true;
            }
            value = 0;
            return false;
        }

        public bool TryReadPoint(out float x, out float y)
        {
            x = 0;
            y = 0;
            return TryReadNumber(out x) && TryReadNumber(out y);
        }

        public bool TryReadNumbers(int count, out float[] values)
        {
            var arr = new float[count];
            for (int i = 0; i < count; i++)
            {
                if (!TryReadNumber(out arr[i]))
                {
                    values = null;
                    return false;
                }
            }
            values = arr;
            return true;
        }

        public bool TryReadArc(out float[] values)
        {
            var arr = new float[7];
            if (TryReadNumber(out arr[0]) && TryReadNumber(out arr[1]) && TryReadNumber(out arr[2])
                && TryReadFlag(out arr[3]) && TryReadFlag(out arr[4])
                && TryReadNumber(out arr[5]) && TryReadNumber(out arr[6]))
            {
                values = arr;
                return true;
            }
            values = null;
            return false;
        }

        private static bool IsDigit(char c) => c >= '0' && c <= '9';

        private static bool IsCommand(char c)
        {
            switch (c)
            {
                case 'M': case 'm': case 'L': case 'l':
                case 'H': case 'h': case 'V': case 'v':
                case 'C': case 'c': case 'S': case 's':
                case 'Q': case 'q': case 'T': case 't':
                case 'A': case 'a': case 'Z': case 'z':
                    return true;
                default:
                    return false;
            }
        }
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
                return ParseXmlElement(xmlDoc.DocumentElement, 0, SvgPaintContext.Root);
            else
                throw new InvalidOperationException("Invalid SVG document: Missing root <svg> element.");
        }

        private static SvgElement ParseXmlElement(XmlElement xmlElement, int depth, SvgPaintContext inherited)
        {
            if (!Enum.TryParse<SvgElement.TagType>(xmlElement.Name, true, out var tag))
                return null;

            SvgElement svgElement = tag switch
            {
                SvgElement.TagType.path => new SvgPathElement(),
                SvgElement.TagType.circle => new SvgCircleElement(),
                SvgElement.TagType.rect => new SvgRectElement(),
                SvgElement.TagType.line => new SvgLineElement(),
                SvgElement.TagType.polyline => new SvgPolylineElement(),
                SvgElement.TagType.polygon => new SvgPolygonElement(),
                SvgElement.TagType.ellipse => new SvgEllipseElement(),
                _ => new SvgElement(),
            };
            svgElement.depth = depth;
            svgElement.tag = tag;

            foreach (XmlAttribute attribute in xmlElement.Attributes)
                svgElement.Attributes[attribute.Name] = attribute.Value;

            svgElement.Parse(inherited);

            var childContext = svgElement.GetPaintContext();
            foreach (XmlNode childNode in xmlElement.ChildNodes)
                if (childNode.NodeType == XmlNodeType.Element)
                {
                    var child = ParseXmlElement((XmlElement)childNode, depth + 1, childContext);
                    if (child != null)
                        svgElement.Children.Add(child);
                }

            return svgElement;
        }
    }
}
