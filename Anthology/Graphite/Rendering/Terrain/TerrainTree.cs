using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// A tree or other prop placed on the terrain by hand or by a tool, kept in its package: which prototype it is, where
/// it stands in metres from <see cref="TerrainData.Position"/>, its turn about up in radians, how much it is scaled across
/// and up, and a tint as RGBA bytes with red lowest.
/// </summary>
public readonly record struct TerrainTree(int Prototype, Double3 Position, float Yaw, float WidthScale, float HeightScale, uint Color)
{
    public const uint White = 0xFFFFFFFF;

    public Float4 Tint => new((Color & 255) / 255f, (Color >> 8 & 255) / 255f, (Color >> 16 & 255) / 255f, (Color >> 24) / 255f);
}
