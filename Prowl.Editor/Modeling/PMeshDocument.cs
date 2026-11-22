// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

using Prowl.Echo;
using Prowl.Vector;
using Prowl.Vector.Geometry;

namespace Prowl.Editor.Modeling;

/// <summary>One object of a model document: who it is, where it sits, its mesh and what it is drawn with.</summary>
public sealed class PMeshObject
{
    /// <summary>Made with the object and kept through renames and moves. Everything built from the object is keyed on it.</summary>
    public Guid Id = Guid.NewGuid();
    public string Name = "Object";

    /// <summary>The object this one hangs under, or empty for one at the top.</summary>
    public Guid Parent;

    public Float3 Position;
    public Quaternion Rotation = Quaternion.Identity;
    public Float3 Scale = Float3.One;

    /// <summary>Faces of any size and every attribute. A face's material slot is its submesh attribute.</summary>
    public GeometryData Mesh = new();

    /// <summary>The material asset of each slot, empty for a slot with none.</summary>
    public List<Guid> Materials = new();

    /// <summary>Edges whose faces meet at less than this many degrees are smooth when normals are worked out.</summary>
    public float SmoothingAngle = 30f;
}

/// <summary>
/// A <c>.pmesh</c> model document: a tree of model objects, written as Echo binary. Meshes are flat columns of numbers,
/// one column per attribute and never one entry per element, so large meshes stay small and quick to read.
/// </summary>
public sealed class PMeshDocument
{
    public const string Extension = ".pmesh";
    private const int FormatVersion = 1;

    public List<PMeshObject> Objects { get; } = new();

    public byte[] Write()
    {
        EchoObject root = EchoObject.NewCompound();
        root["version"] = new EchoObject(FormatVersion);
        EchoObject objects = EchoObject.NewList();
        foreach (PMeshObject item in Objects) objects.ListAdd(WriteObject(item));
        root["objects"] = objects;

        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream)) root.WriteToBinary(writer);
        return stream.ToArray();
    }

    public static PMeshDocument Read(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        EchoObject root = EchoObject.ReadFromBinary(reader);
        int version = root.TryGet("version", out EchoObject? v) ? v!.IntValue : 0;
        if (version > FormatVersion) throw new InvalidDataException($"This .pmesh is version {version}, newer than this editor reads.");

        var document = new PMeshDocument();
        if (root.TryGet("objects", out EchoObject? objects))
            foreach (EchoObject item in objects!.List) document.Objects.Add(ReadObject(item));
        return document;
    }

    private static EchoObject WriteObject(PMeshObject item)
    {
        EchoObject o = EchoObject.NewCompound();
        o["id"] = new EchoObject(item.Id.ToString());
        o["name"] = new EchoObject(item.Name);
        o["parent"] = new EchoObject(item.Parent.ToString());
        o["position"] = Floats([item.Position.X, item.Position.Y, item.Position.Z]);
        o["rotation"] = Floats([item.Rotation.X, item.Rotation.Y, item.Rotation.Z, item.Rotation.W]);
        o["scale"] = Floats([item.Scale.X, item.Scale.Y, item.Scale.Z]);
        o["smoothing"] = new EchoObject(item.SmoothingAngle);
        EchoObject materials = EchoObject.NewList();
        foreach (Guid material in item.Materials) materials.ListAdd(new EchoObject(material.ToString()));
        o["materials"] = materials;
        o["mesh"] = WriteMesh(item.Mesh);
        return o;
    }

    private static PMeshObject ReadObject(EchoObject o)
    {
        var item = new PMeshObject
        {
            Id = Guid.Parse(o["id"].StringValue),
            Name = o["name"].StringValue,
            Parent = Guid.Parse(o["parent"].StringValue),
            SmoothingAngle = o["smoothing"].FloatValue,
        };
        float[] position = FloatsOf(o["position"]), rotation = FloatsOf(o["rotation"]), scale = FloatsOf(o["scale"]);
        item.Position = new Float3(position[0], position[1], position[2]);
        item.Rotation = new Quaternion(rotation[0], rotation[1], rotation[2], rotation[3]);
        item.Scale = new Float3(scale[0], scale[1], scale[2]);
        foreach (EchoObject material in o["materials"].List) item.Materials.Add(Guid.Parse(material.StringValue));
        item.Mesh = ReadMesh(o["mesh"]);
        return item;
    }

    // ================================================================
    //  Meshes as flat columns
    // ================================================================

    private static EchoObject WriteMesh(GeometryData mesh)
    {
        var index = new Dictionary<GeometryData.Vertex, int>(mesh.Vertices.Count);
        for (int i = 0; i < mesh.Vertices.Count; i++) index[mesh.Vertices[i]] = i;

        // Corners face by face from each face's first, the order they are read back in
        var loops = new List<GeometryData.Loop>(mesh.Loops.Count);
        foreach (GeometryData.Face face in mesh.Faces)
        {
            GeometryData.Loop loop = face.Loop!;
            do
            {
                loops.Add(loop);
                loop = loop.Next!;
            } while (loop != face.Loop);
        }

        var points = new float[mesh.Vertices.Count * 3];
        for (int i = 0; i < mesh.Vertices.Count; i++)
        {
            Float3 p = mesh.Vertices[i].Point;
            points[i * 3] = p.X;
            points[i * 3 + 1] = p.Y;
            points[i * 3 + 2] = p.Z;
        }
        var edges = new int[mesh.Edges.Count * 2];
        for (int i = 0; i < mesh.Edges.Count; i++)
        {
            edges[i * 2] = index[mesh.Edges[i].Vert1];
            edges[i * 2 + 1] = index[mesh.Edges[i].Vert2];
        }
        var sizes = new int[mesh.Faces.Count];
        for (int i = 0; i < mesh.Faces.Count; i++) sizes[i] = mesh.Faces[i].VertCount;
        var corners = new int[loops.Count];
        for (int i = 0; i < loops.Count; i++) corners[i] = index[loops[i].Vert];

        EchoObject m = EchoObject.NewCompound();
        m["points"] = Floats(points);
        m["edges"] = Ints(edges);
        m["sizes"] = Ints(sizes);
        m["corners"] = Ints(corners);
        m["vertex"] = WriteColumns(mesh.VertexAttributes, mesh.VertexAttributeNames(), mesh.Vertices);
        m["edge"] = WriteColumns(mesh.EdgeAttributes, mesh.EdgeAttributeNames(), mesh.Edges);
        m["loop"] = WriteColumns(mesh.LoopAttributes, mesh.LoopAttributeNames(), loops);
        m["face"] = WriteColumns(mesh.FaceAttributes, mesh.FaceAttributeNames(), mesh.Faces);
        return m;
    }

    private static GeometryData ReadMesh(EchoObject m)
    {
        var mesh = new GeometryData();
        DeclareColumns(m["vertex"], (name, type, size) => mesh.AddVertexAttribute(name, type, size));
        DeclareColumns(m["edge"], (name, type, size) => mesh.AddEdgeAttribute(name, type, size));
        DeclareColumns(m["loop"], (name, type, size) => mesh.AddLoopAttribute(name, type, size));
        DeclareColumns(m["face"], (name, type, size) => mesh.AddFaceAttribute(name, type, size));

        float[] points = FloatsOf(m["points"]);
        for (int i = 0; i + 2 < points.Length; i += 3) mesh.AddVertex(points[i], points[i + 1], points[i + 2]);

        // Edges first and in their written order, so faces find them and edge columns line up
        int[] edges = IntsOf(m["edges"]);
        for (int i = 0; i + 1 < edges.Length; i += 2) mesh.AddEdge(edges[i], edges[i + 1]);

        int[] sizes = IntsOf(m["sizes"]), corners = IntsOf(m["corners"]);
        var loops = new List<GeometryData.Loop>(corners.Length);
        int at = 0;
        foreach (int size in sizes)
        {
            GeometryData.Face? face = mesh.AddFace(corners.AsSpan(at, size).ToArray());
            at += size;
            if (face == null) continue;
            GeometryData.Loop loop = face.Loop!;
            do
            {
                loops.Add(loop);
                loop = loop.Next!;
            } while (loop != face.Loop);
        }

        ReadColumns(m["vertex"], mesh.Vertices);
        ReadColumns(m["edge"], mesh.Edges);
        ReadColumns(m["loop"], loops);
        ReadColumns(m["face"], mesh.Faces);
        return mesh;
    }

    /// <summary>
    /// The attributes of one kind of element. A declared attribute is one dense column, a value for every element. Any
    /// other value some elements hold, such as flags and weights, is a sparse column naming the elements that have one.
    /// </summary>
    private static EchoObject WriteColumns<T>(List<GeometryData.AttributeDefinition> declared, List<string> names, IReadOnlyList<T> elements)
        where T : GeometryData.Element
    {
        EchoObject columns = EchoObject.NewList();
        var dense = new HashSet<string>();
        foreach (GeometryData.AttributeDefinition definition in declared)
        {
            dense.Add(definition.Name);
            bool isFloat = definition.Type.BaseType == GeometryData.AttributeBaseType.Float;
            int size = definition.Type.Dimensions;
            EchoObject column = EchoObject.NewCompound();
            column["name"] = new EchoObject(definition.Name);
            column["type"] = new EchoObject(isFloat ? "float" : "int");
            column["size"] = new EchoObject(size);
            if (isFloat)
            {
                var values = new float[elements.Count * size];
                for (int i = 0; i < elements.Count; i++)
                    if (elements[i].TryGetAttribute(definition.Name, out GeometryData.AttributeValue? value) && value is GeometryData.FloatAttributeValue f && f.Data.Length == size)
                        f.Data.CopyTo(values, i * size);
                column["floats"] = Floats(values);
            }
            else
            {
                var values = new int[elements.Count * size];
                for (int i = 0; i < elements.Count; i++)
                    if (elements[i].TryGetAttribute(definition.Name, out GeometryData.AttributeValue? value) && value is GeometryData.IntAttributeValue n && n.Data.Length == size)
                        n.Data.CopyTo(values, i * size);
                column["ints"] = Ints(values);
            }
            columns.ListAdd(column);
        }

        foreach (string name in names)
        {
            if (dense.Contains(name)) continue;
            var at = new List<int>();
            var counts = new List<int>();
            var ints = new List<int>();
            var floats = new List<float>();
            string? type = null;
            for (int i = 0; i < elements.Count; i++)
            {
                if (!elements[i].TryGetAttribute(name, out GeometryData.AttributeValue? value)) continue;
                string? kind = value switch
                {
                    GeometryData.IntAttributeValue => "int",
                    GeometryData.FloatAttributeValue => "float",
                    GeometryData.WeightsAttributeValue => "weights",
                    _ => null,
                };
                if (kind == null || type != null && kind != type) continue;
                type = kind;
                at.Add(i);
                switch (value)
                {
                    case GeometryData.IntAttributeValue n:
                        counts.Add(n.Data.Length);
                        ints.AddRange(n.Data);
                        break;
                    case GeometryData.FloatAttributeValue f:
                        counts.Add(f.Data.Length);
                        floats.AddRange(f.Data);
                        break;
                    case GeometryData.WeightsAttributeValue w:
                        counts.Add(w.Groups.Length);
                        ints.AddRange(w.Groups);
                        floats.AddRange(w.Weights);
                        break;
                }
            }
            if (type == null) continue;

            EchoObject column = EchoObject.NewCompound();
            column["name"] = new EchoObject(name);
            column["type"] = new EchoObject(type);
            column["sparse"] = new EchoObject(true);
            column["elements"] = Ints([.. at]);
            column["counts"] = Ints([.. counts]);
            column["ints"] = Ints([.. ints]);
            column["floats"] = Floats([.. floats]);
            columns.ListAdd(column);
        }
        return columns;
    }

    private static void DeclareColumns(EchoObject columns, Action<string, GeometryData.AttributeBaseType, int> declare)
    {
        foreach (EchoObject column in columns.List)
        {
            if (column.TryGet("sparse", out _)) continue;
            declare(column["name"].StringValue,
                column["type"].StringValue == "float" ? GeometryData.AttributeBaseType.Float : GeometryData.AttributeBaseType.Int,
                column["size"].IntValue);
        }
    }

    private static void ReadColumns<T>(EchoObject columns, IReadOnlyList<T> elements) where T : GeometryData.Element
    {
        foreach (EchoObject column in columns.List)
        {
            string name = column["name"].StringValue, type = column["type"].StringValue;
            if (column.TryGet("sparse", out _))
            {
                int[] at = IntsOf(column["elements"]), counts = IntsOf(column["counts"]), ints = IntsOf(column["ints"]);
                float[] floats = FloatsOf(column["floats"]);
                int start = 0;
                for (int i = 0; i < at.Length; i++)
                {
                    int count = counts[i];
                    if (at[i] < elements.Count)
                    {
                        elements[at[i]].SetAttribute(name, type switch
                        {
                            "int" => new GeometryData.IntAttributeValue(ints.AsSpan(start, count).ToArray()),
                            "float" => new GeometryData.FloatAttributeValue(floats.AsSpan(start, count).ToArray()),
                            _ => new GeometryData.WeightsAttributeValue(ints.AsSpan(start, count).ToArray(), floats.AsSpan(start, count).ToArray()),
                        });
                    }
                    start += count;
                }
                continue;
            }

            int size = column["size"].IntValue;
            if (type == "float")
            {
                float[] values = FloatsOf(column["floats"]);
                for (int i = 0; i < elements.Count && (i + 1) * size <= values.Length; i++)
                    elements[i].SetAttribute(name, new GeometryData.FloatAttributeValue(values.AsSpan(i * size, size).ToArray()));
            }
            else
            {
                int[] values = IntsOf(column["ints"]);
                for (int i = 0; i < elements.Count && (i + 1) * size <= values.Length; i++)
                    elements[i].SetAttribute(name, new GeometryData.IntAttributeValue(values.AsSpan(i * size, size).ToArray()));
            }
        }
    }

    private static EchoObject Floats(float[] values) => new(MemoryMarshal.AsBytes(values.AsSpan()).ToArray());

    private static EchoObject Ints(int[] values) => new(MemoryMarshal.AsBytes(values.AsSpan()).ToArray());

    private static float[] FloatsOf(EchoObject column) => MemoryMarshal.Cast<byte, float>(column.ByteArrayValue).ToArray();

    private static int[] IntsOf(EchoObject column) => MemoryMarshal.Cast<byte, int>(column.ByteArrayValue).ToArray();
}
