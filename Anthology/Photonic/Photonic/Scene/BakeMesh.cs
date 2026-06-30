using Prowl.Vector;

namespace Prowl.Photonic;

/// <summary>
/// Immutable bake-time mesh. Built via <see cref="BakeScene.BeginMesh"/>.
/// </summary>
/// <remarks>
/// Layout: a single vertex buffer with parallel attribute streams, multiple UV layers identified
/// by name, and one or more material groups (submeshes) each holding a run of triangle indices.
/// </remarks>
public sealed class BakeMesh
{
    /// <summary>Mesh name as registered on the scene.</summary>
    public string Name { get; }

    /// <summary>Vertex positions in mesh-local space.</summary>
    public Float3[] Positions { get; }

    /// <summary>Per-vertex normals. May be all zeros if the caller didn't supply any.</summary>
    public Float3[] Normals { get; }

    /// <summary>UV layers, keyed by name. <c>"UV0"</c> is the material/shading layer, <c>"UV1"</c> is the bake layer.</summary>
    public System.Collections.Generic.IReadOnlyDictionary<string, Float2[]> UVLayers => _uvLayers;

    /// <summary>Material groups (submeshes), in the order they were added.</summary>
    public System.Collections.Generic.IReadOnlyList<MaterialGroup> MaterialGroups => _materialGroups;

    /// <summary>Local-space AABB enclosing every vertex.</summary>
    public AABB Bounds { get; }

    private readonly System.Collections.Generic.Dictionary<string, Float2[]> _uvLayers;
    private readonly System.Collections.Generic.List<MaterialGroup> _materialGroups;

    internal BakeMesh(string name, Float3[] positions, Float3[] normals,
                     System.Collections.Generic.Dictionary<string, Float2[]> uvLayers,
                     System.Collections.Generic.List<MaterialGroup> materialGroups)
    {
        Name = name;
        Positions = positions;
        Normals = normals;
        _uvLayers = uvLayers;
        _materialGroups = materialGroups;
        Bounds = AABB.FromPoints(positions);
    }

    /// <summary>A submesh: an index range plus the material it shades with.</summary>
    public sealed class MaterialGroup
    {
        /// <summary>Material name as it was registered on the scene; may be null if unmatched.</summary>
        public string MaterialName { get; }

        /// <summary>Triangle indices into <see cref="BakeMesh.Positions"/>. Length is a multiple of 3.</summary>
        public int[] Indices { get; }

        internal MaterialGroup(string materialName, int[] indices)
        {
            MaterialName = materialName;
            Indices = indices;
        }
    }

    /// <summary>
    /// Fluent builder for a <see cref="BakeMesh"/>. Chain <c>AddVertices</c>, <c>AddUVLayer</c>,
    /// and <c>AddMaterialGroup</c> calls, finalise with <c>End</c>.
    /// </summary>
    public sealed class Builder
    {
        private readonly BakeScene _scene;
        private readonly string _name;
        private Float3[]? _positions;
        private Float3[]? _normals;
        private readonly System.Collections.Generic.Dictionary<string, Float2[]> _uvLayers = new();
        private readonly System.Collections.Generic.List<MaterialGroup> _groups = new();

        internal Builder(BakeScene scene, string name) { _scene = scene; _name = name; }

        /// <summary>Set per-vertex positions and (optional) normals. Must be called exactly once.</summary>
        public Builder AddVertices(Float3[] positions, Float3[]? normals = null)
        {
            if (positions is null || positions.Length == 0) throw new System.ArgumentException("positions must be non-empty.");
            if (normals is not null && normals.Length != positions.Length)
                throw new System.ArgumentException("normals must match positions length.");
            _positions = positions;
            _normals = normals ?? new Float3[positions.Length];
            return this;
        }

        /// <summary>Add a named UV layer. Length must match vertex count.</summary>
        public Builder AddUVLayer(string layerName, Float2[] uvs)
        {
            if (_positions is null) throw new System.InvalidOperationException("AddVertices must be called before AddUVLayer.");
            if (uvs.Length != _positions.Length) throw new System.ArgumentException($"UV layer '{layerName}' must match vertex count.");
            _uvLayers[layerName] = uvs;
            return this;
        }

        /// <summary>Add a submesh: a material name + the triangle indices that use it.</summary>
        public Builder AddMaterialGroup(string materialName, int[] indices)
        {
            if (indices.Length % 3 != 0) throw new System.ArgumentException("indices length must be a multiple of 3.");
            _groups.Add(new MaterialGroup(materialName, indices));
            return this;
        }

        /// <summary>Finalise and register on the scene.</summary>
        public BakeMesh End()
        {
            if (_positions is null) throw new System.InvalidOperationException("AddVertices was never called.");
            var m = new BakeMesh(_name, _positions, _normals!, _uvLayers, _groups);

            // The path tracer orients its sampling hemisphere by the surface normal, so a mesh with
            // no (all-zero) normals bakes incorrectly. Warn rather than fail.
            if (AllNormalsZero(_normals!))
                LightmapBaker.RaiseWarning(
                    $"BakeMesh '{_name}' has no usable normals (all zero); bake results will be wrong. " +
                    "Supply per-vertex normals via AddVertices.");

            _scene.RegisterMesh(m);
            return m;
        }

        private static bool AllNormalsZero(Float3[] normals)
        {
            if (normals.Length == 0) return false;
            for (int i = 0; i < normals.Length; i++)
            {
                var n = normals[i];
                if ((float)(n.X * n.X + n.Y * n.Y + n.Z * n.Z) > 1e-12f) return false;
            }
            return true;
        }
    }
}
