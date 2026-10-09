// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// A scene's static mesh renderers merged into world space geometry, grouped by material, lightmap page and layer.
/// Every group's triangles are split into spatial clusters that cull on their own, and the visible clusters of a
/// group go out as a single draw. Materials whose shader, keywords and values are all the same count as one.
/// <para/>
/// Built by <see cref="Scene.UpdateStaticGeometry"/> and stored with the scene, so a loaded scene draws from it
/// without building again. At runtime the merged geometry stays as built until the next build. While editing, a
/// renderer that moves, changes or stops being static drops out and draws on its own until the next build.
/// </summary>
public sealed class StaticGeometry : ISerializable
{
    /// <summary>Triangles a cluster may hold before it is split, which sets how finely merged geometry culls.</summary>
    public static int ClusterTriangles { get; set; } = 1024;

    private const int FormatVersion = 1;

    [Flags]
    private enum Attributes : byte
    {
        UV = 1,
        UV2 = 2,
        Normals = 4,
        Colors = 8,
        Tangents = 16,
    }

    /// <summary>A renderer with submeshes in the merged geometry.</summary>
    internal sealed class Source
    {
        public Guid Id;
        public Float4x4 World;
        public uint WorldVersion;
        public int Layer;
        public int LightmapIndex;
        public Float4 ScaleOffset;
        public int MeshVertexCount;
        public int MeshIndexCount;

        /// <summary>The group drawing each submesh, or null for one left to the renderer.</summary>
        public Group?[] Groups = [];

        public bool AllCovered;
        public bool Live;
        public MeshRenderer? Renderer;
        public Mesh? Mesh;
        public uint MeshVersion;
        public int VisualVersion;
        public Material?[] Materials = [];
        public readonly List<Cluster> Clusters = new();

        public bool Covers(int subMesh) => subMesh < Groups.Length && Groups[subMesh] != null;
    }

    private sealed class Buffer
    {
        public Attributes Attributes;
        public Mesh Mesh = null!;
    }

    internal sealed class Group
    {
        public int Buffer;
        public int Layer;
        public int LightmapIndex;
        public Material? Material;
        public Mesh Mesh = null!;
        public readonly List<Cluster> Clusters = new();
        public readonly PropertyState Properties = new();
        public bool? LightmapLoaded;
    }

    internal readonly record struct Member(Source Source, int SubMesh, uint Start, uint Count);

    /// <summary>A spatially compact run of one group's triangles, culled and drawn as part of the group.</summary>
    internal sealed class Cluster : IIndexRangeRenderable
    {
        public Group Group = null!;
        public uint Start;
        public uint Count;
        public AABB Bounds;
        public Member[] Members = [];
        public int Dropped;
        public int Version;
        public PropertyState Properties = null!;
        public PropertyState? ProbeProperties;

        public bool IsStatic => true;
        public int VisualVersion => Version;

        public Material GetMaterial() => Group.Material!;
        public int GetLayer() => Group.Layer;
        public Float3 GetPosition() => Bounds.Center;
        public Float4x4 GetWorldToObjectMatrix(in Float4x4 model) => Float4x4.Identity;

        public void GetRenderingData(ViewerData viewer, out PropertyState properties, out Mesh mesh, out Float4x4 model, out InstanceData[]? instanceData)
        {
            properties = Properties;
            mesh = Group.Mesh;
            model = Float4x4.Identity;
            instanceData = null;
        }

        public void GetCullingData(out bool isRenderable, out AABB bounds)
        {
            isRenderable = true;
            bounds = Bounds;
        }

        public void AppendRanges(List<IndexRange> ranges)
        {
            if (Dropped == 0)
            {
                IndexRange.Append(ranges, Start, Count);
                return;
            }
            foreach (Member member in Members)
                if (member.Source.Live)
                    IndexRange.Append(ranges, member.Start, member.Count);
        }

        public bool AnyLive()
        {
            if (Dropped == 0) return true;
            foreach (Member member in Members)
                if (member.Source.Live) return true;
            return false;
        }
    }

    private readonly List<Source> _sources = new();
    private readonly List<Buffer> _buffers = new();
    private readonly List<Group> _groups = new();
    private bool _bound;
    private LightProbeVolume? _probes;
    private bool _probesFilled;

    /// <summary>How many material, lightmap page and layer combinations the merged geometry draws as.</summary>
    public int GroupCount => _groups.Count;

    /// <summary>How many clusters the merged geometry culls as.</summary>
    public int ClusterCount
    {
        get
        {
            int count = 0;
            foreach (Group group in _groups) count += group.Clusters.Count;
            return count;
        }
    }

    /// <summary>Renderers still drawn by the merged geometry, the ones that dropped out not counted.</summary>
    public int LiveSourceCount
    {
        get
        {
            int count = 0;
            foreach (Source source in _sources)
                if (source.Live) count++;
            return count;
        }
    }

    // ---------------------------------------------------------------- per frame

    /// <summary>Adds the clusters still drawing anything, first dropping renderers changed since the build while editing.</summary>
    internal void Collect(Scene scene, List<IRenderable> renderables)
    {
        if (_groups.Count == 0) return;
        if (!_bound) Bind(scene);
        if (Application.IsEditor && !Application.IsPlaying) Validate(scene);
        RefreshLighting(scene);

        foreach (Group group in _groups)
        {
            if (group.Material is not { IsLoaded: true }) continue;
            foreach (Cluster cluster in group.Clusters)
                if (cluster.AnyLive())
                    renderables.Add(cluster);
        }
    }

    /// <summary>
    /// Whether building again would draw differently: a renderer dropped out, or a static renderer the merged
    /// geometry could hold is not in it.
    /// </summary>
    public bool NeedsRebuild(Scene scene)
    {
        if (!_bound) Bind(scene);
        Validate(scene);
        foreach (Source source in _sources)
            if (!source.Live) return true;

        foreach (GameObject go in scene.AllObjects)
        {
            foreach (MonoBehaviour component in go._components)
            {
                if (component is not MeshRenderer renderer || !IsEligible(renderer, out Mesh? mesh)) continue;
                StaticGeometry.Source? source = renderer.StaticSource;
                for (int s = 0; s < mesh!.SubMeshCount; s++)
                    if (EligibleMaterial(renderer, mesh, s) != null && (source == null || !source.Covers(s)))
                        return true;
            }
        }
        return false;
    }

    private void Validate(Scene scene)
    {
        Dictionary<Guid, Scene.LightmapPlacement> placements = scene.BakedLighting.Placements;
        int pages = scene.BakedLighting.Lightmaps.Count;

        foreach (Source source in _sources)
        {
            if (!source.Live) continue;
            MeshRenderer? renderer = source.Renderer;
            if (renderer.IsNotValid() || !StillMatches(renderer!, source, scene, placements, pages))
                Drop(source);
        }
    }

    private static bool StillMatches(MeshRenderer renderer, Source source, Scene scene, Dictionary<Guid, Scene.LightmapPlacement> placements, int pages)
    {
        GameObject go = renderer.GameObject;
        if (!ReferenceEquals(go.Scene, scene) || !renderer.EnabledInHierarchy || !go.IsStatic || go.LayerIndex != source.Layer) return false;
        if (!ReferenceEquals(renderer.Mesh, source.Mesh) || source.Mesh!.Version != source.MeshVersion) return false;
        if (renderer.VisualVersion != source.VisualVersion) return false;

        // The world matrix is only compared when the transform's cached one was rebuilt since the last check
        Transform transform = renderer.Transform;
        Float4x4 world = transform.LocalToWorldMatrix;
        if (transform.WorldVersion != source.WorldVersion)
        {
            if (!SameMatrix(world, source.World, 0f)) return false;
            source.WorldVersion = transform.WorldVersion;
        }

        LightmapOf(placements, pages, go.Identifier, out int index, out Float4 scaleOffset);
        if (index != source.LightmapIndex || (index >= 0 && !scaleOffset.Equals(source.ScaleOffset))) return false;

        List<Material> materials = renderer.Materials;
        if (materials.Count == 0) return false;
        for (int s = 0; s < source.Groups.Length; s++)
        {
            Group? group = source.Groups[s];
            if (group == null) continue;
            Material material = s < materials.Count ? materials[s] : materials[^1];
            if (!ReferenceEquals(material, source.Materials[s])) return false;
            if (material is { IsLoaded: true } && group.Material is { IsLoaded: true } && material.GetStateHash() != group.Material.GetStateHash())
                return false;
        }
        return true;
    }

    private static void Drop(Source source)
    {
        source.Live = false;
        if (source.Renderer.IsValid() && ReferenceEquals(source.Renderer!.StaticSource, source))
            source.Renderer.StaticSource = null;
        foreach (Cluster cluster in source.Clusters)
        {
            cluster.Dropped++;
            cluster.Version++;
        }
    }

    // Lightmapped groups follow their page loading, the rest follow the scene's probes, sampled once per cluster
    private void RefreshLighting(Scene scene)
    {
        LightProbeVolume? probes = scene.ProbeVolume;
        bool probesChanged = !_probesFilled || !ReferenceEquals(probes, _probes);
        _probes = probes;
        _probesFilled = true;

        foreach (Group group in _groups)
        {
            if (group.LightmapIndex >= 0)
            {
                Texture2D? page = group.LightmapIndex < scene.BakedLighting.Lightmaps.Count ? scene.BakedLighting.Lightmaps[group.LightmapIndex] : null;
                bool loaded = page is { IsLoaded: true };
                if (group.LightmapLoaded == loaded) continue;
                group.LightmapLoaded = loaded;
                LightmapBinding.Fill(group.Properties, scene, group.LightmapIndex, new Float4(1, 1, 0, 0), Float3.Zero, true);
                foreach (Cluster cluster in group.Clusters)
                    cluster.Properties = group.Properties;
                continue;
            }

            if (!probesChanged) continue;
            LightmapBinding.Fill(group.Properties, scene, -1, new Float4(1, 1, 0, 0), Float3.Zero, false);
            foreach (Cluster cluster in group.Clusters)
            {
                if (probes == null)
                {
                    cluster.Properties = group.Properties;
                    continue;
                }
                cluster.ProbeProperties ??= new PropertyState();
                LightmapBinding.Fill(cluster.ProbeProperties, scene, -1, new Float4(1, 1, 0, 0), cluster.Bounds.Center, false);
                cluster.Properties = cluster.ProbeProperties;
            }
        }
    }

    private static void LightmapOf(Dictionary<Guid, Scene.LightmapPlacement> placements, int pages, Guid id, out int index, out Float4 scaleOffset)
    {
        index = -1;
        scaleOffset = new Float4(1, 1, 0, 0);
        if (!placements.TryGetValue(id, out Scene.LightmapPlacement placement) || placement.Index < 0 || placement.Index >= pages) return;
        index = placement.Index;
        scaleOffset = placement.ScaleOffset;
    }

    // ---------------------------------------------------------------- binding

    // Finds the renderers a stored build was made from, and drops the ones that are gone or no longer match it
    private void Bind(Scene scene)
    {
        _bound = true;
        var renderers = new Dictionary<Guid, MeshRenderer>();
        foreach (GameObject go in scene.AllObjects)
            foreach (MonoBehaviour component in go._components)
                if (component is MeshRenderer renderer && renderer.GetType() == typeof(MeshRenderer))
                    renderers[renderer.Identifier] = renderer;

        Dictionary<Guid, Scene.LightmapPlacement> placements = scene.BakedLighting.Placements;
        int pages = scene.BakedLighting.Lightmaps.Count;

        foreach (Source source in _sources)
        {
            source.Live = renderers.TryGetValue(source.Id, out MeshRenderer? renderer) && renderer.StaticSource == null
                && renderer.Mesh is { IsLoaded: true } mesh && mesh.VertexCount == source.MeshVertexCount && mesh.IndexCount == source.MeshIndexCount
                && renderer.Materials.Count > 0;
            if (!source.Live) continue;

            source.Renderer = renderer;
            source.Mesh = renderer!.Mesh;
            source.MeshVersion = source.Mesh!.Version;
            source.VisualVersion = renderer.VisualVersion;
            source.Materials = new Material?[source.Groups.Length];
            for (int s = 0; s < source.Groups.Length; s++)
                if (source.Groups[s] != null)
                    source.Materials[s] = s < renderer.Materials.Count ? renderer.Materials[s] : renderer.Materials[^1];
            renderer.StaticSource = source;
        }

        // A group draws with the material of its first renderer still there
        foreach (Group group in _groups)
        {
            foreach (Cluster cluster in group.Clusters)
            {
                foreach (Member member in cluster.Members)
                {
                    if (!member.Source.Live) continue;
                    group.Material = member.Source.Materials[member.SubMesh];
                    break;
                }
                if (group.Material != null) break;
            }
        }

        foreach (Source source in _sources)
        {
            if (!source.Live) continue;

            // The stored matrix is checked against the loaded transform once, then the loaded one is kept so later checks are exact
            Float4x4 world = source.Renderer!.Transform.LocalToWorldMatrix;
            if (!SameMatrix(world, source.World, 1e-4f))
            {
                Drop(source);
                continue;
            }
            source.World = world;
            if (!StillMatches(source.Renderer, source, scene, placements, pages))
                Drop(source);
        }
    }

    private static bool SameMatrix(in Float4x4 a, in Float4x4 b, float tolerance)
    {
        ReadOnlySpan<float> x = MemoryMarshal.Cast<Float4x4, float>(new ReadOnlySpan<Float4x4>(in a));
        ReadOnlySpan<float> y = MemoryMarshal.Cast<Float4x4, float>(new ReadOnlySpan<Float4x4>(in b));
        for (int i = 0; i < x.Length; i++)
            if (MathF.Abs(x[i] - y[i]) > tolerance * MathF.Max(1f, MathF.Abs(x[i]))) return false;
        return true;
    }

    /// <summary>Lets every renderer draw itself again and frees the merged buffers.</summary>
    internal void Clear()
    {
        foreach (Source source in _sources)
            if (source.Renderer.IsValid() && ReferenceEquals(source.Renderer!.StaticSource, source))
                source.Renderer.StaticSource = null;
        foreach (Buffer buffer in _buffers)
            if (buffer.Mesh.IsValid()) buffer.Mesh.Dispose();

        _sources.Clear();
        _buffers.Clear();
        _groups.Clear();
        _bound = false;
        _probesFilled = false;
    }

    // ---------------------------------------------------------------- eligibility

    private static readonly Dictionary<Shader, bool> s_transparent = new();

    private static bool IsEligible(MeshRenderer renderer, out Mesh? mesh)
    {
        mesh = renderer.Mesh;
        return renderer.GetType() == typeof(MeshRenderer) && renderer.EnabledInHierarchy && renderer.GameObject.IsStatic
            && mesh is { IsLoaded: true } && mesh.VertexCount > 0 && mesh.IndexCount > 0
            && !mesh.HasBoneIndices && !mesh.HasBoneWeights && !mesh.HasBlendShapes && renderer.Materials.Count > 0;
    }

    // Transparent materials sort back to front per object, which merged geometry cannot
    private static Material? EligibleMaterial(MeshRenderer renderer, Mesh mesh, int subMesh)
    {
        SubMeshDescriptor sub = mesh.GetSubMesh(subMesh);
        if (sub.Topology != Topology.Triangles || sub.IndexCount <= 0) return null;

        List<Material> materials = renderer.Materials;
        Material material = subMesh < materials.Count ? materials[subMesh] : materials[^1];
        if (material is not { IsLoaded: true } || material.Shader is not { IsLoaded: true } shader) return null;

        if (!s_transparent.TryGetValue(shader, out bool transparent))
        {
            transparent = false;
            foreach (Shaders.ShaderPass pass in shader.LoadedPasses)
                transparent |= pass.HasTag("RenderOrder", "Transparent");
            s_transparent[shader] = transparent;
        }
        return transparent ? null : material;
    }

    // ---------------------------------------------------------------- building

    private struct Pending
    {
        public Source Source;
        public int SubMesh;
        public AABB Bounds;
        public uint Triangles;
    }

    private sealed class CenterOrder(int axis) : IComparer<Pending>
    {
        public int Compare(Pending a, Pending b) => Component(a.Bounds.Center, axis).CompareTo(Component(b.Bounds.Center, axis));
    }

    private sealed class SourceData
    {
        public Mesh Mesh = null!;
        public Attributes Attributes;
        public Float3[] Positions = [];
        public bool Mirrored;
    }

    /// <summary>Merges the scene's static renderers, replacing whatever was built before.</summary>
    internal void Build(Scene scene)
    {
        Clear();

        Dictionary<Guid, Scene.LightmapPlacement> placements = scene.BakedLighting.Placements;
        int pages = scene.BakedLighting.Lightmaps.Count;

        var data = new Dictionary<Source, SourceData>();
        var groupLookup = new Dictionary<(Attributes, ulong, int, int), int>();
        var pending = new List<List<Pending>>();

        foreach (GameObject go in scene.AllObjects)
        {
            foreach (MonoBehaviour component in go._components)
            {
                if (component is not MeshRenderer renderer || !IsEligible(renderer, out Mesh? mesh)) continue;

                LightmapOf(placements, pages, go.Identifier, out int lightmap, out Float4 scaleOffset);
                var source = new Source
                {
                    Id = renderer.Identifier,
                    World = renderer.Transform.LocalToWorldMatrix,
                    Layer = go.LayerIndex,
                    LightmapIndex = lightmap,
                    ScaleOffset = scaleOffset,
                    MeshVertexCount = mesh!.VertexCount,
                    MeshIndexCount = mesh.IndexCount,
                    Groups = new Group?[mesh.SubMeshCount],
                    Materials = new Material?[mesh.SubMeshCount],
                    Renderer = renderer,
                    Mesh = mesh,
                    MeshVersion = mesh.Version,
                    VisualVersion = renderer.VisualVersion,
                    Live = true,
                };

                Attributes attributes = AttributesOf(mesh, lightmap >= 0);
                SourceData? sourceData = null;
                for (int s = 0; s < mesh.SubMeshCount; s++)
                {
                    Material? material = EligibleMaterial(renderer, mesh, s);
                    if (material == null) continue;

                    sourceData ??= Transform(mesh, source.World, attributes);
                    var key = (attributes, material.GetStateHash(), lightmap, go.LayerIndex);
                    if (!groupLookup.TryGetValue(key, out int groupIndex))
                    {
                        groupIndex = _groups.Count;
                        groupLookup[key] = groupIndex;
                        _groups.Add(new Group { Layer = go.LayerIndex, LightmapIndex = lightmap, Material = material, Buffer = BufferFor(attributes) });
                        pending.Add(new List<Pending>());
                    }

                    source.Groups[s] = _groups[groupIndex];
                    source.Materials[s] = material;
                    pending[groupIndex].Add(new Pending
                    {
                        Source = source,
                        SubMesh = s,
                        Bounds = SubMeshBounds(mesh, s, sourceData.Positions),
                        Triangles = (uint)(mesh.GetSubMesh(s).IndexCount / 3),
                    });
                }

                if (sourceData == null) continue;
                data[source] = sourceData;
                source.AllCovered = Array.TrueForAll(source.Groups, g => g != null);
                renderer.StaticSource = source;
                _sources.Add(source);
            }
        }

        var leaves = new List<(int Start, int End)>();
        var writers = new BufferWriter[_buffers.Count];
        for (int b = 0; b < writers.Length; b++) writers[b] = new BufferWriter(_buffers[b].Attributes);

        for (int g = 0; g < _groups.Count; g++)
        {
            Group group = _groups[g];
            List<Pending> items = pending[g];
            leaves.Clear();
            Split(items, 0, items.Count, leaves);

            BufferWriter writer = writers[group.Buffer];
            foreach ((int start, int end) in leaves)
            {
                var cluster = new Cluster { Group = group, Start = (uint)writer.Indices.Count, Members = new Member[end - start], Properties = group.Properties };
                AABB bounds = items[start].Bounds;
                for (int i = start; i < end; i++)
                {
                    Pending item = items[i];
                    uint first = (uint)writer.Indices.Count;
                    writer.AddTriangles(item.Source, data[item.Source], item.SubMesh);
                    cluster.Members[i - start] = new Member(item.Source, item.SubMesh, first, (uint)writer.Indices.Count - first);
                    bounds = Union(bounds, item.Bounds);
                    if (!item.Source.Clusters.Contains(cluster)) item.Source.Clusters.Add(cluster);
                }
                cluster.Count = (uint)writer.Indices.Count - cluster.Start;
                cluster.Bounds = bounds;
                group.Clusters.Add(cluster);
            }
        }

        for (int b = 0; b < _buffers.Count; b++)
            _buffers[b].Mesh = writers[b].ToMesh();
        foreach (Group group in _groups)
            group.Mesh = _buffers[group.Buffer].Mesh;

        _bound = true;
    }

    private int BufferFor(Attributes attributes)
    {
        for (int i = 0; i < _buffers.Count; i++)
            if (_buffers[i].Attributes == attributes) return i;
        _buffers.Add(new Buffer { Attributes = attributes });
        return _buffers.Count - 1;
    }

    private static Attributes AttributesOf(Mesh mesh, bool lightmapped)
    {
        Attributes attributes = 0;
        if (mesh.HasUV) attributes |= Attributes.UV;
        if (mesh.HasUV2 || lightmapped) attributes |= Attributes.UV2;
        if (mesh.HasNormals) attributes |= Attributes.Normals;
        if (mesh.HasColors || mesh.HasColors32) attributes |= Attributes.Colors;
        if (mesh.HasTangents) attributes |= Attributes.Tangents;
        return attributes;
    }

    private static SourceData Transform(Mesh mesh, in Float4x4 world, Attributes attributes)
    {
        Float3[] local = mesh.Vertices;
        var positions = new Float3[local.Length];
        for (int i = 0; i < local.Length; i++)
            positions[i] = Float4x4.TransformPoint(local[i], world);
        return new SourceData { Mesh = mesh, Attributes = attributes, Positions = positions, Mirrored = Determinant(world) < 0f };
    }

    private static float Determinant(in Float4x4 m)
    {
        Float3 a = Xyz(m.c0), b = Xyz(m.c1), c = Xyz(m.c2);
        return Float3.Dot(a, Float3.Cross(b, c));
    }

    private static AABB SubMeshBounds(Mesh mesh, int subMesh, Float3[] positions)
    {
        SubMeshDescriptor sub = mesh.GetSubMesh(subMesh);
        uint[] indices = mesh.Indices;
        Float3 min = new(float.MaxValue), max = new(float.MinValue);
        for (int i = sub.IndexStart; i < sub.IndexStart + sub.IndexCount; i++)
        {
            Float3 p = positions[indices[i]];
            min = Maths.Min(min, p);
            max = Maths.Max(max, p);
        }
        return new AABB(min, max);
    }

    private static float Component(Float3 v, int axis) => axis == 0 ? v.X : axis == 1 ? v.Y : v.Z;

    private static Float3 Xyz(Float4 v) => new(v.X, v.Y, v.Z);

    private static AABB Union(in AABB a, in AABB b) => new(Maths.Min(a.Min, b.Min), Maths.Max(a.Max, b.Max));

    // Halves along the longest spread of centers until a cluster fits the triangle budget, so clusters are compact
    // and the depth first order keeps neighbours next to each other in the index buffer
    private static void Split(List<Pending> items, int start, int end, List<(int, int)> leaves)
    {
        long triangles = 0;
        Float3 min = new(float.MaxValue), max = new(float.MinValue);
        for (int i = start; i < end; i++)
        {
            triangles += items[i].Triangles;
            min = Maths.Min(min, items[i].Bounds.Center);
            max = Maths.Max(max, items[i].Bounds.Center);
        }
        if (end - start <= 1 || triangles <= ClusterTriangles)
        {
            leaves.Add((start, end));
            return;
        }

        Float3 spread = max - min;
        int axis = spread.X >= spread.Y && spread.X >= spread.Z ? 0 : spread.Y >= spread.Z ? 1 : 2;
        items.Sort(start, end - start, new CenterOrder(axis));

        long half = triangles / 2, taken = 0;
        int mid = start;
        while (mid < end - 1 && taken + items[mid].Triangles <= half)
            taken += items[mid++].Triangles;
        if (mid == start) mid++;

        Split(items, start, mid, leaves);
        Split(items, mid, end, leaves);
    }

    // Collects one buffer's vertices and indices, each renderer's vertices written once however many groups use it
    private sealed class BufferWriter(Attributes attributes)
    {
        public readonly Attributes Attributes = attributes;
        public readonly List<Float3> Positions = new();
        public readonly List<Float2> UV = new();
        public readonly List<Float2> UV2 = new();
        public readonly List<Float3> Normals = new();
        public readonly List<Color> Colors = new();
        public readonly List<Float4> Tangents = new();
        public readonly List<uint> Indices = new();
        private readonly Dictionary<Source, uint> _bases = new();

        public void AddTriangles(Source source, SourceData data, int subMesh)
        {
            if (!_bases.TryGetValue(source, out uint baseVertex))
            {
                baseVertex = (uint)Positions.Count;
                _bases[source] = baseVertex;
                AddVertices(source, data);
            }

            SubMeshDescriptor sub = data.Mesh.GetSubMesh(subMesh);
            uint[] indices = data.Mesh.Indices;
            for (int i = sub.IndexStart; i + 2 < sub.IndexStart + sub.IndexCount; i += 3)
            {
                // A mirroring transform turns the winding inside out, so it is swapped back
                Indices.Add(baseVertex + indices[i]);
                Indices.Add(baseVertex + indices[data.Mirrored ? i + 2 : i + 1]);
                Indices.Add(baseVertex + indices[data.Mirrored ? i + 1 : i + 2]);
            }
        }

        private void AddVertices(Source source, SourceData data)
        {
            Mesh mesh = data.Mesh;
            Positions.AddRange(data.Positions);
            int count = data.Positions.Length;

            Float4x4 world = source.World;
            Float3 a = Xyz(world.c0), b = Xyz(world.c1), c = Xyz(world.c2);
            float det = Float3.Dot(a, Float3.Cross(b, c));
            // Inverse transpose columns, so normals stay perpendicular under non uniform scale
            Float3 na = Float3.Cross(b, c) / det, nb = Float3.Cross(c, a) / det, nc = Float3.Cross(a, b) / det;

            if ((Attributes & Attributes.UV) != 0) UV.AddRange(mesh.UV);

            if ((Attributes & Attributes.UV2) != 0)
            {
                if (source.LightmapIndex < 0)
                {
                    UV2.AddRange(mesh.UV2);
                }
                else
                {
                    Float2[] from = mesh.HasUV2 ? mesh.UV2 : mesh.HasUV ? mesh.UV : new Float2[count];
                    Float4 so = source.ScaleOffset;
                    foreach (Float2 uv in from)
                        UV2.Add(new Float2(uv.X * so.X + so.Z, uv.Y * so.Y + so.W));
                }
            }

            if ((Attributes & Attributes.Normals) != 0)
                foreach (Float3 n in mesh.Normals)
                    Normals.Add(Float3.Normalize(na * n.X + nb * n.Y + nc * n.Z));

            if ((Attributes & Attributes.Colors) != 0)
            {
                if (mesh.HasColors) Colors.AddRange(mesh.Colors);
                else foreach (Color32 color in mesh.Colors32) Colors.Add((Color)color);
            }

            if ((Attributes & Attributes.Tangents) != 0)
            {
                float handedness = det < 0f ? -1f : 1f;
                foreach (Float4 t in mesh.Tangents)
                {
                    Float3 dir = Float3.Normalize(a * t.X + b * t.Y + c * t.Z);
                    Tangents.Add(new Float4(dir.X, dir.Y, dir.Z, t.W * handedness));
                }
            }
        }

        public Mesh ToMesh() => MakeMesh(Attributes, [.. Positions], [.. UV], [.. UV2], [.. Normals], [.. Colors], [.. Tangents], [.. Indices]);
    }

    private static Mesh MakeMesh(Attributes attributes, Float3[] positions, Float2[] uv, Float2[] uv2, Float3[] normals, Color[] colors, Float4[] tangents, uint[] indices)
    {
        var mesh = new Mesh { Name = "Static Geometry" };
        mesh.IndexFormat = IndexFormat.UInt32;
        if (positions.Length == 0) return mesh;

        mesh.Vertices = positions;
        if ((attributes & Attributes.UV) != 0) mesh.UV = uv;
        if ((attributes & Attributes.UV2) != 0) mesh.UV2 = uv2;
        if ((attributes & Attributes.Normals) != 0) mesh.Normals = normals;
        if ((attributes & Attributes.Colors) != 0) mesh.Colors = colors;
        if ((attributes & Attributes.Tangents) != 0) mesh.Tangents = tangents;
        mesh.Indices = indices;
        mesh.RecalculateBounds();
        return mesh;
    }

    // ---------------------------------------------------------------- storage

    public void Serialize(ref EchoObject compoundTag, SerializationContext ctx)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream))
        {
            w.Write(FormatVersion);

            var sourceIndex = new Dictionary<Source, int>();
            w.Write(_sources.Count);
            foreach (Source source in _sources)
            {
                sourceIndex[source] = sourceIndex.Count;
                w.Write(source.Id.ToByteArray());
                WriteRaw(w, source.World);
                w.Write(source.Layer);
                w.Write(source.LightmapIndex);
                WriteRaw(w, source.ScaleOffset);
                w.Write(source.MeshVertexCount);
                w.Write(source.MeshIndexCount);
                w.Write(source.Groups.Length);
            }

            w.Write(_buffers.Count);
            foreach (Buffer buffer in _buffers)
            {
                Mesh mesh = buffer.Mesh;
                w.Write((byte)buffer.Attributes);
                WriteArray(w, mesh.Vertices);
                WriteArray(w, (buffer.Attributes & Attributes.UV) != 0 ? mesh.UV : []);
                WriteArray(w, (buffer.Attributes & Attributes.UV2) != 0 ? mesh.UV2 : []);
                WriteArray(w, (buffer.Attributes & Attributes.Normals) != 0 ? mesh.Normals : []);
                WriteArray(w, (buffer.Attributes & Attributes.Colors) != 0 ? mesh.Colors : []);
                WriteArray(w, (buffer.Attributes & Attributes.Tangents) != 0 ? mesh.Tangents : []);
                WriteArray(w, mesh.Indices);
            }

            w.Write(_groups.Count);
            foreach (Group group in _groups)
            {
                w.Write(group.Buffer);
                w.Write(group.Layer);
                w.Write(group.LightmapIndex);
                w.Write(group.Clusters.Count);
                foreach (Cluster cluster in group.Clusters)
                {
                    w.Write(cluster.Start);
                    w.Write(cluster.Count);
                    WriteRaw(w, cluster.Bounds.Min);
                    WriteRaw(w, cluster.Bounds.Max);
                    w.Write(cluster.Members.Length);
                    foreach (Member member in cluster.Members)
                    {
                        w.Write(sourceIndex[member.Source]);
                        w.Write(member.SubMesh);
                        w.Write(member.Start);
                        w.Write(member.Count);
                    }
                }
            }
        }
        compoundTag.Add("Data", new EchoObject(stream.ToArray()));
    }

    public void Deserialize(EchoObject value, SerializationContext ctx)
    {
        Clear();
        if (!value.TryGet("Data", out EchoObject? blob) || blob == null) return;

        using var r = new BinaryReader(new MemoryStream(blob.ByteArrayValue));
        if (r.ReadInt32() != FormatVersion) return;

        int sourceCount = r.ReadInt32();
        for (int i = 0; i < sourceCount; i++)
        {
            var source = new Source
            {
                Id = new Guid(r.ReadBytes(16)),
                World = ReadRaw<Float4x4>(r),
                Layer = r.ReadInt32(),
                LightmapIndex = r.ReadInt32(),
                ScaleOffset = ReadRaw<Float4>(r),
                MeshVertexCount = r.ReadInt32(),
                MeshIndexCount = r.ReadInt32(),
            };
            source.Groups = new Group?[r.ReadInt32()];
            _sources.Add(source);
        }

        int bufferCount = r.ReadInt32();
        for (int i = 0; i < bufferCount; i++)
        {
            var attributes = (Attributes)r.ReadByte();
            Float3[] positions = ReadArray<Float3>(r);
            Float2[] uv = ReadArray<Float2>(r);
            Float2[] uv2 = ReadArray<Float2>(r);
            Float3[] normals = ReadArray<Float3>(r);
            Color[] colors = ReadArray<Color>(r);
            Float4[] tangents = ReadArray<Float4>(r);
            uint[] indices = ReadArray<uint>(r);
            _buffers.Add(new Buffer { Attributes = attributes, Mesh = MakeMesh(attributes, positions, uv, uv2, normals, colors, tangents, indices) });
        }

        int groupCount = r.ReadInt32();
        for (int g = 0; g < groupCount; g++)
        {
            var group = new Group { Buffer = r.ReadInt32(), Layer = r.ReadInt32(), LightmapIndex = r.ReadInt32() };
            group.Mesh = _buffers[group.Buffer].Mesh;
            int clusterCount = r.ReadInt32();
            for (int c = 0; c < clusterCount; c++)
            {
                var cluster = new Cluster { Group = group, Start = r.ReadUInt32(), Count = r.ReadUInt32(), Properties = group.Properties };
                cluster.Bounds = new AABB(ReadRaw<Float3>(r), ReadRaw<Float3>(r));
                cluster.Members = new Member[r.ReadInt32()];
                for (int m = 0; m < cluster.Members.Length; m++)
                {
                    Source source = _sources[r.ReadInt32()];
                    int subMesh = r.ReadInt32();
                    cluster.Members[m] = new Member(source, subMesh, r.ReadUInt32(), r.ReadUInt32());
                    source.Groups[subMesh] = group;
                    if (!source.Clusters.Contains(cluster)) source.Clusters.Add(cluster);
                }
                group.Clusters.Add(cluster);
            }
            _groups.Add(group);
        }

        foreach (Source source in _sources)
            source.AllCovered = Array.TrueForAll(source.Groups, g => g != null);
    }

    private static void WriteRaw<T>(BinaryWriter w, T value) where T : unmanaged =>
        w.Write(MemoryMarshal.AsBytes(new ReadOnlySpan<T>(in value)));

    private static T ReadRaw<T>(BinaryReader r) where T : unmanaged =>
        MemoryMarshal.Read<T>(r.ReadBytes(System.Runtime.CompilerServices.Unsafe.SizeOf<T>()));

    private static void WriteArray<T>(BinaryWriter w, T[] values) where T : unmanaged
    {
        w.Write(values.Length);
        w.Write(MemoryMarshal.AsBytes(values.AsSpan()));
    }

    private static T[] ReadArray<T>(BinaryReader r) where T : unmanaged
    {
        int length = r.ReadInt32();
        var values = new T[length];
        r.BaseStream.ReadExactly(MemoryMarshal.AsBytes(values.AsSpan()));
        return values;
    }
}
