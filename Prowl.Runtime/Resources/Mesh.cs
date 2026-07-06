// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using Prowl.Echo;
using Prowl.Graphite;
using Prowl.Vector;

namespace Prowl.Runtime.Resources;


/// <summary>Defines a portion of a Mesh's index buffer as a submesh.</summary>
public struct SubMeshDescriptor
{
    public int IndexStart;
    public int IndexCount;
    public PrimitiveTopology Topology;

    public SubMeshDescriptor(int indexStart, int indexCount, PrimitiveTopology topology = PrimitiveTopology.TriangleList)
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
public class Mesh : EngineObject, ISerializable, IVertexSource
{
    // Vertex attribute streams. Each stream is a separate per-attribute DeviceBuffer, bound by
    // attribute name through the IVertexSource interface (Graphite resolves layout slots by name).
    private const int STREAM_POSITION = 0;
    private const int STREAM_TEXCOORD0 = 1;
    private const int STREAM_TEXCOORD1 = 2;
    private const int STREAM_NORMAL = 3;
    private const int STREAM_COLOR = 4;
    private const int STREAM_TANGENT = 5;
    private const int STREAM_BLENDINDICES = 6;
    private const int STREAM_BLENDWEIGHT = 7;
    private const int StreamCount = 8;

    // Per-stream GPU element size in bytes. Colors are always uploaded as Float4 regardless of
    // whether the CPU array is Color (float) or Color32 (byte), matching the shader's COLOR0 input.
    private static readonly int[] s_streamGpuSizes = [12, 8, 8, 12, 16, 16, 16, 16];

    // Interned attribute names matching the HLSL semantics declared by Prowl's shaders.
    private static readonly VertexAttributeID[] s_streamNames =
    [
        "POSITION0", "TEXCOORD0", "TEXCOORD1", "NORMAL0", "COLOR0", "TANGENT0", "BLENDINDICES0", "BLENDWEIGHT0"
    ];

    private struct VertexStream
    {
        public Array? Data;
        public bool Dirty;
        public DeviceBuffer? Buffer;
        public int UploadedCount;
    }

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
            indicesDirty = true;
        }
    }

    /// <summary> The mesh's primitive type </summary>
    public PrimitiveTopology Topology
    {
        get => topology;
        set
        {
            if (isWritable == false) return;
            changed = true;
            topology = value;
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
        get => GetVertexBufferAt<Float3>(STREAM_POSITION);
        set
        {
            if (isWritable == false)
                return;
            bool needsReset = _streams[STREAM_POSITION].Data == null || VertexCount != value.Length;

            // Copy Vertices
            StoreStream(STREAM_POSITION, CopyArray(value));

            changed = true;
            if (needsReset)
            {
                for (int i = STREAM_POSITION + 1; i < StreamCount; i++)
                    StoreStream(i, null);
                indices = null;
                indicesDirty = true;
                // Blend-shape deltas are indexed by vertex; a vertex-count change invalidates them.
                _blendShapes = Array.Empty<BlendShape>();
                _morphDirty = true;
            }
        }
    }

    public Float3[] Normals
    {
        get => ReadVertexData(GetVertexBufferAt<Float3>(STREAM_NORMAL));
        set => WriteVertexData(STREAM_NORMAL, CopyArray(value), value.Length);
    }

    public Float4[] Tangents
    {
        get => ReadVertexData(GetVertexBufferAt<Float4>(STREAM_TANGENT));
        set => WriteVertexData(STREAM_TANGENT, CopyArray(value), value.Length);
    }

    public Color[] Colors
    {
        get => ReadVertexData(GetVertexBufferAt<Color>(STREAM_COLOR));
        set => WriteVertexData(STREAM_COLOR, CopyArray(value), value.Length);
    }

    public Color32[] Colors32
    {
        get => ReadVertexData(GetVertexBufferAt<Color32>(STREAM_COLOR));
        set => WriteVertexData(STREAM_COLOR, CopyArray(value), value.Length);
    }

    public Float2[] UV
    {
        get => ReadVertexData(GetVertexBufferAt<Float2>(STREAM_TEXCOORD0));
        set => WriteVertexData(STREAM_TEXCOORD0, CopyArray(value), value.Length);
    }

    public Float2[] UV2
    {
        get => ReadVertexData(GetVertexBufferAt<Float2>(STREAM_TEXCOORD1));
        set => WriteVertexData(STREAM_TEXCOORD1, CopyArray(value), value.Length);
    }

    public uint[] Indices
    {
        get => ReadVertexData(indices ?? []);
        set
        {
            if (isWritable == false)
                throw new InvalidOperationException("Mesh is not writable");
            indices = CopyArray(value);
            indicesDirty = true;
            changed = true;
        }
    }

    public Float4[] BoneIndices
    {
        get => ReadVertexData(GetVertexBufferAt<Float4>(STREAM_BLENDINDICES));
        set => WriteVertexData(STREAM_BLENDINDICES, CopyArray(value), value.Length);
    }

    public Float4[] BoneWeights
    {
        get => ReadVertexData(GetVertexBufferAt<Float4>(STREAM_BLENDWEIGHT));
        set => WriteVertexData(STREAM_BLENDWEIGHT, CopyArray(value), value.Length);
    }

    public int VertexCount => _streams[STREAM_POSITION].Data?.Length ?? 0;
    public int IndexCount => indices?.Length ?? 0;

    public DeviceBuffer? VertexBuffer => _streams[STREAM_POSITION].Buffer;
    public DeviceBuffer? IndexBuffer => indexBuffer;

    public bool HasNormals => GetVertexBufferAt<Float3>(STREAM_NORMAL).Length > 0;
    public bool HasTangents => GetVertexBufferAt<Float4>(STREAM_TANGENT).Length > 0;
    public bool HasColors => GetVertexBufferAt<Color>(STREAM_COLOR).Length > 0;
    public bool HasColors32 => GetVertexBufferAt<Color32>(STREAM_COLOR).Length > 0;
    public bool HasUV => GetVertexBufferAt<Float2>(STREAM_TEXCOORD0).Length > 0;
    public bool HasUV2 => GetVertexBufferAt<Float2>(STREAM_TEXCOORD1).Length > 0;

    public bool HasBoneIndices => GetVertexBufferAt<Float4>(STREAM_BLENDINDICES).Length > 0;
    public bool HasBoneWeights => GetVertexBufferAt<Float4>(STREAM_BLENDWEIGHT).Length > 0;

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

        if (_blendShapes.Length == 0 || VertexCount == 0)
            return;

        int vtx = VertexCount;

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
        var tex = new Texture2D((uint)width, (uint)height, false, PixelFormat.R32_G32_B32_A32_Float);
        tex.SetTextureFilters(SamplerFilter.MinPoint_MagPoint_MipPoint);
        tex.SetWrapModes(SamplerAddressMode.Clamp, SamplerAddressMode.Clamp, SamplerAddressMode.Clamp);
        tex.SetData(data.AsMemory());
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
            return new SubMeshDescriptor(0, indices?.Length ?? 0, topology);
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

    private readonly VertexStream[] _streams = new VertexStream[StreamCount];

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
    /// submeshes, ...). Mirrors <see cref="Transform.Version"/>. Useful for invalidating
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

    uint[]? indices;
    bool indicesDirty = true;

    IndexFormat indexFormat = IndexFormat.UInt16;
    PrimitiveTopology topology = PrimitiveTopology.TriangleList;

    DeviceBuffer? indexBuffer;

    // Zero-filled placeholder bound for shader vertex inputs the mesh doesn't provide.
    private DeviceBuffer? _zeroStream;
    private uint _zeroStreamCapacity;

    public Mesh() { }

    public void Clear()
    {
        for (int i = 0; i < StreamCount; i++)
            StoreStream(i, null);
        indices = null;
        indicesDirty = true;

        changed = true;

        // Don't delete GPU buffers - they'll be reused on next Upload()
        // This is important for frequent regeneration (e.g., voxel engines, procedural meshes)
        // Buffers are only deleted when the mesh is disposed
    }

    public void Upload()
    {
        if (changed == false && _streams[STREAM_POSITION].Buffer != null)
            return;

        changed = false;

        if (VertexCount == 0)
            throw new InvalidOperationException($"Mesh has no vertices");

        if (indices == null || indices.Length == 0)
            throw new InvalidOperationException($"Mesh has no indices");

        switch (topology)
        {
            case PrimitiveTopology.TriangleList:
                if (indices.Length % 3 != 0)
                    throw new InvalidOperationException($"Triangle mesh doesn't have the right amount of indices. Has: {indices.Length}. Should be a multiple of 3");
                break;
            case PrimitiveTopology.TriangleStrip:
                if (indices.Length < 3)
                    throw new InvalidOperationException($"Triangle Strip mesh doesn't have the right amount of indices. Has: {indices.Length}. Should have at least 3");
                break;

            case PrimitiveTopology.LineList:
                if (indices.Length % 2 != 0)
                    throw new InvalidOperationException($"Line mesh doesn't have the right amount of indices. Has: {indices.Length}. Should be a multiple of 2");
                break;

            case PrimitiveTopology.LineStrip:
                if (indices.Length < 2)
                    throw new InvalidOperationException($"Line Strip mesh doesn't have the right amount of indices. Has: {indices.Length}. Should have at least 2");
                break;
        }

        for (int i = 0; i < StreamCount; i++)
            UploadStream(i);

        UploadIndices();
    }

    private void UploadStream(int stream)
    {
        ref VertexStream s = ref _streams[stream];
        Array? data = s.Data;
        if (data == null || data.Length == 0)
        {
            // Stream was cleared; stop binding its (now stale) buffer until new data is uploaded.
            s.UploadedCount = 0;
            return;
        }
        if (!s.Dirty && s.Buffer != null)
            return;

        int gpuSize = s_streamGpuSizes[stream];
        int count = data.Length;

        // Colors stored as Color32 are widened to Float4 to match the shader's COLOR0 input.
        Array uploadData = data;
        if (stream == STREAM_COLOR && data is Color32[] c32)
        {
            var widened = new Color[c32.Length];
            for (int i = 0; i < c32.Length; i++)
                widened[i] = (Color)c32[i];
            uploadData = widened;
        }

        EnsureBuffer(ref s.Buffer, count, gpuSize, BufferUsage.VertexBuffer);
        PinUpload(s.Buffer!, uploadData, gpuSize);
        s.Dirty = false;
        s.UploadedCount = count;
    }

    private void UploadIndices()
    {
        if (indices == null || indices.Length == 0)
            return;
        if (!indicesDirty && indexBuffer != null)
            return;

        if (indexFormat == IndexFormat.UInt16)
        {
            ushort[] data = new ushort[indices.Length];
            for (int i = 0; i < indices.Length; i++)
            {
                if (indices[i] > ushort.MaxValue)
                    throw new InvalidOperationException($"[Mesh] Invalid value {indices[i]} for 16-bit indices");
                data[i] = (ushort)indices[i];
            }

            EnsureBuffer(ref indexBuffer, data.Length, sizeof(ushort), BufferUsage.IndexBuffer);
            Graphics.Device.UpdateBuffer(indexBuffer, 0u, data);
        }
        else
        {
            EnsureBuffer(ref indexBuffer, indices.Length, sizeof(uint), BufferUsage.IndexBuffer);
            Graphics.Device.UpdateBuffer(indexBuffer, 0u, indices);
        }

        indicesDirty = false;
    }

    // Reuses an existing buffer when the requested size fits within its capacity and isn't wastefully
    // small; otherwise (re)allocates with 50% headroom to amortise future growth.
    private void EnsureBuffer(ref DeviceBuffer? buffer, int elementCount, int elementStride, BufferUsage usage)
    {
        uint requested = (uint)(elementCount * elementStride);
        uint capacity = buffer?.SizeInBytes ?? 0;

        if (buffer != null && requested <= capacity && requested >= capacity * 0.33f)
            return;

        buffer?.Dispose();
        buffer = Graphics.Device.ResourceFactory.CreateBuffer(new BufferDescription
        {
            Usage = usage,
            SizeInBytes = (uint)(requested * 1.5f),
        });
        buffer.Name = $"{Name} {usage}";
    }

    private static void PinUpload(DeviceBuffer buffer, Array data, int elementSize)
    {
        GCHandle handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try
        {
            Graphics.Device.UpdateBuffer(buffer, 0u, handle.AddrOfPinnedObject(), (uint)(elementSize * data.Length));
        }
        finally
        {
            handle.Free();
        }
    }

    private DeviceBuffer? _instanceBuffer;
    private int _instanceBufferCapacity;

    /// <summary>
    /// Ensures a per-instance <see cref="DeviceBuffer"/> exists with enough capacity for
    /// <paramref name="instanceCount"/> <see cref="Rendering.InstanceData"/> entries (grows with
    /// 50 % headroom to amortise repeated resizes). The buffer is written immediately before each
    /// instanced draw via <c>Device.UpdateBuffer</c> from <see cref="Rendering.RenderPipeline"/>.
    /// </summary>
    public DeviceBuffer EnsureInstanceBuffer(int instanceCount)
    {
        if (_instanceBuffer != null && instanceCount <= _instanceBufferCapacity)
            return _instanceBuffer;

        _instanceBufferCapacity = (int)(instanceCount * 1.5f);
        Graphics.DisposeDeferred(_instanceBuffer!);

        uint sizeBytes = (uint)(_instanceBufferCapacity * Rendering.InstanceData.SizeInBytes);
        _instanceBuffer = Graphics.Device.ResourceFactory.CreateBuffer(new BufferDescription
        {
            Usage = BufferUsage.VertexBuffer | BufferUsage.Dynamic,
            SizeInBytes = sizeBytes,
        });
        _instanceBuffer.Name = $"{Name} Instance Buffer";
        return _instanceBuffer;
    }

    // Graphite has no VAO/vertex-format abstraction; the old pre-Graphite instancing machinery
    // is preserved below in case it is useful as reference.
    /*
    /// <summary>
    /// Ensures the instanced rendering VAO and buffer exist for this mesh with
    /// enough capacity for instanceCount instances.
    /// </summary>
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
    */

    public void RecalculateBounds()
    {
        Float3[] vertices = GetVertexBufferAt<Float3>(STREAM_POSITION);
        if (vertices.Length == 0)
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
        Float3[] vertices = GetVertexBufferAt<Float3>(STREAM_POSITION);
        if (vertices.Length < 3) return;
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
        Float3[] vertices = GetVertexBufferAt<Float3>(STREAM_POSITION);
        Float2[] uv = GetVertexBufferAt<Float2>(STREAM_TEXCOORD0);
        Float3[] normals = GetVertexBufferAt<Float3>(STREAM_NORMAL);
        if (vertices.Length < 3) return;
        if (indices == null || indices.Length < 3) return;
        if (uv.Length == 0) return;

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

            tan1[ai] += tangent; tan1[bi] += tangent; tan1[ci] += tangent;
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

        Float3[] vertices = GetVertexBufferAt<Float3>(STREAM_POSITION);
        Float3[] normals = GetVertexBufferAt<Float3>(STREAM_NORMAL);

        // Make sure we have vertices and indices
        if (vertices.Length == 0 || indices == null || indices.Length == 0)
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
        mesh.Vertices =
        [
            new Float3(-1, -1, 0),
            new Float3(1, -1, 0),
            new Float3(-1, 1, 0),
            new Float3(1, 1, 0)
        ];

        mesh.UV =
        [
            new Float2(0, 0),
            new Float2(1, 0),
            new Float2(0, 1),
            new Float2(1, 1)
        ];

        mesh.Indices = [0, 2, 1, 2, 3, 1];

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

        mesh.Vertices = [.. vertices];
        mesh.UV = [.. uvs];
        mesh.Indices = [.. indices];

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

        mesh.Vertices = vertices;
        mesh.UV = uvs;
        mesh.Indices = indices;

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

        mesh.Vertices = [.. vertices];
        mesh.UV = [.. uvs];
        mesh.Indices = [.. indices];

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

        mesh.Vertices = [.. vertices];
        mesh.UV = [.. uvs];
        mesh.Indices = [.. indices];

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

        mesh.Vertices = [.. vertices];
        mesh.UV = [.. uvs];
        mesh.Indices = [.. indices];

        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();

        return mesh;
    }

    public static Mesh CreateTriangle(Float3 a, Float3 b, Float3 c)
    {
        Mesh mesh = new();
        mesh.Vertices = [a, b, c];
        mesh.Indices = [0, 1, 2];
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        mesh.RecalculateTangents();
        return mesh;
    }

    private void DeleteGPUBuffers()
    {
        for (int i = 0; i < StreamCount; i++)
        {
            _streams[i].Buffer?.Dispose();
            _streams[i].Buffer = null;
            _streams[i].UploadedCount = 0;
            _streams[i].Dirty = true;
        }

        indexBuffer?.Dispose();
        indexBuffer = null;
        indicesDirty = true;

        _zeroStream?.Dispose();
        _zeroStream = null;
        _zeroStreamCapacity = 0;

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

    private void WriteVertexData<T>(int stream, T[] value, int length, bool mustMatchLength = true) where T : unmanaged
    {
        if (isWritable == false)
            throw new InvalidOperationException("Mesh is not writable");
        if ((value == null || length == 0 || length != VertexCount) && mustMatchLength)
            throw new ArgumentException("Array length should match vertices length");
        changed = true;
        StoreStream(stream, value);
    }

    /// <summary>Returns the CPU-side array for a stream, or an empty array if absent or of a different element type.</summary>
    internal T[] GetVertexBufferAt<T>(int stream) where T : unmanaged
        => _streams[stream].Data as T[] ?? [];

    private void StoreStream(int stream, Array? data)
    {
        _streams[stream].Data = data;
        _streams[stream].Dirty = true;
    }

    void IVertexSource.ResolveSlot(uint layoutSlot, in VertexLayoutDescription layout, out VertexBinding binding)
    {
        Upload();

        VertexAttributeID name = layout.Elements[0].Name;
        int stream = -1;
        for (int i = 0; i < StreamCount; i++)
        {
            if (s_streamNames[i] == name)
            {
                stream = i;
                break;
            }
        }

        if (stream >= 0 && _streams[stream].Buffer != null && _streams[stream].UploadedCount > 0)
            binding = new VertexBinding(_streams[stream].Buffer!);
        else
            binding = new VertexBinding(GetOrCreateZeroStream(layout.Stride));
    }

    bool IVertexSource.TryGetIndexBuffer(out DeviceBuffer buffer, out IndexFormat format, out uint indexCount)
    {
        Upload();

        buffer = indexBuffer!;
        format = indexFormat == IndexFormat.UInt32 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        indexCount = (uint)IndexCount;
        return indexBuffer != null;
    }

    private DeviceBuffer GetOrCreateZeroStream(uint stride)
    {
        uint vertices = (uint)Math.Max(1, VertexCount);
        uint required = stride * vertices;

        if (_zeroStream != null && required <= _zeroStreamCapacity)
            return _zeroStream;

        _zeroStream?.Dispose();
        uint capacity = (uint)(required * 1.5f);
        _zeroStream = Graphics.Device.ResourceFactory.CreateBuffer(new BufferDescription
        {
            Usage = BufferUsage.VertexBuffer,
            SizeInBytes = capacity,
        });
        _zeroStream.Name = $"{Name} Zero Stream";
        _zeroStreamCapacity = capacity;

        byte[] zeros = new byte[capacity];
        Graphics.Device.UpdateBuffer(_zeroStream, 0u, zeros);

        return _zeroStream;
    }

    public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
    {
        using (MemoryStream memoryStream = new())
        using (BinaryWriter writer = new(memoryStream))
        {
            writer.Write((byte)indexFormat);
            writer.Write((byte)topology);

            Float3[] vertices = GetVertexBufferAt<Float3>(STREAM_POSITION);
            Float3[] normals = GetVertexBufferAt<Float3>(STREAM_NORMAL);
            Float4[] tangents = GetVertexBufferAt<Float4>(STREAM_TANGENT);
            Color[] colors = GetVertexBufferAt<Color>(STREAM_COLOR);
            Color32[] colors32 = GetVertexBufferAt<Color32>(STREAM_COLOR);
            Float2[] uv = GetVertexBufferAt<Float2>(STREAM_TEXCOORD0);
            Float2[] uv2 = GetVertexBufferAt<Float2>(STREAM_TEXCOORD1);
            Float4[] boneIndices = GetVertexBufferAt<Float4>(STREAM_BLENDINDICES);
            Float4[] boneWeights = GetVertexBufferAt<Float4>(STREAM_BLENDWEIGHT);

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

            writer.Write(uv.Length);
            foreach (Float2 coord in uv)
            {
                writer.Write(coord.X);
                writer.Write(coord.Y);
            }

            writer.Write(uv2.Length);
            foreach (Float2 coord in uv2)
            {
                writer.Write(coord.X);
                writer.Write(coord.Y);
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
            compoundTag.Add("MeshType", new EchoObject((int)topology));
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
        topology = (PrimitiveTopology)value["MeshType"].IntValue;
        indexFormat = (IndexFormat)value["MeshIndexFormat"].IntValue;
        bounds = new AABB(
            new Float3(value["BoundsMinX"].FloatValue, value["BoundsMinY"].FloatValue, value["BoundsMinZ"].FloatValue),
            new Float3(value["BoundsMaxX"].FloatValue, value["BoundsMaxY"].FloatValue, value["BoundsMaxZ"].FloatValue)
        );

        using (MemoryStream memoryStream = new(value["MeshData"].ByteArrayValue))
        using (BinaryReader reader = new(memoryStream))
        {
            indexFormat = (IndexFormat)reader.ReadByte();
            topology = (PrimitiveTopology)reader.ReadByte();

            int vertexCount = reader.ReadInt32();
            Float3[] vertices = new Float3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                vertices[i] = new Float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            StoreStream(STREAM_POSITION, vertices);

            int normalCount = reader.ReadInt32();
            if (normalCount > 0)
            {
                Float3[] normals = new Float3[normalCount];
                for (int i = 0; i < normalCount; i++)
                    normals[i] = new Float3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                StoreStream(STREAM_NORMAL, normals);
            }

            int tangentCount = reader.ReadInt32();
            if (tangentCount > 0)
            {
                Float4[] tangents = new Float4[tangentCount];
                for (int i = 0; i < tangentCount; i++)
                    tangents[i] = new Float4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                StoreStream(STREAM_TANGENT, tangents);
            }

            int colorCount = reader.ReadInt32();
            if (colorCount > 0)
            {
                Color[] colors = new Color[colorCount];
                for (int i = 0; i < colorCount; i++)
                    colors[i] = new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                StoreStream(STREAM_COLOR, colors);
            }

            int color32Count = reader.ReadInt32();
            if (color32Count > 0)
            {
                Color32[] colors32 = new Color32[color32Count];
                for (int i = 0; i < color32Count; i++)
                    colors32[i] = new Color32(reader.ReadByte(), reader.ReadByte(), reader.ReadByte(), reader.ReadByte());
                // Colors and Colors32 share the COLOR0 stream; a mesh stores at most one.
                if (colorCount == 0)
                    StoreStream(STREAM_COLOR, colors32);
            }

            int uvCount = reader.ReadInt32();
            if (uvCount > 0)
            {
                Float2[] uv = new Float2[uvCount];
                for (int i = 0; i < uvCount; i++)
                    uv[i] = new Float2(reader.ReadSingle(), reader.ReadSingle());
                StoreStream(STREAM_TEXCOORD0, uv);
            }

            int uv2Count = reader.ReadInt32();
            if (uv2Count > 0)
            {
                Float2[] uv2 = new Float2[uv2Count];
                for (int i = 0; i < uv2Count; i++)
                    uv2[i] = new Float2(reader.ReadSingle(), reader.ReadSingle());
                StoreStream(STREAM_TEXCOORD1, uv2);
            }

            int indexCount = reader.ReadInt32();
            if (indexCount > 0)
            {
                indices = new uint[indexCount];
                for (int i = 0; i < indexCount; i++)
                    indices[i] = reader.ReadUInt32();
                indicesDirty = true;
            }

            int boneIndexCount = reader.ReadInt32();
            if (boneIndexCount > 0)
            {
                Float4[] boneIndices = new Float4[boneIndexCount];
                for (int i = 0; i < boneIndexCount; i++)
                    boneIndices[i] = new Float4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                StoreStream(STREAM_BLENDINDICES, boneIndices);
            }

            int boneWeightCount = reader.ReadInt32();
            if (boneWeightCount > 0)
            {
                Float4[] boneWeights = new Float4[boneWeightCount];
                for (int i = 0; i < boneWeightCount; i++)
                    boneWeights[i] = new Float4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                StoreStream(STREAM_BLENDWEIGHT, boneWeights);
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
                        var topo = (PrimitiveTopology)reader.ReadInt32();
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
