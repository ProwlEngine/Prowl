using System;
using System.Collections.Generic;
using System.Numerics;
using System.IO;
using System.Linq;
using System.Text;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;

namespace AssimpSharp.Formats.Ply
{
    public enum EDataType
    {
        Char,
        UChar,
        Short,
        UShort,
        Int,
        UInt,
        Float,
        Double,
        Invalid
    }

    public static class EDataTypeExtensions
    {
        public static EDataType FromString(string str)
        {
            switch (str.ToLower())
            {
                case "char":
                case "int8": return EDataType.Char;
                case "uchar":
                case "uint8": return EDataType.UChar;
                case "short":
                case "int16": return EDataType.Short;
                case "ushort":
                case "uint16": return EDataType.UShort;
                case "int32":
                case "int": return EDataType.Int;
                case "uint32":
                case "uint": return EDataType.UInt;
                case "float":
                case "float32": return EDataType.Float;
                case "double64":
                case "double":
                case "float64": return EDataType.Double;
                default:
                    Console.WriteLine("Found unknown data type in PLY file. This is OK");
                    return EDataType.Invalid;
            }
        }
    }

    public enum ESemantic
    {
        XCoord, YCoord, ZCoord,
        XNormal, YNormal, ZNormal,
        UTextureCoord, VTextureCoord,
        Red, Green, Blue, Alpha,
        VertexIndex,
        TextureIndex,
        TextureCoordinates,
        MaterialIndex,
        AmbientRed, AmbientGreen, AmbientBlue, AmbientAlpha,
        DiffuseRed, DiffuseGreen, DiffuseBlue, DiffuseAlpha,
        SpecularRed, SpecularGreen, SpecularBlue, SpecularAlpha,
        PhongPower,
        Opacity,
        Invalid
    }

    public static class ESemanticExtensions
    {
        public static ESemantic FromString(string str)
        {
            switch (str.ToLower())
            {
                case "red":
                case "r": return ESemantic.Red;
                case "green":
                case "g": return ESemantic.Green;
                case "blue":
                case "b": return ESemantic.Blue;
                case "alpha": return ESemantic.Alpha;
                case "vertex_index":
                case "vertex_indices": return ESemantic.VertexIndex;
                case "material_index": return ESemantic.MaterialIndex;
                case "ambient_red": return ESemantic.AmbientRed;
                case "ambient_green": return ESemantic.AmbientGreen;
                case "ambient_blue": return ESemantic.AmbientBlue;
                case "ambient_alpha": return ESemantic.AmbientAlpha;
                case "diffuse_red": return ESemantic.DiffuseRed;
                case "diffuse_green": return ESemantic.DiffuseGreen;
                case "diffuse_blue": return ESemantic.DiffuseBlue;
                case "diffuse_alpha": return ESemantic.DiffuseAlpha;
                case "specular_red": return ESemantic.SpecularRed;
                case "specular_green": return ESemantic.SpecularGreen;
                case "specular_blue": return ESemantic.SpecularBlue;
                case "specular_alpha": return ESemantic.SpecularAlpha;
                case "opacity": return ESemantic.Opacity;
                case "specular_power": return ESemantic.PhongPower;
                case "u":
                case "s":
                case "tx": return ESemantic.UTextureCoord;
                case "v":
                case "t":
                case "ty": return ESemantic.VTextureCoord;
                case "x": return ESemantic.XCoord;
                case "y": return ESemantic.YCoord;
                case "z": return ESemantic.ZCoord;
                case "nx": return ESemantic.XNormal;
                case "ny": return ESemantic.YNormal;
                case "nz": return ESemantic.ZNormal;
                default:
                    Console.WriteLine("Found unknown property semantic in file. This is ok");
                    return ESemantic.Invalid;
            }
        }
    }

    public enum EElementSemantic
    {
        Vertex,
        Face,
        TriStrip,
        Edge,
        Material,
        Invalid
    }

    public static class EElementSemanticExtensions
    {
        public static EElementSemantic FromString(string str)
        {
            switch (str.ToLower())
            {
                case "vertex": return EElementSemantic.Vertex;
                case "face": return EElementSemantic.Face;
                case "tristrips": return EElementSemantic.TriStrip;
                case "edge": return EElementSemantic.Edge;
                case "material": return EElementSemantic.Material;
                default: return EElementSemantic.Invalid;
            }
        }
    }

    public class Property
    {
        public EDataType Type { get; set; } = EDataType.Int;
        public ESemantic Semantic { get; set; } = ESemantic.Invalid;
        public string Name { get; set; } = "";
        public bool IsList { get; set; } = false;
        public EDataType FirstType { get; set; } = EDataType.UChar;

        public static bool ParseProperty(ByteBuffer parser, Property pOut)
        {
            // Skip leading spaces
            parser.SkipSpaces();

            // Read "property" keyword
            string keyword = parser.NextWord();
            if (keyword != "property")
            {
                parser.Position -= keyword.Length;
                return false;
            }

            parser.SkipSpaces();

            string token = parser.NextWord();
            if (token == "list")
            {
                pOut.IsList = true;
                pOut.FirstType = EDataTypeExtensions.FromString(parser.NextWord());
                if (pOut.FirstType == EDataType.Invalid)
                {
                    parser.SkipLine();
                    return false;
                }

                parser.SkipSpaces();
                pOut.Type = EDataTypeExtensions.FromString(parser.NextWord());
                if (pOut.Type == EDataType.Invalid)
                {
                    parser.SkipLine();
                    return false;
                }
            }
            else
            {
                pOut.Type = EDataTypeExtensions.FromString(token);
                if (pOut.Type == EDataType.Invalid)
                {
                    parser.SkipLine();
                    return false;
                }
            }

            parser.SkipSpaces();

            token = parser.NextWord();
            pOut.Semantic = ESemanticExtensions.FromString(token);
            if (pOut.Semantic == ESemantic.Invalid)
            {
                parser.SkipLine();
                pOut.Name = token;
            }

            parser.SkipSpacesAndLineEnd();
            return true;
        }
    }

    public class Element
    {
        public List<Property> Properties { get; set; } = new List<Property>();
        public EElementSemantic Semantic { get; set; } = EElementSemantic.Invalid;
        public string Name { get; set; }
        public int NumOccurrences { get; set; } = 0;

        public static bool ParseElement(ByteBuffer parser, Element pOut)
        {
            parser.SkipSpaces();

            string keyword = parser.NextWord();
            if (keyword != "element")
            {
                parser.Position -= keyword.Length;
                return false;
            }

            parser.SkipSpaces();

            string token = parser.NextWord();
            pOut.Semantic = EElementSemanticExtensions.FromString(token);
            if (pOut.Semantic == EElementSemantic.Invalid)
            {
                pOut.Name = token;
            }

            parser.SkipSpaces();

            pOut.NumOccurrences = int.Parse(parser.NextWord());

            parser.SkipSpacesAndLineEnd();

            while (true)
            {
                DOM.SkipComments(parser);

                var prop = new Property();
                if (!Property.ParseProperty(parser, prop)) break;
                pOut.Properties.Add(prop);
            }

            return true;
        }
    }

    public class PropertyInstance
    {
        public List<object> Values { get; set; } = new List<object>();

        public static bool ParseInstance(ByteBuffer parser, Property prop, List<PropertyInstance> pOut)
        {
            parser.SkipSpaces();

            pOut.Add(new PropertyInstance());
            if (prop.IsList)
            {
                string[] words = parser.RestOfLine().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                int iNum = Convert.ToInt32(words[0]);
                for (int i = 0; i < iNum; i++)
                {
                    ParseValue(words[i + 1], prop.Type, pOut[pOut.Count - 1].Values);
                }
            }
            else
            {
                ParseValue(parser.NextWord(), prop.Type, pOut[pOut.Count - 1].Values);
            }

            parser.SkipSpacesAndLineEnd();
            return true;
        }

        public static bool ParseInstanceBinary(ByteBuffer parser, Property prop, List<PropertyInstance> pOut)
        {
            pOut.Add(new PropertyInstance());
            if (prop.IsList)
            {
                List<object> iNum = new List<object>();
                ParseValueBinary(parser, prop.FirstType, iNum);
                int count = Convert.ToInt32(iNum[0]);
                for (int i = 0; i < count; i++)
                {
                    ParseValueBinary(parser, prop.Type, pOut[pOut.Count - 1].Values);
                }
            }
            else
            {
                ParseValueBinary(parser, prop.Type, pOut[pOut.Count - 1].Values);
            }
            return true;
        }

        private static void ParseValue(string value, EDataType eType, List<object> outList)
        {
            switch (eType)
            {
                case EDataType.UInt:
                case EDataType.UShort:
                case EDataType.UChar:
                    outList.Add(uint.Parse(value));
                    break;
                case EDataType.Int:
                case EDataType.Short:
                case EDataType.Char:
                    outList.Add(int.Parse(value));
                    break;
                case EDataType.Float:
                    outList.Add(float.Parse(value));
                    break;
                case EDataType.Double:
                    outList.Add(double.Parse(value));
                    break;
            }
        }

        private static void ParseValueBinary(ByteBuffer parser, EDataType eType, List<object> outList)
        {
            object value = null;
            switch (eType)
            {
                case EDataType.UInt:
                    value = parser.ReadUInt32();
                    if ((uint)value > 1000000) System.Diagnostics.Debugger.Break();
                    break;
                case EDataType.Int:
                    value = parser.ReadInt32();
                    if (Math.Abs((int)value) > 1000000) System.Diagnostics.Debugger.Break();
                    break;
                case EDataType.UShort:
                    value = parser.ReadUInt16();
                    break;
                case EDataType.Short:
                    value = parser.ReadInt16();
                    break;
                case EDataType.UChar:
                    value = parser.ReadByte();
                    break;
                case EDataType.Char:
                    value = parser.ReadSByte();
                    break;
                case EDataType.Float:
                    value = parser.ReadSingle();
                    if ((float)value > 1000000) System.Diagnostics.Debugger.Break();
                    break;
                case EDataType.Double:
                    value = parser.ReadDouble();
                    if ((double)value > 1000000) System.Diagnostics.Debugger.Break();
                    break;
            }
            outList.Add(value);
        }

        public static object DefaultValue(EDataType eType)
        {
            switch (eType)
            {
                case EDataType.Float:
                    return 0f;
                case EDataType.Double:
                    return 0.0;
                default:
                    return 0;
            }
        }
    }

    public class ElementInstance
    {
        public List<PropertyInstance> Properties { get; set; } = new List<PropertyInstance>();

        public static bool ParseInstance(ByteBuffer parser, Element element, List<ElementInstance> pOut)
        {
            parser.SkipSpaces();

            pOut.Add(new ElementInstance());
            foreach (var prop in element.Properties)
            {
                if (!PropertyInstance.ParseInstance(parser, prop, pOut[pOut.Count - 1].Properties))
                {
                    Console.Error.WriteLine("Unable to parse property instance. Skipping this element instance");
                    parser.SkipLine();

                    var defaultProperty = new PropertyInstance();
                    defaultProperty.Values.Add(PropertyInstance.DefaultValue(prop.Type));
                    pOut[pOut.Count - 1].Properties.Add(defaultProperty);
                }
            }
            return true;
        }

        public static bool ParseInstanceBinary(ByteBuffer parser, Element element, List<ElementInstance> pOut)
        {
            pOut.Add(new ElementInstance());
            foreach (var prop in element.Properties)
            {
                if (!PropertyInstance.ParseInstanceBinary(parser, prop, pOut[pOut.Count - 1].Properties))
                {
                    Console.Error.WriteLine("Unable to parse binary property instance. Skipping this element instance");

                    var defaultProperty = new PropertyInstance();
                    defaultProperty.Values.Add(PropertyInstance.DefaultValue(prop.Type));
                    pOut[pOut.Count - 1].Properties.Add(defaultProperty);
                }
            }
            return true;
        }
    }

    public class ElementInstanceList
    {
        public List<ElementInstance> Instances { get; set; } = new List<ElementInstance>();

        public static bool ParseInstanceList(ByteBuffer parser, Element element, List<ElementInstanceList> pOut)
        {
            if (element.Semantic == EElementSemantic.Invalid || element.Properties.Count == 0)
            {
                for (int i = 0; i < element.NumOccurrences; i++)
                {
                    DOM.SkipComments(parser);
                    parser.SkipLine();
                }
            }
            else
            {
                pOut.Add(new ElementInstanceList());
                for (int i = 0; i < element.NumOccurrences; i++)
                {
                    DOM.SkipComments(parser);
                    ElementInstance.ParseInstance(parser, element, pOut[pOut.Count - 1].Instances);
                }
            }
            return true;
        }

        public static void ParseInstanceListBinary(ByteBuffer parser, Element element, List<ElementInstanceList> pOut)
        {
            pOut.Add(new ElementInstanceList());
            for (int i = 0; i < element.NumOccurrences; i++)
            {
                ElementInstance.ParseInstanceBinary(parser, element, pOut[pOut.Count - 1].Instances);
            }
        }
    }

    public class DOM
    {
        public List<Element> Elements { get; set; } = new List<Element>();
        public List<ElementInstanceList> ElementData { get; set; } = new List<ElementInstanceList>();

        public static bool ParseInstance(ByteBuffer parser, DOM pOut)
        {
            Console.WriteLine("PLY::DOM::ParseInstance() begin");

            if (!pOut.ParseHeader(parser, false))
            {
                Console.WriteLine("PLY::DOM::ParseInstance() failure");
                return false;
            }
            if (!pOut.ParseElementInstanceLists(parser))
            {
                Console.WriteLine("PLY::DOM::ParseInstance() failure");
                return false;
            }
            Console.WriteLine("PLY::DOM::ParseInstance() succeeded");
            return true;
        }

        public static bool ParseInstanceBinary(ByteBuffer parser, DOM pOut)
        {
            Console.WriteLine("PLY::DOM::ParseInstanceBinary() begin");

            if (!pOut.ParseHeader(parser, true))
            {
                Console.WriteLine("PLY::DOM::ParseInstanceBinary() failure");
                return false;
            }
            if (!pOut.ParseElementInstanceListsBinary(parser))
            {
                Console.WriteLine("PLY::DOM::ParseInstanceBinary() failure");
                return false;
            }
            Console.WriteLine("PLY::DOM::ParseInstanceBinary() succeeded");
            return true;
        }

        public static void SkipComments(ByteBuffer parser)
        {
            int position = parser.Position;
            parser.SkipSpaces();
            if (parser.NextWord() == "comment")
            {
                parser.SkipLine();
                SkipComments(parser);
            }
            else
            {
                parser.Position = position;
            }
        }

        private bool ParseHeader(ByteBuffer parser, bool isBinary)
        {
            Console.WriteLine("PLY::DOM::ParseHeader() begin");

            while (true)
            {
                SkipComments(parser);

                var element = new Element();
                if (Element.ParseElement(parser, element))
                {
                    Elements.Add(element);
                }
                else if (parser.NextWord() == "end_header")
                {
                    break;
                }
                else
                {
                    parser.SkipLine();
                }
            }

            if (!isBinary)
            {
                parser.SkipSpacesAndLineEnd();
            }

            Console.WriteLine("PLY::DOM::ParseHeader() succeeded");
            return true;
        }

        private bool ParseElementInstanceLists(ByteBuffer parser)
        {
            Console.WriteLine("PLY::DOM::ParseElementInstanceLists() begin");

            foreach (var element in Elements)
            {
                ElementInstanceList.ParseInstanceList(parser, element, ElementData);
            }

            Console.WriteLine("PLY::DOM::ParseElementInstanceLists() succeeded");
            return true;
        }

        private bool ParseElementInstanceListsBinary(ByteBuffer parser)
        {
            Console.WriteLine("PLY::DOM::ParseElementInstanceListsBinary() begin");

            foreach (var element in Elements)
            {
                ElementInstanceList.ParseInstanceListBinary(parser, element, ElementData);
            }

            Console.WriteLine("PLY::DOM::ParseElementInstanceListsBinary() succeeded");
            return true;
        }
    }

    public class Face
    {
        public int MaterialIndex { get; set; } = -1;
        public int[] Indices { get; set; } = new int[3];
    }

    public class ByteBuffer
    {
        private byte[] data;
        private int position;
        public bool IsBigEndian { get; set; }

        public ByteBuffer(byte[] data)
        {
            this.data = data;
            this.position = 0;
        }

        public int Position
        {
            get { return position; }
            set { position = value; }
        }

        public string NextWord()
        {
            StringBuilder sb = new StringBuilder();
            SkipSpaces();
            while (position < data.Length && !char.IsWhiteSpace((char)data[position]))
            {
                sb.Append((char)data[position]);
                position++;
            }
            return sb.ToString();
        }

        public void SkipSpaces()
        {
            while (position < data.Length && char.IsWhiteSpace((char)data[position]))
            {
                position++;
            }
        }

        public void SkipLine()
        {
            while (position < data.Length && data[position] != '\n')
            {
                position++;
            }
            if (position < data.Length) position++;
        }

        public void SkipSpacesAndLineEnd()
        {
            SkipSpaces();
            if (position < data.Length && data[position] == '\n') position++;
        }

        public string RestOfLine()
        {
            StringBuilder sb = new StringBuilder();
            while (position < data.Length && data[position] != '\n')
            {
                sb.Append((char)data[position]);
                position++;
            }
            if (position < data.Length) position++;
            return sb.ToString().Trim();
        }

        public bool StartsWith(string prefix)
        {
            if (data.Length - position < prefix.Length) return false;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (data[position + i] != prefix[i]) return false;
            }
            return true;
        }

        public uint ReadUInt32()
        {
            uint value = BitConverter.ToUInt32(data, position);
            if (IsBigEndian != BitConverter.IsLittleEndian)
            {
                value = ((value & 0x000000FF) << 24) |
                        ((value & 0x0000FF00) << 8) |
                        ((value & 0x00FF0000) >> 8) |
                        ((value & 0xFF000000) >> 24);
            }
            position += 4;
            return value;
        }

        public int ReadInt32()
        {
            int value = BitConverter.ToInt32(data, position);
            if (IsBigEndian != BitConverter.IsLittleEndian)
            {
                value = (int)(((value & 0x000000FF) << 24) |
                        ((value & 0x0000FF00) << 8) |
                        ((value & 0x00FF0000) >> 8) |
                        ((value & 0xFF000000) >> 24));
            }
            position += 4;
            return value;
        }

        public ushort ReadUInt16()
        {
            ushort value = BitConverter.ToUInt16(data, position);
            if (IsBigEndian != BitConverter.IsLittleEndian)
            {
                value = (ushort)(((value & 0x00FF) << 8) |
                                 ((value & 0xFF00) >> 8));
            }
            position += 2;
            return value;
        }

        public short ReadInt16()
        {
            short value = BitConverter.ToInt16(data, position);
            if (IsBigEndian != BitConverter.IsLittleEndian)
            {
                value = (short)(((value & 0x00FF) << 8) |
                                ((value & 0xFF00) >> 8));
            }
            position += 2;
            return value;
        }

        public byte ReadByte()
        {
            if (position >= data.Length)
                throw new EndOfStreamException("Attempted to read past the end of the buffer.");
            return data[position++];
        }

        public sbyte ReadSByte()
        {
            return (sbyte)data[position++];
        }

        public float ReadSingle()
        {
            if (IsBigEndian == BitConverter.IsLittleEndian)
            {
                float value = BitConverter.ToSingle(data, position);
                position += 4;
                return value;
            }
            else
            {
                byte[] reversed = new byte[4];
                Buffer.BlockCopy(data, position, reversed, 0, 4);
                Array.Reverse(reversed);
                position += 4;
                return BitConverter.ToSingle(reversed, 0);
            }
        }

        public double ReadDouble()
        {
            if (IsBigEndian == BitConverter.IsLittleEndian)
            {
                double value = BitConverter.ToDouble(data, position);
                position += 8;
                return value;
            }
            else
            {
                byte[] reversed = new byte[8];
                Buffer.BlockCopy(data, position, reversed, 0, 8);
                Array.Reverse(reversed);
                position += 8;
                return BitConverter.ToDouble(reversed, 0);
            }
        }
    }
}
