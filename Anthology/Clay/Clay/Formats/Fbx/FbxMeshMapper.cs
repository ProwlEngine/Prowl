using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.Formats.Fbx;

/// <summary>
/// Maps an FBX <c>Geometry::Mesh</c> object into one or more <see cref="IntermediateMesh"/>es,
/// one per material slot.
/// </summary>
/// <remarks>
/// FBX stores per-polygon-vertex layered data (positions are per-vertex; normals/UVs/colors may
/// be per-vertex, per-polygon-vertex, per-polygon, or all-same; either Direct or IndexToDirect).
/// We unpack everything to per-polygon-vertex and let <see cref="PostProcess.JoinIdenticalVerticesStep"/>
/// re-dedup it.
/// </remarks>
internal static class FbxMeshMapper
{
    public sealed class MeshMapping
    {
        /// <summary>Geometry id -&gt; range of emitted IntermediateMeshes plus the
        /// FBX-vertex -&gt; (mesh, vertex) expansion map.</summary>
        public Dictionary<long, GeometryMapping> GeometryToMeshes { get; } = new();

        /// <summary>For each emitted IntermediateMesh, the FBX material slot index (0-based) it represents.</summary>
        public List<int> MaterialSlotPerMesh { get; } = new();
    }

    /// <summary>
    /// Records the per-FBX-vertex expansion produced by <see cref="MapOne"/>. Each FBX vertex
    /// (a control point) expands to N pairs of (intermediateMeshIndex, intermediateVertexIndex)
    /// because per-polygon-vertex unpack duplicates the position once per face usage. Skinning
    /// later needs this to scatter cluster weights from per-FBX-vertex form onto the produced
    /// intermediate vertices.
    /// </summary>
    public sealed class GeometryMapping
    {
        public required int FirstMeshIndex;
        public required int MeshCount;
        /// <summary>Prefix-sum array of length <c>fbxVertexCount + 1</c>. The expansions for FBX
        /// vertex <c>v</c> live in <c>[Starts[v] .. Starts[v+1])</c> of the two flat arrays.</summary>
        public required int[] Starts;
        public required int[] MeshIndices;
        public required int[] VertexIndices;
    }

    public static MeshMapping MapAll(FbxDocument doc, IntermediateScene scene, ImportContext ctx)
    {
        var result = new MeshMapping();
        foreach (var obj in doc.Objects.Values)
        {
            if (obj.ObjectType != "Geometry" || obj.Subtype != "Mesh") continue;
            int first = scene.Meshes.Count;
            var mapping = MapOne(obj, scene, ctx, result, first);
            if (mapping is not null)
                result.GeometryToMeshes[obj.Id] = mapping;
        }
        return result;
    }

    private static GeometryMapping? MapOne(FbxObject geo, IntermediateScene scene, ImportContext ctx, MeshMapping mapping, int firstMeshIndex)
    {
        var node = geo.Node;
        var verticesNode = node.FindChild("Vertices");
        var indicesNode = node.FindChild("PolygonVertexIndex");
        if (verticesNode is null || indicesNode is null || verticesNode.Properties.Count == 0 || indicesNode.Properties.Count == 0)
        {
            ctx.Log.Warning($"FBX geometry '{geo.Name}' missing Vertices or PolygonVertexIndex; skipping.", "FbxMeshMapper");
            return null;
        }

        double[] vertsRaw = verticesNode.Properties[0].AsDoubleArray();
        int[] polyIndices = indicesNode.Properties[0].AsIntArray();
        if (vertsRaw.Length % 3 != 0)
        {
            ctx.Log.Warning($"FBX geometry '{geo.Name}' Vertices length {vertsRaw.Length} is not a multiple of 3; skipping.", "FbxMeshMapper");
            return null;
        }

        int vertexCount = vertsRaw.Length / 3;
        var sourceVertices = new Float3[vertexCount];
        for (int i = 0; i < vertexCount; i++)
            sourceVertices[i] = new Float3((float)vertsRaw[i * 3], (float)vertsRaw[i * 3 + 1], (float)vertsRaw[i * 3 + 2]);

        // PolygonVertexIndex format: each polygon is a run of indices into Vertices, terminated by
        // a "negate-and-subtract-one" marker on the last index (e.g. 5 6 -8 = polygon 5,6,7).
        // Build:
        //   faceStarts[poly] = start of poly in unpacked stream
        //   faceSizes[poly]  = number of vertices in poly
        //   unpackedVertexIndices[i] = the source vertex index for unpacked position i
        var faceSizes = new List<int>(polyIndices.Length / 3 + 1);
        var unpackedVertexIndices = new int[polyIndices.Length];
        int faceStart = 0;
        for (int i = 0; i < polyIndices.Length; i++)
        {
            int idx = polyIndices[i];
            bool isLast = idx < 0;
            int absIdx = isLast ? -idx - 1 : idx;
            unpackedVertexIndices[i] = absIdx;
            if (isLast)
            {
                faceSizes.Add(i - faceStart + 1);
                faceStart = i + 1;
            }
        }

        // Layered data: normals/UVs/colors/material indices.
        var unpackedNormals = ReadLayerVec3(node, "LayerElementNormal", "Normals", "NormalIndex", polyIndices.Length, vertexCount, faceSizes, unpackedVertexIndices, ctx);
        var unpackedTangents = ReadLayerVec3(node, "LayerElementTangent", "Tangents", "TangentIndex", polyIndices.Length, vertexCount, faceSizes, unpackedVertexIndices, ctx);
        // UV: enumerate every LayerElementUV node (each carries its own slot index in property[0])
        // and route into the per-channel slots on the intermediate mesh. Slot indices that go out
        // of range are dropped with a warning.
        var unpackedUVsPerChannel = new Float2[]?[Prowl.Clay.Mesh.MaxUVChannels];
        var uvChannelNames = new string?[Prowl.Clay.Mesh.MaxUVChannels];
        foreach (var uvLayer in node.FindChildren("LayerElementUV"))
        {
            int slot = uvLayer.Properties.Count > 0 ? uvLayer.IntAt(0) : 0;
            if ((uint)slot >= Prowl.Clay.Mesh.MaxUVChannels)
            {
                ctx.Log.Warning($"LayerElementUV slot {slot} exceeds limit {Prowl.Clay.Mesh.MaxUVChannels}; dropped.", "FbxMeshMapper");
                continue;
            }
            if (unpackedUVsPerChannel[slot] is not null) continue; // first-wins on duplicates
            unpackedUVsPerChannel[slot] = ReadLayerVec2InScope(uvLayer, "UV", "UVIndex", polyIndices.Length, vertexCount, faceSizes, unpackedVertexIndices, ctx, "LayerElementUV");
            uvChannelNames[slot] = uvLayer.FindChild("Name")?.StringAt(0);
        }
        var unpackedColors = ReadLayerColor(node, "LayerElementColor", "Colors", "ColorIndex", polyIndices.Length, vertexCount, faceSizes, unpackedVertexIndices, ctx);
        var faceMaterialSlot = ReadMaterialIndices(node, faceSizes.Count, ctx);

        // Group faces by material slot. Build one IntermediateMesh per slot present.
        int maxSlot = 0;
        for (int i = 0; i < faceMaterialSlot.Length; i++)
            if (faceMaterialSlot[i] > maxSlot) maxSlot = faceMaterialSlot[i];
        int slotCount = maxSlot + 1;

        // First pass: count how many polygonvertex entries each slot needs.
        var slotPVCount = new int[slotCount];
        for (int f = 0; f < faceSizes.Count; f++)
            slotPVCount[faceMaterialSlot[f]] += faceSizes[f];

        // Build the FBX-vertex -> (mesh, intermediateVertex) expansion table. The total number of
        // expansions equals the total polygon-vertex count (one expansion per face usage of a
        // control point). We count first, then prefix-sum, then fill.
        int totalExpansions = polyIndices.Length;
        int[] expansionsPerVertex = new int[vertexCount];
        for (int pv = 0; pv < polyIndices.Length; pv++)
        {
            int fbxV = unpackedVertexIndices[pv];
            if ((uint)fbxV < (uint)vertexCount) expansionsPerVertex[fbxV]++;
        }
        int[] starts = new int[vertexCount + 1];
        for (int v = 0; v < vertexCount; v++) starts[v + 1] = starts[v] + expansionsPerVertex[v];
        int[] expansionMeshIdx = new int[totalExpansions];
        int[] expansionVertIdx = new int[totalExpansions];
        int[] expansionCursor = new int[vertexCount]; // running count per FBX vertex
        // Walks: we fill expansion[v] as we visit each pv in the per-slot loop below.

        // Build a mesh per slot.
        int emitted = 0;
        for (int slot = 0; slot < slotCount; slot++)
        {
            if (slotPVCount[slot] == 0) continue;
            var mesh = new IntermediateMesh
            {
                Name = string.IsNullOrEmpty(geo.Name) ? $"Mesh_{geo.Id}_slot{slot}" : $"{geo.Name}/slot{slot}",
                MaterialIndex = -1, // resolved later by the model mapper from connections
                PrimitiveKinds = PrimitiveKind.Polygon,
            };
            // Set MaxInfluencesPerVertex later if skinning lands.

            // Per-polygon-vertex unpack: every entry in unpackedVertexIndices becomes its own mesh
            // vertex with the corresponding layered attributes. JoinIdenticalVertices later collapses.
            var faceList = new List<int[]>(faceSizes.Count);
            int cursor = 0;
            for (int f = 0; f < faceSizes.Count; f++)
            {
                int n = faceSizes[f];
                if (faceMaterialSlot[f] != slot)
                {
                    cursor += n;
                    continue;
                }
                int[] faceIndices = new int[n];
                for (int k = 0; k < n; k++)
                {
                    int pv = cursor + k;
                    int fbxV = unpackedVertexIndices[pv];
                    int meshVi = mesh.Positions.Count;

                    // Record the expansion so skinning can scatter cluster weights later.
                    if ((uint)fbxV < (uint)vertexCount)
                    {
                        int dst = starts[fbxV] + expansionCursor[fbxV]++;
                        expansionMeshIdx[dst] = scene.Meshes.Count;
                        expansionVertIdx[dst] = meshVi;
                    }

                    mesh.Positions.Add(sourceVertices[fbxV]);
                    if (unpackedNormals is not null)
                    {
                        mesh.Normals ??= new List<Float3>();
                        mesh.Normals.Add(unpackedNormals[pv]);
                    }
                    if (unpackedTangents is not null)
                    {
                        mesh.Tangents ??= new List<Float4>();
                        var t = unpackedTangents[pv];
                        mesh.Tangents.Add(new Float4(t.X, t.Y, t.Z, 1f));
                    }
                    for (int uv = 0; uv < Prowl.Clay.Mesh.MaxUVChannels; uv++)
                    {
                        var src = unpackedUVsPerChannel[uv];
                        if (src is null) continue;
                        mesh.UVs[uv] ??= new List<Float2>();
                        mesh.UVs[uv]!.Add(src[pv]);
                    }
                    if (unpackedColors is not null)
                    {
                        mesh.Colors0 ??= new List<Color>();
                        mesh.Colors0.Add(unpackedColors[pv]);
                    }
                    faceIndices[k] = meshVi;
                }
                faceList.Add(faceIndices);
                cursor += n;
            }

            foreach (var f in faceList)
            {
                if (f.Length == 3)
                {
                    mesh.PrimitiveKinds |= PrimitiveKind.Triangle;
                    mesh.PrimitiveKinds &= ~PrimitiveKind.Polygon;
                    mesh.Faces.Add(new IntermediateFace(f));
                }
                else if (f.Length == 2)
                {
                    mesh.PrimitiveKinds |= PrimitiveKind.Line;
                    mesh.Faces.Add(new IntermediateFace(f));
                }
                else if (f.Length == 1)
                {
                    mesh.PrimitiveKinds |= PrimitiveKind.Point;
                    mesh.Faces.Add(new IntermediateFace(f));
                }
                else
                {
                    mesh.PrimitiveKinds |= PrimitiveKind.Polygon;
                    mesh.Faces.Add(new IntermediateFace(f));
                }
            }

            scene.Meshes.Add(mesh);
            mapping.MaterialSlotPerMesh.Add(slot);
            emitted++;
        }

        return new GeometryMapping
        {
            FirstMeshIndex = firstMeshIndex,
            MeshCount = emitted,
            Starts = starts,
            MeshIndices = expansionMeshIdx,
            VertexIndices = expansionVertIdx,
        };
    }

    // ----------------------------------------------------------------------------------------
    // Layer-element unpack helpers
    // ----------------------------------------------------------------------------------------

    /// <summary>
    /// Resolves a single layer element with double-precision triplets (normals, tangents). Returns
    /// a per-polygon-vertex array, or null if absent.
    /// </summary>
    private static Float3[]? ReadLayerVec3(
        FbxNode geometry, string elementName, string dataKey, string indexKey,
        int polyVertexCount, int vertexCount, List<int> faceSizes, int[] unpackedVertexIndices, ImportContext ctx)
    {
        var layer = geometry.FindChild(elementName);
        if (layer is null) return null;
        var data = layer.FindChild(dataKey);
        if (data is null || data.Properties.Count == 0) return null;
        double[] raw = data.Properties[0].AsDoubleArray();
        if (raw.Length % 3 != 0)
        {
            ctx.Log.Warning($"{elementName}: {dataKey} length {raw.Length} not divisible by 3.", "FbxMeshMapper");
            return null;
        }

        int dataCount = raw.Length / 3;
        Float3[] values = new Float3[dataCount];
        for (int i = 0; i < dataCount; i++)
            values[i] = new Float3((float)raw[i * 3], (float)raw[i * 3 + 1], (float)raw[i * 3 + 2]);

        return ResolveLayer(
            layer, indexKey, polyVertexCount, vertexCount, faceSizes, unpackedVertexIndices, values, Float3.Zero, ctx, elementName);
    }

    /// <summary>
    /// Variant of <see cref="ReadLayerVec3"/> that takes the layer node directly. Used by the
    /// multi-channel UV path which enumerates every LayerElementUV under the Geometry rather
    /// than just finding the first by name.
    /// </summary>
    private static Float2[]? ReadLayerVec2InScope(
        FbxNode layer, string dataKey, string indexKey,
        int polyVertexCount, int vertexCount, List<int> faceSizes, int[] unpackedVertexIndices, ImportContext ctx, string elementName)
    {
        var data = layer.FindChild(dataKey);
        if (data is null || data.Properties.Count == 0) return null;
        double[] raw = data.Properties[0].AsDoubleArray();
        if (raw.Length % 2 != 0)
        {
            ctx.Log.Warning($"{elementName}: {dataKey} length {raw.Length} not divisible by 2.", "FbxMeshMapper");
            return null;
        }
        int dataCount = raw.Length / 2;
        Float2[] values = new Float2[dataCount];
        for (int i = 0; i < dataCount; i++)
            values[i] = new Float2((float)raw[i * 2], (float)raw[i * 2 + 1]);

        return ResolveLayer(
            layer, indexKey, polyVertexCount, vertexCount, faceSizes, unpackedVertexIndices, values, Float2.Zero, ctx, elementName);
    }

    private static Color[]? ReadLayerColor(
        FbxNode geometry, string elementName, string dataKey, string indexKey,
        int polyVertexCount, int vertexCount, List<int> faceSizes, int[] unpackedVertexIndices, ImportContext ctx)
    {
        var layer = geometry.FindChild(elementName);
        if (layer is null) return null;
        var data = layer.FindChild(dataKey);
        if (data is null || data.Properties.Count == 0) return null;
        double[] raw = data.Properties[0].AsDoubleArray();
        if (raw.Length % 4 != 0)
        {
            ctx.Log.Warning($"{elementName}: {dataKey} length {raw.Length} not divisible by 4.", "FbxMeshMapper");
            return null;
        }
        int dataCount = raw.Length / 4;
        Color[] values = new Color[dataCount];
        for (int i = 0; i < dataCount; i++)
            values[i] = new Color((float)raw[i * 4], (float)raw[i * 4 + 1], (float)raw[i * 4 + 2], (float)raw[i * 4 + 3]);

        return ResolveLayer(
            layer, indexKey, polyVertexCount, vertexCount, faceSizes, unpackedVertexIndices, values, new Color(1f, 1f, 1f, 1f), ctx, elementName);
    }

    /// <summary>Generic Direct/IndexToDirect + ByVertex/ByPolygonVertex/ByPolygon/AllSame resolver.</summary>
    private static T[]? ResolveLayer<T>(
        FbxNode layer, string indexKey,
        int polyVertexCount, int _vertexCount, List<int> faceSizes, int[] unpackedVertexIndices,
        T[] values, T fallback, ImportContext ctx, string elementName) where T : struct
    {
        string mapping = layer.FindChild("MappingInformationType")?.StringAt(0) ?? "ByPolygonVertex";
        string reference = layer.FindChild("ReferenceInformationType")?.StringAt(0) ?? "Direct";

        int[]? indices = null;
        if (reference == "IndexToDirect" || reference == "Index")
        {
            var idxNode = layer.FindChild(indexKey);
            if (idxNode is null || idxNode.Properties.Count == 0)
            {
                ctx.Log.Warning($"{elementName}: ReferenceInformationType={reference} but no {indexKey} array.", "FbxMeshMapper");
                return null;
            }
            indices = idxNode.Properties[0].AsIntArray();
        }

        T Pick(int directIndex)
        {
            int actual = indices is null ? directIndex : (directIndex < indices.Length ? indices[directIndex] : -1);
            return (uint)actual < (uint)values.Length ? values[actual] : fallback;
        }

        T[] result = new T[polyVertexCount];
        switch (mapping)
        {
            case "ByVertex":
            case "ByVertice":
            case "ByControlPoint":
                for (int pv = 0; pv < polyVertexCount; pv++)
                    result[pv] = Pick(unpackedVertexIndices[pv]);
                return result;

            case "ByPolygonVertex":
                for (int pv = 0; pv < polyVertexCount; pv++)
                    result[pv] = Pick(pv);
                return result;

            case "ByPolygon":
            {
                int cursor = 0;
                for (int f = 0; f < faceSizes.Count; f++)
                {
                    T v = Pick(f);
                    for (int k = 0; k < faceSizes[f]; k++)
                        result[cursor + k] = v;
                    cursor += faceSizes[f];
                }
                return result;
            }

            case "AllSame":
            {
                T v = Pick(0);
                for (int pv = 0; pv < polyVertexCount; pv++)
                    result[pv] = v;
                return result;
            }

            default:
                ctx.Log.Warning($"{elementName}: unsupported MappingInformationType '{mapping}'.", "FbxMeshMapper");
                return null;
        }
    }

    /// <summary>
    /// Reads per-face material slot indices for the geometry. Returns an array of size
    /// <c>faceCount</c>. AllSame -&gt; slot 0, ByPolygon -&gt; per-face, otherwise unsupported.
    /// Includes the "all entries -1 means drop the layer" check (dummy material layers sometimes
    /// ship attached).
    /// </summary>
    private static int[] ReadMaterialIndices(FbxNode geometry, int faceCount, ImportContext ctx)
    {
        var layer = geometry.FindChild("LayerElementMaterial");
        if (layer is null)
            return new int[faceCount];

        string mapping = layer.FindChild("MappingInformationType")?.StringAt(0) ?? "AllSame";
        var indices = layer.FindChild("Materials")?.Properties.ElementAtOrDefault(0)?.AsIntArray();

        // Drop a dummy layer where every entry is negative. Some exporters emit a placeholder
        // LayerElementMaterial with -1 sentinels and expect the converter to fall through to
        // "all slot 0".
        if (indices is not null && indices.Length > 0)
        {
            bool allNeg = true;
            for (int i = 0; i < indices.Length; i++)
                if (indices[i] >= 0) { allNeg = false; break; }
            if (allNeg)
            {
                ctx.Log.Info("LayerElementMaterial has only -1 entries; treating as no material assignment.", "FbxMeshMapper");
                return new int[faceCount];
            }
        }

        int[] result = new int[faceCount];
        switch (mapping)
        {
            case "AllSame":
                int allSlot = indices is { Length: > 0 } ? Math.Max(0, indices[0]) : 0;
                Array.Fill(result, allSlot);
                return result;
            case "ByPolygon":
                if (indices is null)
                {
                    ctx.Log.Warning("LayerElementMaterial ByPolygon without Materials array.", "FbxMeshMapper");
                    return result;
                }
                for (int i = 0; i < faceCount; i++)
                {
                    int idx = i < indices.Length ? indices[i] : 0;
                    result[i] = Math.Max(0, idx); // clamp negatives so they fall into slot 0 rather than crashing on negative array index downstream.
                }
                return result;
            default:
                ctx.Log.Warning($"LayerElementMaterial mapping '{mapping}' not supported; defaulting to slot 0.", "FbxMeshMapper");
                return result;
        }
    }
}
