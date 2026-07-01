// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;

using Prowl.Echo;
using Prowl.Vector;

using static Prowl.Runtime.VertexFormat;

namespace Prowl.Runtime.Resources;

public enum IndexFormat : byte
{
    UInt16 = 0,
    UInt32 = 1
}

/// <summary>Defines a portion of a Mesh's index buffer as a submesh.</summary>
public struct SubMeshDescriptor
{
    public int IndexStart;
    public int IndexCount;
    public Topology Topology;

    public SubMeshDescriptor(int indexStart, int indexCount, Topology topology = Topology.Triangles)
    {
        IndexStart = indexStart;
        IndexCount = indexCount;
        Topology = topology;
    }
}

/// <summary>
/// A named morph target (blend shape). Holds one or more <see cref="BlendShapeFrame"/>s; a single
/// frame is the common case (glTF), multiple frames describe a progressive morph (FBX). Per-vertex
/// deltas are added to the base mesh, weighted by the renderer's blend-shape weight.
/// </summary>
public sealed class BlendShape
{
    public string Name = string.Empty;
    public BlendShapeFrame[] Frames = Array.Empty<BlendShapeFrame>();
}

/// <summary>
/// One frame of a <see cref="BlendShape"/>. <see cref="Weight"/> is the weight (0-100) at which this
/// frame is fully applied. Delta arrays are parallel to the mesh's vertices; normals/tangents are optional.
/// </summary>
public sealed class BlendShapeFrame
{
    public float Weight = 100f;
    public Float3[] DeltaVertices = Array.Empty<Float3>();
    public Float3[]? DeltaNormals;
    public Float3[]? DeltaTangents;
}

[CreateAssetMenu("Mesh", Extension = ".mesh", Order = 4)]
public class Mesh : EngineObject, ISerializable
{
    /// <summary> Whether this mesh is readable by the CPU </summary>
    public readonly bool isReadable = true;

    /// <summary> Whether this mesh is writable </summary>
    public readonly bool isWritable = true;

    /// <summary> The bounds of the mesh </summary>
    public AABB bounds { get; internal set; }

    /// <summary> The format of the indices for this mesh </summary>
    public IndexFormat IndexFormat
    {
        get => indexFormat;
        set
        {
            if (isWritable == false) return;
            changed = true;
            indexFormat = value;
            indices = [];
        }
    }

    /// <summary> The mesh's primitive type </summary>
    public Topology MeshTopology
    {
        get => meshTopology;
        set
        {
            if (isWritable == false) return;
            changed = true;
            meshTopology = value;
        }
    }

    private T[] CopyArray<T>(T[] source)
    {
        if (source == null)
            return [];
        var copy = new T[source.Length];
        for (int i = 0; i < source.Length; i++)
            copy[i] = source[i];
        return copy;
    }

    /// <summary>
    /// Sets or gets the current vertices.
    /// Getting depends on isReadable.
    /// Note: When setting, if the vertex count is different than previous, it'll reset all other vertex data fields.
    /// </summary>
    public Float3[] Vertices
    {
        get => vertices ?? [];
        set
        {
            if (isWritable == false)
                return;
            bool needsReset = vertices == null || vertices.Length != value.Length;

            // Copy Vertices
            vertices = CopyArray(value);

            changed = true;
            if (needsReset)
            {
                normals = null;
                tangents = null;
                colors = null;
                colors32 = null;
                uv = null;
                uv2 = null;
                indices = null;
                // Blend-shape deltas are indexed by vertex; a vertex-count change invalidates them.
                _blendShapes = Array.Empty<BlendShape>();
                _morphDirty = true;
            }
        }
    }

    public Float3[] Normals
    {
        get => ReadVertexData(normals ?? []);
        set => WriteVertexData(ref normals, CopyArray(value), value.Length);
    }

    public Float4[] Tangents
    {
        get => ReadVertexData(tangents ?? []);
        set => WriteVertexData(ref tangents, CopyArray(value), value.Length);
    }

    public Color[] Colors
    {
        get => ReadVertexData(colors ?? []);
        set => WriteVertexData(ref colors, CopyArray(value), value.Length);
    }

    public Color32[] Colors32
    {
        get => ReadVertexData(colors32 ?? []);
        set => WriteVertexData(ref colors32, CopyArray(value), value.Length);
    }

    public Float2[] UV
    {
        get => ReadVertexData(uv ?? []);
        set => WriteVertexData(ref uv, CopyArray(value), value.Length);
    }

    public Float2[] UV2
    {
        get => ReadVertexData(uv2 ?? []);
        set => WriteVertexData(ref uv2, CopyArray(value), value.Length);
    }

    public uint[] Indices
    {
        get => ReadVertexData(indices ?? []);
        set => WriteVertexData(ref indices, CopyArray(value), value.Length, false);
    }

    public Float4[] BoneIndices
    {
        get => ReadVertexData(boneIndices ?? []);
        set => WriteVertexData(ref boneIndices, CopyArray(value), value.Length);
    }

    public Float4[] BoneWeights
    {
        get => ReadVertexData(boneWeights ?? []);
        set => WriteVertexData(ref boneWeights, CopyArray(value), value.Length);
    }

    public int VertexCount => vertices?.Length ?? 0;
    public int IndexCount => indices?.Length ?? 0;

    public GraphicsVertexArray? VertexArrayObject => vertexArrayObject;
    public GraphicsBuffer VertexBuffer => vertexBuffer;
    public GraphicsBuffer IndexBuffer => indexBuffer;

    public bool HasNormals => (normals?.Length ?? 0) > 0;
    public bool HasTangents => (tangents?.Length ?? 0) > 0;
    public bool HasColors => (colors?.Length ?? 0) > 0;
    public bool HasColors32 => (colors32?.Length ?? 0) > 0;
    public bool HasUV => (uv?.Length ?? 0) > 0;
    public bool HasUV2 => (uv2?.Length ?? 0) > 0;

    public bool HasBoneIndices => (boneIndices?.Length ?? 0) > 0;
    public bool HasBoneWeights => (boneWeights?.Length ?? 0) > 0;

    public Float4x4[]? BindPoses;
    public string[]? BoneNames;

    // ─────────────────────── Blend shapes (morph targets) ───────────────────────
    private BlendShape[] _blendShapes = Array.Empty<BlendShape>();

    // GPU morph delta textures, built lazily from _blendShapes. Each "layer" is one
    // BlendShapeFrame; deltas are laid out linearly as idx = layer * vertexCount + vertexID
    // and tiled into a 2D RGBA32F texture (width capped to MaxTextureSize).
    private Texture2D? _morphPosTex, _morphNrmTex, _morphTanTex;
    private int[] _morphLayerOffsets = Array.Empty<int>(); // per-shape starting layer
    private int _morphLayerCount;
    private int _morphTexWidth;
    private bool _morphDirty = true;

    /// <summary>The blend shapes (morph targets) on this mesh.</summary>
    public BlendShape[] BlendShapes
    {
        get => _blendShapes;
        set { _blendShapes = value ?? Array.Empty<BlendShape>(); _morphDirty = true; }
    }

    public bool HasBlendShapes => _blendShapes.Length > 0;
    public int BlendShapeCount => _blendShapes.Length;

    public string GetBlendShapeName(int index) =>
        (index >= 0 && index < _blendShapes.Length) ? _blendShapes[index].Name : string.Empty;

    /// <summary>Index of the blend shape with the given name, or -1 if not found.</summary>
    public int GetBlendShapeIndex(string name)
    {
        for (int i = 0; i < _blendShapes.Length; i++)
            if (_blendShapes[i].Name == name) return i;
        return -1;
    }

    public int GetBlendShapeFrameCount(int shapeIndex) =>
        (shapeIndex >= 0 && shapeIndex < _blendShapes.Length) ? _blendShapes[shapeIndex].Frames.Length : 0;

    public float GetBlendShapeFrameWeight(int shapeIndex, int frameIndex)
    {
        if (shapeIndex < 0 || shapeIndex >= _blendShapes.Length) return 0f;
        var frames = _blendShapes[shapeIndex].Frames;
        return (frameIndex >= 0 && frameIndex < frames.Length) ? frames[frameIndex].Weight : 0f;
    }

    // GPU morph resources (valid after EnsureMorphTextures).
    public Texture2D? MorphPositionTexture => _morphPosTex;
    public Texture2D? MorphNormalTexture => _morphNrmTex;
    public Texture2D? MorphTangentTexture => _morphTanTex;
    public bool MorphHasNormals => _morphNrmTex != null;
    public bool MorphHasTangents => _morphTanTex != null;
    public int MorphLayerCount => _morphLayerCount;
    public int MorphTexWidth => _morphTexWidth;

    /// <summary>Global morph-texture layer (row block) for a given shape's frame.</summary>
    public int GetMorphLayerIndex(int shapeIndex, int frameIndex) => _morphLayerOffsets[shapeIndex] + frameIndex;

    /// <summary>Builds the GPU morph delta textures from the blend-shape data if dirty. Cheap no-op otherwise.</summary>
    public void EnsureMorphTextures()
    {
        if (!_morphDirty) return;
        BuildMorphTextures();
    }

    private void BuildMorphTextures()
    {
        _morphDirty = false;
        DisposeMorphTextures();
        _morphLayerCount = 0;

        if (_blendShapes.Length == 0 || vertices == null || vertices.Length == 0)
            return;

        int vtx = vertices.Length;

        // Flatten frames into layers and record per-shape offsets.
        _morphLayerOffsets = new int[_blendShapes.Length];
        int layers = 0;
        bool anyNormals = false, anyTangents = false;
        for (int s = 0; s < _blendShapes.Length; s++)
        {
            _morphLayerOffsets[s] = layers;
            var frames = _blendShapes[s].Frames;
            layers += frames.Length;
            foreach (var f in frames)
            {
                if (f.DeltaNormals != null) anyNormals = true;
                if (f.DeltaTangents != null) anyTangents = true;
            }
        }
        if (layers == 0) return;

        int maxTex = Graphics.MaxTextureSize;
        int width = Math.Min(vtx, maxTex);
        long total = (long)layers * vtx;
        int height = (int)((total + width - 1) / width);
        if (height > maxTex)
        {
            Debug.LogError($"[Mesh] Blend-shape morph data ({layers} layers x {vtx} verts) exceeds the max texture size ({maxTex}); morphs disabled for '{Name}'.");
            return;
        }

        _morphLayerCount = layers;
        _morphTexWidth = width;

        int texels = width * height;
        var pos = new Float4[texels];
        var nrm = anyNormals ? new Float4[texels] : null;
        var tan = anyTangents ? new Float4[texels] : null;

        for (int s = 0; s < _blendShapes.Length; s++)
        {
            var frames = _blendShapes[s].Frames;
            for (int fi = 0; fi < frames.Length; fi++)
            {
                var f = frames[fi];
                long baseIdx = (long)(_morphLayerOffsets[s] + fi) * vtx;

                var dv = f.DeltaVertices;
                int count = Math.Min(vtx, dv.Length);
                for (int v = 0; v < count; v++)
                    pos[(int)(baseIdx + v)] = new Float4(dv[v].X, dv[v].Y, dv[v].Z, 0f);

                if (nrm != null && f.DeltaNormals != null)
                {
                    var dn = f.DeltaNormals;
                    int cn = Math.Min(vtx, dn.Length);
                    for (int v = 0; v < cn; v++)
                        nrm[(int)(baseIdx + v)] = new Float4(dn[v].X, dn[v].Y, dn[v].Z, 0f);
                }
                if (tan != null && f.DeltaTangents != null)
                {
                    var dt = f.DeltaTangents;
                    int ct = Math.Min(vtx, dt.Length);
                    for (int v = 0; v < ct; v++)
                        tan[(int)(baseIdx + v)] = new Float4(dt[v].X, dt[v].Y, dt[v].Z, 0f);
                }
            }
        }

        _morphPosTex = CreateMorphTexture(width, height, pos);
        if (nrm != null) _morphNrmTex = CreateMorphTexture(width, height, nrm);
        if (tan != null) _morphTanTex = CreateMorphTexture(width, height, tan);
    }

    private static Texture2D CreateMorphTexture(int width, int height, Float4[] data)
    {
        var tex = new Texture2D((uint)width, (uint)height, false, TextureImageFormat.Float4);
        tex.SetTextureFilters(TextureMin.Nearest, TextureMag.Nearest);
        Graphics.SetWrapS(tex.Handle, TextureWrap.ClampToEdge);
        Graphics.SetWrapT(tex.Handle, TextureWrap.ClampToEdge);
        tex.SetData<Float4>(data.AsMemory());
        return tex;
    }

    private void DisposeMorphTextures()
    {
        _morphPosTex?.Dispose(); _morphPosTex = null;
        _morphNrmTex?.Dispose(); _morphNrmTex = null;
        _morphTanTex?.Dispose(); _morphTanTex = null;
    }

    // Submesh support: each submesh defines a range within the shared index buffer
    private List<SubMeshDescriptor> _subMeshes = new();

    /// <summary>Number of submeshes. Returns 1 if no submeshes defined (entire mesh is one submesh).</summary>
    public int SubMeshCount => _subMeshes.Count > 0 ? _subMeshes.Count : 1;

    /// <summary>Get a submesh descriptor. If no submeshes defined, index 0 returns the full mesh range.</summary>
    public SubMeshDescriptor GetSubMesh(int index)
    {
        if (_subMeshes.Count == 0)
            return new SubMeshDescriptor(0, indices?.Length ?? 0, meshTopology);
        return _subMeshes[index];
    }

    /// <summary>Set the number of submeshes.</summary>
    public void SetSubMeshCount(int count)
    {
        while (_subMeshes.Count < count) _subMeshes.Add(default);
        while (_subMeshes.Count > count) _subMeshes.RemoveAt(_subMeshes.Count - 1);
        changed = true;
    }

    /// <summary>Set a submesh descriptor at the given index.</summary>
    public void SetSubMesh(int index, SubMeshDescriptor desc)
    {
        if (index >= _subMeshes.Count) SetSubMeshCount(index + 1);
        _subMeshes[index] = desc;
        changed = true;
    }

    private bool _changed = true;
    // Setting changed=true bumps the geometry Version (changed=false on Upload does not), so every
    // existing mutation site advances Version without per-site edits.
    private bool changed
    {
        get => _changed;
        set
        {
            _changed = value;
            if (value) _version++;
        }
    }

    [SerializeIgnore] private uint _version = 1;

    /// <summary>
    /// Monotonic version that advances whenever the mesh's data changes (vertices, indices, topology,
    /// submeshes, ...). Mirrors <see cref="Prowl.Vector.Transform.Version"/>. Useful for invalidating
    /// caches derived from this mesh (e.g. baked physics meshes - see <see cref="PhysicsWorld.BakeMesh"/>).
    /// </summary>
    public uint Version => _version;

    /// <summary>
    /// True if <see cref="Version"/> differs from <paramref name="lastVersion"/>; updates the reference
    /// to the current version so the next call compares against today's state.
    /// </summary>
    public bool HasChanged(ref uint lastVersion)
    {
        if (_version == lastVersion) return false;
        lastVersion = _version;
        return true;
    }

    Float3[]? vertices;
    Float3[]? normals;
    Float4[]? tangents;
    Color[]? colors;
    Color32[]? colors32;
    Float2[]? uv;
    Float2[]? uv2;
    uint[]? indices;
    Float4[]? boneIndices;
    Float4[]? boneWeights;

    IndexFormat indexFormat = IndexFormat.UInt16;
    Topology meshTopology = Topology.Triangles;

    GraphicsVertexArray? vertexArrayObject;
    GraphicsBuffer vertexBuffer;
    GraphicsBuffer indexBuffer;

    // Instanced rendering - cached VAO and buffer (created lazily on first instanced draw)
    GraphicsVertexArray? instancedVAO;
    GraphicsBuffer? instanceBuffer;
    int instanceBufferCapacity = 0;

    // Track last uploaded state for buffer reuse optimization
    private int lastVertexCount = 0;
    private int lastIndexCount = 0;
    private VertexFormat lastVertexLayout = null;

    public Mesh() { }

    public void Clear()
    {
        vertices = null;
        normals = null;
        colors = null;
        colors32 = null;
        uv = null;
        uv2 = null;
        indices = null;
        tangents = null;
        boneIndices = null;
        boneWeights = null;

        changed = true;

        // Don't delete GPU buffers - they'll be reused on next Upload()
        // This is important for frequent regeneration (e.g., voxel engines, procedural meshes)
        // Buffers are only deleted when the mesh is disposed
    }

    public void Upload()
    {
        if (changed == false && vertexArrayObject != null)
            return;

        changed = false;

        if (vertices == null || vertices.Length == 0)
            throw new InvalidOperationException($"Mesh has no vertices");

        if (indices == null || indices.Length == 0)
            throw new InvalidOperationException($"Mesh has no indices");

        switch (meshTopology)
        {
            case Topology.Triangles:
                if (indices.Length % 3 != 0)
                    throw new InvalidOperationException($"Triangle mesh doesn't have the right amount of indices. Has: {indices.Length}. Should be a multiple of 3");
                break;
            case Topology.TriangleStrip:
                if (indices.Length < 3)
                    throw new InvalidOperationException($"Triangle Strip mesh doesn't have the right amount of indices. Has: {indices.Length}. Should have at least 3");
                break;

            case Topology.Lines:
                if (indices.Length % 2 != 0)
                    throw new InvalidOperationException($"Line mesh doesn't have the right amount of indices. Has: {indices.Length}. Should be a multiple of 2");
                break;

            case Topology.LineStrip:
                if (indices.Length < 2)
                    throw new InvalidOperationException($"Line Strip mesh doesn't have the right amount of indices. Has: {indices.Length}. Should have at least 2");
                break;
        }

        VertexFormat layout = GetVertexLayout(this);

        if (layout == null)
        {
            Debug.LogError($"[Mesh] Failed to get vertex layout for this mesh!");
            return;
        }

        byte[] vertexBlob = MakeVertexDataBlob(layout);
        if (vertexBlob == null)
            return;

        // Check if we can reuse existing buffers
        bool canReuseVertexBuffer = vertexBuffer != null && lastVertexCount == vertices.Length && VertexLayoutMatches(lastVertexLayout, layout);
        bool canReuseIndexBuffer = indexBuffer != null && lastIndexCount == indices.Length;

        // Resource creation and reuse both flow through CommandBuffers submitted in
        // order: Graphics.CreateBuffer enqueues a create+upload CB, and reuses encode
        // an UpdateBuffer here. Any rendering CB submitted after this Upload is
        // guaranteed to see the new data because the executor preserves submit order.
        using var cmd = Graphics.GetCommandBuffer("Mesh.Upload");

        if (canReuseVertexBuffer)
        {
            cmd.UpdateBuffer<byte>(vertexBuffer, vertexBlob);
        }
        else
        {
            vertexBuffer?.Dispose();
            vertexBuffer = Graphics.CreateBuffer(BufferType.VertexBuffer, vertexBlob, true);
            lastVertexCount = vertices.Length;
            lastVertexLayout = layout;
        }

        if (indexFormat == IndexFormat.UInt16)
        {
            ushort[] data = new ushort[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                if (indices[i] >= ushort.MaxValue)
                    throw new InvalidOperationException($"[Mesh] Invalid value {indices[i]} for 16-bit indices");
                data[i] = (ushort)indices[i];
            }

            if (canReuseIndexBuffer)
            {
                cmd.UpdateBuffer<ushort>(indexBuffer, data);
            }
            else
            {
                indexBuffer?.Dispose();
                indexBuffer = Graphics.CreateBuffer(BufferType.ElementsBuffer, data, true);
                lastIndexCount = indices.Length;
            }
        }
        else if (indexFormat == IndexFormat.UInt32)
        {
            if (canReuseIndexBuffer)
            {
                cmd.UpdateBuffer<uint>(indexBuffer, indices);
            }
            else
            {
                indexBuffer?.Dispose();
                indexBuffer = Graphics.CreateBuffer(BufferType.ElementsBuffer, indices, true);
                lastIndexCount = indices.Length;
            }
        }

        Graphics.Submit(cmd);

        // VAO recreation must come AFTER the upload submit so the create-VAO CB is
        // sequenced behind the buffer create/update CBs. CreateGLObject (run later on
        // the render thread) then binds buffers whose handles are already valid.
        if (!canReuseVertexBuffer || !canReuseIndexBuffer || vertexArrayObject == null)
        {
            vertexArrayObject?.Dispose();
            vertexArrayObject = Graphics.CreateVertexArray(layout, vertexBuffer, indexBuffer);
            Debug.Log($"VAO: [ID {vertexArrayObject}] Mesh uploaded successfully to VRAM (GPU)");
        }
    }

    /// <summary>
    /// Ensures the instanced rendering VAO and buffer exist for this mesh with
    /// enough capacity for <paramref name="instanceCount"/> instances. Does NOT
    /// upload data caller must encode a <c>cmd.UpdateBuffer(instanceBuffer, ...)</c>
    /// in the same CommandBuffer as their <c>cmd.DrawIndexedInstanced</c> so the
    /// upload is sequenced against the draw.
    ///
    /// <para>
    /// The instanceBuffer is shared per-mesh across all InstancedMeshRenderables
    /// using this mesh. Multiple batches can encode different uploads + draws into
    /// one CommandBuffer; the executor processes them in order so each draw sees
    /// its own data. If <paramref name="instanceCount"/> exceeds current capacity,
    /// the old buffer is queued for DEFERRED dispose (after frame end) so previously
    /// encoded commands holding the old handle still execute against valid GL state.
    /// </para>
    /// </summary>
    /// <param name="instanceCount">Maximum number of instances this call needs to draw.</param>
    /// <param name="instanceBuf">Output: the instance buffer to upload data into via cmd.UpdateBuffer.</param>
    /// <returns>The instanced VAO to bind for drawing.</returns>
    public GraphicsVertexArray EnsureInstanceVAO(int instanceCount, out GraphicsBuffer instanceBuf)
    {
        Upload();

        var instanceFormat = new VertexFormat(new[]
        {
            new Element((VertexSemantic)8, VertexType.Float, 4, divisor: 1),  // ModelRow0
            new Element((VertexSemantic)9, VertexType.Float, 4, divisor: 1),  // ModelRow1
            new Element((VertexSemantic)10, VertexType.Float, 4, divisor: 1), // ModelRow2
            new Element((VertexSemantic)11, VertexType.Float, 4, divisor: 1), // ModelRow3
            new Element((VertexSemantic)12, VertexType.Float, 4, divisor: 1), // Color (RGBA)
            new Element((VertexSemantic)13, VertexType.Float, 4, divisor: 1), // CustomData
        });

        if (instanceBuffer == null || instanceCount > instanceBufferCapacity)
        {
            // Grow with 50% headroom to amortise resizes.
            instanceBufferCapacity = (int)(instanceCount * 1.5f);

            // Defer-dispose the old buffer: earlier batches in the SAME outer
            // CommandBuffer hold the old handle in their encoded opcodes, and
            // would crash if we deleted the GL object before those commands
            // executed. Graphics.FlushDeferredDisposes() runs at end of frame.
            if (instanceBuffer != null) Graphics.DeferDispose(instanceBuffer);
            if (instancedVAO != null) Graphics.DeferDispose(instancedVAO);

            // Create the buffer with placeholder data sized to capacity. Real
            // per-batch data is uploaded by the caller via cmd.UpdateBuffer.
            var placeholder = new Rendering.InstanceData[instanceBufferCapacity];
            instanceBuffer = Graphics.CreateBuffer(BufferType.VertexBuffer, placeholder, dynamic: true);
            instancedVAO = null;
        }

        if (instancedVAO == null)
        {
            var meshFormat = GetVertexLayout(this);
            instancedVAO = Graphics.CreateVertexArray(
                meshFormat,
                vertexBuffer,
                indexBuffer,
                instanceFormat,
                instanceBuffer
            );
        }

        instanceBuf = instanceBuffer;
        return instancedVAO;
    }

    private bool VertexLayoutMatches(VertexFormat a, VertexFormat b)
    {
        if (a == null || b == null) return false;
        if (a.Size != b.Size) return false;
        if (a.Elements.Length != b.Elements.Length) return false;

        for (int i = 0; i < a.Elements.Length; i++)
        {
            Element elemA = a.Elements[i];
            Element elemB = b.Elements[i];
            if (elemA.Semantic != elemB.Semantic ||
                elemA.Type != elemB.Type ||
                elemA.Count != elemB.Count)
                return false;
        }

        return true;
    }

    public void RecalculateBounds()
    {
        if (vertices == null)
            throw new ArgumentNullException();

        bool empty = true;
        Float3 minVec = Float3.One * float.MaxValue;
        Float3 maxVec = Float3.One * float.MinValue;
        foreach (Float3 ptVector in vertices)
        {
            minVec = Maths.Min(minVec, ptVector);
            maxVec = Maths.Max(maxVec, ptVector);

            empty = false;
        }
        if (empty)
            throw new ArgumentException();

        bounds = new AABB(minVec, maxVec);
    }

    public void RecalculateNormals()
    {
        if (vertices == null || vertices.Length < 3) return;
        if (indices == null || indices.Length < 3) return;

        var normals = new Float3[vertices.Length];

        for (int i = 0; i < indices.Length; i += 3)
        {
            uint ai = indices[i];
            uint bi = indices[i + 1];
            uint ci = indices[i + 2];

            Float3 n = Float3.Normalize(Float3.Cross(
                vertices[bi] - vertices[ai],
                vertices[ci] - vertices[ai]
            ));

            normals[ai] += n;
            normals[bi] += n;
            normals[ci] += n;
        }

        for (int i = 0; i < vertices.Length; i++)
        {
            normals[i] = Float3.Normalize(normals[i]);
            if (float.IsNaN(normals[i].X) || float.IsNaN(normals[i].Y) || float.IsNaN(normals[i].Z))
                normals[i] = Float3.UnitY;
        }

        Normals = normals;
    }

    public void RecalculateTangents()
    {
        if (vertices == null || vertices.Length < 3) return;
        if (indices == null || indices.Length < 3) return;
        if (uv == null) return;

        var tan1 = new Float3[vertices.Length]; // tangent accumulator
        var tan2 = new Float3[vertices.Length]; // bitangent accumulator (for handedness)

        for (int i = 0; i < indices.Length; i += 3)
        {
            uint ai = indices[i];
            uint bi = indices[i + 1];
            uint ci = indices[i + 2];

            Float3 edge1 = vertices[bi] - vertices[ai];
            Float3 edge2 = vertices[ci] - vertices[ai];

            Float2 deltaUV1 = uv[bi] - uv[ai];
            Float2 deltaUV2 = uv[ci] - uv[ai];

            float det = deltaUV1.X * deltaUV2.Y - deltaUV2.X * deltaUV1.Y;
            if (MathF.Abs(det) < 1e-8f)
                continue; // Degenerate UV triangle skip to avoid NaN

            float f = 1.0f / det;

            Float3 tangent;
            tangent.X = f * (deltaUV2.Y * edge1.X - deltaUV1.Y * edge2.X);
            tangent.Y = f * (deltaUV2.Y * edge1.Y - deltaUV1.Y * edge2.Y);
            tangent.Z = f * (deltaUV2.Y * edge1.Z - deltaUV1.Y * edge2.Z);

            Float3 bitangent;
            bitangent.X = f * (-deltaUV2.X * edge1.X + deltaUV1.X * edge2.X);
            bitangent.Y = f * (-deltaUV2.X * edge1.Y + deltaUV1.X * edge2.Y);
            bitangent.Z = f * (-deltaUV2.X * edge1.Z + deltaUV1.X * edge2.Z);

            tan1[ai] += tangent;  tan1[bi] += tangent;  tan1[ci] += tangent;
            tan2[ai] += bitangent; tan2[bi] += bitangent; tan2[ci] += bitangent;
        }

        var result = new Float4[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            Float3 n = normals != null && i < normals.Length ? normals[i] : Float3.UnitY;
            Float3 t = tan1[i];

            // Gram-Schmidt orthogonalize: t' = normalize(t - n * dot(n, t))
            Float3 orthoT = Float3.Normalize(t - n * Float3.Dot(n, t));
            if (float.IsNaN(orthoT.X) || float.IsNaN(orthoT.Y) || float.IsNaN(orthoT.Z))
                orthoT = Float3.UnitX;

            // Handedness: sign of dot(cross(n, t), bitangent)
            float w = Float3.Dot(Float3.Cross(t, n), tan2[i]) < 0.0f ? -1.0f : 1.0f;

            result[i] = new Float4(orthoT.X, orthoT.Y, orthoT.Z, w);
        }

        Tangents = result;
    }

    #region Raytracing

    /// <summary>
    /// Tests if a ray intersects with this mesh.
    /// </summary>
    /// <param name="ray">The ray to test intersection with</param>
    /// <param name="hitDistance">The distance from ray origin to the closest hit point, if any</param>
    /// <param name="hitNormal">The normal vector at the hit point, if any</param>
    /// <returns>True if the ray intersects with the mesh, false otherwise</returns>
    public bool Raycast(Ray ray, out float hitDistance, out Float3 hitNormal)
    {
        // Initialize out parameters
        hitDistance = float.MaxValue;
        hitNormal = Float3.Zero;

        // Make sure we have vertices and indices
        if (vertices == null || vertices.Length == 0 || indices == null || indices.Length == 0)
            return false;

        bool hit = false;

        // Iterate through triangles in the mesh
        for (int i = 0; i < indices.Length; i += 3)
        {
            // Ensure we have 3 indices for a triangle
            if (i + 2 >= indices.Length)
                break;

            // Get triangle vertices
            uint i1 = indices[i];
            uint i2 = indices[i + 1];
            uint i3 = indices[i + 2];

            // Ensure indices are within bounds
            if (i1 >= vertices.Length || i2 >= vertices.Length || i3 >= vertices.Length)
                continue;

            Float3 v1 = vertices[i1];
            Float3 v2 = vertices[i2];
            Float3 v3 = vertices[i3];

            // Test ray-triangle intersection
            if (ray.Intersects(new Triangle(v1, v2, v3), out float distance, out _, out _) && distance < hitDistance)
            {
                hit = true;
                hitDistance = distance;

                // Calculate normal at hit point (using cross product of triangle edges)
                if (HasNormals)
                {
                    // Use the average of the vertex normals if available
                    hitNormal = (normals[i1] + normals[i2] + normals[i3]) / 3.0f;
                }
                else
                {
                    // Calculate face normal using cross product
                    hitNormal = Float3.Normalize(
                        Float3.Cross(v2 - v1, v3 - v1)
                    );
                }
            }
        }

        return hit;
    }

    /// <summary>
    /// Tests if a ray intersects with this mesh.
    /// </summary>
    /// <param name="ray">The ray to test intersection with</param>
    /// <param name="hitDistance">The distance from ray origin to the hit point, if any</param>
    /// <returns>True if the ray intersects with the mesh, false otherwise</returns>
    public bool Raycast(Ray ray, out float hitDistance)
    {
        bool result = Raycast(ray, out hitDistance, out Float3 hitNormal);
        return result;
    }

    /// <summary>
    /// Tests if a ray intersects with this mesh.
    /// </summary>
    /// <param name="ray">The ray to test intersection with</param>
    /// <returns>True if the ray intersects with the mesh, false otherwise</returns>
    public bool Raycast(Ray ray)
    {
        return Raycast(ray, out float hitDistance);
    }

    #endregion

    public override void OnDispose() => DeleteGPUBuffers();

    private static Mesh fullScreenQuad;
    public static Mesh GetFullscreenQuad()
    {
        if (fullScreenQuad.IsValid()) return fullScreenQuad;
        Mesh mesh = new();
        mesh.vertices = new Float3[4];
        mesh.vertices[0] = new Float3(-1, -1, 0);
        mesh.vertices[1] = new Float3(1, -1, 0);
        mesh.vertices[2] = new Float3(-1, 1, 0);
        mesh.vertices[3] = new Float3(1, 1, 0);

        mesh.uv = new Float2[4];
        mesh.uv[0] = new Float2(0, 0);
        mesh.uv[1] = new Float2(1, 0);
        mesh.uv[2] = new Float2(0, 1);
        mesh.uv[3] = new Float2(1, 1);

        mesh.indices = [0, 2, 1, 2, 3, 1];

        fullScreenQuad = mesh;
        return mesh;
    }

    public static Mesh CreateSphere(float radius, int rings, int slices)
    {
        Mesh mesh = new();

        List<Float3> vertices = [];
        List<Float2> uvs = [];
        List<uint> indices = [];

        for (int i = 0; i <= rings; i++)
        {
            float v = 1 - (float)i / rings;
            float phi = v * MathF.PI;

            for (int j = 0; j <= slices; j++)
            {
                float u = (float)j / slices;
                float theta = u * MathF.PI * 2;

                float x = MathF.Sin(phi) * MathF.Cos(theta);
                float y = MathF.Cos(phi);
                float z = MathF.Sin(phi) * MathF.Sin(theta);

                vertices.Add(new Float3(x, y, z) * radius);
                uvs.Add(new Float2(u, v));
            }
        }

        for (int i = 0; i < rings; i++)
        {
            for (int j = 0; j < slices; j++)
            {
                uint a = (uint)(i * (slices + 1) + j);
                uint b = (uint)(a + slices + 1);

                indices.Add(a);
                indices.Add(b);
                indices.Add(a + 1);

                indices.Add(b);
                indices.Add(b + 1);
                indices.Add(a + 1);
            }
        }

        mesh.vertices = [.. vertices];
        mesh.uv = [.. uvs];
        mesh.indices = [.. indices];

        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();

        return mesh;
    }

    public static Mesh CreateCube(Float3 size)
    {
        Mesh mesh = new();
        float x = (float)size.X / 2f;
        float y = (float)size.Y / 2f;
        float z = (float)size.Z / 2f;

        Float3[] vertices =
        [
            // Front face
            new(-x, -y, z), new(x, -y, z), new(x, y, z), new(-x, y, z),
            
            // Back face
            new(-x, -y, -z), new(x, -y, -z), new(x, y, -z), new(-x, y, -z),
            
            // Left face
            new(-x, -y, -z), new(-x, y, -z), new(-x, y, z), new(-x, -y, z),
            
            // Right face
            new(x, -y, z), new(x, y, z), new(x, y, -z), new(x, -y, -z),
            
            // Top face
            new(-x, y, z), new(x, y, z), new(x, y, -z), new(-x, y, -z),
            
            // Bottom face
            new(-x, -y, -z), new(x, -y, -z), new(x, -y, z), new(-x, -y, z)
        ];

        Float2[] uvs =
        [
            // Front face
            new(0, 0), new(1, 0), new(1, 1), new(0, 1),
            // Back face
            new(1, 0), new(0, 0), new(0, 1), new(1, 1),
            // Left face
            new(0, 0), new(1, 0), new(1, 1), new(0, 1),
            // Right face
            new(1, 0), new(1, 1), new(0, 1), new(0, 0),
            // Top face
            new(0, 1), new(1, 1), new(1, 0), new(0, 0),
            // Bottom face
            new(0, 0), new(1, 0), new(1, 1), new(0, 1)
        ];

        uint[] indices =
        [
            0, 1, 2, 0, 2, 3,       // Front face
            4, 6, 5, 4, 7, 6,       // Back face
            8, 10, 9, 8, 11, 10,    // Left face
            12, 14, 13, 12, 15, 14, // Right face
            16, 17, 18, 16, 18, 19, // Top face
            20, 21, 22, 20, 22, 23  // Bottom face
        ];

        mesh.vertices = vertices;
        mesh.uv = uvs;
        mesh.indices = indices;

        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();

        return mesh;
    }

    public static Mesh CreateCylinder(float radius, float length, int sliceCount)
    {
        // TODO: Test — this hasn't been tested at all, just assumed it will work.
        Mesh mesh = new();

        List<Float3> vertices = [];
        List<Float2> uvs = [];
        List<uint> indices = [];

        float halfLength = length / 2.0f;

        // Create the vertices and UVs for the top and bottom circles
        for (int i = 0; i <= sliceCount; i++)
        {
            float angle = 2 * MathF.PI * i / sliceCount;
            float x = radius * MathF.Cos(angle);
            float z = radius * MathF.Sin(angle);

            // Top circle
            vertices.Add(new Float3(x, halfLength, z));
            uvs.Add(new Float2((float)i / sliceCount, 1));

            // Bottom circle
            vertices.Add(new Float3(x, -halfLength, z));
            uvs.Add(new Float2((float)i / sliceCount, 0));
        }

        // Add the center vertices for the top and bottom circles
        vertices.Add(new Float3(0, halfLength, 0));
        uvs.Add(new Float2(0.5f, 1));
        vertices.Add(new Float3(0, -halfLength, 0));
        uvs.Add(new Float2(0.5f, 0));

        int topCenterIndex = vertices.Count - 2;
        int bottomCenterIndex = vertices.Count - 1;

        // Create the indices for the sides of the cylinder
        for (int i = 0; i < sliceCount; i++)
        {
            int top1 = i * 2;
            int top2 = top1 + 2;
            int bottom1 = top1 + 1;
            int bottom2 = top2 + 1;

            if (i == sliceCount - 1)
            {
                top2 = 0;
                bottom2 = 1;
            }

            indices.Add((uint)top1);
            indices.Add((uint)top2);
            indices.Add((uint)bottom1);

            indices.Add((uint)bottom1);
            indices.Add((uint)top2);
            indices.Add((uint)bottom2);
        }

        // Create the indices for the top and bottom circles
        for (int i = 0; i < sliceCount; i++)
        {
            int top1 = i * 2;
            int top2 = (i == sliceCount - 1) ? 0 : top1 + 2;
            int bottom1 = top1 + 1;
            int bottom2 = (i == sliceCount - 1) ? 1 : bottom1 + 2;

            // Top circle
            indices.Add((uint)top1);
            indices.Add((uint)topCenterIndex);
            indices.Add((uint)top2);

            // Bottom circle
            indices.Add((uint)bottom2);
            indices.Add((uint)bottomCenterIndex);
            indices.Add((uint)bottom1);
        }

        mesh.vertices = [.. vertices];
        mesh.uv = [.. uvs];
        mesh.indices = [.. indices];

        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();

        return mesh;
    }

    /// <summary>
    /// Creates a capsule mesh (cylinder with hemisphere caps).
    /// </summary>
    /// <param name="radius">Radius of the capsule.</param>
    /// <param name="height">Total height of the capsule including the hemisphere caps.</param>
    /// <param name="slices">Number of subdivisions around the capsule.</param>
    /// <param name="stacks">Number of subdivisions along the height of the cylinder portion.</param>
    /// <returns>A new capsule mesh.</returns>
    public static Mesh CreateCapsule(float radius, float height, int slices = 16, int stacks = 4)
    {
        Mesh mesh = new();

        List<Float3> vertices = [];
        List<Float2> uvs = [];
        List<uint> indices = [];

        // Calculate cylinder height (total height minus the two hemisphere radii)
        float cylinderHeight = MathF.Max(0, height - 2 * radius);
        float halfCylinderHeight = cylinderHeight / 2.0f;

        // Generate vertices for the cylinder portion
        for (int i = 0; i <= stacks; i++)
        {
            float v = (float)i / stacks;
            float y = -halfCylinderHeight + cylinderHeight * v;

            for (int j = 0; j <= slices; j++)
            {
                float u = (float)j / slices;
                float angle = u * MathF.PI * 2;
                float x = radius * MathF.Cos(angle);
                float z = radius * MathF.Sin(angle);

                vertices.Add(new Float3(x, y, z));
                uvs.Add(new Float2(u, v * 0.5f + 0.25f)); // Middle 50% of UV space
            }
        }

        // Generate indices for cylinder
        int cylinderVertexCount = (stacks + 1) * (slices + 1);
        for (int i = 0; i < stacks; i++)
        {
            for (int j = 0; j < slices; j++)
            {
                uint a = (uint)(i * (slices + 1) + j);
                uint b = (uint)(a + slices + 1);

                indices.Add(a);
                indices.Add(b);
                indices.Add(a + 1);

                indices.Add(b);
                indices.Add(b + 1);
                indices.Add(a + 1);
            }
        }

        // Generate top hemisphere (cap)
        int hemisphereStacks = (int)MathF.Max(2, stacks / 2);
        int topHemisphereStart = vertices.Count;

        for (int i = 0; i <= hemisphereStacks; i++)
        {
            float v = (float)i / hemisphereStacks;
            float phi = v * MathF.PI / 2; // 0 to PI/2 for top hemisphere

            for (int j = 0; j <= slices; j++)
            {
                float u = (float)j / slices;
                float theta = u * MathF.PI * 2;

                float x = radius * MathF.Sin(phi) * MathF.Cos(theta);
                float y = halfCylinderHeight + radius * MathF.Cos(phi);
                float z = radius * MathF.Sin(phi) * MathF.Sin(theta);

                vertices.Add(new Float3(x, y, z));
                uvs.Add(new Float2(u, 0.75f + v * 0.25f)); // Top 25% of UV space
            }
        }

        // Generate indices for top hemisphere
        for (int i = 0; i < hemisphereStacks; i++)
        {
            for (int j = 0; j < slices; j++)
            {
                uint a = (uint)(topHemisphereStart + i * (slices + 1) + j);
                uint b = (uint)(a + slices + 1);

                indices.Add(a);
                indices.Add(a + 1);
                indices.Add(b);

                indices.Add(b);
                indices.Add(a + 1);
                indices.Add(b + 1);
            }
        }

        // Generate bottom hemisphere (cap)
        int bottomHemisphereStart = vertices.Count;

        for (int i = 0; i <= hemisphereStacks; i++)
        {
            float v = (float)i / hemisphereStacks;
            float phi = MathF.PI / 2 + v * MathF.PI / 2; // PI/2 to PI for bottom hemisphere

            for (int j = 0; j <= slices; j++)
            {
                float u = (float)j / slices;
                float theta = u * MathF.PI * 2;

                float x = radius * MathF.Sin(phi) * MathF.Cos(theta);
                float y = -halfCylinderHeight + radius * MathF.Cos(phi);
                float z = radius * MathF.Sin(phi) * MathF.Sin(theta);

                vertices.Add(new Float3(x, y, z));
                uvs.Add(new Float2(u, v * 0.25f)); // Bottom 25% of UV space
            }
        }

        // Generate indices for bottom hemisphere
        for (int i = 0; i < hemisphereStacks; i++)
        {
            for (int j = 0; j < slices; j++)
            {
                uint a = (uint)(bottomHemisphereStart + i * (slices + 1) + j);
                uint b = (uint)(a + slices + 1);

                indices.Add(a);
                indices.Add(a + 1);
                indices.Add(b);

                indices.Add(b);
                indices.Add(a + 1);
                indices.Add(b + 1);
            }
        }

        mesh.vertices = [.. vertices];
        mesh.uv = [.. uvs];
        mesh.indices = [.. indices];

        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();

        return mesh;
    }

    /// <summary>
    /// Creates a cone mesh pointing up along the Y axis.
    /// </summary>
    /// <param name="radius">Radius of the cone base.</param>
    /// <param name="height">Height of the cone.</param>
    /// <param name="slices">Number of subdivisions around the cone.</param>
    /// <returns>A new cone mesh.</returns>
    public static Mesh CreateCone(float radius, float height, int slices = 16)
    {
        Mesh mesh = new();

        List<Float3> vertices = [];
        List<Float2> uvs = [];
        List<uint> indices = [];

        float halfHeight = height / 2.0f;

        // Apex vertex (top of cone)
        int apexIndex = 0;
        vertices.Add(new Float3(0, halfHeight, 0));
        uvs.Add(new Float2(0.5f, 1.0f));

        // Base center vertex (for base cap)
        int baseCenterIndex = 1;
        vertices.Add(new Float3(0, -halfHeight, 0));
        uvs.Add(new Float2(0.5f, 0.0f));

        // Generate vertices around the base circle
        for (int i = 0; i <= slices; i++)
        {
            float angle = 2 * MathF.PI * i / slices;
            float x = radius * MathF.Cos(angle);
            float z = radius * MathF.Sin(angle);
            float u = (float)i / slices;

            // Vertex for sides
            vertices.Add(new Float3(x, -halfHeight, z));
            uvs.Add(new Float2(u, 0.0f));
        }

        int baseStart = 2; // First base vertex index

        // Generate indices for cone sides (from apex to base)
        for (int i = 0; i < slices; i++)
        {
            indices.Add((uint)apexIndex);
            indices.Add((uint)(baseStart + i));
            indices.Add((uint)(baseStart + i + 1));
        }

        // Generate indices for base cap (circle at bottom)
        for (int i = 0; i < slices; i++)
        {
            indices.Add((uint)baseCenterIndex);
            indices.Add((uint)(baseStart + i + 1));
            indices.Add((uint)(baseStart + i));
        }

        mesh.vertices = [.. vertices];
        mesh.uv = [.. uvs];
        mesh.indices = [.. indices];

        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();

        return mesh;
    }

    public static Mesh CreateTriangle(Float3 a, Float3 b, Float3 c)
    {
        Mesh mesh = new();
        mesh.vertices = [a, b, c];
        mesh.indices = [0, 1, 2];
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        return mesh;
    }

    private void DeleteGPUBuffers()
    {
        vertexArrayObject?.Dispose();
        vertexArrayObject = null;
        vertexBuffer?.Dispose();
        vertexBuffer = null;
        indexBuffer?.Dispose();
        indexBuffer = null;

        // Clean up instanced rendering resources
        instancedVAO?.Dispose();
        instancedVAO = null;
        instanceBuffer?.Dispose();
        instanceBuffer = null;
        instanceBufferCapacity = 0;

        // Morph delta textures will be rebuilt from CPU blend-shape data on next use.
        DisposeMorphTextures();
        _morphDirty = true;
    }

    private T ReadVertexData<T>(T value)
    {
        if (isReadable == false)
            throw new InvalidOperationException("Mesh is not readable");
        return value;
    }

    private void WriteVertexData<T>(ref T target, T value, int length, bool mustMatchLength = true)
    {
        if (isWritable == false)
            throw new InvalidOperationException("Mesh is not writable");
        if ((value == null || length == 0 || length != (vertices?.Length ?? 0)) && mustMatchLength)
            throw new ArgumentException("Array length should match vertices length");
        changed = true;
        target = value;
    }

    internal static VertexFormat GetVertexLayout(Mesh mesh)
    {
        List<Element> elements = [new Element(VertexSemantic.Position, VertexType.Float, 3)];

        if (mesh.HasUV)
            elements.Add(new Element(VertexSemantic.TexCoord0, VertexType.Float, 2));

        if (mesh.HasUV2)
            elements.Add(new Element(VertexSemantic.TexCoord1, VertexType.Float, 2));

        if (mesh.HasNormals)
            elements.Add(new Element(VertexSemantic.Normal, VertexType.Float, 3, 0, true));

        if (mesh.HasColors || mesh.HasColors32)
            elements.Add(new Element(VertexSemantic.Color, VertexType.Float, 4));

        if (mesh.HasTangents)
            elements.Add(new Element(VertexSemantic.Tangent, VertexType.Float, 4, 0, true));

        if (mesh.HasBoneIndices)
            elements.Add(new Element(VertexSemantic.BoneIndex, VertexType.Float, 4));

        if (mesh.HasBoneWeights)
            elements.Add(new Element(VertexSemantic.BoneWeight, VertexType.Float, 4));

        return new VertexFormat([.. elements]);
    }

    internal byte[] MakeVertexDataBlob(VertexFormat layout)
    {
        byte[] buffer = new byte[layout.Size * vertices.Length];

        void Copy(byte[] source, ref int index)
        {
            if (index + source.Length > buffer.Length)
            {
                throw new InvalidOperationException($"[Mesh] Buffer Overrun while generating vertex data blob: {index} -> {index + source.Length} "
                    + $"is larger than buffer {buffer.Length}");
            }

            System.Buffer.BlockCopy(source, 0, buffer, index, source.Length);

            index += source.Length;
        }

        int index = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            if (index % layout.Size != 0)
                throw new InvalidOperationException("[Mesh] Exceeded expected byte count while generating vertex data blob");

            //Copy position
            Copy(BitConverter.GetBytes(vertices[i].X), ref index);
            Copy(BitConverter.GetBytes(vertices[i].Y), ref index);
            Copy(BitConverter.GetBytes(vertices[i].Z), ref index);

            if (HasUV)
            {
                Copy(BitConverter.GetBytes(uv[i].X), ref index);
                Copy(BitConverter.GetBytes(uv[i].Y), ref index);
            }

            if (HasUV2)
            {
                Copy(BitConverter.GetBytes(uv2[i].X), ref index);
                Copy(BitConverter.GetBytes(uv2[i].Y), ref index);
            }

            //Copy normals
            if (HasNormals)
            {
                Copy(BitConverter.GetBytes(normals[i].X), ref index);
                Copy(BitConverter.GetBytes(normals[i].Y), ref index);
                Copy(BitConverter.GetBytes(normals[i].Z), ref index);
            }

            if (HasColors)
            {
                Copy(BitConverter.GetBytes((float)colors[i].R), ref index);
                Copy(BitConverter.GetBytes((float)colors[i].G), ref index);
                Copy(BitConverter.GetBytes((float)colors[i].B), ref index);
                Copy(BitConverter.GetBytes((float)colors[i].A), ref index);
            }
            else if (HasColors32)
            {
                var c = (Color)colors32[i];

                Copy(BitConverter.GetBytes(c.R), ref index);
                Copy(BitConverter.GetBytes(c.G), ref index);
                Copy(BitConverter.GetBytes(c.B), ref index);
                Copy(BitConverter.GetBytes(c.A), ref index);
            }

            if (HasTangents)
            {
                Copy(BitConverter.GetBytes(tangents[i].X), ref index);
                Copy(BitConverter.GetBytes(tangents[i].Y), ref index);
                Copy(BitConverter.GetBytes(tangents[i].Z), ref index);
                Copy(BitConverter.GetBytes(tangents[i].W), ref index);
            }

            if (HasBoneIndices)
            {
                //Copy(new byte[] { boneIndices[i].red, boneIndices[i].green, boneIndices[i].blue, boneIndices[i].alpha }, ref index);
                Copy(BitConverter.GetBytes(boneIndices[i].X), ref index);
                Copy(BitConverter.GetBytes(boneIndices[i].Y), ref index);
                Copy(BitConverter.GetBytes(boneIndices[i].Z), ref index);
                Copy(BitConverter.GetBytes(boneIndices[i].W), ref index);
            }

            if (HasBoneWeights)
            {
                Copy(BitConverter.GetBytes(boneWeights[i].X), ref index);
                Copy(BitConverter.GetBytes(boneWeights[i].Y), ref index);
                Copy(BitConverter.GetBytes(boneWeights[i].Z), ref index);
                Copy(BitConverter.GetBytes(boneWeights[i].W), ref index);
            }
        }

        return buffer;
    }

    public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
    {
        using (MemoryStream memoryStream = new())
        using (BinaryWriter writer = new(memoryStream))
        {
            writer.Write((byte)indexFormat);
            writer.Write((byte)meshTopology);

            writer.Write(vertices.Length);
            foreach (Float3 vertex in vertices)
            {
                writer.Write(vertex.X);
                writer.Write(vertex.Y);
                writer.Write(vertex.Z);
            }

            writer.Write(normals?.Length ?? 0);
            if (normals != null)
            {
                foreach (Float3 normal in normals)
                {
                    writer.Write(normal.X);
                    writer.Write(normal.Y);
                    writer.Write(normal.Z);
                }
            }

            writer.Write(tangents?.Length ?? 0);
            if (tangents != null)
            {
                foreach (Float4 tangent in tangents)
                {
                    writer.Write(tangent.X);
                    writer.Write(tangent.Y);
                    writer.Write(tangent.Z);
                    writer.Write(tangent.W);
                }
            }

            writer.Write(colors?.Length ?? 0);
            if (colors != null)
            {
                foreach (Color color in colors)
                {
                    writer.Write(color.R);
                    writer.Write(color.G);
                    writer.Write(color.B);
                    writer.Write(color.A);
                }
            }

            writer.Write(colors32?.Length ?? 0);
            if (colors32 != null)
            {
                foreach (Color32 color in colors32)
                {
                    writer.Write(color.R);
                    writer.Write(color.G);
                    writer.Write(color.B);
                    writer.Write(color.A);
                }
            }

            writer.Write(uv?.Length ?? 0);
            if (uv != null)
            {
                foreach (Float2 uv in uv)
                {
                    writer.Write(uv.X);
                    writer.Write(uv.Y);
                }
            }

            writer.Write(uv2?.Length ?? 0);
            if (uv2 != null)
            {
                foreach (Float2 uv in uv2)
                {
                    writer.Write(uv.X);
                    writer.Write(uv.Y);
                }
            }

            writer.Write(indices?.Length ?? 0);
            if (indices != null)
            {
                foreach (uint index in indices)
                    writer.Write(index);
            }

            writer.Write(boneIndices?.Length ?? 0);
            if (boneIndices != null)
            {
                foreach (Float4 boneIndex in boneIndices)
                {
                    //writer.Write(boneIndex.red);
                    //writer.Write(boneIndex.green);
                    //writer.Write(boneIndex.blue);
                    //writer.Write(boneIndex.alpha);
                    writer.Write(boneIndex.X);
                    writer.Write(boneIndex.Y);
                    writer.Write(boneIndex.Z);
                    writer.Write(boneIndex.W);
                }
            }

            writer.Write(boneWeights?.Length ?? 0);
            if (boneWeights != null)
            {
                foreach (Float4 boneWeight in boneWeights)
                {
                    writer.Write(boneWeight.X);
                    writer.Write(boneWeight.Y);
                    writer.Write(boneWeight.Z);
                    writer.Write(boneWeight.W);
                }
            }

            writer.Write(BindPoses?.Length ?? 0);
            if (BindPoses != null)
            {
                foreach (Float4x4 bindPose in BindPoses)
                {
                    writer.Write(bindPose[0, 0]);
                    writer.Write(bindPose[0, 1]);
                    writer.Write(bindPose[0, 2]);
                    writer.Write(bindPose[0, 3]);

                    writer.Write(bindPose[1, 0]);
                    writer.Write(bindPose[1, 1]);
                    writer.Write(bindPose[1, 2]);
                    writer.Write(bindPose[1, 3]);

                    writer.Write(bindPose[2, 0]);
                    writer.Write(bindPose[2, 1]);
                    writer.Write(bindPose[2, 2]);
                    writer.Write(bindPose[2, 3]);

                    writer.Write(bindPose[3, 0]);
                    writer.Write(bindPose[3, 1]);
                    writer.Write(bindPose[3, 2]);
                    writer.Write(bindPose[3, 3]);
                }
            }

            writer.Write(BoneNames?.Length ?? 0);
            if (BoneNames != null)
            {
                foreach (string boneName in BoneNames)
                    writer.Write(boneName);
            }

            // Submeshes
            writer.Write(_subMeshes.Count);
            foreach (var sub in _subMeshes)
            {
                writer.Write(sub.IndexStart);
                writer.Write(sub.IndexCount);
                writer.Write((int)sub.Topology);
            }

            // Blend shapes (written after submeshes; older meshes simply lack this trailing block)
            writer.Write(_blendShapes.Length);
            foreach (var bs in _blendShapes)
            {
                writer.Write(bs.Name ?? string.Empty);
                writer.Write(bs.Frames.Length);
                foreach (var f in bs.Frames)
                {
                    writer.Write(f.Weight);
                    bool hasN = f.DeltaNormals != null;
                    bool hasT = f.DeltaTangents != null;
                    writer.Write(hasN);
                    writer.Write(hasT);
                    writer.Write(f.DeltaVertices.Length);
                    foreach (var d in f.DeltaVertices) { writer.Write(d.X); writer.Write(d.Y); writer.Write(d.Z); }
                    if (hasN) foreach (var d in f.DeltaNormals) { writer.Write(d.X); writer.Write(d.Y); writer.Write(d.Z); }
                    if (hasT) foreach (var d in f.DeltaTangents) { writer.Write(d.X); writer.Write(d.Y); writer.Write(d.Z); }
                }
            }

            compoundTag.Add("MeshData", new EchoObject(memoryStream.ToArray()));
            compoundTag.Add("MeshType", new EchoObject((int)meshTopology));
            compoundTag.Add("MeshIndexFormat", new EchoObject((int)indexFormat));
            compoundTag.Add("BoundsMinX", new EchoObject(bounds.Min.X));
            compoundTag.Add("BoundsMinY", new EchoObject(bounds.Min.Y));
            compoundTag.Add("BoundsMinZ", new EchoObject(bounds.Min.Z));
            compoundTag.Add("BoundsMaxX", new EchoObject(bounds.Max.X));
            compoundTag.Add("BoundsMaxY", new EchoObject(bounds.Max.Y));
            compoundTag.Add("BoundsMaxZ", new EchoObject(bounds.Max.Z));
        }
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        meshTopology = (Topology)value["MeshType"].IntValue;
        indexFormat = (IndexFormat)value["MeshIndexFormat"].IntValue;
        bounds = new AABB(
            new Float3(value["BoundsMinX"].FloatValue, value["BoundsMinY"].FloatValue, value["BoundsMinZ"].FloatValue),
            new Float3(value["BoundsMaxX"].FloatValue, value["BoundsMaxY"].FloatValue, value["BoundsMaxZ"].FloatValue)
        );

        using (MemoryStream memoryStream = new(value["MeshData"].ByteArrayValue))
        using (BinaryReader reader = new(memoryStream))
        {
            indexFormat = (IndexFormat)reader.ReadByte();
            meshTopology = (Topology)reader.ReadByte();

            int vertexCount = reader.ReadInt32();
            vertices = new Float3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                vertices[i] = new Float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

            int normalCount = reader.ReadInt32();
            if (normalCount > 0)
            {
                normals = new Float3[normalCount];
                for (int i = 0; i < normalCount; i++)
                    normals[i] = new Float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }

            int tangentCount = reader.ReadInt32();
            if (tangentCount > 0)
            {
                tangents = new Float4[tangentCount];
                for (int i = 0; i < tangentCount; i++)
                    tangents[i] = new Float4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }

            int colorCount = reader.ReadInt32();
            if (colorCount > 0)
            {
                colors = new Color[colorCount];
                for (int i = 0; i < colorCount; i++)
                    colors[i] = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }

            int color32Count = reader.ReadInt32();
            if (color32Count > 0)
            {
                colors32 = new Color32[color32Count];
                for (int i = 0; i < color32Count; i++)
                    colors32[i] = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
            }

            int uvCount = reader.ReadInt32();
            if (uvCount > 0)
            {
                uv = new Float2[uvCount];
                for (int i = 0; i < uvCount; i++)
                    uv[i] = new Float2(reader.ReadSingle(), reader.ReadSingle());
            }

            int uv2Count = reader.ReadInt32();
            if (uv2Count > 0)
            {
                uv2 = new Float2[uv2Count];
                for (int i = 0; i < uv2Count; i++)
                    uv2[i] = new Float2(reader.ReadSingle(), reader.ReadSingle());
            }

            int indexCount = reader.ReadInt32();
            if (indexCount > 0)
            {
                indices = new uint[indexCount];
                for (int i = 0; i < indexCount; i++)
                    indices[i] = reader.ReadUInt32();
            }

            int boneIndexCount = reader.ReadInt32();
            if (boneIndexCount > 0)
            {
                boneIndices = new Float4[boneIndexCount];
                for (int i = 0; i < boneIndexCount; i++)
                {
                    //boneIndices[i] = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
                    boneIndices[i] = new Float4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                }
            }

            int boneWeightCount = reader.ReadInt32();
            if (boneWeightCount > 0)
            {
                boneWeights = new Float4[boneWeightCount];
                for (int i = 0; i < boneWeightCount; i++)
                    boneWeights[i] = new Float4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }

            int BindPosesCount = reader.ReadInt32();
            if (BindPosesCount > 0)
            {
                BindPoses = new Float4x4[BindPosesCount];
                for (int i = 0; i < BindPosesCount; i++)
                {
                    var val = new Float4x4();

                    val[0, 0] = reader.ReadSingle();
                    val[0, 1] = reader.ReadSingle();
                    val[0, 2] = reader.ReadSingle();
                    val[0, 3] = reader.ReadSingle();

                    val[1, 0] = reader.ReadSingle();
                    val[1, 1] = reader.ReadSingle();
                    val[1, 2] = reader.ReadSingle();
                    val[1, 3] = reader.ReadSingle();

                    val[2, 0] = reader.ReadSingle();
                    val[2, 1] = reader.ReadSingle();
                    val[2, 2] = reader.ReadSingle();
                    val[2, 3] = reader.ReadSingle();

                    val[3, 0] = reader.ReadSingle();
                    val[3, 1] = reader.ReadSingle();
                    val[3, 2] = reader.ReadSingle();
                    val[3, 3] = reader.ReadSingle();

                    BindPoses[i] = val;
                }
            }

            // Try to read bone names
            int BoneNamesCount = reader.ReadInt32();
            if (BoneNamesCount > 0)
            {
                BoneNames = new string[BoneNamesCount];
                for (int i = 0; i < BoneNamesCount; i++)
                    BoneNames[i] = reader.ReadString();
            }

            // Try to read submeshes (may not exist in older mesh data)
            _subMeshes.Clear();
            if (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                try
                {
                    int subMeshCount = reader.ReadInt32();
                    for (int i = 0; i < subMeshCount; i++)
                    {
                        int start = reader.ReadInt32();
                        int count = reader.ReadInt32();
                        var topo = (Topology)reader.ReadInt32();
                        _subMeshes.Add(new SubMeshDescriptor(start, count, topo));
                    }
                }
                catch { /* Old format without submeshes ignore */ }
            }

            // Blend shapes (trailing block; absent in meshes serialized before morph support)
            _blendShapes = Array.Empty<BlendShape>();
            if (reader.BaseStream.Position < reader.BaseStream.Length)
            {
                try
                {
                    int bsCount = reader.ReadInt32();
                    var list = new BlendShape[bsCount];
                    for (int i = 0; i < bsCount; i++)
                    {
                        var bs = new BlendShape { Name = reader.ReadString() };
                        int frameCount = reader.ReadInt32();
                        bs.Frames = new BlendShapeFrame[frameCount];
                        for (int fi = 0; fi < frameCount; fi++)
                        {
                            var f = new BlendShapeFrame { Weight = reader.ReadSingle() };
                            bool hasN = reader.ReadBoolean();
                            bool hasT = reader.ReadBoolean();
                            int dvCount = reader.ReadInt32();
                            f.DeltaVertices = new Float3[dvCount];
                            for (int v = 0; v < dvCount; v++)
                                f.DeltaVertices[v] = new Float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            if (hasN)
                            {
                                f.DeltaNormals = new Float3[dvCount];
                                for (int v = 0; v < dvCount; v++)
                                    f.DeltaNormals[v] = new Float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            }
                            if (hasT)
                            {
                                f.DeltaTangents = new Float3[dvCount];
                                for (int v = 0; v < dvCount; v++)
                                    f.DeltaTangents[v] = new Float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                            }
                            bs.Frames[fi] = f;
                        }
                        list[i] = bs;
                    }
                    _blendShapes = list;
                    _morphDirty = true;
                }
                catch { /* Old format without blend shapes ignore */ }
            }

            changed = true;
        }
    }
}
