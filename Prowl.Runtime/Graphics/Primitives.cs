// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
using System;

namespace Prowl.Runtime;

public enum TextureWrap { Repeat, ClampToBorder, ClampToEdge, MirroredRepeat }
public enum TextureType { Texture2D, Texture3D, }
public enum TextureParameter { WrapS, WrapT, WrapR, MinFilter, MagFilter }
public enum TextureMin { Nearest, Linear, NearestMipmapNearest, LinearMipmapNearest, NearestMipmapLinear, LinearMipmapLinear }
public enum TextureMag { Nearest, Linear }
public enum TextureImageFormat
{
    Color4b,
    Byte,

    Short,
    Short2,
    Short3,
    Short4,

    Float,
    Float2,
    Float3,
    Float4,
    Depth16f,
    Depth24f,
    Depth32f,

    Int,
    Int2,
    Int3,
    Int4,

    UnsignedShort,
    UnsignedShort2,
    UnsignedShort3,
    UnsignedShort4,

    UnsignedInt,
    UnsignedInt2,
    UnsignedInt3,
    UnsignedInt4,

    Depth24Stencil8,
}

public enum Topology { Points, Lines, LineLoop, LineStrip, Triangles, TriangleStrip, TriangleFan, Quads }

public enum FBOTarget { Read, Draw, Framebuffer, }

[Flags]
public enum ClearFlags
{
    Color = 1 << 1,
    Depth = 1 << 2,
    Stencil = 1 << 3,
}

public enum BlitFilter { Nearest, Linear }

public enum BufferType { VertexBuffer, ElementsBuffer, UniformBuffer, StructuredBuffer, Count }

public struct RasterizerState
{
    public enum DepthMode { Never, Less, Equal, Lequal, Greater, Notequal, Gequal, Always }
    public enum Blending { Zero, One, SrcColor, OneMinusSrcColor, DstColor, OneMinusDstColor, SrcAlpha, OneMinusSrcAlpha, DstAlpha, OneMinusDstAlpha, ConstantColor, OneMinusConstantColor, ConstantAlpha, OneMinusConstantAlpha, SrcAlphaSaturate, Src1Color, OneMinusSrc1Color, Src1Alpha, OneMinusSrc1Alpha }
    public enum BlendMode { Add, Subtract, ReverseSubtract, Min, Max }
    public enum PolyFace { None, Front, Back, FrontAndBack }
    public enum WindingOrder { CW, CCW }

    public bool DepthTest = true;
    public bool DepthWrite = true;
    public DepthMode Depth = DepthMode.Lequal;

    public bool DoBlend = false;
    public Blending BlendSrc = Blending.SrcAlpha;
    public Blending BlendDst = Blending.OneMinusSrcAlpha;
    public BlendMode Blend = BlendMode.Add;

    public PolyFace CullFace = PolyFace.Back;

    public WindingOrder Winding = WindingOrder.CW;

    public RasterizerState()
    {
        // Default constructor
    }
}
