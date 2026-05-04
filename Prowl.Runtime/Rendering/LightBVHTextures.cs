// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.CompilerServices;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// Owns the GPU mirror of a single <see cref="LightBVH"/>: one RGBA32F texture for per-light
/// data (5 texels per slot) and one for the BVH nodes (2 texels per node). Both textures are
/// square and grow on demand by doubling the dimension; the CPU keeps a staging buffer so we
/// can issue exactly one <c>glTexSubImage2D</c> per frame, covering only the affected rows.
/// </summary>
public sealed class LightBVHTextures : IDisposable
{
    /// <summary>Smallest size we ever allocate. 32x32 RGBA32F = 16 KB and packs ~204 lights / ~256 leaves.</summary>
    public const int InitialDimension = 32;

    /// <summary>Hard upper bound on either texture dimension. GL 4.1 guarantees 16384 minimum.</summary>
    public const int MaxDimension = 16384;

    private Texture2D _lightTex;
    private Texture2D _nodeTex;

    /// <summary>Width = height = power of 2.</summary>
    public int LightTextureSize { get; private set; }

    /// <summary>Width = height = power of 2.</summary>
    public int NodeTextureSize { get; private set; }

    /// <summary>Live light count uploaded last <see cref="Sync"/>.</summary>
    public int LightCount { get; private set; }

    /// <summary>Live BVH node count uploaded last <see cref="Sync"/>.</summary>
    public int NodeCount { get; private set; }

    /// <summary>Index of the BVH root, or -1 if the tree is empty. Always 0 in the current builder.</summary>
    public int RootNode { get; private set; } = -1;

    /// <summary>Texture sampled by the shader for per-light data. <c>texelFetch</c>-only.</summary>
    public Texture2D LightDataTexture => _lightTex;

    /// <summary>Texture sampled by the shader for BVH nodes. <c>texelFetch</c>-only.</summary>
    public Texture2D NodeDataTexture => _nodeTex;

    // Staging mirrors GPU contents so we can re-upload contiguous row ranges without rebuilding
    // the entire payload each frame. 4 floats per texel.
    private float[] _lightStaging = Array.Empty<float>();
    private float[] _nodeStaging = Array.Empty<float>();

    /// <summary>
    /// Pull dirty rows out of <paramref name="bvh"/> and upload them. Resizes textures if the
    /// BVH outgrew the current allocation; resize forces a full re-upload of every active row.
    /// </summary>
    public void Sync(LightBVH bvh)
    {
        if (bvh == null) throw new ArgumentNullException(nameof(bvh));

        // Required texel counts.
        int lightTexelsNeeded = bvh.SlotHighWater * LightBVH.TexelsPerLight;
        int nodeTexelsNeeded = bvh.NodeCount * LightBVH.TexelsPerNode;

        bool lightResized = EnsureLightCapacity(lightTexelsNeeded);
        bool nodeResized = EnsureNodeCapacity(nodeTexelsNeeded);

        // After a resize the GPU buffer is fresh, every row needs to be pushed.
        if (lightResized || nodeResized) bvh.MarkAllDirty();

        // Sync per-light data.
        if (bvh.TryGetSlotDirtyRange(out int slotMin, out int slotMax))
        {
            var slots = bvh.Slots;
            for (int s = slotMin; s <= slotMax; s++)
                WriteSlotToStaging(s, slots[s]);

            // Convert texel range to row range and upload.
            int loTexel = slotMin * LightBVH.TexelsPerLight;
            int hiTexel = slotMax * LightBVH.TexelsPerLight + (LightBVH.TexelsPerLight - 1);
            UploadRows(_lightTex, _lightStaging, LightTextureSize, loTexel, hiTexel);
        }

        // Sync BVH node data.
        if (bvh.TryGetNodeDirtyRange(out int nodeMin, out int nodeMax))
        {
            var nodes = bvh.Nodes;
            for (int n = nodeMin; n <= nodeMax; n++)
                WriteNodeToStaging(n, nodes[n]);

            int loTexel = nodeMin * LightBVH.TexelsPerNode;
            int hiTexel = nodeMax * LightBVH.TexelsPerNode + (LightBVH.TexelsPerNode - 1);
            UploadRows(_nodeTex, _nodeStaging, NodeTextureSize, loTexel, hiTexel);
        }

        bvh.ClearDirtyRanges();

        LightCount = bvh.ActiveLightCount;
        NodeCount = bvh.NodeCount;
        RootNode = NodeCount > 0 ? 0 : -1;
    }

    public void Dispose()
    {
        _lightTex?.Dispose();
        _nodeTex?.Dispose();
        _lightTex = null;
        _nodeTex = null;
        _lightStaging = Array.Empty<float>();
        _nodeStaging = Array.Empty<float>();
        LightTextureSize = 0;
        NodeTextureSize = 0;
        LightCount = 0;
        NodeCount = 0;
        RootNode = -1;
    }

    // ─────────────────────── allocation ───────────────────────

    private bool EnsureLightCapacity(int texelsNeeded)
    {
        // Don't burn a 1 MB GPU texture when the scene has no lights yet. The first frame with
        // lights will trip this and allocate.
        if (texelsNeeded == 0 && _lightTex == null) return false;

        int dim = LightTextureSize;
        if (_lightTex == null) dim = InitialDimension;
        while ((long)dim * dim < texelsNeeded) dim *= 2;
        if (dim > MaxDimension)
            throw new InvalidOperationException($"LightBVH light texture exceeded MaxDimension ({MaxDimension}). Reduce light count or raise the cap.");

        if (_lightTex != null && dim == LightTextureSize) return false;

        _lightTex?.Dispose();
        _lightTex = new Texture2D((uint)dim, (uint)dim, false, TextureImageFormat.Float4);
        // texelFetch ignores filtering and wrap, so leaving them at the texture defaults is fine.
        LightTextureSize = dim;

        // Resize staging buffer in place; existing data carries over because the texture is row-
        // major and slot N's linear texel range is independent of texture width. We just have
        // more room. A full re-upload happens above (caller marks all-dirty after a resize).
        int floats = dim * dim * 4;
        if (_lightStaging.Length < floats) Array.Resize(ref _lightStaging, floats);
        return true;
    }

    private bool EnsureNodeCapacity(int texelsNeeded)
    {
        if (texelsNeeded == 0 && _nodeTex == null) return false;

        int dim = NodeTextureSize;
        if (_nodeTex == null) dim = InitialDimension;
        while ((long)dim * dim < texelsNeeded) dim *= 2;
        if (dim > MaxDimension)
            throw new InvalidOperationException($"LightBVH node texture exceeded MaxDimension ({MaxDimension}).");

        if (_nodeTex != null && dim == NodeTextureSize) return false;

        _nodeTex?.Dispose();
        _nodeTex = new Texture2D((uint)dim, (uint)dim, false, TextureImageFormat.Float4);
        NodeTextureSize = dim;

        int floats = dim * dim * 4;
        if (_nodeStaging.Length < floats) Array.Resize(ref _nodeStaging, floats);
        return true;
    }

    // ─────────────────────── packing ───────────────────────
    //
    // Layout MUST stay in lockstep with LightBVH.glsl. See comments in that file.
    //
    //   Light slot s, linear texel base = s * 5:
    //     +0 : Position.xyz, Range
    //     +1 : Color.rgb,    Intensity
    //     +2 : Direction.xyz, TypeAsFloat   (0=Directional, 1=Point, 2=Spot)
    //     +3 : SpotCos, InnerSpotCos, ShadowBias, ShadowNormalBias
    //     +4 : ShadowStrength, ShadowQuality, ShadowSlotAsFloat, ShadowEnabledAsFloat
    //
    //   Node n, linear texel base = n * 2:
    //     +0 : Min.xyz, Hit (floatBitsToInt to recover signed int)
    //     +1 : Max.xyz, Miss

    private void WriteSlotToStaging(int slot, in LightBVH.SlotInfo s)
    {
        int o = slot * LightBVH.TexelsPerLight * 4;
        if (o + 19 >= _lightStaging.Length) return;

        // Texel 0
        _lightStaging[o + 0] = s.Position.X;
        _lightStaging[o + 1] = s.Position.Y;
        _lightStaging[o + 2] = s.Position.Z;
        _lightStaging[o + 3] = s.Range;
        // Texel 1
        _lightStaging[o + 4] = s.Color.X;
        _lightStaging[o + 5] = s.Color.Y;
        _lightStaging[o + 6] = s.Color.Z;
        _lightStaging[o + 7] = s.Intensity;
        // Texel 2
        _lightStaging[o + 8] = s.Direction.X;
        _lightStaging[o + 9] = s.Direction.Y;
        _lightStaging[o + 10] = s.Direction.Z;
        _lightStaging[o + 11] = s.Type switch
        {
            LightType.Directional => 0f,
            LightType.Point => 1f,
            LightType.Spot => 2f,
            _ => 1f
        };
        // Texel 3 spot uses cosines so the GLSL inner-loop avoids cos() calls.
        float spotCos = s.Type == LightType.Spot ? MathF.Cos(s.SpotAngle * MathF.PI / 180f) : -1f;
        float innerCos = s.Type == LightType.Spot ? MathF.Cos(s.InnerSpotAngle * MathF.PI / 180f) : 1f;
        _lightStaging[o + 12] = spotCos;
        _lightStaging[o + 13] = innerCos;
        _lightStaging[o + 14] = s.ShadowBias;
        _lightStaging[o + 15] = s.ShadowNormalBias;
        // Texel 4
        _lightStaging[o + 16] = s.ShadowStrength;
        _lightStaging[o + 17] = s.ShadowQuality;
        _lightStaging[o + 18] = s.ShadowSlot;
        _lightStaging[o + 19] = s.ShadowEnabled ? 1f : 0f;
    }

    private void WriteNodeToStaging(int nodeIdx, in LightBVH.Node n)
    {
        int o = nodeIdx * LightBVH.TexelsPerNode * 4;
        if (o + 7 >= _nodeStaging.Length) return;

        _nodeStaging[o + 0] = n.Min.X;
        _nodeStaging[o + 1] = n.Min.Y;
        _nodeStaging[o + 2] = n.Min.Z;
        _nodeStaging[o + 3] = IntAsFloat(n.Hit);

        _nodeStaging[o + 4] = n.Max.X;
        _nodeStaging[o + 5] = n.Max.Y;
        _nodeStaging[o + 6] = n.Max.Z;
        _nodeStaging[o + 7] = IntAsFloat(n.Miss);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static unsafe float IntAsFloat(int i) => *(float*)&i;

    // ─────────────────────── upload ───────────────────────

    private static unsafe void UploadRows(Texture2D tex, float[] staging, int dim, int loTexel, int hiTexel)
    {
        int loRow = loTexel / dim;
        int hiRow = hiTexel / dim;
        if (loRow < 0) loRow = 0;
        if (hiRow >= dim) hiRow = dim - 1;
        if (hiRow < loRow) return;

        int rows = hiRow - loRow + 1;
        int floatStart = loRow * dim * 4;

        // Single TexSubImage covering all touched rows. We always upload full rows because the
        // texture's row stride is `dim` texels; partial-row uploads would need a tighter rect
        // and we'd have to slice the staging buffer for it. Worst case: a single touched texel
        // costs us re-uploading its row (32-1024 texels), well below any bandwidth concern.
        fixed (float* p = &staging[floatStart])
        {
            tex.SetDataPtr(p, 0, loRow, (uint)dim, (uint)rows);
        }
    }
}
