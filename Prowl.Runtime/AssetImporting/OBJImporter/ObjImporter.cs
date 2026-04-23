// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Prowl.Echo;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;

namespace Prowl.Runtime.AssetImporting.Obj;

/// <summary>
/// Full-featured Wavefront OBJ + MTL importer. Mirrors the <c>GltfImporter</c> pipeline:
/// parses the OBJ and companion <c>.mtl</c>, splits faces into submeshes per <c>usemtl</c>
/// group, loads textures, and emits a <see cref="ModelImportResult"/> with a populated
/// <see cref="MeshRenderer"/> whose <see cref="MeshRenderer.Materials"/> list is one entry
/// per submesh.
/// </summary>
public class ObjImporter
{
    // ================================================================
    //  Public entry points
    // ================================================================

    public ModelImportResult Import(FileInfo assetPath, ModelImporterSettings? settings = null)
    {
        string baseDir = assetPath.DirectoryName ?? "";
        string name = Path.GetFileNameWithoutExtension(assetPath.Name);
        using var stream = File.OpenRead(assetPath.FullName);
        return Build(stream, name, baseDir, canReadDisk: true, settings ?? new ModelImporterSettings());
    }

    public ModelImportResult Import(Stream stream, string virtualPath, ModelImporterSettings? settings = null)
    {
        string baseDir = Path.GetDirectoryName(virtualPath) ?? "";
        string name = Path.GetFileNameWithoutExtension(virtualPath);
        // We can only load companion .mtl + textures if the base dir is an actual filesystem path.
        bool canReadDisk = !string.IsNullOrEmpty(baseDir) && Directory.Exists(baseDir);
        return Build(stream, name, baseDir, canReadDisk, settings ?? new ModelImporterSettings());
    }

    /// <summary>
    /// Convenience for callers that only need a single <see cref="Mesh"/> (e.g. default
    /// built-in models and the sky dome). Returns the first mesh from the import, or an
    /// empty mesh if the OBJ was empty.
    /// </summary>
    public static Mesh ParseMeshOnly(Stream stream, string name, ModelImporterSettings? settings = null)
    {
        var result = new ObjImporter().Import(stream, name, settings);
        return result.Meshes.Count > 0 ? result.Meshes[0] : new Mesh { Name = name };
    }

    // ================================================================
    //  Build
    // ================================================================

    private ModelImportResult Build(Stream stream, string modelName, string baseDir, bool canReadDisk, ModelImporterSettings settings)
    {
        var parsed = ParseObj(stream, settings);

        // Parse all referenced .mtl files (first-wins on duplicate material names).
        var mtlMaterials = new Dictionary<string, MtlMaterial>(StringComparer.Ordinal);
        string mtlDir = baseDir;
        if (canReadDisk)
        {
            foreach (var mtlRef in parsed.MtlLibs)
            {
                string mtlPath = Path.Combine(baseDir, mtlRef);
                if (!File.Exists(mtlPath)) continue;
                try
                {
                    using var mtlStream = File.OpenRead(mtlPath);
                    ParseMtl(mtlStream, mtlMaterials);
                    mtlDir = Path.GetDirectoryName(mtlPath) ?? baseDir;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[OBJ] Failed to parse MTL '{mtlPath}': {ex.Message}");
                }
            }
        }

        // Build Prowl Materials out of the MTL definitions we actually referenced.
        var materials = new List<Material>();
        var materialIndexByName = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var matName in parsed.UsedMaterialNames)
        {
            MtlMaterial? src = null;
            if (mtlMaterials.TryGetValue(matName, out var m)) src = m;

            var mat = BuildMaterial(matName, src, mtlDir, canReadDisk);
            materialIndexByName[matName] = materials.Count;
            materials.Add(mat);
        }

        // Assemble the combined mesh with one submesh per usemtl group.
        var mesh = AssembleMesh(parsed, modelName, settings);

        // Root GO + MeshRenderer with the per-submesh Materials list.
        var rootGO = new GameObject(string.IsNullOrEmpty(modelName) ? "Model" : modelName);
        if (mesh.Vertices != null && mesh.Vertices.Length > 0)
        {
            var mr = rootGO.AddComponent<MeshRenderer>();
            mr.Mesh = new AssetRef<Mesh>(mesh);

            // One AssetRef<Material> per submesh, in the order submeshes were recorded.
            var matRefs = new List<AssetRef<Material>>(parsed.Groups.Count);
            foreach (var g in parsed.Groups)
            {
                if (g.MaterialName != null && materialIndexByName.TryGetValue(g.MaterialName, out int idx))
                    matRefs.Add(new AssetRef<Material>(materials[idx]));
                else
                    matRefs.Add(default);
            }
            mr.Materials = matRefs;
        }

        return new ModelImportResult
        {
            RootGO = rootGO,
            Meshes = new List<Mesh> { mesh },
            Materials = materials,
            Animations = new List<AnimationClip>(),
        };
    }

    // ================================================================
    //  OBJ parse first pass collects raw positions/uvs/normals and
    //  face vertex tuples grouped by active material.
    // ================================================================

    private class SubMeshGroup
    {
        public string? MaterialName;
        public List<(int v, int t, int n)[]> Faces = new();
    }

    private class ParsedObj
    {
        public List<Float3> Positions = new();
        public List<Float3> Normals = new();
        public List<Float2> TexCoords = new();
        public List<Color> Colors = new();
        public bool AnyVertexColor;

        public List<string> MtlLibs = new();
        /// <summary>Order of <c>usemtl</c> names encountered. Drives submesh material assignment.</summary>
        public List<string> UsedMaterialNames = new();

        /// <summary>One SubMeshGroup per contiguous run of faces sharing a material.</summary>
        public List<SubMeshGroup> Groups = new();
    }

    private static ParsedObj ParseObj(Stream stream, ModelImporterSettings settings)
    {
        var p = new ParsedObj();
        var usedSet = new HashSet<string>(StringComparer.Ordinal);

        SubMeshGroup? current = null;
        SubMeshGroup EnsureGroup(string? materialName)
        {
            // Start a new submesh whenever the material changes, even if the new name is null.
            if (current == null || current.MaterialName != materialName)
            {
                current = new SubMeshGroup { MaterialName = materialName };
                p.Groups.Add(current);
            }
            return current;
        }

        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            // Trim comments (# to end-of-line).
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line.Substring(0, hash);
            line = line.Trim();
            if (line.Length == 0) continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            string kw = parts[0];

            switch (kw)
            {
                case "v" when parts.Length >= 4:
                {
                    float x = ParseF(parts[1]);
                    float y = ParseF(parts[2]);
                    float z = ParseF(parts[3]);
                    p.Positions.Add(new Float3(x, y, z));

                    // Extended vertex colors: v x y z r g b (Blender/ZBrush extension).
                    if (parts.Length >= 7)
                    {
                        float r = ParseF(parts[4]);
                        float g = ParseF(parts[5]);
                        float b = ParseF(parts[6]);
                        float a = parts.Length >= 8 ? ParseF(parts[7]) : 1f;
                        p.Colors.Add(new Color(r, g, b, a));
                        p.AnyVertexColor = true;
                    }
                    else
                    {
                        p.Colors.Add(Color.White);
                    }
                    break;
                }

                case "vn" when parts.Length >= 4:
                    p.Normals.Add(new Float3(ParseF(parts[1]), ParseF(parts[2]), ParseF(parts[3])));
                    break;

                case "vt" when parts.Length >= 3:
                {
                    float u = ParseF(parts[1]);
                    float v = ParseF(parts[2]);
                    if (settings.FlipUVs) v = 1f - v;
                    p.TexCoords.Add(new Float2(u, v));
                    break;
                }

                case "f" when parts.Length >= 4:
                {
                    var face = new (int v, int t, int n)[parts.Length - 1];
                    for (int i = 1; i < parts.Length; i++)
                        face[i - 1] = ParseFaceVertex(parts[i], p.Positions.Count, p.TexCoords.Count, p.Normals.Count);

                    // Face goes into the current submesh group (or a default null-material one).
                    EnsureGroup(current?.MaterialName).Faces.Add(face);
                    break;
                }

                case "usemtl" when parts.Length >= 2:
                {
                    string name = string.Join(' ', parts, 1, parts.Length - 1);
                    EnsureGroup(name);
                    if (usedSet.Add(name)) p.UsedMaterialNames.Add(name);
                    break;
                }

                case "mtllib":
                    // Rest of line can be a path with spaces or multiple files separated by spaces.
                    for (int i = 1; i < parts.Length; i++)
                        p.MtlLibs.Add(parts[i]);
                    break;

                // o / g / s / l are intentionally ignored the OBJ de-facto convention is that
                // material boundaries (usemtl) are what split a file into renderable submeshes,
                // so that's what we honour. Smoothing groups are replaced by the normal-gen
                // settings in ModelImporterSettings.
            }
        }

        return p;
    }

    private static (int v, int t, int n) ParseFaceVertex(string token, int posCount, int uvCount, int normCount)
    {
        // v | v/vt | v//vn | v/vt/vn
        var components = token.Split('/');

        int vi = ResolveIndex(components[0], posCount);
        int ti = components.Length > 1 && components[1].Length > 0 ? ResolveIndex(components[1], uvCount) : -1;
        int ni = components.Length > 2 && components[2].Length > 0 ? ResolveIndex(components[2], normCount) : -1;

        return (vi, ti, ni);
    }

    private static int ResolveIndex(string s, int count)
    {
        int index = int.Parse(s, CultureInfo.InvariantCulture);
        return index < 0 ? count + index : index - 1;
    }

    private static float ParseF(string s) => float.Parse(s, CultureInfo.InvariantCulture);

    // ================================================================
    //  Mesh assembly de-dupe vertex tuples, build submeshes
    // ================================================================

    private static Mesh AssembleMesh(ParsedObj parsed, string meshName, ModelImporterSettings settings)
    {
        var outPositions = new List<Float3>();
        var outNormals = new List<Float3>();
        var outUVs = new List<Float2>();
        var outColors = new List<Color>();
        var outIndices = new List<uint>();

        // Unique vertex tuples keeps the final buffer tight.
        var vertexMap = new Dictionary<(int v, int t, int n), uint>();
        uint GetOrCreateVertex((int v, int t, int n) key)
        {
            if (vertexMap.TryGetValue(key, out uint idx)) return idx;
            idx = (uint)outPositions.Count;
            vertexMap[key] = idx;

            // Positions scale by UnitScale at assembly time (match GltfImporter's behavior).
            var pos = key.v >= 0 && key.v < parsed.Positions.Count ? parsed.Positions[key.v] : Float3.Zero;
            outPositions.Add(pos * settings.UnitScale);

            outNormals.Add(key.n >= 0 && key.n < parsed.Normals.Count ? parsed.Normals[key.n] : Float3.Zero);
            outUVs.Add(key.t >= 0 && key.t < parsed.TexCoords.Count ? parsed.TexCoords[key.t] : Float2.Zero);
            outColors.Add(key.v >= 0 && key.v < parsed.Colors.Count ? parsed.Colors[key.v] : Color.White);

            return idx;
        }

        var subMeshDescriptors = new List<SubMeshDescriptor>(parsed.Groups.Count);
        foreach (var group in parsed.Groups)
        {
            int indexStart = outIndices.Count;
            foreach (var face in group.Faces)
            {
                // Fan-triangulate polygons: (0,1,2), (0,2,3), (0,3,4), ...
                uint i0 = GetOrCreateVertex(face[0]);
                for (int i = 1; i < face.Length - 1; i++)
                {
                    uint ia = GetOrCreateVertex(face[i]);
                    uint ib = GetOrCreateVertex(face[i + 1]);
                    outIndices.Add(i0);
                    outIndices.Add(ia);
                    outIndices.Add(ib);
                }
            }
            int indexCount = outIndices.Count - indexStart;
            subMeshDescriptors.Add(new SubMeshDescriptor(indexStart, indexCount, Topology.Triangles));
        }

        var mesh = new Mesh
        {
            Name = meshName,
            MeshTopology = Topology.Triangles,
        };

        if (outPositions.Count == 0)
            return mesh; // nothing to assemble; return empty mesh (consumers check .Vertices.Length).

        mesh.Vertices = outPositions.ToArray();
        if (parsed.Normals.Count > 0) mesh.Normals = outNormals.ToArray();
        if (parsed.TexCoords.Count > 0) mesh.UV = outUVs.ToArray();
        if (parsed.AnyVertexColor) mesh.Colors = outColors.ToArray();
        mesh.IndexFormat = outPositions.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.Indices = outIndices.ToArray();

        // Only register submesh ranges when there's more than one material group otherwise
        // SubMeshCount==1 (the default) draws the full index buffer just fine.
        if (subMeshDescriptors.Count > 1)
        {
            mesh.SetSubMeshCount(subMeshDescriptors.Count);
            for (int s = 0; s < subMeshDescriptors.Count; s++)
                mesh.SetSubMesh(s, subMeshDescriptors[s]);
        }

        // Normal / tangent generation (same policy as GltfImporter).
        bool hadNormals = parsed.Normals.Count > 0;
        if (settings.RecalculateNormals || (!hadNormals && settings.GenerateNormals))
            GenerateNormals(mesh, settings.GenerateSmoothNormals);

        if (settings.CalculateTangentSpace && mesh.HasNormals && mesh.HasUV)
            mesh.RecalculateTangents();

        mesh.RecalculateBounds();
        return mesh;
    }

    private static void GenerateNormals(Mesh mesh, bool smooth)
    {
        if (smooth) { mesh.RecalculateNormals(); return; }

        // Flat shading: face normal assigned to each of the triangle's three vertices.
        var normals = new Float3[mesh.Vertices.Length];
        for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
        {
            int i0 = (int)mesh.Indices[i], i1 = (int)mesh.Indices[i + 1], i2 = (int)mesh.Indices[i + 2];
            var e1 = mesh.Vertices[i1] - mesh.Vertices[i0];
            var e2 = mesh.Vertices[i2] - mesh.Vertices[i0];
            var fn = Float3.Cross(e1, e2);
            float lenSq = Float3.Dot(fn, fn);
            fn = lenSq > 1e-8f ? fn / MathF.Sqrt(lenSq) : Float3.UnitY;
            normals[i0] = fn; normals[i1] = fn; normals[i2] = fn;
        }
        mesh.Normals = normals;
    }

    // ================================================================
    //  MTL parser
    // ================================================================

    private class MtlMaterial
    {
        public string Name = "";
        public Color BaseColor = Color.White;      // Kd + d
        public Color EmissiveColor = Color.Black;  // Ke
        public float Metallic = 0f;                // Pm (PBR extension) defaults 0 if absent
        public bool MetallicSet;
        public float Roughness = 1f;               // Pr (PBR extension)
        public bool RoughnessSet;
        public float SpecularExponent = 32f;       // Ns used as roughness fallback
        public bool SpecularExponentSet;
        public Float3 KsLuminance = new(0.5f, 0.5f, 0.5f); // Ks used as metallic fallback

        public string? MapBaseColor;     // map_Kd
        public string? MapNormal;        // map_Bump / bump / map_bump / norm
        public string? MapMetallic;      // map_Pm
        public string? MapRoughness;     // map_Pr
        public string? MapEmissive;      // map_Ke
    }

    private static void ParseMtl(Stream stream, Dictionary<string, MtlMaterial> output)
    {
        MtlMaterial? current = null;
        using var reader = new StreamReader(stream);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line.Substring(0, hash);
            line = line.Trim();
            if (line.Length == 0) continue;

            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;
            string kw = parts[0].ToLowerInvariant();

            if (kw == "newmtl" && parts.Length >= 2)
            {
                string name = string.Join(' ', parts, 1, parts.Length - 1);
                current = new MtlMaterial { Name = name };
                // First-wins: don't overwrite if the name already exists (e.g. multiple mtllibs).
                if (!output.ContainsKey(name))
                    output[name] = current;
                continue;
            }
            if (current == null) continue; // stray lines before first newmtl

            switch (kw)
            {
                case "kd" when parts.Length >= 4:
                    current.BaseColor = new Color(ParseF(parts[1]), ParseF(parts[2]), ParseF(parts[3]), current.BaseColor.A);
                    break;
                case "ke" when parts.Length >= 4:
                    current.EmissiveColor = new Color(ParseF(parts[1]), ParseF(parts[2]), ParseF(parts[3]), 1f);
                    break;
                case "ks" when parts.Length >= 4:
                    current.KsLuminance = new Float3(ParseF(parts[1]), ParseF(parts[2]), ParseF(parts[3]));
                    break;
                case "ns" when parts.Length >= 2:
                    current.SpecularExponent = ParseF(parts[1]);
                    current.SpecularExponentSet = true;
                    break;
                case "d" when parts.Length >= 2:
                    // Opacity. Kd.A carries alpha through to _MainColor.
                    current.BaseColor = new Color(current.BaseColor.R, current.BaseColor.G, current.BaseColor.B, ParseF(parts[1]));
                    break;
                case "tr" when parts.Length >= 2:
                    // Transparency inverse of d.
                    current.BaseColor = new Color(current.BaseColor.R, current.BaseColor.G, current.BaseColor.B, 1f - ParseF(parts[1]));
                    break;

                // PBR extension.
                case "pm" when parts.Length >= 2:
                    current.Metallic = ParseF(parts[1]);
                    current.MetallicSet = true;
                    break;
                case "pr" when parts.Length >= 2:
                    current.Roughness = ParseF(parts[1]);
                    current.RoughnessSet = true;
                    break;

                case "map_kd":     current.MapBaseColor = ExtractMapPath(parts); break;
                case "map_ke":     current.MapEmissive  = ExtractMapPath(parts); break;
                case "map_pm":     current.MapMetallic  = ExtractMapPath(parts); break;
                case "map_pr":     current.MapRoughness = ExtractMapPath(parts); break;
                case "map_bump":
                case "bump":
                case "norm":
                case "map_norm":
                    current.MapNormal = ExtractMapPath(parts);
                    break;
            }
        }
    }

    /// <summary>
    /// Extracts the texture path from a <c>map_*</c> line, skipping option flags like
    /// <c>-o u v w</c>, <c>-s u v w</c>, <c>-bm n</c>, <c>-clamp on</c>, etc.
    /// </summary>
    private static string? ExtractMapPath(string[] parts)
    {
        for (int i = 1; i < parts.Length; i++)
        {
            string tok = parts[i];
            if (tok.StartsWith('-'))
            {
                // Skip the flag and its expected operand(s). Worst case we skip one token —
                // the loop will re-evaluate the next one, which is fine.
                if (tok == "-o" || tok == "-s" || tok == "-t") i += 3; // three float operands
                else if (tok == "-mm") i += 2;                          // base + gain
                else i += 1;                                            // single operand
                continue;
            }
            // Texture paths may contain spaces rejoin everything from here on.
            return string.Join(' ', parts, i, parts.Length - i);
        }
        return null;
    }

    // ================================================================
    //  Material build from parsed MTL info onto Prowl's Standard shader
    // ================================================================

    private static Material BuildMaterial(string name, MtlMaterial? src, string mtlDir, bool canReadDisk)
    {
        var mat = new Material(Shader.LoadDefault(DefaultShader.Standard))
        {
            Name = name,
        };

        if (src == null)
        {
            // No MTL match leave the material at defaults and stamp the standard slots so
            // the shader gets the usual white/grey/flat-normal textures instead of garbage.
            mat.SetColor("_MainColor", Color.White);
            mat.SetTexture("_MainTex", Texture2D.LoadDefault(DefaultTexture.Grid));
            mat.SetTexture("_NormalTex", Texture2D.LoadDefault(DefaultTexture.Normal));
            mat.SetTexture("_SurfaceTex", Texture2D.LoadDefault(DefaultTexture.Surface));
            mat.SetTexture("_EmissionTex", Texture2D.LoadDefault(DefaultTexture.Emission));
            mat.SetColor("_EmissiveColor", Color.Black);
            mat.SetFloat("_EmissionIntensity", 0f);
            return mat;
        }

        mat.SetColor("_MainColor", src.BaseColor);

        var baseTex = TryLoadTexture(src.MapBaseColor, mtlDir, canReadDisk);
        mat.SetTexture("_MainTex", baseTex ?? Texture2D.LoadDefault(DefaultTexture.Grid));

        var normalTex = TryLoadTexture(src.MapNormal, mtlDir, canReadDisk);
        mat.SetTexture("_NormalTex", normalTex ?? Texture2D.LoadDefault(DefaultTexture.Normal));

        // Derive Metallic/Roughness. Explicit Pm/Pr win; otherwise derive reasonable defaults
        // from the classic Phong params: metallic ≈ max(Ks) (often ~0 for matte, ~1 for metal),
        // roughness ≈ 1 - clamp(log2(Ns)/11, 0, 1) (Ns of 2 → 1, Ns of 1000 → ~0.1).
        float metallic = src.MetallicSet ? src.Metallic : MathF.Max(MathF.Max(src.KsLuminance.X, src.KsLuminance.Y), src.KsLuminance.Z);
        float roughness;
        if (src.RoughnessSet) roughness = src.Roughness;
        else if (src.SpecularExponentSet) roughness = Math.Clamp(1f - MathF.Log2(MathF.Max(1f, src.SpecularExponent)) / 11f, 0f, 1f);
        else roughness = 1f;
        mat.SetFloat("_Metallic", metallic);
        mat.SetFloat("_Roughness", roughness);

        var roughTex = TryLoadTexture(src.MapRoughness, mtlDir, canReadDisk);
        var metalTex = TryLoadTexture(src.MapMetallic, mtlDir, canReadDisk);
        // Prowl's Standard shader uses a single packed "_SurfaceTex" for metallic/roughness/AO.
        // OBJ/MTL don't pack these together, so if we have only one, we still use the default
        // surface texture and let the material's _Metallic/_Roughness floats multiply through.
        mat.SetTexture("_SurfaceTex", roughTex ?? metalTex ?? Texture2D.LoadDefault(DefaultTexture.Surface));

        var emissiveTex = TryLoadTexture(src.MapEmissive, mtlDir, canReadDisk);
        mat.SetTexture("_EmissionTex", emissiveTex ?? Texture2D.LoadDefault(DefaultTexture.Emission));

        mat.SetColor("_EmissiveColor", src.EmissiveColor);
        float emissiveIntensity = MathF.Max(src.EmissiveColor.R, MathF.Max(src.EmissiveColor.G, src.EmissiveColor.B));
        mat.SetFloat("_EmissionIntensity", emissiveIntensity > 0 ? 1f : 0f);

        return mat;
    }

    private static Texture2D? TryLoadTexture(string? relPath, string mtlDir, bool canReadDisk)
    {
        if (!canReadDisk || string.IsNullOrWhiteSpace(relPath)) return null;
        try
        {
            // Accept absolute paths (some .mtl exporters emit them) as well as relative.
            string full = Path.IsPathRooted(relPath) ? relPath : Path.Combine(mtlDir, relPath);
            if (!File.Exists(full)) return null;
            return Texture2D.LoadFromFile(full, true);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[OBJ] Failed to load texture '{relPath}': {ex.Message}");
            return null;
        }
    }
}
