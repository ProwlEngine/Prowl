using System.Globalization;
using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.Formats.Wavefront;

/// <summary>
/// Wavefront OBJ importer (with companion MTL parsing). Produces one
/// <see cref="IntermediateMesh"/> per (object, material) pair so the
/// "node -&gt; mesh -&gt; material" mapping holds.
/// </summary>
/// <remarks>
/// Supports:
/// <list type="bullet">
///   <item>positions (<c>v</c>), texture coordinates (<c>vt</c>), normals (<c>vn</c>)</item>
///   <item>faces (<c>f</c>) with <c>v</c>, <c>v/vt</c>, <c>v//vn</c>, and <c>v/vt/vn</c> tokens; positive and negative 1-based indices</item>
///   <item>lines (<c>l</c>) and points (<c>p</c>)</item>
///   <item>objects (<c>o</c>) and groups (<c>g</c>) - both start a new mesh group</item>
///   <item>material binding (<c>usemtl</c>) and MTL library refs (<c>mtllib</c>)</item>
/// </list>
/// Free-form geometry, vertex parameters, smoothing groups, and other rarely-used directives are
/// parsed-then-ignored so the file as a whole still loads.
/// </remarks>
internal sealed class ObjFormat : IModelFormat
{
    public string Token => "obj";
    public bool CanRead(string formatToken) => formatToken == "obj";

    public IntermediateScene Read(Stream stream, ImportContext context)
    {
        string text;
        using (var reader = new StreamReader(stream, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false))
            text = reader.ReadToEnd();

        var state = new ParserState(context);

        foreach (var rawLine in EnumerateLogicalLines(text))
        {
            ReadOnlySpan<char> line = StripComment(rawLine);
            if (line.IsEmpty) continue;

            var tok = new ObjTokenizer(line);
            var keyword = tok.NextToken();
            if (keyword.IsEmpty) continue;

            try { ApplyDirective(keyword, ref tok, state); }
            catch (FormatException ex)
            {
                context.Log.Warning($"OBJ parse error on '{rawLine.Trim()}': {ex.Message}", "ObjFormat");
            }
        }

        return state.Build();
    }

    private static ReadOnlySpan<char> StripComment(string line)
    {
        var span = line.AsSpan();
        int hash = span.IndexOf('#');
        if (hash >= 0) span = span[..hash];
        return span.TrimEnd();
    }

    /// <summary>
    /// Iterates physical lines, joining lines terminated with a trailing backslash into one
    /// logical line.
    /// </summary>
    private static IEnumerable<string> EnumerateLogicalLines(string text)
    {
        using var reader = new StringReader(text);
        System.Text.StringBuilder? pending = null;
        string? l;
        while ((l = reader.ReadLine()) is not null)
        {
            if (l.EndsWith("\\", StringComparison.Ordinal))
            {
                pending ??= new System.Text.StringBuilder();
                pending.Append(l, 0, l.Length - 1);
                continue;
            }
            if (pending is not null)
            {
                pending.Append(l);
                yield return pending.ToString();
                pending = null;
            }
            else
            {
                yield return l;
            }
        }
        if (pending is not null) yield return pending.ToString();
    }

    private static void ApplyDirective(ReadOnlySpan<char> keyword, ref ObjTokenizer tok, ParserState state)
    {
        if (keyword.SequenceEqual("v"))
        {
            float x = tok.NextFloat();
            float y = tok.NextFloat();
            float z = tok.NextFloat();
            state.Positions.Add(new Float3(x, y, z));

            // Optional trailing r g b (vertex color extension produced by Blender, MeshLab, etc.)
            // or a single w (homogeneous-coord, rare). We treat 3 trailing floats as RGB.
            if (!tok.AtEnd)
            {
                float a = tok.NextFloatOr(float.NaN);
                if (!tok.AtEnd)
                {
                    float b = tok.NextFloat();
                    float c = tok.NextFloat();
                    state.RecordVertexColor(new Color(a, b, c, 1f));
                }
                // else: trailing 'w' - safely ignored.
            }
            return;
        }
        if (keyword.SequenceEqual("vt"))
        {
            float u = tok.NextFloat();
            float v = tok.NextFloatOr(0f);
            state.UVs.Add(new Float2(u, v));
            return;
        }
        if (keyword.SequenceEqual("vn"))
        {
            float x = tok.NextFloat();
            float y = tok.NextFloat();
            float z = tok.NextFloat();
            state.Normals.Add(new Float3(x, y, z));
            return;
        }
        if (keyword.SequenceEqual("f"))
        {
            state.AddFace(ref tok, PrimitiveKind.Triangle);
            return;
        }
        if (keyword.SequenceEqual("l"))
        {
            state.AddFace(ref tok, PrimitiveKind.Line);
            return;
        }
        if (keyword.SequenceEqual("p"))
        {
            state.AddFace(ref tok, PrimitiveKind.Point);
            return;
        }
        if (keyword.SequenceEqual("o"))
        {
            state.SetObject(tok.Rest().ToString());
            return;
        }
        if (keyword.SequenceEqual("g"))
        {
            // OBJ groups can list multiple names; we use the first as the node name.
            var groupName = tok.NextToken();
            state.SetGroup(groupName.IsEmpty ? "default" : groupName.ToString());
            return;
        }
        if (keyword.SequenceEqual("usemtl"))
        {
            state.SetMaterial(tok.Rest().ToString());
            return;
        }
        if (keyword.SequenceEqual("mtllib"))
        {
            // Multiple libraries can be listed on one line.
            while (!tok.AtEnd)
                state.LoadMtlLibrary(tok.NextToken().ToString());
            return;
        }
        if (keyword.SequenceEqual("s") ||
            keyword.SequenceEqual("vp") ||
            keyword.SequenceEqual("cstype") || keyword.SequenceEqual("deg") ||
            keyword.SequenceEqual("bmat") || keyword.SequenceEqual("step") ||
            keyword.SequenceEqual("curv") || keyword.SequenceEqual("curv2") ||
            keyword.SequenceEqual("surf") || keyword.SequenceEqual("parm") ||
            keyword.SequenceEqual("trim") || keyword.SequenceEqual("hole") ||
            keyword.SequenceEqual("scrv") || keyword.SequenceEqual("sp") ||
            keyword.SequenceEqual("end") || keyword.SequenceEqual("con") ||
            keyword.SequenceEqual("mg") || keyword.SequenceEqual("bevel") ||
            keyword.SequenceEqual("c_interp") || keyword.SequenceEqual("d_interp") ||
            keyword.SequenceEqual("lod") || keyword.SequenceEqual("maplib") ||
            keyword.SequenceEqual("usemap") || keyword.SequenceEqual("shadow_obj") ||
            keyword.SequenceEqual("trace_obj") || keyword.SequenceEqual("ctech") ||
            keyword.SequenceEqual("stech"))
        {
            // Recognized-but-unsupported directive; silently consume.
            return;
        }
    }

    private sealed class ParserState
    {
        public List<Float3> Positions { get; } = new();
        public List<Float2> UVs { get; } = new();
        public List<Float3> Normals { get; } = new();
        /// <summary>Per-position vertex color (when the OBJ used the v r g b extension).
        /// <c>null</c> until the first colored vertex shows up.</summary>
        private List<Color>? _vertexColors;

        private readonly ImportContext _ctx;
        private readonly IntermediateScene _scene = new();
        private readonly MtlReader _mtl;

        // (objectName, materialName) -> IntermediateMesh
        private readonly Dictionary<(string Obj, string Mat), int> _meshByKey = new();
        // Per-mesh vertex dedup: (posIdx, uvIdx, normalIdx) -> mesh-local vertex index
        private readonly Dictionary<int, Dictionary<(int p, int u, int n), int>> _meshVertexDedup = new();

        private string _objectName = "default";
        private string _materialName = "default";

        public ParserState(ImportContext ctx)
        {
            _ctx = ctx;
            _scene.Format = ctx.Format;
            _scene.SourceCoordinateSystem = CoordinateSystem.RightHandedYUp;
            _scene.SourceUnitToMeters = 1f;

            // Pre-seed a "default" material so faces that come before any usemtl still have somewhere to go.
            _scene.Materials.Add(new IntermediateMaterial { Name = "default" });
            _mtl = new MtlReader(_scene, ctx, ctx.SourcePath);
            _mtl.MaterialIndexByName["default"] = 0;
        }

        public void RecordVertexColor(Color color)
        {
            // Backfill any earlier vertices that didn't have colors.
            if (_vertexColors is null)
            {
                _vertexColors = new List<Color>(Positions.Count);
                for (int i = 0; i + 1 < Positions.Count; i++)
                    _vertexColors.Add(new Color(1f, 1f, 1f, 1f));
            }
            _vertexColors.Add(color);
        }

        public void SetObject(string name)
        {
            _objectName = string.IsNullOrWhiteSpace(name) ? "default" : name.Trim();
        }

        public void SetGroup(string name)
        {
            // We treat group like object - both partition meshes.
            SetObject(name);
        }

        public void SetMaterial(string name)
        {
            _materialName = string.IsNullOrWhiteSpace(name) ? "default" : name.Trim();
        }

        public void LoadMtlLibrary(string fileRef)
        {
            if (_ctx.SourcePath is null)
            {
                _ctx.Log.Warning(
                    $"OBJ references mtllib '{fileRef}' but no source path is set; materials may be missing.",
                    "ObjFormat");
                return;
            }
            string? resolved = _ctx.Resolver.Resolve(_ctx.SourcePath, fileRef);
            if (resolved is null)
            {
                _ctx.Log.Warning($"Could not resolve mtllib '{fileRef}'.", "ObjFormat");
                return;
            }
            try
            {
                using var stream = _ctx.Resolver.OpenRead(resolved);
                using var reader = new StreamReader(stream);
                _mtl.Read(reader.ReadToEnd());
            }
            catch (Exception ex)
            {
                _ctx.Log.Warning($"Failed to read mtllib '{resolved}': {ex.Message}", "ObjFormat");
            }
        }

        public void AddFace(ref ObjTokenizer tok, PrimitiveKind kind)
        {
            int materialIndex = ResolveMaterial(_materialName);
            int meshIdx = GetOrCreateMesh(_objectName, _materialName, materialIndex);

            var mesh = _scene.Meshes[meshIdx];
            var dedup = _meshVertexDedup[meshIdx];

            var faceIndices = new List<int>(4);
            while (!tok.AtEnd)
            {
                var faceTok = tok.NextToken();
                if (faceTok.IsEmpty) break;
                if (!TryParseFaceVertex(faceTok, out int posI, out int uvI, out int normI))
                {
                    _ctx.Log.Warning($"Malformed face vertex '{faceTok}'; skipping face.", "ObjFormat");
                    return;
                }

                if (!dedup.TryGetValue((posI, uvI, normI), out int localIndex))
                {
                    localIndex = mesh.Positions.Count;
                    if ((uint)posI >= (uint)Positions.Count)
                    {
                        _ctx.Log.Warning($"Face references out-of-range position index {posI}.", "ObjFormat");
                        return;
                    }
                    mesh.Positions.Add(Positions[posI]);

                    if (_vertexColors is not null && posI < _vertexColors.Count)
                    {
                        mesh.Colors0 ??= EnsureColorListFor(mesh);
                        mesh.Colors0.Add(_vertexColors[posI]);
                    }
                    else if (mesh.Colors0 is not null)
                    {
                        mesh.Colors0.Add(new Color(1f, 1f, 1f, 1f));
                    }

                    if (normI >= 0 && normI < Normals.Count)
                    {
                        mesh.Normals ??= EnsureNormalListFor(mesh);
                        mesh.Normals.Add(Normals[normI]);
                    }
                    else if (mesh.Normals is not null)
                    {
                        // Keep the per-vertex stream length matched even when this vertex has no normal.
                        mesh.Normals.Add(new Float3(0f, 1f, 0f));
                    }

                    if (uvI >= 0 && uvI < UVs.Count)
                    {
                        mesh.UVs[0] ??= EnsureUVListFor(mesh);
                        mesh.UVs[0]!.Add(UVs[uvI]);
                    }
                    else if (mesh.UVs[0] is not null)
                    {
                        mesh.UVs[0]!.Add(Float2.Zero);
                    }

                    dedup[(posI, uvI, normI)] = localIndex;
                }
                faceIndices.Add(localIndex);
            }

            if (faceIndices.Count == 0) return;

            switch (kind)
            {
                case PrimitiveKind.Point:
                    mesh.PrimitiveKinds |= PrimitiveKind.Point;
                    foreach (int v in faceIndices)
                        mesh.Faces.Add(new IntermediateFace(new[] { v }));
                    break;

                case PrimitiveKind.Line:
                    mesh.PrimitiveKinds |= PrimitiveKind.Line;
                    for (int i = 0; i + 1 < faceIndices.Count; i++)
                        mesh.Faces.Add(new IntermediateFace(new[] { faceIndices[i], faceIndices[i + 1] }));
                    break;

                default:
                    if (faceIndices.Count == 3)
                    {
                        mesh.PrimitiveKinds |= PrimitiveKind.Triangle;
                        mesh.Faces.Add(new IntermediateFace(faceIndices.ToArray()));
                    }
                    else if (faceIndices.Count < 3)
                    {
                        _ctx.Log.Warning($"Face with {faceIndices.Count} vertices is degenerate; ignored.", "ObjFormat");
                    }
                    else
                    {
                        // Polygon - mark and let TriangulateStep break it up.
                        mesh.PrimitiveKinds |= PrimitiveKind.Polygon;
                        mesh.Faces.Add(new IntermediateFace(faceIndices.ToArray()));
                    }
                    break;
            }
        }

        private List<Color> EnsureColorListFor(IntermediateMesh mesh)
        {
            var list = new List<Color>(mesh.Positions.Count);
            for (int i = 0; i + 1 < mesh.Positions.Count; i++)
                list.Add(new Color(1f, 1f, 1f, 1f));
            return list;
        }

        private List<Float3> EnsureNormalListFor(IntermediateMesh mesh)
        {
            // Backfill with placeholders for any vertices that were added before normals appeared.
            var list = new List<Float3>(mesh.Positions.Count);
            for (int i = 0; i + 1 < mesh.Positions.Count; i++) // -1 because Positions just gained the current vertex
                list.Add(new Float3(0f, 1f, 0f));
            return list;
        }

        private List<Float2> EnsureUVListFor(IntermediateMesh mesh)
        {
            var list = new List<Float2>(mesh.Positions.Count);
            for (int i = 0; i + 1 < mesh.Positions.Count; i++)
                list.Add(Float2.Zero);
            return list;
        }

        private bool TryParseFaceVertex(ReadOnlySpan<char> token, out int pos, out int uv, out int normal)
        {
            pos = uv = normal = -1;

            // Split on '/' manually to handle empty middle slot (v//n).
            int firstSlash = token.IndexOf('/');
            if (firstSlash < 0)
            {
                return TryResolveIndex(token, Positions.Count, out pos);
            }

            if (!TryResolveIndex(token[..firstSlash], Positions.Count, out pos))
                return false;

            ReadOnlySpan<char> rest = token[(firstSlash + 1)..];
            int secondSlash = rest.IndexOf('/');
            if (secondSlash < 0)
            {
                // v/vt form
                return TryResolveIndex(rest, UVs.Count, out uv);
            }

            var uvSpan = rest[..secondSlash];
            if (!uvSpan.IsEmpty && !TryResolveIndex(uvSpan, UVs.Count, out uv))
                return false;

            var normSpan = rest[(secondSlash + 1)..];
            if (!normSpan.IsEmpty && !TryResolveIndex(normSpan, Normals.Count, out normal))
                return false;
            return true;
        }

        private static bool TryResolveIndex(ReadOnlySpan<char> span, int arrayLength, out int zeroBasedIndex)
        {
            zeroBasedIndex = -1;
            if (span.IsEmpty) return true; // Empty slot is legal between slashes.
            if (!int.TryParse(span, NumberStyles.Integer, CultureInfo.InvariantCulture, out int oneBased))
                return false;
            if (oneBased > 0)
                zeroBasedIndex = oneBased - 1;
            else if (oneBased < 0)
                zeroBasedIndex = arrayLength + oneBased; // -1 -> last element, etc.
            else
                return false; // Zero is invalid per OBJ spec.
            return true;
        }

        private int ResolveMaterial(string name)
        {
            if (_mtl.MaterialIndexByName.TryGetValue(name, out int idx))
                return idx;

            // Material referenced but not (yet?) defined - create a placeholder so the index is stable.
            idx = _scene.Materials.Count;
            _scene.Materials.Add(new IntermediateMaterial { Name = name });
            _mtl.MaterialIndexByName[name] = idx;
            return idx;
        }

        private int GetOrCreateMesh(string obj, string mat, int materialIndex)
        {
            var key = (obj, mat);
            if (_meshByKey.TryGetValue(key, out int idx)) return idx;

            var mesh = new IntermediateMesh
            {
                Name = $"{obj}/{mat}",
                MaterialIndex = materialIndex,
            };
            idx = _scene.Meshes.Count;
            _scene.Meshes.Add(mesh);
            _meshByKey[key] = idx;
            _meshVertexDedup[idx] = new Dictionary<(int, int, int), int>();
            return idx;
        }

        public IntermediateScene Build()
        {
            // Build the node tree: one node per distinct object, with mesh-bearing child nodes
            // per material when multiple materials share an object.
            var root = new IntermediateNode { Name = "<RootNode>" };

            var objectsInOrder = new List<string>();
            var seenObjects = new HashSet<string>(StringComparer.Ordinal);
            foreach (var key in _meshByKey.Keys)
            {
                if (seenObjects.Add(key.Obj))
                    objectsInOrder.Add(key.Obj);
            }

            foreach (string obj in objectsInOrder)
            {
                var objectNode = new IntermediateNode { Name = obj, Parent = root };
                root.Children.Add(objectNode);

                var meshesForObj = new List<int>();
                foreach (var kvp in _meshByKey)
                    if (kvp.Key.Obj == obj)
                        meshesForObj.Add(kvp.Value);
                if (meshesForObj.Count == 0)
                    continue;

                if (meshesForObj.Count == 1)
                {
                    objectNode.MeshIndex = meshesForObj[0];
                }
                else
                {
                    foreach (int meshIdx in meshesForObj)
                    {
                        var sub = new IntermediateNode
                        {
                            Name = $"{obj}_{_scene.Meshes[meshIdx].MaterialIndex}",
                            Parent = objectNode,
                            MeshIndex = meshIdx,
                        };
                        objectNode.Children.Add(sub);
                    }
                }
            }

            _scene.Root = root;
            _scene.Nodes.Clear();
            AppendDepthFirst(root, _scene.Nodes);
            return _scene;
        }

        private static void AppendDepthFirst(IntermediateNode node, List<IntermediateNode> list)
        {
            list.Add(node);
            foreach (var c in node.Children)
                AppendDepthFirst(c, list);
        }
    }
}
