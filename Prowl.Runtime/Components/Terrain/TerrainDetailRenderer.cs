// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.
//
// Procedural detail renderer. Placement lives entirely in the vertex shader, so a whole grass
// field is a handful of draw calls with nothing uploaded per frame.

using System;
using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Terrain;

/// <summary>
/// Draws terrain details as camera-relative cascades. The draw distance is split evenly into bands;
/// each band out doubles its cell size while sampling every other cell of the same world grid, so it
/// draws a strict subset of the band inside it. A blade thins out with distance but never moves,
/// which is what keeps the field stable as the camera travels.
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
    private static Texture2D? s_defaultWhite;

    private readonly List<CascadeSlot> _slots = [];

    private struct CascadeSlot
    {
        public ProceduralInstancedRenderable Renderable;
        public PropertyState Properties;
    }

    public void Initialize()
    {
        if (_quadMesh.IsNotValid()) _quadMesh = CreateQuadMesh();
    }

    public void Dispose()
    {
        if (_quadMesh.IsValid()) _quadMesh.Dispose();
        _quadMesh = null;
        _slots.Clear();
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

    public void CollectRenderables(
        TerrainData data, TerrainComponent terrain, Camera camera, Material baseMaterial,
        List<IRenderable> renderables)
    {
        if (_quadMesh == null || data.DetailPrototypes.Count == 0) return;

        var detailTextures = data.GetDetailTextures();
        if (detailTextures.Count == 0) return;

        Float3 camLocal = terrain.WorldToTerrain(camera.Transform.Position);
        Float3 terrainScale = terrain.Transform.LocalScale;
        float avgScale = MathF.Max(0.001f, (terrainScale.X + terrainScale.Z) * 0.5f);

        float distance = MathF.Max(terrain.DetailDistance, 0.01f) / avgScale;
        float density = MathF.Max(terrain.DetailDensity, 0.001f) * avgScale;
        int cascades = Math.Clamp(terrain.DetailCascades, 1, kMaxCascades);
        var centre = new Float2(camLocal.X, camLocal.Z);

        int slot = 0;
        for (int protoIdx = 0; protoIdx < data.DetailPrototypes.Count; protoIdx++)
        {
            var proto = data.DetailPrototypes[protoIdx];
            if (proto.RenderMode == DetailRenderMode.Mesh) continue; // meshes have their own renderer
            if (protoIdx >= data.DetailLayers.Count) continue;

            int textureIndex = protoIdx / 4;
            if (textureIndex >= detailTextures.Count) continue;

            // Nothing painted, nothing to sweep
            if (!data.TryGetDetailBounds(protoIdx, out Float4 painted)) continue;

            // Blades wander out of their cell, so the painted rect reaches a little further
            float wanderMargin = (1 << kMaxJitterLevel) * 0.5f * CascadeCellSize(density, 0);

            Material? material = null;

            for (int cascade = 0; cascade < cascades; cascade++)
            {
                CascadeBand(distance, cascades, cascade, out float inner, out float outer);
                if (!CascadeTouchesBounds(centre, inner, outer, painted, wanderMargin)) continue;

                if (material.IsNotValid())
                    material = PrepareMaterial(data, terrain, baseMaterial, proto, detailTextures[textureIndex], protoIdx);
                int cellsPerSide = CascadeCellsPerSide(density, distance, cascades, cascade);
                float cellSize = CascadeCellSize(density, 0);

                CascadeSlot slotData = GetSlot(slot++);
                var props = slotData.Properties;
                props.Clear();

                Int2 origin = CascadeOriginCell(camLocal.X, camLocal.Z, density, cascade, cellsPerSide);
                props.SetVector("_ScatterOriginCell", new Float2(origin.X, origin.Y));
                props.SetFloat("_ScatterCellSize", cellSize);
                props.SetInt("_ScatterCellsPerSide", cellsPerSide);
                props.SetInt("_ScatterLevel", cascade);
                props.SetInt("_ScatterMaxLevel", cascades - 1);
                props.SetVector("_ScatterCentre", centre);
                props.SetVector("_ScatterRadii", new Float2(inner, outer));
                props.SetInt("_ObjectID", terrain.InstanceID);

                float span = cellsPerSide * CascadeCellSize(density, cascade);
                Float3 localMin = new(origin.X * cellSize, 0f, origin.Y * cellSize);
                Float3 localMax = localMin + new Float3(span, data.Height + proto.MaxHeight, span);
                AABB bounds = TransformBounds(terrain, localMin, localMax);

                Float3 cascadeCentre = terrain.TerrainToWorld((localMin + localMax) * 0.5f);

                slotData.Renderable.Set(_quadMesh, material, props, cellsPerSide * cellsPerSide,
                    cascadeCentre, bounds, terrain.GameObject.LayerIndex);
                renderables.Add(slotData.Renderable);
            }
        }
    }

    private CascadeSlot GetSlot(int index)
    {
        while (_slots.Count <= index)
        {
            var props = new PropertyState();
            _slots.Add(new CascadeSlot
            {
                Properties = props,
                Renderable = new ProceduralInstancedRenderable(_quadMesh!, null!, props),
            });
        }
        return _slots[index];
    }

    /// <summary>Per-prototype material carrying the look parameters that used to be baked per blade.</summary>
    private static Material PrepareMaterial(TerrainData data, TerrainComponent terrain, Material baseMaterial,
        DetailPrototype proto, Texture2D detailTexture, int protoIdx)
    {
        // Clone per prototype: material state is read at draw time, so prototypes sharing one
        // material would otherwise all render with the last one's texture and parameters.
        var protoMat = proto.GrassMaterial.Res;
        Material material = (protoMat.IsValid() ? protoMat : baseMaterial).Clone();
        terrain.ApplyDetailUniforms(material);

        if (s_defaultWhite.IsNotValid()) s_defaultWhite = Texture2D.LoadDefault(DefaultTexture.White);
        var texRes = proto.Texture.Res;
        material.SetTexture("_MainTex", texRes.IsValid() ? texRes : s_defaultWhite);

        material.SetKeyword("TERRAIN_BICUBIC", data.Interpolation == TerrainInterpolation.Bicubic);

        material.SetFloat("_Billboard", proto.RenderMode == DetailRenderMode.TextureBillboard ? 1f : 0f);
        material.SetFloat("_AlignToNormal", proto.AlignToNormal ? 1f : 0f);

        material.SetVector("_ProtoSize", new Float4(proto.MinWidth, proto.MaxWidth, proto.MinHeight, proto.MaxHeight));
        material.SetColor("_ProtoHealthyColor", proto.HealthyColor);
        material.SetColor("_ProtoDryColor", proto.DryColor);
        material.SetFloat("_ProtoNoiseSpread", proto.NoiseSpread);
        material.SetFloat("_ProtoBendFactor", proto.BendFactor);

        material.SetTexture("_DetailMap", detailTexture);
        material.SetInt("_DetailChannel", protoIdx % 4);

        return material;
    }

    private static AABB TransformBounds(TerrainComponent terrain, Float3 localMin, Float3 localMax)
    {
        Float3 min = new(float.MaxValue), max = new(float.MinValue);
        for (int i = 0; i < 8; i++)
        {
            Float3 corner = new(
                (i & 1) == 0 ? localMin.X : localMax.X,
                (i & 2) == 0 ? localMin.Y : localMax.Y,
                (i & 4) == 0 ? localMin.Z : localMax.Z);

            Float3 world = terrain.TerrainToWorld(corner);
            min = new Float3(MathF.Min(min.X, world.X), MathF.Min(min.Y, world.Y), MathF.Min(min.Z, world.Z));
            max = new Float3(MathF.Max(max.X, world.X), MathF.Max(max.Y, world.Y), MathF.Max(max.Z, world.Z));
        }
        return new AABB(min, max);
    }

    private static Mesh CreateQuadMesh()
    {
        var mesh = new Mesh();
        mesh.Vertices = [new(-0.5f, 0, 0), new(0.5f, 0, 0), new(0.5f, 1, 0), new(-0.5f, 1, 0)];
        mesh.UV = [new(0, 0), new(1, 0), new(1, 1), new(0, 1)];
        mesh.Normals = [Float3.UnitY, Float3.UnitY, Float3.UnitY, Float3.UnitY];
        mesh.Indices = [0, 2, 1, 0, 3, 2];
        mesh.RecalculateBounds();
        mesh.Upload();
        return mesh;
    }
}
