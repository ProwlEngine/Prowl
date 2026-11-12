using System;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>Which view directions an impostor's frames cover.</summary>
public enum ImpostorLayout : byte
{
    /// <summary>The upper half, twice the frames per angle, for things seen from the side or above.</summary>
    Hemi,

    /// <summary>Every direction, for things also seen from below.</summary>
    Full,
}

/// <summary>
/// Where an impostor's frames look from: an N by N grid over an octahedron, y up, frame x and y spanning -1 to 1 from the
/// first frame to the last. Each frame looks at the object's center along minus its direction, with the frame's right
/// and up on screen from <see cref="Basis"/>. A frame's texel u runs along its right and v along its up, each from minus
/// the radius at 0 to plus it at 1. Graphite/Impostor.shader works the same out.
/// </summary>
public static class ImpostorFrames
{
    /// <summary>The direction toward the viewer of frame x, y.</summary>
    public static Float3 Direction(ImpostorLayout layout, int frames, int x, int y)
    {
        float scale = frames > 1 ? 2f / (frames - 1) : 0;
        return Direction(layout, new Float2(x * scale - 1, y * scale - 1));
    }

    /// <summary>The direction at a point of the grid, each axis -1 to 1.</summary>
    public static Float3 Direction(ImpostorLayout layout, Float2 grid)
    {
        if (layout == ImpostorLayout.Hemi)
        {
            float px = (grid.X + grid.Y) * 0.5f, pz = (grid.X - grid.Y) * 0.5f;
            return Float3.Normalize(new Float3(px, 1 - MathF.Abs(px) - MathF.Abs(pz), pz));
        }
        Float3 n = new(grid.X, grid.Y, 1 - MathF.Abs(grid.X) - MathF.Abs(grid.Y));
        if (n.Z < 0)
            n = new Float3((1 - MathF.Abs(n.Y)) * MathF.Sign(n.X == 0 ? 1 : n.X), (1 - MathF.Abs(n.X)) * MathF.Sign(n.Y == 0 ? 1 : n.Y), n.Z);
        return Float3.Normalize(new Float3(n.X, n.Z, n.Y));
    }

    /// <summary>The up a frame's view leans toward: world up, or +Z for a frame looking straight along it.</summary>
    public static Float3 UpReference(Float3 direction) => MathF.Abs(direction.Y) > 0.999f ? new Float3(0, 0, 1) : new Float3(0, 1, 0);

    /// <summary>A frame's right and up on screen, the way <see cref="ViewCamera"/> looks along minus the direction.</summary>
    public static (Float3 Right, Float3 Up) Basis(Float3 direction)
    {
        Float3 forward = -direction;
        Float3 right = Float3.Normalize(Float3.Cross(UpReference(direction), forward));
        return (right, Float3.Cross(forward, right));
    }
}

/// <summary>
/// An impostor ready to draw: its atlases, as ImpostorBuilder in Prowl.Graphite.Rendering.Tools bakes them, on a
/// Graphite/Impostor material and the card it draws on. Owns its textures. Host thread.
/// </summary>
public sealed class ImpostorAsset : IDisposable
{
    private readonly Texture[] _textures;

    /// <param name="albedo">sRGB base colour and coverage.</param>
    /// <param name="normalDepth">The octahedral object space normal, depth and occlusion.</param>
    /// <param name="material">Roughness and metalness.</param>
    public ImpostorAsset(Renderer renderer, Texture albedo, Texture normalDepth, Texture material, ImpostorLayout layout, int frames, Float3 center, float radius)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _textures = [albedo, normalDepth, material];
        Layout = layout;
        Frames = frames;
        Center = center;
        Radius = radius;

        Material = renderer.CreateMaterial(renderer.Standard.Impostor);
        Material.Surface = new SurfaceSettings(SurfaceType.Cutout, Cull: SurfaceCull.Off);
        Material.SetTexture("_ImpostorAlbedo", albedo);
        Material.SetTexture("_ImpostorNormalDepth", normalDepth);
        Material.SetTexture("_ImpostorMaterial", material);
        Material.SetFloat("_ImpostorFrames", frames);
        Material.SetFloat("_ImpostorHemi", layout == ImpostorLayout.Hemi ? 1 : 0);
        Material.SetFloat4("_ImpostorSphere", new Float4(center.X, center.Y, center.Z, radius));

        // The card's corners sit at the center, its vertex stage spreads them across the sphere toward the viewer
        Float3[] positions = [center, center, center, center];
        Float2[] uvs = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        Card = renderer.CreateMesh(new MeshData
        {
            VertexCount = 4,
            Streams =
            [
                new VertexStream { Semantic = VertexSemantic.Position, Format = VertexElementFormat.Float3, Data = MemoryMarshal.AsBytes(positions.AsSpan()).ToArray() },
                new VertexStream { Semantic = VertexSemantic.UV0, Format = VertexElementFormat.Float2, Data = MemoryMarshal.AsBytes(uvs.AsSpan()).ToArray() },
            ],
            Indices = new uint[] { 0, 1, 2, 0, 2, 3 },
            Displacement = new Float3(radius, radius, radius),
        });
    }

    public ImpostorLayout Layout { get; }
    public int Frames { get; }

    /// <summary>The object space sphere the frames frame.</summary>
    public Float3 Center { get; }
    public float Radius { get; }

    public Material Material { get; }
    public GpuMesh Card { get; }

    /// <summary>The impostor as the last level of a <see cref="LodSet"/>.</summary>
    public LodLevel Level(float screenSize, bool castShadows = true) => new(Card, screenSize, castShadows, Material);

    public void Dispose()
    {
        Material.Dispose();
        Card.Dispose();
        foreach (Texture texture in _textures)
            texture.Dispose();
    }
}
