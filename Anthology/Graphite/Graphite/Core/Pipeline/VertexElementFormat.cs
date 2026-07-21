namespace Prowl.Graphite;

/// <summary>
/// Vertex element data format.
/// </summary>
public enum VertexElementFormat : byte
{
    /// <summary>
    /// 1x float32.
    /// </summary>
    Float1,
    /// <summary>
    /// 2x float32.
    /// </summary>
    Float2,
    /// <summary>
    /// 3x float32.
    /// </summary>
    Float3,
    /// <summary>
    /// 4x float32.
    /// </summary>
    Float4,
    /// <summary>
    /// 2x uint8, normalized.
    /// </summary>
    Byte2_Norm,
    /// <summary>
    /// 2x uint8.
    /// </summary>
    Byte2,
    /// <summary>
    /// 4x uint8, normalized.
    /// </summary>
    Byte4_Norm,
    /// <summary>
    /// 4x uint8.
    /// </summary>
    Byte4,
    /// <summary>
    /// 2x int8, normalized.
    /// </summary>
    SByte2_Norm,
    /// <summary>
    /// 2x int8.
    /// </summary>
    SByte2,
    /// <summary>
    /// 4x int8, normalized.
    /// </summary>
    SByte4_Norm,
    /// <summary>
    /// 4x int8.
    /// </summary>
    SByte4,
    /// <summary>
    /// 2x uint16, normalized.
    /// </summary>
    UShort2_Norm,
    /// <summary>
    /// 2x uint16.
    /// </summary>
    UShort2,
    /// <summary>
    /// 4x uint16, normalized.
    /// </summary>
    UShort4_Norm,
    /// <summary>
    /// 4x uint16.
    /// </summary>
    UShort4,
    /// <summary>
    /// 2x int16, normalized.
    /// </summary>
    Short2_Norm,
    /// <summary>
    /// 2x int16.
    /// </summary>
    Short2,
    /// <summary>
    /// 4x int16, normalized.
    /// </summary>
    Short4_Norm,
    /// <summary>
    /// 4x int16.
    /// </summary>
    Short4,
    /// <summary>
    /// 1x uint32.
    /// </summary>
    UInt1,
    /// <summary>
    /// 2x uint32.
    /// </summary>
    UInt2,
    /// <summary>
    /// 3x uint32.
    /// </summary>
    UInt3,
    /// <summary>
    /// 4x uint32.
    /// </summary>
    UInt4,
    /// <summary>
    /// 1x int32.
    /// </summary>
    Int1,
    /// <summary>
    /// 2x int32.
    /// </summary>
    Int2,
    /// <summary>
    /// 3x int32.
    /// </summary>
    Int3,
    /// <summary>
    /// 4x int32.
    /// </summary>
    Int4,
    /// <summary>
    /// 1x float16.
    /// </summary>
    Half1,
    /// <summary>
    /// 2x float16.
    /// </summary>
    Half2,
    /// <summary>
    /// 4x float16.
    /// </summary>
    Half4,
}
