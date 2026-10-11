// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using Prowl.Echo;
using Prowl.Runtime.Rendering;
using Prowl.Vector;
using Prowl.Vector.Geometry;

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
public class Mesh : Asset, ISerializable
{
    private readonly bool _isReadable = true;
    private readonly bool _isWritable = true;

    /// <summary> Whether this mesh is readable by the CPU </summary>
    public bool isReadable { get { EnsureLoaded(); return _isReadable; } }

    /// <summary> Whether this mesh is writable </summary>
    public bool isWritable { get { EnsureLoaded(); return _isWritable; } }

    private AABB _bounds;

    /// <summary> The bounds of the mesh </summary>
    public AABB bounds { get { EnsureLoaded(); return _bounds; } internal set => _bounds = value; }

    /// <summary> The format of the indices for this mesh </summary>
    public IndexFormat IndexFormat
    {
        get { EnsureLoaded(); return indexFormat; }
        set
        {
            EnsureLoaded();
            if (isWritable == false) return;
            changed = true;
            indexFormat = value;
            indices = [];
        }
    }

    /// <summary> The mesh's primitive type </summary>
    public Topology MeshTopology
    {
        get { EnsureLoaded(); return meshTopology; }
        set
        {
            EnsureLoaded();
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
        get { EnsureLoaded(); return vertices ?? []; }
        set
        {
            EnsureLoaded();
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

    public int VertexCount { get { EnsureLoaded(); return vertices?.Length ?? 0; } }
    public int IndexCount { get { EnsureLoaded(); return indices?.Length ?? 0; } }

    public GraphicsVertexArray? VertexArrayObject { get { EnsureLoaded(); return vertexArrayObject; } }
    public GraphicsBuffer VertexBuffer { get { EnsureLoaded(); return vertexBuffer; } }
    public GraphicsBuffer IndexBuffer { get { EnsureLoaded(); return indexBuffer; } }

    public bool HasNormals { get { EnsureLoaded(); return (normals?.Length ?? 0) > 0; } }
    public bool HasTangents { get { EnsureLoaded(); return (tangents?.Length ?? 0) > 0; } }
    public bool HasColors { get { EnsureLoaded(); return (colors?.Length ?? 0) > 0; } }
    public bool HasColors32 { get { EnsureLoaded(); return (colors32?.Length ?? 0) > 0; } }
    public bool HasUV { get { EnsureLoaded(); return (uv?.Length ?? 0) > 0; } }
    public bool HasUV2 { get { EnsureLoaded(); return (uv2?.Length ?? 0) > 0; } }

    public bool HasBoneIndices { get { EnsureLoaded(); return (boneIndices?.Length ?? 0) > 0; } }
    public bool HasBoneWeights { get { EnsureLoaded(); return (boneWeights?.Length ?? 0) > 0; } }

    private Float4x4[]? _bindPoses;
    private string[]? _boneNames;
    public Float4x4[]? BindPoses { get { EnsureLoaded(); return _bindPoses; } set { EnsureLoaded(); _bindPoses = value; } }
    public string[]? BoneNames { get { EnsureLoaded(); return _boneNames; } set { EnsureLoaded(); _boneNames = value; } }

    // ─────────────────────── Blend shapes (morph targets) ───────────────────────
    private BlendShape[] _blendShapes = Array.Empty<BlendShape>();

    // GPU morph deltas, built lazily from _blendShapes. Each layer is one BlendShapeFrame, its deltas at
    // layer * vertexCount + vertexID, positions first, then normals and tangents when any frame has them
    private ShaderDataTable? _morphDeltas;
    private int _morphNormalBase = -1, _morphTangentBase = -1;
    private int[] _morphLayerOffsets = Array.Empty<int>(); // per-shape starting layer
    private int _morphLayerCount;
    private bool _morphDirty = true;

    /// <summary>The blend shapes (morph targets) on this mesh.</summary>
    public BlendShape[] BlendShapes
    {
        get { EnsureLoaded(); return _blendShapes; }
        set { EnsureLoaded(); _blendShapes = value ?? Array.Empty<BlendShape>(); _morphDirty = true; }
    }

    public bool HasBlendShapes { get { EnsureLoaded(); return _blendShapes.Length > 0; } }
    public int BlendShapeCount { get { EnsureLoaded(); return _blendShapes.Length; } }

    public string GetBlendShapeName(int index)
    {
        EnsureLoaded();
        return (index >= 0 && index < _blendShapes.Length) ? _blendShapes[index].Name : string.Empty;
    }

    /// <summary>Index of the blend shape with the given name, or -1 if not found.</summary>
    public int GetBlendShapeIndex(string name)
    {
        EnsureLoaded();
        for (int i = 0; i < _blendShapes.Length; i++)
            if (_blendShapes[i].Name == name) return i;
        return -1;
    }

    public int GetBlendShapeFrameCount(int shapeIndex)
    {
        EnsureLoaded();
        return (shapeIndex >= 0 && shapeIndex < _blendShapes.Length) ? _blendShapes[shapeIndex].Frames.Length : 0;
    }

    public float GetBlendShapeFrameWeight(int shapeIndex, int frameIndex)
    {
        EnsureLoaded();
        if (shapeIndex < 0 || shapeIndex >= _blendShapes.Length) return 0f;
        var frames = _blendShapes[shapeIndex].Frames;
        return (frameIndex >= 0 && frameIndex < frames.Length) ? frames[frameIndex].Weight : 0f;
    }

    // GPU morph resources (valid after EnsureMorphDeltas).
    internal ShaderDataTable? MorphDeltas { get { EnsureLoaded(); return _morphDeltas; } }

    /// <summary>Where the normal deltas start in <see cref="MorphDeltas"/>, -1 when no frame has any.</summary>
    internal int MorphNormalBase { get { EnsureLoaded(); return _morphNormalBase; } }

    /// <summary>Where the tangent deltas start in <see cref="MorphDeltas"/>, -1 when no frame has any.</summary>
    internal int MorphTangentBase { get { EnsureLoaded(); return _morphTangentBase; } }

    public int MorphLayerCount { get { EnsureLoaded(); return _morphLayerCount; } }

    /// <summary>Global morph-texture layer (row block) for a given shape's frame.</summary>
    public int GetMorphLayerIndex(int shapeIndex, int frameIndex)
    {
        EnsureLoaded();
        return _morphLayerOffsets[shapeIndex] + frameIndex;
    }

    /// <summary>Builds the GPU morph deltas from the blend-shape data if dirty. Cheap no-op otherwise.</summary>
    internal void EnsureMorphDeltas()
    {
        EnsureLoaded();
        if (!_morphDirty) return;
        BuildMorphDeltas();
    }

    private void BuildMorphDeltas()
    {
        _morphDirty = false;
        DisposeMorphDeltas();
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

        long total = (long)layers * vtx;
        long texels = total * (1 + (anyNormals ? 1 : 0) + (anyTangents ? 1 : 0));
        if (texels > int.MaxValue / 2)
        {
            Debug.LogError($"[Mesh] Blend-shape morph data ({layers} layers x {vtx} verts) is too large; morphs disabled for '{Name}'.");
            return;
        }

        _morphLayerCount = layers;
        int perKind = (int)total;
        var pos = new Float4[perKind];
        var nrm = anyNormals ? new Float4[perKind] : null;
        var tan = anyTangents ? new Float4[perKind] : null;

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

        _morphDeltas = new ShaderDataTable("ProwlMorphDeltas", "_MorphDeltaTex", GraphicsFeature.VertexStorageBuffers);
        _morphDeltas.EnsureCapacity((int)texels);
        _morphDeltas.Write(0, pos);
        int next = perKind;
        if (nrm != null)
        {
            _morphNormalBase = next;
            _morphDeltas.Write(next, nrm);
            next += perKind;
        }
        if (tan != null)
        {
            _morphTangentBase = next;
            _morphDeltas.Write(next, tan);
        }
    }

    private void DisposeMorphDeltas()
    {
        _morphDeltas?.Dispose();
        _morphDeltas = null;
        _morphNormalBase = _morphTangentBase = -1;
    }

    // Submesh support: each submesh defines a range within the shared index buffer
    private List<SubMeshDescriptor> _subMeshes = new();

    /// <summary>Number of submeshes. Returns 1 if no submeshes defined (entire mesh is one submesh).</summary>
    public int SubMeshCount { get { EnsureLoaded(); return _subMeshes.Count > 0 ? _subMeshes.Count : 1; } }

    /// <summary>Get a submesh descriptor. If no submeshes defined, index 0 returns the full mesh range.</summary>
    public SubMeshDescriptor GetSubMesh(int index)
    {
        EnsureLoaded();
        if (_subMeshes.Count == 0)
            return new SubMeshDescriptor(0, indices?.Length ?? 0, meshTopology);
        return _subMeshes[index];
    }

    /// <summary>Set the number of submeshes.</summary>
    public void SetSubMeshCount(int count)
    {
        EnsureLoaded();
        while (_subMeshes.Count < count) _subMeshes.Add(default);
        while (_subMeshes.Count > count) _subMeshes.RemoveAt(_subMeshes.Count - 1);
        changed = true;
    }

    /// <summary>Set a submesh descriptor at the given index.</summary>
    public void SetSubMesh(int index, SubMeshDescriptor desc)
    {
        EnsureLoaded();
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

    [SerializeIgnore, NotContent] private uint _version = 1;

    /// <summary>
    /// Monotonic version that advances whenever the mesh's data changes (vertices, indices, topology,
    /// submeshes, ...). Mirrors <see cref="Prowl.Vector.Transform.Version"/>. Useful for invalidating
    /// caches derived from this mesh (e.g. baked physics meshes - see <see cref="PhysicsWorld.BakeMesh"/>).
    /// </summary>
    public uint Version { get { EnsureLoaded(); return _version; } }

    /// <summary>
    /// True if <see cref="Version"/> differs from <paramref name="lastVersion"/>; updates the reference
    /// to the current version so the next call compares against today's state.
    /// </summary>
    public bool HasChanged(ref uint lastVersion)
    {
        EnsureLoaded();
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

    /// <summary>Cached physics bake (see <see cref="PhysicsWorld.BakeMesh"/>).</summary>
    internal BakedPhysicsMesh? BakedPhysics;

    public Mesh() { }

    public void Clear()
    {
        EnsureLoaded();
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
        EnsureLoaded();
        if (changed == false && vertexArrayObject != null)
            return;

        changed = false;

        // Invalid geometry is a data problem, not a programmer error: it commonly comes from a
        // procedural or imported mesh mid edit. Uploading such a mesh is skipped (VAO stays null, so
        // callers that check VertexArrayObject simply do not draw it) and reported instead of
        // throwing into the render loop, which would take down the whole frame.
        if (vertices == null || vertices.Length == 0)
            { WarnUploadSkipped("mesh has no vertices"); return; }

        if (indices == null || indices.Length == 0)
            { WarnUploadSkipped("mesh has no indices"); return; }

        switch (meshTopology)
        {
            case Topology.Triangles:
                if (indices.Length % 3 != 0)
                    { WarnUploadSkipped($"triangle mesh index count {indices.Length} is not a multiple of 3"); return; }
                break;
            case Topology.TriangleStrip:
                if (indices.Length < 3)
                    { WarnUploadSkipped($"triangle strip mesh has {indices.Length} indices, needs at least 3"); return; }
                break;

            case Topology.Lines:
                if (indices.Length % 2 != 0)
                    { WarnUploadSkipped($"line mesh index count {indices.Length} is not a multiple of 2"); return; }
                break;

            case Topology.LineStrip:
                if (indices.Length < 2)
                    { WarnUploadSkipped($"line strip mesh has {indices.Length} indices, needs at least 2"); return; }
                break;
        }

        VertexFormat layout = GetVertexLayout(this);

        if (layout == null)
        {
            Debug.LogError($"[Mesh] Failed to get vertex layout for this mesh!");
            return;
        }

        byte[] vertexBlob = MakeVertexDataBlob(layout, out int vertexBlobLength);
        ReadOnlySpan<byte> vertexData = vertexBlob.AsSpan(0, vertexBlobLength);

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
            cmd.UpdateBuffer<byte>(vertexBuffer, vertexData);
        }
        else
        {
            vertexBuffer?.Dispose();
            vertexBuffer = new GraphicsBuffer(BufferType.VertexBuffer, vertexData, true);
            lastVertexCount = vertices.Length;
            lastVertexLayout = layout;
        }

        // Both paths above copy the data, so the pooled blob can go back straight away.
        ArrayPool<byte>.Shared.Return(vertexBlob);

        if (indexFormat == IndexFormat.UInt16)
        {
            ushort[] data = new ushort[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                if (indices[i] > ushort.MaxValue)
                    { WarnUploadSkipped($"index {indices[i]} exceeds the 16-bit range (use IndexFormat.UInt32)"); return; }
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

            // The instanced VAO points at the old buffers too; EnsureInstanceVAO rebuilds it.
            if (instancedVAO != null) Graphics.DeferDispose(instancedVAO);
            instancedVAO = null;
        }
    }

    /// <summary>Logs an upload skipped reason so <see cref="Upload"/> can bail without throwing into a
    /// render or update loop.</summary>
    private void WarnUploadSkipped(string reason)
        => Debug.LogWarning($"[Mesh] Upload skipped ({Name ?? "unnamed"}): {reason}");

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
        EnsureLoaded();
        Upload();

        // Base upload was skipped (invalid geometry), so there is no VAO to instance from. Bail.
        if (vertexArrayObject == null)
        {
            instanceBuf = null;
            return null;
        }

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
            var instanceFormat = new VertexFormat(new[]
            {
                new Element((VertexSemantic)8, VertexType.Float, 4, divisor: 1),  // ModelRow0
                new Element((VertexSemantic)9, VertexType.Float, 4, divisor: 1),  // ModelRow1
                new Element((VertexSemantic)10, VertexType.Float, 4, divisor: 1), // ModelRow2
                new Element((VertexSemantic)11, VertexType.Float, 4, divisor: 1), // ModelRow3
                new Element((VertexSemantic)12, VertexType.Float, 4, divisor: 1), // Color (RGBA)
                new Element((VertexSemantic)13, VertexType.Float, 4, divisor: 1), // CustomData
            });
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
        EnsureLoaded();
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
        EnsureLoaded();
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
            // Float3.Normalize returns Zero (not NaN) for a zero-length vector, which a vertex no
            // triangle references - or one whose face normals cancel - ends up with. A zero normal
            // reaching the GPU becomes NaN at the shader's normalize and poisons every lighting
            // term downstream, so length is what has to be tested, not NaN.
            Float3 n = Float3.Normalize(normals[i]);
            normals[i] = Float3.LengthSquared(n) > 1e-12f ? n : Float3.UnitY;
        }

        Normals = normals;
    }

    public void RecalculateTangents()
    {
        EnsureLoaded();
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

            Float3 tangent = default;
            tangent.X = f * (deltaUV2.Y * edge1.X - deltaUV1.Y * edge2.X);
            tangent.Y = f * (deltaUV2.Y * edge1.Y - deltaUV1.Y * edge2.Y);
            tangent.Z = f * (deltaUV2.Y * edge1.Z - deltaUV1.Y * edge2.Z);

            Float3 bitangent = default;
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
            // Same trap as RecalculateNormals: a vertex whose incident triangles were all skipped as
            // degenerate-UV accumulates a zero tangent, and Float3.Normalize hands back Zero rather
            // than NaN, so a NaN test misses it and the zero survives to the GPU as a NaN frame.
            Float3 orthoT = Float3.Normalize(t - n * Float3.Dot(n, t));
            if (!(Float3.LengthSquared(orthoT) > 1e-12f))
                orthoT = MathF.Abs(n.Y) < 0.999f
                    ? Float3.Normalize(Float3.Cross(n, Float3.UnitY))
                    : Float3.Normalize(Float3.Cross(n, Float3.UnitX));

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
        EnsureLoaded();
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

    protected override void OnUnload() => DeleteGPUBuffers();

    // Version keeps moving forward across a refill, so a cache built from the old content never matches the new.
    protected override void TakeContent(Asset staging)
    {
        base.TakeContent(staging);
        _version++;
    }

    protected internal override long EstimateBytes()
        => (long)(vertices?.Length ?? 0) * 64 * 2 + (long)(indices?.Length ?? 0) * 4;

    // A runtime mesh nothing references still has to free its GPU buffers. Database meshes are never collected.
    ~Mesh()
    {
        if (!Registered) Dispose();
    }

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
        // TODO: Test this hasn't been tested at all, just assumed it will work.
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

        // The cap gets its own ring so its flat normals don't blend into the sides
        int capStart = vertices.Count;
        for (int i = 0; i <= slices; i++)
        {
            vertices.Add(vertices[baseStart + i]);
            uvs.Add(uvs[baseStart + i]);
        }

        // Generate indices for cone sides (from apex to base)
        for (int i = 0; i < slices; i++)
        {
            indices.Add((uint)apexIndex);
            indices.Add((uint)(baseStart + i + 1));
            indices.Add((uint)(baseStart + i));
        }

        // Generate indices for base cap (circle at bottom)
        for (int i = 0; i < slices; i++)
        {
            indices.Add((uint)baseCenterIndex);
            indices.Add((uint)(capStart + i));
            indices.Add((uint)(capStart + i + 1));
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

        // Morph deltas will be rebuilt from CPU blend-shape data on next use.
        DisposeMorphDeltas();
        _morphDirty = true;
    }

    private T ReadVertexData<T>(T value)
    {
        EnsureLoaded();
        if (isReadable == false)
            throw new InvalidOperationException("Mesh is not readable");
        return value;
    }

    private void WriteVertexData<T>(ref T target, T value, int length, bool mustMatchLength = true)
    {
        EnsureLoaded();
        if (isWritable == false)
            throw new InvalidOperationException("Mesh is not writable");
        if (vertices?.Length == 0)
            throw new ArgumentException("Vertices data must not be empty when assigning vertex data");
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

    // Interleaves the vertex attributes into a buffer rented from ArrayPool<byte>.Shared, which the
    // caller returns once the data has been handed to the GPU.
    internal byte[] MakeVertexDataBlob(VertexFormat layout, out int length)
    {
        length = layout.Size * vertices.Length;
        byte[] buffer = ArrayPool<byte>.Shared.Rent(length);
        Span<float> dst = MemoryMarshal.Cast<byte, float>(buffer.AsSpan(0, length));

        bool hasUV = HasUV, hasUV2 = HasUV2, hasNormals = HasNormals, hasTangents = HasTangents;
        bool hasColors = HasColors, hasColors32 = !hasColors && HasColors32;
        bool hasBoneIndices = HasBoneIndices, hasBoneWeights = HasBoneWeights;

        int k = 0;
        for (int i = 0; i < vertices.Length; i++)
        {
            Float3 p = vertices[i];
            dst[k++] = p.X; dst[k++] = p.Y; dst[k++] = p.Z;

            if (hasUV)
            {
                Float2 t = uv[i];
                dst[k++] = t.X; dst[k++] = t.Y;
            }

            if (hasUV2)
            {
                Float2 t = uv2[i];
                dst[k++] = t.X; dst[k++] = t.Y;
            }

            if (hasNormals)
            {
                Float3 n = normals[i];
                dst[k++] = n.X; dst[k++] = n.Y; dst[k++] = n.Z;
            }

            if (hasColors || hasColors32)
            {
                Color c = hasColors ? colors[i] : (Color)colors32[i];
                dst[k++] = c.R; dst[k++] = c.G; dst[k++] = c.B; dst[k++] = c.A;
            }

            if (hasTangents)
            {
                Float4 t = tangents[i];
                dst[k++] = t.X; dst[k++] = t.Y; dst[k++] = t.Z; dst[k++] = t.W;
            }

            if (hasBoneIndices)
            {
                Float4 b = boneIndices[i];
                dst[k++] = b.X; dst[k++] = b.Y; dst[k++] = b.Z; dst[k++] = b.W;
            }

            if (hasBoneWeights)
            {
                Float4 w = boneWeights[i];
                dst[k++] = w.X; dst[k++] = w.Y; dst[k++] = w.Z; dst[k++] = w.W;
            }
        }

        if (k != dst.Length)
        {
            ArrayPool<byte>.Shared.Return(buffer);
            throw new InvalidOperationException($"[Mesh] Vertex data blob wrote {k * sizeof(float)} bytes but the layout expects {length}");
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

            writer.Write(vertices?.Length ?? 0);
            if (vertices != null)
            {
                foreach (Float3 vertex in vertices)
                {
                    writer.Write(vertex.X);
                    writer.Write(vertex.Y);
                    writer.Write(vertex.Z);
                }
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

            SerializeHeader(compoundTag);
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
        DeserializeHeader(value);

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

/// <summary>
/// Converts between <see cref="Mesh"/> and <see cref="GeometryData"/>, so the geometry operators can work
/// on engine meshes.
/// <para/>
/// Mesh vertices that share a position become one geometry vertex, and everything a mesh vertex carries
/// (normal, tangent, UVs, color, skinning, blend shape offsets) is kept per corner on the loops. Split
/// vertices therefore show up as loops at one vertex with different values, which is what the operators
/// treat as seams, so both sides of a split stay joined. Each face records its submesh.
/// <para/>
/// A triangle that reuses corners another triangle already took, like the back of a double sided card,
/// goes onto its own copy of those vertices so each side stays a clean surface. A triangle whose corners
/// share a position gets vertices of its own and is kept as it is.
/// <para/>
/// Skinning comes back in a canonical slot order: sorted by bone, unused slots on bone 0.
/// </summary>
public static class MeshGeometry
{
    // Loop attributes
    public const string Normal = "normal";
    public const string Tangent = "tangent";
    public const string UV = "uv";
    public const string UV2 = "uv2";
    public const string VertexColor = "color";
    public const string BoneIndices = "bone_indices";
    public const string BoneWeights = "bone_weights";

    // Face attributes
    public const string SubMesh = "submesh";

    public const string BlendShapePrefix = "blendshape/";

    public static string BlendShapePosition(int shape, int frame) => $"{BlendShapePrefix}{shape}/{frame}/position";
    public static string BlendShapeNormal(int shape, int frame) => $"{BlendShapePrefix}{shape}/{frame}/normal";
    public static string BlendShapeTangent(int shape, int frame) => $"{BlendShapePrefix}{shape}/{frame}/tangent";

    /// <summary>One blend shape frame's attribute names, resolved once rather than per corner.</summary>
    private readonly struct FrameNames(int shape, int frame)
    {
        public readonly string Position = BlendShapePosition(shape, frame);
        public readonly string Normal = BlendShapeNormal(shape, frame);
        public readonly string Tangent = BlendShapeTangent(shape, frame);
    }

    /// <summary>Builds geometry from a triangle mesh. Throws for a submesh with triangles in another topology.</summary>
    public static GeometryData ToGeometryData(Mesh mesh)
    {
        for (int s = 0; s < mesh.SubMeshCount; s++)
        {
            var sub = mesh.GetSubMesh(s);
            if (sub.IndexCount > 0 && sub.Topology != Topology.Triangles)
                throw new InvalidOperationException($"Mesh '{mesh.Name}' has a {sub.Topology} submesh, only triangle meshes convert to geometry.");
        }

        Float3[] positions = mesh.Vertices;
        uint[] indices = mesh.Indices;
        BlendShape[] shapes = mesh.BlendShapes;
        int count = positions.Length;

        Float3[] normals = Usable(mesh, mesh.Normals, count, "normals");
        Float4[] tangents = Usable(mesh, mesh.Tangents, count, "tangents");
        Float2[] uv = Usable(mesh, mesh.UV, count, "UVs");
        Float2[] uv2 = Usable(mesh, mesh.UV2, count, "second UVs");
        Color[] colors = Usable(mesh, mesh.Colors, count, "colors");
        Color32[] colors32 = colors.Length == 0 ? Usable(mesh, mesh.Colors32, count, "colors") : [];
        Float4[] boneIndices = Usable(mesh, mesh.BoneIndices, count, "bone indices");
        Float4[] boneWeights = Usable(mesh, mesh.BoneWeights, count, "bone weights");
        bool hasSkin = boneIndices.Length > 0 && boneWeights.Length > 0;

        var geometry = new GeometryData();
        if (normals.Length > 0) geometry.AddLoopAttribute(Normal, GeometryData.AttributeBaseType.Float, 3);
        if (tangents.Length > 0) geometry.AddLoopAttribute(Tangent, GeometryData.AttributeBaseType.Float, 4);
        if (uv.Length > 0) geometry.AddLoopAttribute(UV, GeometryData.AttributeBaseType.Float, 2);
        if (uv2.Length > 0) geometry.AddLoopAttribute(UV2, GeometryData.AttributeBaseType.Float, 2);
        if (colors.Length > 0 || colors32.Length > 0) geometry.AddLoopAttribute(VertexColor, GeometryData.AttributeBaseType.Float, 4);
        if (hasSkin)
        {
            geometry.AddLoopAttribute(BoneIndices, GeometryData.AttributeBaseType.Int, 4);
            geometry.AddLoopAttribute(BoneWeights, GeometryData.AttributeBaseType.Float, 4);
        }

        var frames = new List<(BlendShapeFrame Frame, FrameNames Names, bool Normals, bool Tangents)>();
        for (int s = 0; s < shapes.Length; s++)
        {
            for (int f = 0; f < shapes[s].Frames.Length; f++)
            {
                var frame = shapes[s].Frames[f];
                var names = new FrameNames(s, f);
                bool hasNormals = frame.DeltaNormals?.Length == count, hasTangents = frame.DeltaTangents?.Length == count;
                geometry.AddLoopAttribute(names.Position, GeometryData.AttributeBaseType.Float, 3);
                if (hasNormals) geometry.AddLoopAttribute(names.Normal, GeometryData.AttributeBaseType.Float, 3);
                if (hasTangents) geometry.AddLoopAttribute(names.Tangent, GeometryData.AttributeBaseType.Float, 3);
                frames.Add((frame, names, hasNormals, hasTangents));
            }
        }
        geometry.AddFaceAttribute(SubMesh, GeometryData.AttributeBaseType.Int, 1);

        // Vertices that share a position share a geometry vertex
        var weld = new int[count];
        var points = new Dictionary<Float3, int>(count, PositionComparer.Instance);
        var basePoints = new List<Float3>();
        for (int i = 0; i < count; i++)
        {
            if (!points.TryGetValue(positions[i], out weld[i]))
            {
                weld[i] = points[positions[i]] = basePoints.Count;
                basePoints.Add(positions[i]);
            }
        }

        // Layer 0 holds every point. A triangle goes on the lowest layer where neither its three points nor
        // any of its edges in the same direction are taken yet, onto copies of those points. A back face runs
        // its edges against the front's, so it always lands a layer up whatever order the triangles come in,
        // and each side stays a clean surface.
        var layers = new Dictionary<(int Point, int Layer), GeometryData.Vertex>();
        var layerTriangles = new List<HashSet<(int, int, int)>>();
        var layerEdges = new List<HashSet<long>>();
        GeometryData.Vertex VertexAt(int point, int layer)
        {
            if (!layers.TryGetValue((point, layer), out var vertex))
                layers[(point, layer)] = vertex = geometry.AddVertex(basePoints[point]);
            return vertex;
        }

        int LayerFor(int a, int b, int c)
        {
            var key = Sorted(a, b, c);
            long ab = Directed(a, b), bc = Directed(b, c), ca = Directed(c, a);
            for (int layer = 0; ; layer++)
            {
                if (layer == layerTriangles.Count)
                {
                    layerTriangles.Add(new HashSet<(int, int, int)>());
                    layerEdges.Add(new HashSet<long>());
                }
                var edges = layerEdges[layer];
                if (layerTriangles[layer].Contains(key) || edges.Contains(ab) || edges.Contains(bc) || edges.Contains(ca)) continue;

                layerTriangles[layer].Add(key);
                edges.Add(ab);
                edges.Add(bc);
                edges.Add(ca);
                return layer;
            }
        }

        var corners = new GeometryData.Vertex[3];
        for (int s = 0; s < mesh.SubMeshCount; s++)
        {
            var sub = mesh.GetSubMesh(s);
            for (int i = sub.IndexStart; i + 2 < sub.IndexStart + sub.IndexCount; i += 3)
            {
                int a = weld[indices[i]], b = weld[indices[i + 1]], c = weld[indices[i + 2]];
                if (a == b || b == c || a == c)
                {
                    // Corners sharing a position, like a card the shader spreads out from its pivot, get
                    // vertices of their own so the triangle survives untouched
                    corners[0] = geometry.AddVertex(basePoints[a]);
                    corners[1] = geometry.AddVertex(basePoints[b]);
                    corners[2] = geometry.AddVertex(basePoints[c]);
                }
                else
                {
                    int layer = LayerFor(a, b, c);
                    corners[0] = VertexAt(a, layer);
                    corners[1] = VertexAt(b, layer);
                    corners[2] = VertexAt(c, layer);
                }
                var face = geometry.AddFace(corners);
                if (face == null) continue;
                face.Attributes[SubMesh] = new GeometryData.IntAttributeValue(s);

                for (int k = 0; k < 3; k++)
                {
                    int source = (int)indices[i + k];
                    var attributes = face.GetLoop(corners[k])!.Attributes;
                    if (normals.Length > 0) Set(attributes, Normal, normals[source]);
                    if (tangents.Length > 0) Set(attributes, Tangent, tangents[source]);
                    if (uv.Length > 0) Set(attributes, UV, uv[source]);
                    if (uv2.Length > 0) Set(attributes, UV2, uv2[source]);
                    if (colors.Length > 0) Set(attributes, VertexColor, colors[source]);
                    if (colors32.Length > 0) Set(attributes, VertexColor, colors32[source]);
                    if (hasSkin)
                        SetSkin(((GeometryData.IntAttributeValue)attributes[BoneIndices]).Data, Floats(attributes, BoneWeights), boneIndices[source], boneWeights[source]);
                    foreach (var (frame, names, hasNormals, hasTangents) in frames)
                    {
                        Set(attributes, names.Position, frame.DeltaVertices.Length == count ? frame.DeltaVertices[source] : Float3.Zero);
                        if (hasNormals) Set(attributes, names.Normal, frame.DeltaNormals![source]);
                        if (hasTangents) Set(attributes, names.Tangent, frame.DeltaTangents![source]);
                    }
                }
            }
        }

        // Points no triangle used still belong to the mesh, as loose vertices
        for (int p = 0; p < basePoints.Count; p++) VertexAt(p, 0);

        return geometry;
    }

    /// <summary>
    /// Builds a mesh from geometry. One mesh vertex is made per distinct corner of each geometry vertex.
    /// <paramref name="template"/> supplies what geometry does not carry: name, bind poses, bone names,
    /// blend shape names and frame weights, whether colors were bytes, and the submesh count, so a submesh
    /// that lost every triangle still keeps its slot and the material order holds. Without one, byte colors
    /// come back as float colors.
    /// </summary>
    public static Mesh ToMesh(GeometryData geometry, Mesh? template = null)
    {
        Mesh? source = template.IsValid() ? template : null;
        bool hasNormals = geometry.HasLoopAttribute(Normal), hasTangents = geometry.HasLoopAttribute(Tangent);
        bool hasUV = geometry.HasLoopAttribute(UV), hasUV2 = geometry.HasLoopAttribute(UV2);
        bool hasColor = geometry.HasLoopAttribute(VertexColor);
        bool hasSkin = geometry.HasLoopAttribute(BoneIndices) && geometry.HasLoopAttribute(BoneWeights);

        // Blend shape layout comes from the template when there is one, otherwise from the attributes found
        var frameCounts = new List<int>();
        if (source != null)
        {
            foreach (var shape in source.BlendShapes) frameCounts.Add(shape.Frames.Length);
        }
        else
        {
            for (int s = 0; geometry.HasLoopAttribute(BlendShapePosition(s, 0)); s++)
            {
                int frames = 0;
                while (geometry.HasLoopAttribute(BlendShapePosition(s, frames))) frames++;
                frameCounts.Add(frames);
            }
        }

        var frameNames = new List<FrameNames>();
        for (int s = 0; s < frameCounts.Count; s++)
            for (int f = 0; f < frameCounts[s]; f++)
                frameNames.Add(new FrameNames(s, f));

        var loopNames = new List<string>();
        foreach (var def in geometry.LoopAttributes) loopNames.Add(def.Name);

        var positions = new List<Float3>();
        var normals = new List<Float3>();
        var tangents = new List<Float4>();
        var uv = new List<Float2>();
        var uv2 = new List<Float2>();
        var colors = new List<Color>();
        var boneIndices = new List<Float4>();
        var boneWeights = new List<Float4>();
        var deltaPositions = new List<List<Float3>>();
        var deltaNormals = new List<List<Float3>?>();
        var deltaTangents = new List<List<Float3>?>();
        foreach (var names in frameNames)
        {
            deltaPositions.Add(new List<Float3>());
            deltaNormals.Add(geometry.HasLoopAttribute(names.Normal) ? new List<Float3>() : null);
            deltaTangents.Add(geometry.HasLoopAttribute(names.Tangent) ? new List<Float3>() : null);
        }

        var emitted = new Dictionary<GeometryData.Vertex, List<(GeometryData.Loop Loop, int Index)>>();
        int Emit(GeometryData.Loop loop)
        {
            var vertex = loop.Vert;
            if (!emitted.TryGetValue(vertex, out var list))
                emitted[vertex] = list = new List<(GeometryData.Loop, int)>(1);
            foreach (var (other, index) in list)
                if (SameValues(other, loop, loopNames)) return index;

            int created = positions.Count;
            list.Add((loop, created));

            var attributes = loop.Attributes;
            positions.Add(vertex.Point);
            if (hasNormals) normals.Add(Float3Of(attributes, Normal));
            if (hasTangents) tangents.Add(Float4Of(attributes, Tangent));
            if (hasUV) uv.Add(Float2Of(attributes, UV));
            if (hasUV2) uv2.Add(Float2Of(attributes, UV2));
            if (hasColor)
            {
                Float4 c = Float4Of(attributes, VertexColor);
                colors.Add(new Color(c.X, c.Y, c.Z, c.W));
            }
            if (hasSkin)
            {
                var ids = attributes.TryGetValue(BoneIndices, out var value) && value is GeometryData.IntAttributeValue n ? n.Data : new int[4];
                boneIndices.Add(new Float4(ids[0], ids[1], ids[2], ids[3]));
                boneWeights.Add(Float4Of(attributes, BoneWeights));
            }
            for (int layer = 0; layer < frameNames.Count; layer++)
            {
                deltaPositions[layer].Add(Float3Of(attributes, frameNames[layer].Position));
                deltaNormals[layer]?.Add(Float3Of(attributes, frameNames[layer].Normal));
                deltaTangents[layer]?.Add(Float3Of(attributes, frameNames[layer].Tangent));
            }
            return created;
        }

        int subMeshCount = source != null ? source.SubMeshCount : 1;
        foreach (var face in geometry.Faces)
            subMeshCount = Math.Max(subMeshCount, SubMeshOf(face) + 1);

        var subIndices = new List<uint>[subMeshCount];
        for (int s = 0; s < subMeshCount; s++) subIndices[s] = new List<uint>();

        foreach (var face in geometry.Faces)
        {
            if (face.VertCount < 3 || face.Loop == null) continue;

            // Fan out faces with more than three corners
            var target = subIndices[Math.Max(0, SubMeshOf(face))];
            var first = face.Loop;
            for (var loop = first.Next!; loop.Next != first; loop = loop.Next!)
            {
                target.Add((uint)Emit(first));
                target.Add((uint)Emit(loop));
                target.Add((uint)Emit(loop.Next!));
            }
        }

        var mesh = new Mesh { Name = source != null ? source.Name : "Mesh" };
        if (source != null && source.BindPoses is { } bindPoses) mesh.BindPoses = (Float4x4[])bindPoses.Clone();
        if (source != null && source.BoneNames is { } boneNames) mesh.BoneNames = (string[])boneNames.Clone();

        // Nothing to draw: vertex streams cannot be assigned without vertices, so only the submesh slots stay
        if (positions.Count == 0)
        {
            mesh.SetSubMeshCount(subMeshCount);
            return mesh;
        }

        mesh.Vertices = positions.ToArray();
        if (hasNormals) mesh.Normals = normals.ToArray();
        if (hasTangents) mesh.Tangents = tangents.ToArray();
        if (hasUV) mesh.UV = uv.ToArray();
        if (hasUV2) mesh.UV2 = uv2.ToArray();
        if (hasColor)
        {
            if (source != null && source.HasColors32 && !source.HasColors)
            {
                var bytes = new Color32[colors.Count];
                for (int i = 0; i < bytes.Length; i++)
                    bytes[i] = new Color32(ToByte(colors[i].R), ToByte(colors[i].G), ToByte(colors[i].B), ToByte(colors[i].A));
                mesh.Colors32 = bytes;
            }
            else
            {
                mesh.Colors = colors.ToArray();
            }
        }
        if (hasSkin)
        {
            mesh.BoneIndices = boneIndices.ToArray();
            mesh.BoneWeights = boneWeights.ToArray();
        }
        if (frameCounts.Count > 0)
        {
            var shapes = new BlendShape[frameCounts.Count];
            int layer = 0;
            for (int s = 0; s < shapes.Length; s++)
            {
                var shape = source != null && s < source.BlendShapes.Length ? source.BlendShapes[s] : null;
                shapes[s] = new BlendShape { Name = shape?.Name ?? $"Shape{s}", Frames = new BlendShapeFrame[frameCounts[s]] };
                for (int f = 0; f < frameCounts[s]; f++, layer++)
                {
                    shapes[s].Frames[f] = new BlendShapeFrame
                    {
                        Weight = shape != null && f < shape.Frames.Length ? shape.Frames[f].Weight : 100f,
                        DeltaVertices = deltaPositions[layer].ToArray(),
                        DeltaNormals = deltaNormals[layer]?.ToArray(),
                        DeltaTangents = deltaTangents[layer]?.ToArray(),
                    };
                }
            }
            mesh.BlendShapes = shapes;
        }

        var all = new List<uint>();
        foreach (var list in subIndices) all.AddRange(list);
        mesh.IndexFormat = positions.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.MeshTopology = Topology.Triangles;
        mesh.Indices = all.ToArray();

        int start = 0;
        mesh.SetSubMeshCount(subMeshCount);
        for (int s = 0; s < subMeshCount; s++)
        {
            mesh.SetSubMesh(s, new SubMeshDescriptor(start, subIndices[s].Count));
            start += subIndices[s].Count;
        }

        if (positions.Count > 0) mesh.RecalculateBounds();
        return mesh;
    }

    /// <summary>The stream when it covers every vertex, otherwise empty, with a warning when it was there but the wrong length.</summary>
    private static T[] Usable<T>(Mesh mesh, T[] stream, int count, string what)
    {
        if (stream.Length == count) return stream;
        if (stream.Length > 0)
            Debug.LogWarning($"Mesh '{mesh.Name}' has {stream.Length} {what} for {count} vertices, so they were left out of its geometry.");
        return [];
    }

    /// <summary>
    /// Writes skinning in one canonical form: indices rounded, unused slots pointing at bone 0, slots sorted
    /// by bone. It deforms the same, and the same bones always compare equal however they were ordered.
    /// </summary>
    private static void SetSkin(int[] ids, float[] weights, Float4 index, Float4 weight)
    {
        Span<(int Bone, float Weight)> slots =
        [
            (weight.X == 0 ? 0 : (int)MathF.Round(index.X), weight.X),
            (weight.Y == 0 ? 0 : (int)MathF.Round(index.Y), weight.Y),
            (weight.Z == 0 ? 0 : (int)MathF.Round(index.Z), weight.Z),
            (weight.W == 0 ? 0 : (int)MathF.Round(index.W), weight.W),
        ];
        slots.Sort((x, y) => x.Bone != y.Bone ? x.Bone.CompareTo(y.Bone) : x.Weight.CompareTo(y.Weight));
        for (int i = 0; i < 4; i++)
        {
            ids[i] = slots[i].Bone;
            weights[i] = slots[i].Weight;
        }
    }

    private static long Directed(int from, int to) => ((long)from << 32) | (uint)to;

    private static (int, int, int) Sorted(int a, int b, int c)
    {
        if (a > b) (a, b) = (b, a);
        if (b > c) (b, c) = (c, b);
        if (a > b) (a, b) = (b, a);
        return (a, b, c);
    }

    private static int SubMeshOf(GeometryData.Face face)
        => face.Attributes.TryGetValue(SubMesh, out var value) && value is GeometryData.IntAttributeValue n && n.Data.Length > 0 ? n.Data[0] : 0;

    private static byte ToByte(float value) => (byte)Math.Clamp(MathF.Round(value * 255f), 0f, 255f);

    private static bool SameValues(GeometryData.Loop a, GeometryData.Loop b, List<string> names)
    {
        foreach (string name in names)
        {
            a.Attributes.TryGetValue(name, out var va);
            b.Attributes.TryGetValue(name, out var vb);
            if (va is GeometryData.FloatAttributeValue fa && vb is GeometryData.FloatAttributeValue fb)
            {
                if (!fa.Data.AsSpan().SequenceEqual(fb.Data)) return false;
            }
            else if (va is GeometryData.IntAttributeValue ia && vb is GeometryData.IntAttributeValue ib)
            {
                if (!ia.Data.AsSpan().SequenceEqual(ib.Data)) return false;
            }
            else if (va != null || vb != null)
            {
                return false;
            }
        }
        return true;
    }

    private static float[]? Read(Dictionary<string, GeometryData.AttributeValue> attributes, string name)
        => attributes.TryGetValue(name, out var value) && value is GeometryData.FloatAttributeValue f ? f.Data : null;

    private static Float2 Float2Of(Dictionary<string, GeometryData.AttributeValue> attributes, string name)
        => Read(attributes, name) is { Length: >= 2 } d ? new Float2(d[0], d[1]) : Float2.Zero;

    private static Float3 Float3Of(Dictionary<string, GeometryData.AttributeValue> attributes, string name)
        => Read(attributes, name) is { Length: >= 3 } d ? new Float3(d[0], d[1], d[2]) : Float3.Zero;

    private static Float4 Float4Of(Dictionary<string, GeometryData.AttributeValue> attributes, string name)
        => Read(attributes, name) is { Length: >= 4 } d ? new Float4(d[0], d[1], d[2], d[3]) : Float4.Zero;

    private static float[] Floats(Dictionary<string, GeometryData.AttributeValue> attributes, string name)
        => ((GeometryData.FloatAttributeValue)attributes[name]).Data;

    private static void Set(Dictionary<string, GeometryData.AttributeValue> attributes, string name, Float2 v)
    {
        var d = Floats(attributes, name);
        d[0] = v.X; d[1] = v.Y;
    }

    private static void Set(Dictionary<string, GeometryData.AttributeValue> attributes, string name, Float3 v)
    {
        var d = Floats(attributes, name);
        d[0] = v.X; d[1] = v.Y; d[2] = v.Z;
    }

    private static void Set(Dictionary<string, GeometryData.AttributeValue> attributes, string name, Float4 v)
    {
        var d = Floats(attributes, name);
        d[0] = v.X; d[1] = v.Y; d[2] = v.Z; d[3] = v.W;
    }

    private static void Set(Dictionary<string, GeometryData.AttributeValue> attributes, string name, Color v)
    {
        var d = Floats(attributes, name);
        d[0] = v.R; d[1] = v.G; d[2] = v.B; d[3] = v.A;
    }

    private static void Set(Dictionary<string, GeometryData.AttributeValue> attributes, string name, Color32 v)
    {
        var d = Floats(attributes, name);
        d[0] = v.R / 255f; d[1] = v.G / 255f; d[2] = v.B / 255f; d[3] = v.A / 255f;
    }

    /// <summary>Exact position equality, with negative zero treated as zero and a hash that mixes all three axes.</summary>
    private sealed class PositionComparer : IEqualityComparer<Float3>
    {
        public static readonly PositionComparer Instance = new();

        public bool Equals(Float3 a, Float3 b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

        public int GetHashCode(Float3 p) => HashCode.Combine(Bits(p.X), Bits(p.Y), Bits(p.Z));

        private static int Bits(float value) => value == 0f ? 0 : BitConverter.SingleToInt32Bits(value);
    }
}
