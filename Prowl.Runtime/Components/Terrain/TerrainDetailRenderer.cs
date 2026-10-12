// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Procedural detail renderer. Placement lives entirely in the vertex shader, so a whole grass
// field is a handful of instanced draws with nothing uploaded per frame.

using System;
using System.Collections.Generic;

using Prowl.Graphite.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using GMaterial = Prowl.Graphite.Rendering.Material;
using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;

namespace Prowl.Runtime.Terrain;

/// <summary>
/// Draws terrain details as camera-relative cascades. The draw distance is split evenly into bands;
/// each band out doubles its cell size while sampling every other cell of the same world grid, so it
/// draws a strict subset of the band inside it. A blade thins out with distance but never moves,
/// which is what keeps the field stable as the camera travels. Each view places its cascades around
/// its own camera on the render thread.
/// </summary>
internal class TerrainDetailRenderer
{
    /// <summary>Cascades a terrain may ask for, so a wild setting cannot run away.</summary>
    public const int kMaxCascades = 6;

    /// <summary>Instances per cascade side, capped so density and distance together stay affordable.</summary>
    private const int kMaxCellsPerSide = 512;

    /// <summary>Mirrors SCATTER_MAX_JITTER in TerrainScatter.glsl: the widest a blade may wander,
    /// as a power of two in fine cells. Cascades are padded to keep those blades generated.</summary>
    private const int kMaxJitterLevel = 3;

    private Mesh? _quadMesh;
    private readonly List<TerrainMaterial> _materials = [];

    /// <summary>One prototype's grass as the render thread draws it.</summary>
    public readonly struct Prototype(GMaterial material, Float4 painted, float maxHeight)
    {
        public readonly GMaterial Material = material;
        public readonly Float4 Painted = painted;
        public readonly float MaxHeight = maxHeight;
    }

    /// <summary>A terrain's grass for one frame, copied on the host.</summary>
    public sealed class Frame
    {
        public GpuMesh Quad = null!;
        public float Distance, Density, WanderMargin, TerrainHeight;
        public int Cascades;
        public int[] CellsPerSide = [];
        public Prototype[] Prototypes = [];

        /// <summary>Adds the cascades around each camera, in terrain local space. Render thread.</summary>
        public void Cull(TerrainFrame terrain, List<Float3> cameras, in ProviderView view, DrawItemSink sink)
        {
            float cellSize = CascadeCellSize(Density, 0);
            foreach (Float3 camera in cameras)
            {
                var centre = new Float2(camera.X, camera.Z);
                foreach (Prototype proto in Prototypes)
                {
                    for (int cascade = 0; cascade < Cascades; cascade++)
                    {
                        CascadeBand(Distance, Cascades, cascade, out float inner, out float outer);
                        if (!CascadeTouchesBounds(centre, inner, outer, proto.Painted, WanderMargin)) continue;

                        int cellsPerSide = CellsPerSide[cascade];
                        Int2 origin = CascadeOriginCell(camera.X, camera.Z, Density, cascade, cellsPerSide);

                        float span = cellsPerSide * CascadeCellSize(Density, cascade);
                        Float3 localMin = new(origin.X * cellSize, 0f, origin.Y * cellSize);
                        Float3 localMax = localMin + new Float3(span, TerrainHeight + proto.MaxHeight, span);
                        (Double3 sphereCentre, float radius) = terrain.Sphere(localMin, localMax);
                        if (!view.Sees(sphereCentre, radius)) continue;

                        DrawItem item = terrain.Item(
                            new Float4(origin.X, origin.Y, cellsPerSide, cascade),
                            new Float4(centre.X, centre.Y, inner, outer));
                        sink.AddInstanced(Quad, 0, proto.Material, item, (uint)(cellsPerSide * cellsPerSide));
                    }
                }
            }
        }
    }

    public void Dispose()
    {
        if (_quadMesh.IsValid()) _quadMesh.Dispose();
        _quadMesh = null;
        foreach (TerrainMaterial material in _materials) material.Release();
        _materials.Clear();
    }

    /// <summary>World size of one cell in a cascade. The nearest cascade owns the painted density,
    /// and every band out doubles, which is what makes it a subset of the one before it.</summary>
    public static float CascadeCellSize(float density, int cascade)
        => (1f / MathF.Max(density, 0.001f)) * (1 << cascade);

    /// <summary>Band a cascade covers. Bands split the draw distance evenly and meet exactly.</summary>
    public static void CascadeBand(float distance, int cascades, int cascade, out float inner, out float outer)
    {
        float width = MathF.Max(distance, 0.001f) / Math.Max(cascades, 1);
        inner = width * cascade;
        outer = width * (cascade + 1);
    }

    /// <summary>
    /// Instances per side for a cascade: enough cells to cover its band, plus padding for the grid
    /// snap and for blades that jitter in from just outside. Always even, so the band has a centre.
    /// </summary>
    public static int CascadeCellsPerSide(float density, float distance, int cascades, int cascade)
    {
        CascadeBand(distance, cascades, cascade, out _, out float outer);
        float cell = CascadeCellSize(density, cascade);
        float pad = 1f + (1 << kMaxJitterLevel) * 0.5f;

        int half = (int)MathF.Ceiling(outer / cell + pad);
        if (half > kMaxCellsPerSide / 2)
        {
            Debug.LogWarningOnce("terrain_detail_cells",
                $"Detail density {density:F2} over {distance:F0} units needs {half * 2} cells in cascade " +
                $"{cascade}, capped at {kMaxCellsPerSide}. The outer part of that band will be bare: " +
                "lower Detail Density, shorten Detail Distance, or add a cascade.");
            half = kMaxCellsPerSide / 2;
        }
        return Math.Max(half, 1) * 2;
    }

    /// <summary>
    /// Whether a cascade's band can reach the rect a prototype was painted in. Prototypes are drawn
    /// as a full sweep of cells regardless of coverage, so skipping the ones nobody painted near the
    /// camera is what keeps the cost proportional to the grass you can actually see.
    /// </summary>
    public static bool CascadeTouchesBounds(Float2 centre, float inner, float outer, Float4 rect, float margin)
    {
        float minX = rect.X - margin, minZ = rect.Y - margin;
        float maxX = rect.Z + margin, maxZ = rect.W + margin;

        // Nearest point of the rect: outside the band's reach means nothing to draw
        float nearX = Maths.Clamp(centre.X, minX, maxX);
        float nearZ = Maths.Clamp(centre.Y, minZ, maxZ);
        float nearDX = centre.X - nearX, nearDZ = centre.Y - nearZ;
        if (nearDX * nearDX + nearDZ * nearDZ > outer * outer) return false;

        // Farthest corner inside the hole means the band has already passed the rect by
        float farX = MathF.Max(MathF.Abs(centre.X - minX), MathF.Abs(centre.X - maxX));
        float farZ = MathF.Max(MathF.Abs(centre.Y - minZ), MathF.Abs(centre.Y - maxZ));
        return farX * farX + farZ * farZ >= inner * inner;
    }

    /// <summary>
    /// Lower-left cell of a cascade on the fine grid. Snapping to the cascade's own cell size is
    /// what makes the grid stand still while the camera moves through it.
    /// </summary>
    public static Int2 CascadeOriginCell(float cameraX, float cameraZ, float density, int cascade, int cellsPerSide)
    {
        int stride = 1 << cascade;
        float cascadeCell = CascadeCellSize(density, cascade);
        int half = cellsPerSide / 2;
        int originX = (int)MathF.Floor(cameraX / cascadeCell) - half;
        int originZ = (int)MathF.Floor(cameraZ / cascadeCell) - half;
        return new Int2(originX * stride, originZ * stride);
    }

    /// <summary>Copies what the render thread needs to draw the grass this frame, the materials made up to date. Host thread.</summary>
    public Frame? Prepare(TerrainData data, TerrainComponent terrain, Material? baseMaterial)
    {
        if (data.DetailPrototypes.Count == 0) return null;

        IReadOnlyList<Texture2D> detailTextures = data.GetDetailTextures();
        if (detailTextures.Count == 0) return null;

        if (_quadMesh.IsNotValid()) _quadMesh = CreateQuadMesh();
        _quadMesh.Upload();
        GpuMesh? quad = _quadMesh.GpuIfLoaded;
        if (quad == null) return null;

        Float3 terrainScale = terrain.Transform.LocalScale;
        float avgScale = MathF.Max(0.001f, (terrainScale.X + terrainScale.Z) * 0.5f);
        float distance = MathF.Max(terrain.DetailDistance, 0.01f) / avgScale;
        float density = MathF.Max(terrain.DetailDensity, 0.001f) * avgScale;
        int cascades = Math.Clamp(terrain.DetailCascades, 1, kMaxCascades);

        var frame = new Frame
        {
            Quad = quad,
            Distance = distance,
            Density = density,
            Cascades = cascades,
            TerrainHeight = data.Height,
            // Blades wander out of their cell, so the painted rect reaches a little further
            WanderMargin = (1 << kMaxJitterLevel) * 0.5f * CascadeCellSize(density, 0),
            CellsPerSide = new int[cascades],
        };
        for (int cascade = 0; cascade < cascades; cascade++)
            frame.CellsPerSide[cascade] = CascadeCellsPerSide(density, distance, cascades, cascade);

        var prototypes = new List<Prototype>();
        for (int protoIdx = 0; protoIdx < data.DetailPrototypes.Count; protoIdx++)
        {
            DetailPrototype proto = data.DetailPrototypes[protoIdx];
            if (proto.RenderMode == DetailRenderMode.Mesh) continue; // meshes have their own renderer
            if (protoIdx >= data.DetailLayers.Count) continue;

            int textureIndex = protoIdx / 4;
            if (textureIndex >= detailTextures.Count) continue;

            // Nothing painted, nothing to sweep
            if (!data.TryGetDetailBounds(protoIdx, out Float4 painted)) continue;

            while (_materials.Count <= protoIdx) _materials.Add(new TerrainMaterial());
            TerrainMaterial material = _materials[protoIdx];
            Material? protoMat = proto.GrassMaterial;
            GMaterial? gpu = material.Bind(protoMat.IsValid() ? protoMat : baseMaterial);
            if (gpu == null) continue;

            ApplyPrototype(material, data, terrain, proto, detailTextures[textureIndex], protoIdx, frame);
            prototypes.Add(new Prototype(gpu, painted, proto.MaxHeight));
        }

        frame.Prototypes = [.. prototypes];
        return frame;
    }

    /// <summary>Per-prototype values: the look parameters, the detail map and the scatter grid.</summary>
    private static void ApplyPrototype(TerrainMaterial material, TerrainData data, TerrainComponent terrain,
        DetailPrototype proto, Texture2D detailTexture, int protoIdx, Frame frame)
    {
        TerrainComponent.ApplyHeightfield(material, data);
        terrain.ApplyWindZones(material);

        material.SetTexture("_MainTex", proto.Texture);
        material.SetFloat("_Billboard", proto.RenderMode == DetailRenderMode.TextureBillboard ? 1f : 0f);
        material.SetFloat("_AlignToNormal", proto.AlignToNormal ? 1f : 0f);

        material.SetVector("_ProtoSize", new Float4(proto.MinWidth, proto.MaxWidth, proto.MinHeight, proto.MaxHeight), 4);
        material.SetColor("_ProtoHealthyColor", proto.HealthyColor);
        material.SetColor("_ProtoDryColor", proto.DryColor);
        material.SetFloat("_ProtoNoiseSpread", proto.NoiseSpread);
        material.SetFloat("_ProtoBendFactor", proto.BendFactor);

        material.SetTexture("_DetailMap", detailTexture);
        material.SetInt("_DetailChannel", protoIdx % 4);
        material.SetFloat("_ScatterCellSize", CascadeCellSize(frame.Density, 0));
        material.SetInt("_ScatterMaxLevel", frame.Cascades - 1);
    }

    // A quad standing on its base
    private static Mesh CreateQuadMesh()
    {
        var mesh = new Mesh
        {
            Vertices = [new(-0.5f, 0, 0), new(0.5f, 0, 0), new(0.5f, 1, 0), new(-0.5f, 1, 0)],
            UV = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)],
            Normals = [Float3.UnitY, Float3.UnitY, Float3.UnitY, Float3.UnitY],
            Indices = [0, 2, 1, 0, 3, 2],
        };
        mesh.RecalculateBounds();
        mesh.Upload();
        return mesh;
    }
}
