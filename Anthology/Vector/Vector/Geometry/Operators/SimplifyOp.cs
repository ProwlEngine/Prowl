// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

namespace Prowl.Vector.Geometry.Operators;

/// <summary>
/// Quadric error simplification by half edge collapse: a vertex only ever merges onto one of its
/// neighbours, so no vertex is created and every corner keeps values an original corner had.
/// </summary>
internal static class SimplifyOp
{
    private enum Kind : byte { Manifold, Border, Seam, Locked }

    // Constraint planes along borders and seams, weighted so outlines survive long after the interior
    private const double BoundaryWeight = 10.0;
    private const float SeamTolerance = 1e-6f;

    internal static SimplifyResult Simplify(GeometryData mesh, SimplifyOptions options) => new Simplifier(mesh, options).Run();

    private struct Quadric
    {
        public double A00, A01, A02, A11, A12, A22, B0, B1, B2, C, W;

        public static Quadric FromPlane(Float3 n, double d, double weight)
        {
            return new Quadric
            {
                A00 = weight * n.X * n.X, A01 = weight * n.X * n.Y, A02 = weight * n.X * n.Z,
                A11 = weight * n.Y * n.Y, A12 = weight * n.Y * n.Z, A22 = weight * n.Z * n.Z,
                B0 = weight * n.X * d, B1 = weight * n.Y * d, B2 = weight * n.Z * d,
                C = weight * d * d, W = weight,
            };
        }

        public void Add(in Quadric q)
        {
            A00 += q.A00; A01 += q.A01; A02 += q.A02; A11 += q.A11; A12 += q.A12; A22 += q.A22;
            B0 += q.B0; B1 += q.B1; B2 += q.B2; C += q.C; W += q.W;
        }

        /// <summary>Weighted sum of squared distances, before dividing by <see cref="W"/>.</summary>
        public readonly double Sum(Float3 p)
        {
            double x = p.X, y = p.Y, z = p.Z;
            return A00 * x * x + 2 * A01 * x * y + 2 * A02 * x * z + A11 * y * y + 2 * A12 * y * z + A22 * z * z
                 + 2 * (B0 * x + B1 * y + B2 * z) + C;
        }

        /// <summary>Mean squared distance from the planes this quadric gathered.</summary>
        public readonly double Error(Float3 p) => W > 0 ? Math.Max(Sum(p), 0) / W : 0;
    }

    /// <summary>
    /// Min heap of collapse candidates. Equal costs go shortest edge first, then by a scrambled vertex order, so
    /// flat areas thin out evenly instead of being swept in index order into a few huge fans. Deterministic.
    /// </summary>
    private sealed class CandidateHeap
    {
        private readonly List<(double Cost, float Length, int Vertex, int Version)> _items = new();

        public int Count => _items.Count;

        private static bool Less((double Cost, float Length, int Vertex, int Version) a, (double Cost, float Length, int Vertex, int Version) b)
        {
            if (a.Cost != b.Cost) return a.Cost < b.Cost;
            if (a.Length != b.Length) return a.Length < b.Length;
            return Scramble(a.Vertex) < Scramble(b.Vertex);
        }

        private static uint Scramble(int vertex) => (uint)vertex * 2654435761u;

        public void Push(in Candidate candidate, int vertex, int version)
        {
            _items.Add((candidate.Cost, candidate.EdgeLength, vertex, version));
            int i = _items.Count - 1;
            while (i > 0)
            {
                int parent = (i - 1) / 2;
                if (!Less(_items[i], _items[parent])) break;
                (_items[i], _items[parent]) = (_items[parent], _items[i]);
                i = parent;
            }
        }

        public (double Cost, float Length, int Vertex, int Version) Pop()
        {
            var top = _items[0];
            int last = _items.Count - 1;
            _items[0] = _items[last];
            _items.RemoveAt(last);

            int i = 0;
            while (true)
            {
                int left = i * 2 + 1, right = left + 1, smallest = i;
                if (left < _items.Count && Less(_items[left], _items[smallest])) smallest = left;
                if (right < _items.Count && Less(_items[right], _items[smallest])) smallest = right;
                if (smallest == i) break;
                (_items[i], _items[smallest]) = (_items[smallest], _items[i]);
                i = smallest;
            }
            return top;
        }
    }

    /// <summary>Spreads packed vertex pairs across buckets. The default long hash xors the halves, which piles neighbours together.</summary>
    private sealed class PairComparer : IEqualityComparer<long>
    {
        public static readonly PairComparer Instance = new();
        public bool Equals(long a, long b) => a == b;
        public int GetHashCode(long key) => (int)(((ulong)key * 0x9E3779B97F4A7C15UL) >> 32);
    }

    private sealed class IntArrayComparer : IEqualityComparer<int[]>
    {
        public bool Equals(int[]? a, int[]? b) => a!.AsSpan().SequenceEqual(b);
        public int GetHashCode(int[] values)
        {
            int hash = 17;
            foreach (int v in values) hash = hash * 31 + v;
            return hash;
        }
    }

    private static long PairKey(int a, int b) => a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;

    /// <summary>A costed float attribute, read from loops or, for vertex attributes, from the corner's vertex.</summary>
    private readonly struct Channel
    {
        public readonly string Name;
        public readonly bool OnLoop;
        public readonly int Component;
        public readonly double Weight;

        public Channel(string name, bool onLoop, int component, double weight)
        {
            Name = name;
            OnLoop = onLoop;
            Component = component;
            Weight = weight;
        }
    }

    private struct Candidate
    {
        public int Target;
        public double Cost;
        public double PositionError;
        public int FromA, ToA, FromB, ToB;
        public GeometryData.Loop? LoopA, LoopB;
        public int Shared0, Shared1, SharedCount;
        public float EdgeLength;
        public int TargetValence;

        /// <summary>
        /// Cheaper first. Between equals, the target with fewer triangles and then the shorter edge, so equally
        /// cheap collapses spread out instead of piling into one vertex.
        /// </summary>
        public readonly bool IsBetterThan(in Candidate other)
        {
            if (Cost != other.Cost) return Cost < other.Cost;
            if (TargetValence != other.TargetValence) return TargetValence < other.TargetValence;
            if (EdgeLength != other.EdgeLength) return EdgeLength < other.EdgeLength;
            return Target < other.Target;
        }
    }

    private sealed class Simplifier
    {
        private readonly GeometryData _mesh;
        private readonly SimplifyOptions _options;
        private readonly double _limit;

        private readonly GeometryData.Vertex[] _vertices;
        private readonly Float3[] _positions;      // normalized into the unit cube of the mesh's extent
        private readonly float _extent;
        private readonly Kind[] _kinds;
        private readonly bool[] _pinned;           // never moves: wire edges, the lock attribute, no faces
        private readonly bool[] _faceless;
        private readonly Quadric[] _quadrics;
        private readonly List<int>[] _adjacency;   // vertex to triangles, dead triangles pruned lazily
        private readonly bool[] _dead;
        private readonly int[] _versions;

        private readonly int[] _triangles;          // three vertex indices per triangle
        private readonly int[] _cornerWedges;       // seam class of each corner at its vertex
        private readonly GeometryData.Loop[] _cornerLoops; // the original loop each corner takes its values from
        private readonly int[] _regions;
        private readonly bool[] _alive;
        private readonly int[] _triangleFace;
        private readonly GeometryData.Face[] _faces;

        // Wedges are the seam classes at each vertex. Each carries an attribute quadric over the costed channels.
        private readonly Channel[] _channels;
        private readonly List<string> _intAttributes = new();
        private readonly List<bool> _intOnLoop = new();
        private readonly List<double> _intWeights = new();
        private readonly List<GeometryData.Loop> _wedgeLoops = new();
        private readonly List<double> _wedgeValues = new();   // channel values per wedge
        private Quadric[] _wedgeQuadrics = Array.Empty<Quadric>();
        private double[] _wedgeGradients = Array.Empty<double>(); // four per channel per wedge: gradient and offset

        private readonly List<int> _neighbours = new();
        private readonly List<Candidate> _candidates = new();
        private readonly int[] _mark;
        private readonly int[] _slot;
        private int _markStamp;

        // Classification scratch
        private readonly List<int> _edgeNeighbour = new();
        private readonly List<int> _edgeCount = new();
        private readonly List<int> _edgeFirst = new();
        private readonly List<int> _edgeSecond = new();
        private readonly List<int> _fan = new();
        private readonly List<int> _fanParent = new();
        private readonly HashSet<int> _wedgeSet = new();

        public Simplifier(GeometryData mesh, SimplifyOptions options)
        {
            _mesh = mesh;
            _options = options;

            int vertexCount = mesh.Vertices.Count;
            _vertices = mesh.Vertices.ToArray();
            var vertexIndex = new Dictionary<GeometryData.Vertex, int>(vertexCount);
            for (int i = 0; i < vertexCount; i++) vertexIndex[_vertices[i]] = i;

            AABB bounds = mesh.GetAABB();
            Float3 size = bounds.Max - bounds.Min;
            _extent = Math.Max(size.X, Math.Max(size.Y, size.Z));
            if (_extent <= 0) _extent = 1;
            _positions = new Float3[vertexCount];
            for (int i = 0; i < vertexCount; i++)
                _positions[i] = (_vertices[i].Point - bounds.Min) / _extent;

            float maxError = options.MaxError;
            _limit = float.IsNaN(maxError) || float.IsPositiveInfinity(maxError)
                ? double.PositiveInfinity
                : Math.Pow(Math.Max(0, maxError) / _extent, 2);

            _kinds = new Kind[vertexCount];
            _pinned = new bool[vertexCount];
            _faceless = new bool[vertexCount];
            _quadrics = new Quadric[vertexCount];
            _adjacency = new List<int>[vertexCount];
            for (int i = 0; i < vertexCount; i++) _adjacency[i] = new List<int>();
            _dead = new bool[vertexCount];
            _versions = new int[vertexCount];
            _mark = new int[vertexCount];
            _slot = new int[vertexCount];

            // Faces with more corners are fanned into triangles here, the mesh itself is only rebuilt at the end
            int triangleCount = 0;
            foreach (var face in mesh.Faces)
                if (face.VertCount >= 3) triangleCount += face.VertCount - 2;

            _triangles = new int[triangleCount * 3];
            _cornerWedges = new int[triangleCount * 3];
            _cornerLoops = new GeometryData.Loop[triangleCount * 3];
            _regions = new int[triangleCount];
            _alive = new bool[triangleCount];
            _triangleFace = new int[triangleCount];
            _faces = mesh.Faces.ToArray();

            var regionIds = new Dictionary<int[], int>(new IntArrayComparer());
            int t = 0;
            for (int f = 0; f < _faces.Length; f++)
            {
                var face = _faces[f];
                if (face.VertCount < 3 || face.Loop == null) continue;

                int region = RegionOf(face, regionIds);
                var first = face.Loop;
                for (var loop = first.Next!; loop.Next != first; loop = loop.Next!, t++)
                {
                    SetCorner(t * 3, first, vertexIndex);
                    SetCorner(t * 3 + 1, loop, vertexIndex);
                    SetCorner(t * 3 + 2, loop.Next!, vertexIndex);
                    _regions[t] = region;
                    _alive[t] = true;
                    _triangleFace[t] = f;
                }
            }

            for (int v = 0; v < vertexCount; v++)
            {
                _faceless[v] = _adjacency[v].Count == 0;
                _pinned[v] = _faceless[v] || IsLockedByAttribute(_vertices[v]) || HasWireEdge(_vertices[v]);
            }

            _channels = BuildChannels();
            BuildWedges();
            BuildPositionQuadrics();
            BuildAttributeQuadrics();

            for (int v = 0; v < vertexCount; v++) Classify(v);
        }

        private void SetCorner(int corner, GeometryData.Loop loop, Dictionary<GeometryData.Vertex, int> vertexIndex)
        {
            int v = vertexIndex[loop.Vert];
            _triangles[corner] = v;
            _cornerLoops[corner] = loop;
            _adjacency[v].Add(corner / 3);
        }

        private int RegionOf(GeometryData.Face face, Dictionary<int[], int> regionIds)
        {
            var key = new List<int>();
            foreach (var def in _mesh.FaceAttributes)
            {
                if (_options.RegionAttributes != null && !_options.RegionAttributes.Contains(def.Name)) continue;
                key.Add(-1);
                if (!face.Attributes.TryGetValue(def.Name, out var value)) continue;
                if (value is GeometryData.IntAttributeValue n) key.AddRange(n.Data);
                else if (value is GeometryData.FloatAttributeValue f) foreach (float x in f.Data) key.Add(FloatBits(x));
            }

            int[] array = key.ToArray();
            if (!regionIds.TryGetValue(array, out int region))
                regionIds[array] = region = regionIds.Count;
            return region;
        }

        private static unsafe int FloatBits(float value) => *(int*)&value;
        private static unsafe float IntAsFloat(int value) => *(float*)&value;

        private bool IsLockedByAttribute(GeometryData.Vertex vertex)
        {
            if (_options.LockAttribute == null || !vertex.Attributes.TryGetValue(_options.LockAttribute, out var value))
                return false;
            if (value is GeometryData.IntAttributeValue n) foreach (int x in n.Data) if (x != 0) return true;
            if (value is GeometryData.FloatAttributeValue f) foreach (float x in f.Data) if (x != 0) return true;
            return false;
        }

        private static bool HasWireEdge(GeometryData.Vertex vertex)
        {
            var start = vertex.Edge;
            if (start == null) return false;
            var e = start;
            do
            {
                if (e!.Loop == null) return true;
                e = e.Next(vertex);
            } while (e != null && e != start);
            return false;
        }

        private Channel[] BuildChannels()
        {
            var channels = new List<Channel>();
            void Gather(List<GeometryData.AttributeDefinition> definitions, bool onLoop)
            {
                foreach (var def in definitions)
                {
                    if (!_options.AttributeWeights.TryGetValue(def.Name, out float weight) || weight <= 0) continue;
                    if (def.Type.BaseType == GeometryData.AttributeBaseType.Int)
                    {
                        _intAttributes.Add(def.Name);
                        _intOnLoop.Add(onLoop);
                        _intWeights.Add(weight);
                        continue;
                    }
                    for (int c = 0; c < def.Type.Dimensions; c++)
                        channels.Add(new Channel(def.Name, onLoop, c, weight));
                }
            }
            Gather(_mesh.LoopAttributes, true);
            Gather(_mesh.VertexAttributes, false);
            return channels.ToArray();
        }

        private static float ChannelValue(GeometryData.Loop loop, in Channel channel)
        {
            var attributes = channel.OnLoop ? loop.Attributes : loop.Vert.Attributes;
            return attributes.TryGetValue(channel.Name, out var value) && value is GeometryData.FloatAttributeValue f && channel.Component < f.Data.Length
                ? f.Data[channel.Component]
                : 0f;
        }

        /// <summary>Groups each vertex's corners into seam classes: corners whose seam attributes match.</summary>
        private void BuildWedges()
        {
            var seamNames = new List<(string Name, bool IsInt, int Dimensions)>();
            int width = 0;
            foreach (var def in _mesh.LoopAttributes)
            {
                if (_options.SeamAttributes != null && !_options.SeamAttributes.Contains(def.Name)) continue;
                seamNames.Add((def.Name, def.Type.BaseType == GeometryData.AttributeBaseType.Int, def.Type.Dimensions));
                width += def.Type.Dimensions;
            }

            // One flat run of seam values per corner, ints kept as their bits so they compare exactly
            var cornerKeys = new float[_cornerLoops.Length * width];
            var exact = new bool[width];
            for (int corner = 0; corner < _cornerLoops.Length; corner++)
            {
                int o = 0;
                foreach (var (name, isInt, dimensions) in seamNames)
                {
                    _cornerLoops[corner].Attributes.TryGetValue(name, out var value);
                    for (int c = 0; c < dimensions; c++, o++)
                    {
                        exact[o] = isInt;
                        if (value is GeometryData.FloatAttributeValue f && c < f.Data.Length) cornerKeys[corner * width + o] = f.Data[c];
                        else if (value is GeometryData.IntAttributeValue n && c < n.Data.Length) cornerKeys[corner * width + o] = IntAsFloat(n.Data[c]);
                    }
                }
            }

            var representatives = new List<int>();
            var representativeWedges = new List<int>();
            for (int v = 0; v < _vertices.Length; v++)
            {
                representatives.Clear();
                representativeWedges.Clear();
                foreach (int t in _adjacency[v])
                {
                    int corner = CornerOf(t, v);
                    int wedge = -1;
                    for (int r = 0; r < representatives.Count; r++)
                    {
                        if (SameKey(cornerKeys, representatives[r] * width, corner * width, width, exact))
                        {
                            wedge = representativeWedges[r];
                            break;
                        }
                    }

                    if (wedge < 0)
                    {
                        wedge = _wedgeLoops.Count;
                        _wedgeLoops.Add(_cornerLoops[corner]);
                        foreach (var channel in _channels) _wedgeValues.Add(ChannelValue(_cornerLoops[corner], channel));
                        representatives.Add(corner);
                        representativeWedges.Add(wedge);
                    }
                    _cornerWedges[corner] = wedge;
                }
            }
        }

        private static bool SameKey(float[] keys, int a, int b, int width, bool[] exact)
        {
            for (int i = 0; i < width; i++)
            {
                float x = keys[a + i], y = keys[b + i];
                if (exact[i] ? FloatBits(x) != FloatBits(y) : Math.Abs(x - y) > SeamTolerance) return false;
            }
            return true;
        }

        private int CornerOf(int t, int v)
        {
            int c = t * 3;
            if (_triangles[c] == v) return c;
            if (_triangles[c + 1] == v) return c + 1;
            return c + 2;
        }

        private bool IsSeamBetween(int t0, int t1, int a, int b)
        {
            return _regions[t0] != _regions[t1]
                || _cornerWedges[CornerOf(t0, a)] != _cornerWedges[CornerOf(t1, a)]
                || _cornerWedges[CornerOf(t0, b)] != _cornerWedges[CornerOf(t1, b)];
        }

        private Float3 FaceCross(int t)
        {
            int a = _triangles[t * 3], b = _triangles[t * 3 + 1], c = _triangles[t * 3 + 2];
            return Float3.Cross(_positions[b] - _positions[a], _positions[c] - _positions[a]);
        }

        private void BuildPositionQuadrics()
        {
            for (int t = 0; t < _alive.Length; t++)
            {
                Float3 cross = FaceCross(t);
                float length = Float3.Length(cross);
                if (length <= 0) continue;

                int a = _triangles[t * 3];
                Float3 n = cross / length;
                var q = Quadric.FromPlane(n, -Float3.Dot(n, _positions[a]), length * 0.5);
                for (int k = 0; k < 3; k++) _quadrics[_triangles[t * 3 + k]].Add(q);
            }

            // Planes standing on border and seam edges, so moving along an outline is cheap and off it is not
            var edges = new Dictionary<long, (int First, int Second, int Count)>(_alive.Length * 2, PairComparer.Instance);
            for (int t = 0; t < _alive.Length; t++)
            {
                for (int k = 0; k < 3; k++)
                {
                    long key = PairKey(_triangles[t * 3 + k], _triangles[t * 3 + (k + 1) % 3]);
                    edges[key] = edges.TryGetValue(key, out var info)
                        ? (info.First, info.Count == 1 ? t : info.Second, info.Count + 1)
                        : (t, -1, 1);
                }
            }

            foreach (var pair in edges)
            {
                int a = (int)(pair.Key >> 32), b = (int)(pair.Key & 0xFFFFFFFF);
                var (first, second, count) = pair.Value;
                bool boundary = count == 1 || (count == 2 && IsSeamBetween(first, second, a, b));
                if (!boundary) continue;

                Float3 normal = Float3.Normalize(FaceCross(first));
                if (count == 2)
                {
                    // Faces folded back onto each other cancel out, so fall back to the first one alone
                    Float3 sum = normal + Float3.Normalize(FaceCross(second));
                    if (Float3.LengthSquared(sum) > 0.01f) normal = sum;
                }

                Float3 edge = _positions[b] - _positions[a];
                Float3 plane = Float3.Cross(edge, normal);
                float length = Float3.Length(plane);
                if (!(length > 1e-6f * Float3.Length(edge))) continue;

                plane /= length;
                var q = Quadric.FromPlane(plane, -Float3.Dot(plane, _positions[a]), Float3.LengthSquared(edge) * BoundaryWeight);
                _quadrics[a].Add(q);
                _quadrics[b].Add(q);
            }
        }

        /// <summary>
        /// Attribute quadrics: each triangle defines a linear field per channel, and a wedge gathers how far
        /// a target value is from those fields at a target position. A smooth gradient therefore costs
        /// nothing to collapse along, and only real detail in the attributes resists.
        /// </summary>
        private void BuildAttributeQuadrics()
        {
            int wedgeCount = _wedgeLoops.Count;
            int channelCount = _channels.Length;
            _wedgeQuadrics = new Quadric[wedgeCount];
            _wedgeGradients = new double[wedgeCount * channelCount * 4];
            if (channelCount == 0) return;

            for (int t = 0; t < _alive.Length; t++)
            {
                int c0 = t * 3;
                Float3 p0 = _positions[_triangles[c0]], p1 = _positions[_triangles[c0 + 1]], p2 = _positions[_triangles[c0 + 2]];
                Float3 e1 = p1 - p0, e2 = p2 - p0;
                double d00 = Float3.Dot(e1, e1), d01 = Float3.Dot(e1, e2), d11 = Float3.Dot(e2, e2);
                double denominator = d00 * d11 - d01 * d01;
                double area = 0.5 * Float3.Length(Float3.Cross(e1, e2));
                if (area <= 0 || denominator <= 0) continue;

                for (int k = 0; k < 3; k++) _wedgeQuadrics[_cornerWedges[c0 + k]].W += area;

                for (int ch = 0; ch < channelCount; ch++)
                {
                    ref readonly Channel channel = ref _channels[ch];
                    double a0 = ChannelValue(_cornerLoops[c0], channel);
                    double a1 = ChannelValue(_cornerLoops[c0 + 1], channel);
                    double a2 = ChannelValue(_cornerLoops[c0 + 2], channel);

                    double s = ((a1 - a0) * d11 - (a2 - a0) * d01) / denominator;
                    double r = ((a2 - a0) * d00 - (a1 - a0) * d01) / denominator;
                    double gx = e1.X * s + e2.X * r, gy = e1.Y * s + e2.Y * r, gz = e1.Z * s + e2.Z * r;
                    double d = a0 - (gx * p0.X + gy * p0.Y + gz * p0.Z);
                    double w = area * channel.Weight;

                    var q = new Quadric
                    {
                        A00 = w * gx * gx, A01 = w * gx * gy, A02 = w * gx * gz,
                        A11 = w * gy * gy, A12 = w * gy * gz, A22 = w * gz * gz,
                        B0 = w * gx * d, B1 = w * gy * d, B2 = w * gz * d, C = w * d * d,
                    };

                    for (int k = 0; k < 3; k++)
                    {
                        int wedge = _cornerWedges[c0 + k];
                        _wedgeQuadrics[wedge].Add(q);
                        int g = (wedge * channelCount + ch) * 4;
                        _wedgeGradients[g] += w * gx;
                        _wedgeGradients[g + 1] += w * gy;
                        _wedgeGradients[g + 2] += w * gz;
                        _wedgeGradients[g + 3] += w * d;
                    }
                }
            }
        }

        /// <summary>Error of giving wedge <paramref name="from"/>'s corners wedge <paramref name="to"/>'s values at position p.</summary>
        private double AttributeError(int from, int to, Float3 p)
        {
            int channelCount = _channels.Length;
            ref readonly Quadric q = ref _wedgeQuadrics[from];
            if (channelCount == 0 || q.W <= 0) return 0;

            // Sum over triangles of w (g.p + d - a)^2, expanded into the stored sums
            double error = q.Sum(p);
            for (int ch = 0; ch < channelCount; ch++)
            {
                double a = _wedgeValues[to * channelCount + ch];
                int g = (from * channelCount + ch) * 4;
                double field = _wedgeGradients[g] * p.X + _wedgeGradients[g + 1] * p.Y + _wedgeGradients[g + 2] * p.Z + _wedgeGradients[g + 3];
                error += -2 * a * field + _channels[ch].Weight * a * a * q.W;
            }
            return Math.Max(error, 0) / q.W;
        }

        private double IntPenalty(int u, int v, int from, int to)
        {
            double penalty = 0;
            for (int i = 0; i < _intAttributes.Count; i++)
            {
                string name = _intAttributes[i];
                var a = _intOnLoop[i] ? _wedgeLoops[from].Attributes : _vertices[u].Attributes;
                var b = _intOnLoop[i] ? _wedgeLoops[to].Attributes : _vertices[v].Attributes;
                a.TryGetValue(name, out var va);
                b.TryGetValue(name, out var vb);
                bool same = va is GeometryData.IntAttributeValue ia && vb is GeometryData.IntAttributeValue ib && ia.Data.AsSpan().SequenceEqual(ib.Data);
                if (!same) penalty += _intWeights[i];
            }
            return penalty;
        }

        private int NextStamp()
        {
            if (++_markStamp == int.MaxValue)
            {
                Array.Clear(_mark, 0, _mark.Length);
                _markStamp = 1;
            }
            return _markStamp;
        }

        /// <summary>
        /// Works out what a vertex may do from its current triangles: slide anywhere (manifold), slide along
        /// its border or its seam, or stay put. Rerun after every collapse nearby, since collapses change what
        /// a vertex touches.
        /// </summary>
        private void Classify(int v)
        {
            _kinds[v] = ComputeKind(v);
        }

        private Kind ComputeKind(int v)
        {
            if (_dead[v] || _pinned[v]) return Kind.Locked;

            _edgeNeighbour.Clear();
            _edgeCount.Clear();
            _edgeFirst.Clear();
            _edgeSecond.Clear();
            _fan.Clear();
            _fanParent.Clear();
            _wedgeSet.Clear();

            int stamp = NextStamp();
            foreach (int t in _adjacency[v])
            {
                if (!_alive[t]) continue;
                int local = _fan.Count;
                _fan.Add(t);
                _fanParent.Add(local);
                _wedgeSet.Add(_cornerWedges[CornerOf(t, v)]);

                for (int k = 0; k < 3; k++)
                {
                    int n = _triangles[t * 3 + k];
                    if (n == v) continue;
                    if (_mark[n] != stamp)
                    {
                        _mark[n] = stamp;
                        _slot[n] = _edgeNeighbour.Count;
                        _edgeNeighbour.Add(n);
                        _edgeCount.Add(0);
                        _edgeFirst.Add(local);
                        _edgeSecond.Add(-1);
                    }
                    int s = _slot[n];
                    if (_edgeCount[s] == 1) _edgeSecond[s] = local;
                    _edgeCount[s]++;
                }
            }

            if (_fan.Count == 0) return Kind.Locked;

            int border = 0, seam = 0;
            for (int s = 0; s < _edgeNeighbour.Count; s++)
            {
                int count = _edgeCount[s];
                if (count == 1)
                {
                    border++;
                }
                else if (count == 2)
                {
                    if (IsSeamBetween(_fan[_edgeFirst[s]], _fan[_edgeSecond[s]], v, _edgeNeighbour[s])) seam++;
                    Union(_edgeFirst[s], _edgeSecond[s]);
                }
                else
                {
                    return Kind.Locked;
                }
            }

            // Two fans meeting only at this point would be pulled together by any collapse
            int root = Find(0);
            for (int i = 1; i < _fan.Count; i++)
                if (Find(i) != root) return Kind.Locked;

            int wedges = _wedgeSet.Count;
            if (_options.LockBorders && border > 0) return Kind.Locked;
            if (border == 0 && seam == 0 && wedges == 1) return Kind.Manifold;
            if (border == 2 && seam == 0 && wedges == 1) return Kind.Border;
            if (seam == 2 && border == 0 && wedges <= 2) return Kind.Seam;
            return Kind.Locked;
        }

        private int Find(int i)
        {
            while (_fanParent[i] != i)
            {
                _fanParent[i] = _fanParent[_fanParent[i]];
                i = _fanParent[i];
            }
            return i;
        }

        private void Union(int a, int b)
        {
            int ra = Find(a), rb = Find(b);
            if (ra != rb) _fanParent[ra] = rb;
        }

        private bool Contains(int t, int v) => _triangles[t * 3] == v || _triangles[t * 3 + 1] == v || _triangles[t * 3 + 2] == v;

        public SimplifyResult Run()
        {
            int before = _alive.Length;
            int aliveCount = before;
            int target = _options.TargetTriangleCount >= 0
                ? Math.Min(_options.TargetTriangleCount, before)
                : (int)Math.Floor(before * (double)Math.Clamp(_options.TargetRatio, 0f, 1f));
            double worst = 0;

            var heap = new CandidateHeap();
            for (int v = 0; v < _vertices.Length; v++)
            {
                if (TryFindCandidate(v, out var candidate))
                    heap.Push(candidate, v, _versions[v]);
            }

            bool collapsed = false;
            var touched = new List<int>();
            while (aliveCount > target && heap.Count > 0)
            {
                var (cost, length, u, version) = heap.Pop();
                if (_dead[u] || version != _versions[u]) continue;

                // Costs were taken optimistically, so the topology and flip checks run only now, on the
                // collapse about to happen, falling back to the next cheapest when one fails
                if (!TryPickValid(u, cost, out var candidate, out var requeue))
                {
                    if (requeue.HasValue) heap.Push(requeue.Value, u, _versions[u]);
                    continue;
                }

                aliveCount -= Collapse(u, candidate);
                worst = Math.Max(worst, candidate.PositionError);
                collapsed = true;

                int v = candidate.Target;
                touched.Clear();
                touched.Add(v);
                CollectNeighbours(v, touched);
                foreach (int n in touched) Classify(n);
                foreach (int n in touched)
                {
                    _versions[n]++;
                    if (TryFindCandidate(n, out var next))
                        heap.Push(next, n, _versions[n]);
                }
            }

            if (!collapsed) return new SimplifyResult(_mesh.Faces.Count, _mesh.Faces.Count, 0f);

            Rebuild();
            return new SimplifyResult(before, aliveCount, (float)Math.Sqrt(worst) * _extent);
        }

        private void CollectNeighbours(int v, List<int> into)
        {
            int stamp = NextStamp();
            _mark[v] = stamp;
            foreach (int t in _adjacency[v])
            {
                if (!_alive[t]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int n = _triangles[t * 3 + k];
                    if (_mark[n] == stamp) continue;
                    _mark[n] = stamp;
                    into.Add(n);
                }
            }
        }

        /// <summary>The cheapest collapse for u by cost alone, which is what the heap is ordered by.</summary>
        private bool TryFindCandidate(int u, out Candidate best)
        {
            best = default;
            best.Cost = double.PositiveInfinity;
            if (_dead[u] || _kinds[u] == Kind.Locked) return false;

            _neighbours.Clear();
            CollectNeighbours(u, _neighbours);

            bool found = false;
            foreach (int v in _neighbours)
            {
                if (TryEvaluate(u, v, out var candidate) && (!found || candidate.IsBetterThan(best)))
                {
                    best = candidate;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>
        /// The cheapest collapse for u that passes the topology and flip checks, as long as it is no dearer
        /// than what u was queued at. Otherwise <paramref name="requeue"/> is the candidate to queue it again
        /// with, or null when nothing is valid.
        /// </summary>
        private bool TryPickValid(int u, double queuedCost, out Candidate chosen, out Candidate? requeue)
        {
            chosen = default;
            requeue = null;
            if (_dead[u] || _kinds[u] == Kind.Locked) return false;

            _neighbours.Clear();
            CollectNeighbours(u, _neighbours);
            _candidates.Clear();
            foreach (int v in _neighbours)
            {
                if (TryEvaluate(u, v, out var candidate)) _candidates.Add(candidate);
            }
            _candidates.Sort((a, b) => a.IsBetterThan(b) ? -1 : b.IsBetterThan(a) ? 1 : 0);

            foreach (var candidate in _candidates)
            {
                if (candidate.Cost > queuedCost + 1e-12)
                {
                    requeue = candidate;
                    return false;
                }
                if (!LinkConditionHolds(u, candidate)) continue;
                if (FlipsAnyTriangle(u, candidate.Target)) continue;

                chosen = candidate;
                return true;
            }
            return false;
        }

        private bool TryEvaluate(int u, int v, out Candidate candidate)
        {
            candidate = default;
            candidate.Target = v;

            int shared0 = -1, shared1 = -1, sharedCount = 0;
            foreach (int t in _adjacency[u])
            {
                if (!_alive[t] || !Contains(t, v)) continue;
                if (sharedCount == 0) shared0 = t;
                else if (sharedCount == 1) shared1 = t;
                sharedCount++;
            }
            if (sharedCount == 0 || sharedCount > 2) return false;

            bool border = sharedCount == 1;
            bool seam = sharedCount == 2 && IsSeamBetween(shared0, shared1, u, v);
            switch (_kinds[u])
            {
                case Kind.Manifold when border || seam: return false;
                case Kind.Border when !border: return false;
                case Kind.Seam when !seam: return false;
            }

            // v must be left with some triangle, from either side, or a lone triangle would vanish entirely
            int valence = 0;
            foreach (int t in _adjacency[v]) if (_alive[t]) valence++;
            int remaining = valence - 2 * sharedCount;
            foreach (int t in _adjacency[u]) if (_alive[t]) remaining++;
            if (remaining <= 0) return false;
            candidate.TargetValence = valence;

            // Each of u's seam classes carries over to the class v has in the same triangle, taking the
            // values of v's corner there
            candidate.FromA = candidate.FromB = -1;
            if (!AddMapping(ref candidate, shared0, u, v)) return false;
            if (sharedCount == 2 && !AddMapping(ref candidate, shared1, u, v)) return false;
            foreach (int t in _adjacency[u])
            {
                if (!_alive[t]) continue;
                int w = _cornerWedges[CornerOf(t, u)];
                if (w != candidate.FromA && w != candidate.FromB) return false;
            }

            candidate.Shared0 = shared0;
            candidate.Shared1 = shared1;
            candidate.SharedCount = sharedCount;

            // Ranked by the moving vertex's own planes, so a heavy target cannot dilute how far u travels
            candidate.PositionError = _quadrics[u].Error(_positions[v]);
            if (candidate.PositionError > _limit) return false;

            double attributes = AttributeError(candidate.FromA, candidate.ToA, _positions[v]) + IntPenalty(u, v, candidate.FromA, candidate.ToA);
            if (candidate.FromB >= 0)
                attributes += AttributeError(candidate.FromB, candidate.ToB, _positions[v]) + IntPenalty(u, v, candidate.FromB, candidate.ToB);

            candidate.Cost = candidate.PositionError + attributes;
            candidate.EdgeLength = Float3.LengthSquared(_positions[u] - _positions[v]);
            return true;
        }

        private bool AddMapping(ref Candidate candidate, int t, int u, int v)
        {
            int from = _cornerWedges[CornerOf(t, u)];
            int vCorner = CornerOf(t, v);
            int to = _cornerWedges[vCorner];

            if (candidate.FromA == from) return candidate.ToA == to;
            if (candidate.FromB == from) return candidate.ToB == to;
            if (candidate.FromA < 0)
            {
                candidate.FromA = from;
                candidate.ToA = to;
                candidate.LoopA = _cornerLoops[vCorner];
                return true;
            }
            if (candidate.FromB < 0)
            {
                candidate.FromB = from;
                candidate.ToB = to;
                candidate.LoopB = _cornerLoops[vCorner];
                return true;
            }
            return false;
        }

        /// <summary>
        /// The neighbours u and v share must be exactly the far corners of the triangles on edge uv, and
        /// those far corners must not already be joined around both u and v, otherwise the collapse would
        /// pinch the surface into a fold or flatten a closed part into two back to back triangles.
        /// </summary>
        private bool LinkConditionHolds(int u, in Candidate candidate)
        {
            int v = candidate.Target;
            int opposite0 = ThirdVertex(candidate.Shared0, u, v);
            int opposite1 = candidate.SharedCount == 2 ? ThirdVertex(candidate.Shared1, u, v) : -1;

            int stamp = NextStamp();
            foreach (int t in _adjacency[u])
            {
                if (!_alive[t]) continue;
                for (int k = 0; k < 3; k++) _mark[_triangles[t * 3 + k]] = stamp;
            }

            foreach (int t in _adjacency[v])
            {
                if (!_alive[t]) continue;
                for (int k = 0; k < 3; k++)
                {
                    int n = _triangles[t * 3 + k];
                    if (n == u || n == v || _mark[n] != stamp) continue;
                    if (n != opposite0 && n != opposite1) return false;
                }
            }

            if (opposite1 >= 0 && HasTriangleWith(u, opposite0, opposite1, v) && HasTriangleWith(v, opposite0, opposite1, u))
                return false;
            return true;
        }

        /// <summary>Whether some live triangle of <paramref name="owner"/> holds both a and b but not <paramref name="excluded"/>.</summary>
        private bool HasTriangleWith(int owner, int a, int b, int excluded)
        {
            foreach (int t in _adjacency[owner])
            {
                if (_alive[t] && Contains(t, a) && Contains(t, b) && !Contains(t, excluded)) return true;
            }
            return false;
        }

        private int ThirdVertex(int t, int a, int b)
        {
            for (int k = 0; k < 3; k++)
            {
                int n = _triangles[t * 3 + k];
                if (n != a && n != b) return n;
            }
            return -1;
        }

        /// <summary>Whether moving u onto v turns any of u's other triangles over or squashes it flat, judged relative to its own size.</summary>
        private bool FlipsAnyTriangle(int u, int v)
        {
            foreach (int t in _adjacency[u])
            {
                if (!_alive[t] || Contains(t, v)) continue;

                Float3 before = FaceCross(t);
                float beforeSquared = Float3.LengthSquared(before);

                // A triangle that was already flat has no facing to lose
                if (beforeSquared <= 0) continue;

                int a = _triangles[t * 3], b = _triangles[t * 3 + 1], c = _triangles[t * 3 + 2];
                Float3 pa = a == u ? _positions[v] : _positions[a];
                Float3 pb = b == u ? _positions[v] : _positions[b];
                Float3 pc = c == u ? _positions[v] : _positions[c];
                Float3 after = Float3.Cross(pb - pa, pc - pa);

                if (Float3.LengthSquared(after) <= 1e-12f * beforeSquared) return true;
                if (Float3.Dot(before, after) <= 0) return true;
            }
            return false;
        }

        private int Collapse(int u, in Candidate candidate)
        {
            int v = candidate.Target;
            int removed = 0;
            foreach (int t in _adjacency[u])
            {
                if (!_alive[t]) continue;
                if (Contains(t, v))
                {
                    _alive[t] = false;
                    removed++;
                    continue;
                }

                int corner = CornerOf(t, u);
                _triangles[corner] = v;
                bool first = _cornerWedges[corner] == candidate.FromA;
                _cornerWedges[corner] = first ? candidate.ToA : candidate.ToB;
                _cornerLoops[corner] = first ? candidate.LoopA! : candidate.LoopB!;
                _adjacency[v].Add(t);
            }

            _quadrics[v].Add(_quadrics[u]);
            MergeWedge(candidate.FromA, candidate.ToA);
            if (candidate.FromB >= 0) MergeWedge(candidate.FromB, candidate.ToB);

            _dead[u] = true;
            _adjacency[u].Clear();
            _adjacency[v].RemoveAll(t => !_alive[t]);
            return removed;
        }

        private void MergeWedge(int from, int to)
        {
            _wedgeQuadrics[to].Add(_wedgeQuadrics[from]);
            int stride = _channels.Length * 4;
            for (int i = 0; i < stride; i++)
                _wedgeGradients[to * stride + i] += _wedgeGradients[from * stride + i];
        }

        /// <summary>Writes the surviving triangles back into the mesh, keeping every surviving element's attributes.</summary>
        private void Rebuild()
        {
            var vertexIndex = new Dictionary<GeometryData.Vertex, int>(_vertices.Length);
            for (int i = 0; i < _vertices.Length; i++) vertexIndex[_vertices[i]] = i;

            Dictionary<long, Dictionary<string, GeometryData.AttributeValue>>? edgeAttributes = null;
            if (_mesh.EdgeAttributes.Count > 0)
            {
                edgeAttributes = new Dictionary<long, Dictionary<string, GeometryData.AttributeValue>>(PairComparer.Instance);
                foreach (var e in _mesh.Edges)
                    edgeAttributes[PairKey(vertexIndex[e.Vert1], vertexIndex[e.Vert2])] = e.Attributes;
            }

            var wireEdges = new List<GeometryData.Edge>();
            foreach (var e in _mesh.Edges)
                if (e.Loop == null) wireEdges.Add(e);

            // Pinned vertices never collapse, so faceless and wire vertices are all still here
            var keep = new bool[_vertices.Length];
            for (int v = 0; v < _vertices.Length; v++) keep[v] = _pinned[v];
            for (int t = 0; t < _alive.Length; t++)
            {
                if (!_alive[t]) continue;
                for (int k = 0; k < 3; k++) keep[_triangles[t * 3 + k]] = true;
            }

            _mesh.Faces.Clear();
            _mesh.Edges.Clear();
            _mesh.Loops.Clear();
            _mesh.Vertices.Clear();
            for (int v = 0; v < _vertices.Length; v++)
            {
                _vertices[v].Edge = null;
                if (keep[v]) _mesh.Vertices.Add(_vertices[v]);
            }

            // The first corner to use a source loop takes its dictionary, the rest take copies
            var takenLoops = new HashSet<GeometryData.Loop>();
            var takenFaces = new bool[_faces.Length];
            var corners = new GeometryData.Vertex[3];
            for (int t = 0; t < _alive.Length; t++)
            {
                if (!_alive[t]) continue;

                for (int k = 0; k < 3; k++) corners[k] = _vertices[_triangles[t * 3 + k]];
                var face = _mesh.AddFace(corners, fillAttributes: false);
                if (face == null) continue;

                int f = _triangleFace[t];
                var oldFace = _faces[f];
                face.Id = oldFace.Id;
                face.Attributes = takenFaces[f] ? CopyAttributes(oldFace.Attributes) : oldFace.Attributes;
                takenFaces[f] = true;

                for (int k = 0; k < 3; k++)
                {
                    var loop = face.GetLoop(corners[k])!;
                    var source = _cornerLoops[t * 3 + k];
                    loop.Attributes = takenLoops.Add(source) ? source.Attributes : CopyAttributes(source.Attributes);
                }
            }

            foreach (var e in wireEdges)
            {
                var edge = _mesh.AddEdge(e.Vert1, e.Vert2);
                edge.Id = e.Id;
                edge.Attributes = e.Attributes;
            }

            if (edgeAttributes != null)
            {
                foreach (var e in _mesh.Edges)
                {
                    if (e.Loop != null && edgeAttributes.TryGetValue(PairKey(vertexIndex[e.Vert1], vertexIndex[e.Vert2]), out var attributes))
                        e.Attributes = CopyAttributes(attributes);
                }
            }
        }

        private static Dictionary<string, GeometryData.AttributeValue> CopyAttributes(Dictionary<string, GeometryData.AttributeValue> source)
        {
            var copy = new Dictionary<string, GeometryData.AttributeValue>(source.Count);
            foreach (var pair in source) copy[pair.Key] = GeometryData.AttributeValue.Copy(pair.Value);
            return copy;
        }
    }
}
