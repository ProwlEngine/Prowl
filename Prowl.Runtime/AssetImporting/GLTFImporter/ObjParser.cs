// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using Prowl.Runtime;
using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.AssetImporting;

/// <summary>
/// Minimal OBJ parser for loading embedded default models (Cube, Sphere, Cylinder, Plane, SkyDome, UnitCube).
/// Handles basic OBJ features: positions, normals, texture coordinates, and triangular/polygonal faces.
/// </summary>
public static class ObjParser
{
    /// <summary>Parse an OBJ stream and return just the Mesh using default settings (generate all).</summary>
    public static Mesh ParseMesh(Stream stream, string name)
    {
        return ParseMesh(stream, name, new ModelImporterSettings());
    }

    /// <summary>Parse an OBJ stream and return just the Mesh with import settings.</summary>
    public static Mesh ParseMesh(Stream stream, string name, ModelImporterSettings settings)
    {
        return ParseInternal(stream, name, settings);
    }

    /// <summary>Parse an OBJ stream and return a Model with GameObjectData.</summary>
    public static Model Parse(Stream stream, string name)
    {
        var mesh = ParseInternal(stream, name, new ModelImporterSettings());
        var rootGo = new GameObject(name);
        var mr = rootGo.AddComponent<MeshRenderer>();
        mr.Mesh = new AssetRef<Mesh>(mesh);
        var model = new Model(name);
        model.GameObjectData = Echo.Serializer.Serialize(typeof(object), rootGo);
        return model;
    }

    private static Mesh ParseInternal(Stream stream, string name, ModelImporterSettings settings)
    {
        var positions = new List<Float3>();
        var normals = new List<Float3>();
        var texcoords = new List<Float2>();

        var vertexMap = new Dictionary<(int, int, int), uint>();
        var outVertices = new List<Float3>();
        var outNormals = new List<Float3>();
        var outUVs = new List<Float2>();
        var outIndices = new List<uint>();

        using var reader = new StreamReader(stream);
        string line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            // Split on whitespace
            var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            string keyword = parts[0];

            switch (keyword)
            {
                case "v" when parts.Length >= 4:
                    positions.Add(new Float3(
                        float.Parse(parts[1], CultureInfo.InvariantCulture),
                        float.Parse(parts[2], CultureInfo.InvariantCulture),
                        float.Parse(parts[3], CultureInfo.InvariantCulture)));
                    break;

                case "vn" when parts.Length >= 4:
                    normals.Add(new Float3(
                        float.Parse(parts[1], CultureInfo.InvariantCulture),
                        float.Parse(parts[2], CultureInfo.InvariantCulture),
                        float.Parse(parts[3], CultureInfo.InvariantCulture)));
                    break;

                case "vt" when parts.Length >= 3:
                    texcoords.Add(new Float2(
                        float.Parse(parts[1], CultureInfo.InvariantCulture),
                        float.Parse(parts[2], CultureInfo.InvariantCulture)));
                    break;

                case "f" when parts.Length >= 4:
                    ParseFace(parts, positions, normals, texcoords,
                        vertexMap, outVertices, outNormals, outUVs, outIndices);
                    break;

                // Skip: o, g, s, mtllib, usemtl, and anything else
                default:
                    break;
            }
        }

        var mesh = new Mesh();
        mesh.Vertices = outVertices.ToArray();
        if (outNormals.Count == outVertices.Count)
            mesh.Normals = outNormals.ToArray();
        if (outUVs.Count == outVertices.Count)
            mesh.UV = outUVs.ToArray();
        mesh.IndexFormat = outVertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        mesh.MeshTopology = Topology.Triangles;
        mesh.Indices = outIndices.ToArray();

        // Generate or recalculate normals based on import settings
        bool hasFileNormals = normals.Count > 0;
        if (settings.RecalculateNormals || (!hasFileNormals && settings.GenerateNormals))
        {
            if (settings.GenerateSmoothNormals)
                mesh.RecalculateNormals(); // Smooth area-weighted normals with NaN fallback
            else
            {
                // Flat normals: each face gets its own face normal
                var flatNormals = new Float3[mesh.Vertices.Length];
                for (int i = 0; i + 2 < mesh.Indices.Length; i += 3)
                {
                    int i0 = (int)mesh.Indices[i], i1 = (int)mesh.Indices[i + 1], i2 = (int)mesh.Indices[i + 2];
                    var e1 = mesh.Vertices[i1] - mesh.Vertices[i0];
                    var e2 = mesh.Vertices[i2] - mesh.Vertices[i0];
                    var fn = Float3.Cross(e1, e2);
                    float lenSq = Float3.Dot(fn, fn);
                    fn = lenSq > 1e-8f ? fn / MathF.Sqrt(lenSq) : Float3.UnitY;
                    flatNormals[i0] = fn;
                    flatNormals[i1] = fn;
                    flatNormals[i2] = fn;
                }
                mesh.Normals = flatNormals;
            }
        }

        // Generate tangents
        if (settings.CalculateTangentSpace && mesh.HasNormals && mesh.HasUV)
            mesh.RecalculateTangents();

        mesh.RecalculateBounds();
        mesh.Name = name;
        return mesh;
    }

    private static void ParseFace(
        string[] parts,
        List<Float3> positions,
        List<Float3> normals,
        List<Float2> texcoords,
        Dictionary<(int, int, int), uint> vertexMap,
        List<Float3> outVertices,
        List<Float3> outNormals,
        List<Float2> outUVs,
        List<uint> outIndices)
    {
        // Collect the vertex indices for this face
        var faceIndices = new List<uint>(parts.Length - 1);
        for (int i = 1; i < parts.Length; i++)
        {
            uint idx = GetOrCreateVertex(parts[i], positions, normals, texcoords,
                vertexMap, outVertices, outNormals, outUVs);
            faceIndices.Add(idx);
        }

        // Triangulate by fan: (0,1,2), (0,2,3), (0,3,4), ...
        for (int i = 1; i < faceIndices.Count - 1; i++)
        {
            outIndices.Add(faceIndices[0]);
            outIndices.Add(faceIndices[i]);
            outIndices.Add(faceIndices[i + 1]);
        }
    }

    private static uint GetOrCreateVertex(
        string faceVertex,
        List<Float3> positions,
        List<Float3> normals,
        List<Float2> texcoords,
        Dictionary<(int, int, int), uint> vertexMap,
        List<Float3> outVertices,
        List<Float3> outNormals,
        List<Float2> outUVs)
    {
        // Parse face vertex: v, v/vt, v/vt/vn, v//vn
        var components = faceVertex.Split('/');

        int vi = ResolveIndex(components[0], positions.Count);
        int ti = -1;
        int ni = -1;

        if (components.Length > 1 && components[1].Length > 0)
            ti = ResolveIndex(components[1], texcoords.Count);

        if (components.Length > 2 && components[2].Length > 0)
            ni = ResolveIndex(components[2], normals.Count);

        var key = (vi, ti, ni);
        if (vertexMap.TryGetValue(key, out uint existingIndex))
            return existingIndex;

        uint newIndex = (uint)outVertices.Count;
        vertexMap[key] = newIndex;

        outVertices.Add(positions[vi]);
        outNormals.Add(ni >= 0 ? normals[ni] : Float3.Zero);
        outUVs.Add(ti >= 0 ? texcoords[ti] : Float2.Zero);

        return newIndex;
    }

    /// <summary>
    /// Resolves a 1-based OBJ index (possibly negative for relative) to a 0-based index.
    /// </summary>
    private static int ResolveIndex(string s, int count)
    {
        int index = int.Parse(s, CultureInfo.InvariantCulture);
        if (index < 0)
            return count + index; // negative = relative from end
        return index - 1; // OBJ is 1-based
    }
}
