// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Prowl.Runtime.GraphicsBackend;

public class VertexFormat
{
    public Element[] Elements;
    public int Size;

    public VertexFormat() { }

    public VertexFormat(Element[] elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        if (elements.Length == 0) throw new($"The argument '{nameof(elements)}' is null!");

        Elements = elements;

        foreach (var element in Elements)
        {
            element.Offset = (short)Size;
            int s = 0;
            if ((int)element.Type > 5122) s = 4 * element.Count; //Greater than short then its either a Float or Int
            else if ((int)element.Type > 5121) s = 2 * element.Count; //Greater than byte then its a Short
            else s = 1 * element.Count; //Byte or Unsigned Byte
            Size += s;
            element.Stride = (short)s;
        }
    }

    public class Element
    {
        public uint Semantic;
        public VertexType Type;
        public byte Count;
        public short Offset; // Automatically assigned in VertexFormats constructor
        public short Stride; // Automatically assigned in VertexFormats constructor
        public short Divisor;
        public bool Normalized;
        public Element() { }
        public Element(VertexSemantic semantic, VertexType type, byte count, short divisor = 0, bool normalized = false)
        {
            Semantic = (uint)semantic;
            Type = type;
            Count = count;
            Divisor = divisor;
            Normalized = normalized;
        }
        public Element(uint semantic, VertexType type, byte count, short divisor = 0, bool normalized = false)
        {
            Semantic = semantic;
            Type = type;
            Count = count;
            Divisor = divisor;
            Normalized = normalized;
        }
    }

    public enum VertexSemantic { Position, TexCoord0, TexCoord1, Normal, Color, Tangent, BoneIndex, BoneWeight }

    public enum VertexType { Byte = 5120, UnsignedByte = 5121, Short = 5122, Int = 5124, Float = 5126, }
}
