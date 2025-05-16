using System;
using System.Collections.Generic;
using System.Xml;
using System.IO;

namespace Prowl.Quill
{
    public class SvgElement
    {
        public TagType tag;
        public int depth;
        public Dictionary<string, string> Attributes { get; }
        public List<SvgElement> Children { get; }

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

        public enum TagType
        {
            svg,
            path,
            circle,
            g,
        }
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
            SvgElement svgElement = new SvgElement();

            svgElement.depth = depth;
            svgElement.tag = Enum.Parse<SvgElement.TagType>(xmlElement.Name);

            foreach (XmlAttribute attribute in xmlElement.Attributes)
                svgElement.Attributes[attribute.Name] = attribute.Value;

            foreach (XmlNode childNode in xmlElement.ChildNodes)
                if (childNode.NodeType == XmlNodeType.Element)
                    svgElement.Children.Add(ParseXmlElement((XmlElement)childNode, depth + 1));

            return svgElement;
        }
    }
}
