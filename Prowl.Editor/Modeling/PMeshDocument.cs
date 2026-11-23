// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

using Prowl.Echo;
using Prowl.Runtime;
using Prowl.Runtime.Resources;
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

    /// <summary>Run in order on a copy of the mesh, whose end is what the import builds.</summary>
    public List<ModelModifier> Modifiers = new();

    /// <summary>The bones the mesh is skinned to, in the order its bone indices count them. Empty for a mesh with no skin.</summary>
    public List<PMeshBone> Bones = new();

    /// <summary>The names and weights of the blend shapes the mesh's shape attributes hold, in their order.</summary>
    public List<PMeshShape> Shapes = new();

    /// <summary>A mesh holding what the geometry does not: bone names, bind poses, blend shape names and frame weights.</summary>
    public Mesh SkinTemplate() => new()
    {
        Name = Name,
        BoneNames = Bones.Count > 0 ? [.. Bones.Select(b => b.Name)] : null,
        BindPoses = Bones.Count > 0 ? [.. Bones.Select(b => b.BindPose)] : null,
        BlendShapes = [.. Shapes.Select(s => new BlendShape { Name = s.Name, Frames = [.. s.FrameWeights.Select(w => new BlendShapeFrame { Weight = w })] })],
    };
}

/// <summary>A bone of a model object's skeleton, its rest transform under its parent bone or, for a root, the object.</summary>
public sealed class PMeshBone
{
    public string Name = "Bone";

    /// <summary>The index of the parent bone, -1 for a bone at the top.</summary>
    public int Parent = -1;

    public Float3 Position;
    public Quaternion Rotation = Quaternion.Identity;
    public Float3 Scale = Float3.One;

    /// <summary>From the mesh's space into the bone's at rest.</summary>
    public Float4x4 BindPose = Float4x4.Identity;

    /// <summary>
    /// Keeps the bone on its mesh when the mesh's points are moved by a matrix: a bone at the top moves, turns and
    /// scales with them, and the bind pose takes the mesh's new space.
    /// </summary>
    public void Transform(Float4x4 change)
    {
        if (Parent < 0)
        {
            change.Decompose(out _, out Quaternion turn, out Float3 stretch);
            Position = Float4x4.TransformPoint(Position, change);
            Rotation = turn * Rotation;
            Scale *= stretch;
        }
        BindPose *= change.Invert();
    }
}

/// <summary>A blend shape of a model object: its name, its weight from 0 to 100, and the weight each frame is reached at.</summary>
public sealed class PMeshShape
{
    public string Name = "Shape";
    public float Weight;
    public float[] FrameWeights = [100f];
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

    /// <summary>What an object's modifiers see: its bones, and the document's other objects placed into its space.</summary>
    public ModifierContext ContextFor(PMeshObject item)
    {
        Float4x4 toItem = WorldOf(item).Invert();
        return new ModifierContext([.. item.Bones.Select(b => b.Name)],
            id => id != item.Id && Objects.Find(o => o.Id == id) is { } other ? (other.Mesh, toItem * WorldOf(other)) : null);
    }

    private Float4x4 WorldOf(PMeshObject item)
    {
        Float4x4 local = Float4x4.CreateTRS(item.Position, item.Rotation, item.Scale);
        return Objects.Find(o => o.Id == item.Parent) is { } parent ? WorldOf(parent) * local : local;
    }

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
        if (item.Modifiers.Count > 0) o["modifiers"] = Serializer.Serialize(typeof(List<ModelModifier>), item.Modifiers);
        if (item.Bones.Count > 0)
        {
            EchoObject bones = EchoObject.NewList();
            foreach (PMeshBone bone in item.Bones)
            {
                EchoObject b = EchoObject.NewCompound();
                b["name"] = new EchoObject(bone.Name);
                b["parent"] = new EchoObject(bone.Parent);
                b["position"] = Floats([bone.Position.X, bone.Position.Y, bone.Position.Z]);
                b["rotation"] = Floats([bone.Rotation.X, bone.Rotation.Y, bone.Rotation.Z, bone.Rotation.W]);
                b["scale"] = Floats([bone.Scale.X, bone.Scale.Y, bone.Scale.Z]);
                Float4x4 m = bone.BindPose;
                b["bind"] = Floats([m.c0.X, m.c0.Y, m.c0.Z, m.c0.W, m.c1.X, m.c1.Y, m.c1.Z, m.c1.W, m.c2.X, m.c2.Y, m.c2.Z, m.c2.W, m.c3.X, m.c3.Y, m.c3.Z, m.c3.W]);
                bones.ListAdd(b);
            }
            o["bones"] = bones;
        }
        if (item.Shapes.Count > 0)
        {
            EchoObject shapes = EchoObject.NewList();
            foreach (PMeshShape shape in item.Shapes)
            {
                EchoObject s = EchoObject.NewCompound();
                s["name"] = new EchoObject(shape.Name);
                s["weight"] = new EchoObject(shape.Weight);
                s["frames"] = Floats(shape.FrameWeights);
                shapes.ListAdd(s);
            }
            o["shapes"] = shapes;
        }
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
        if (o.TryGet("modifiers", out EchoObject? modifiers) && Serializer.Deserialize<List<ModelModifier>>(modifiers) is { } stack)
            item.Modifiers = stack;
        if (o.TryGet("bones", out EchoObject? bones))
            foreach (EchoObject b in bones!.List)
            {
                float[] p = FloatsOf(b["position"]), r = FloatsOf(b["rotation"]), sc = FloatsOf(b["scale"]), m = FloatsOf(b["bind"]);
                item.Bones.Add(new PMeshBone
                {
                    Name = b["name"].StringValue,
                    Parent = b["parent"].IntValue,
                    Position = new Float3(p[0], p[1], p[2]),
                    Rotation = new Quaternion(r[0], r[1], r[2], r[3]),
                    Scale = new Float3(sc[0], sc[1], sc[2]),
                    BindPose = new Float4x4(new Float4(m[0], m[1], m[2], m[3]), new Float4(m[4], m[5], m[6], m[7]), new Float4(m[8], m[9], m[10], m[11]), new Float4(m[12], m[13], m[14], m[15])),
                });
            }
        if (o.TryGet("shapes", out EchoObject? shapes))
            foreach (EchoObject s in shapes!.List)
                item.Shapes.Add(new PMeshShape { Name = s["name"].StringValue, Weight = s["weight"].FloatValue, FrameWeights = FloatsOf(s["frames"]) });
        return item;
    }

    /// <summary>One mesh on its own in the document's format, for whatever keeps a mesh as bytes.</summary>
    internal static byte[] MeshToBytes(GeometryData mesh)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream)) WriteMesh(mesh).WriteToBinary(writer);
        return stream.ToArray();
    }

    internal static GeometryData MeshFromBytes(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes));
        return ReadMesh(EchoObject.ReadFromBinary(reader));
    }

    /// <summary>Triangles of a mesh brought in that turn less than this against each other join into one face where their corners agree.</summary>
    public const float JoinDegrees = 1f;

    /// <summary>
    /// A model made from copies of objects that draw meshes. Objects keep where they are relative to the active one and
    /// hang under the nearest of the others they were under. Each gets its own copy of its mesh, and material slots keep
    /// their materials, a material that is not an asset left empty with a warning. Objects drawing nothing are left out.
    /// </summary>
    public static PMeshDocument FromObjects(IReadOnlyList<GameObject> objects, GameObject active)
    {
        var document = new PMeshDocument();
        var ids = new Dictionary<GameObject, Guid>();
        foreach (GameObject go in objects)
            if (DrawnMesh(go) is not null) ids[go] = Guid.NewGuid();

        Transform frame = active.Transform;
        foreach (GameObject go in objects)
        {
            if (!ids.TryGetValue(go, out Guid id)) continue;
            (Mesh drawnMesh, IReadOnlyList<Material> materials) = DrawnMesh(go)!.Value;

            GameObject? parent = go.Parent;
            while (parent.IsValid() && !ids.ContainsKey(parent!)) parent = parent!.Parent;

            Transform transform = go.Transform;
            var item = new PMeshObject { Id = id, Name = go.Name, Mesh = MeshGeometry.ToGeometryData(drawnMesh, JoinDegrees) };
            if (go.GetComponent<SkinnedMeshRenderer>() is { } skinned && skinned.IsValid()) TakeSkin(item, skinned, drawnMesh);

            // The mesh brings its own normals, and edges flagged by the smoothing angle say how those are worked out again after an edit
            GeometryOperators.SoftenByAngle(item.Mesh, item.Mesh.Edges, item.SmoothingAngle);
            if (parent.IsValid())
            {
                Transform above = parent!.Transform;
                item.Parent = ids[parent];
                item.Position = above.InverseTransformPoint(transform.Position);
                item.Rotation = Quaternion.Inverse(above.Rotation) * transform.Rotation;
                item.Scale = transform.LossyScale / above.LossyScale;
            }
            else
            {
                item.Position = frame.InverseTransformPoint(transform.Position);
                item.Rotation = Quaternion.Inverse(frame.Rotation) * transform.Rotation;
                item.Scale = transform.LossyScale / frame.LossyScale;
            }

            foreach (Material material in materials)
            {
                if (material.IsValid() && material.AssetID != Guid.Empty) item.Materials.Add(material.AssetID);
                else
                {
                    item.Materials.Add(Guid.Empty);
                    Debug.LogWarning($"[Model] A material of '{go.Name}' is not an asset, so its slot is left empty.");
                }
            }
            document.Objects.Add(item);
        }
        return document;
    }

    /// <summary>The mesh an object draws and its materials, through a mesh renderer or a skinned one, or null.</summary>
    public static (Mesh Mesh, IReadOnlyList<Material> Materials)? DrawnMesh(GameObject go)
    {
        if (go.GetComponent<MeshRenderer>() is { } renderer && renderer.IsValid() && renderer.Mesh is { } mesh && mesh.IsValid())
            return (mesh, renderer.Materials);
        if (go.GetComponent<SkinnedMeshRenderer>() is { } skinned && skinned.IsValid() && skinned.SharedMesh is { } shared && shared.IsValid())
            return (shared, skinned.Materials);
        return null;
    }

    /// <summary>
    /// Copies a skinned renderer's skeleton and blend shapes onto a model object: each bone's rest transform under its
    /// nearest ancestor among the bones, or under the renderer's object for a bone with none, and its bind pose.
    /// </summary>
    private static void TakeSkin(PMeshObject item, SkinnedMeshRenderer skinned, Mesh mesh)
    {
        Transform?[] bones = skinned.Bones ?? [];
        Float4x4[] binds = mesh.BindPoses ?? [];
        string[] names = mesh.BoneNames ?? [];
        Transform owner = skinned.GameObject.Transform;
        for (int i = 0; i < bones.Length; i++)
        {
            Transform? bone = bones[i];
            int parent = -1;
            for (Transform? up = bone?.Parent; up != null && parent < 0; up = up.Parent) parent = Array.IndexOf(bones, up);
            Float4x4 above = parent >= 0 ? bones[parent]!.LocalToWorldMatrix : owner.LocalToWorldMatrix;
            Float4x4 local = bone != null ? above.Invert() * bone.LocalToWorldMatrix : Float4x4.Identity;
            item.Bones.Add(new PMeshBone
            {
                Name = bone != null ? bone.GameObject.Name : i < names.Length ? names[i] : $"Bone{i}",
                Parent = parent,
                Position = new Float3(local.c3.X, local.c3.Y, local.c3.Z),
                Rotation = bone != null ? Quaternion.Inverse(parent >= 0 ? bones[parent]!.Rotation : owner.Rotation) * bone.Rotation : Quaternion.Identity,
                Scale = bone != null ? bone.LossyScale / (parent >= 0 ? bones[parent]!.LossyScale : owner.LossyScale) : Float3.One,
                BindPose = i < binds.Length ? binds[i] : Float4x4.Identity,
            });
        }
        for (int s = 0; s < mesh.BlendShapeCount; s++)
            item.Shapes.Add(new PMeshShape
            {
                Name = mesh.GetBlendShapeName(s),
                Weight = skinned.GetBlendShapeWeight(s),
                FrameWeights = [.. mesh.BlendShapes[s].Frames.Select(f => f.Weight)],
            });
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
            if (isFloat && definition.Name.StartsWith(MeshGeometry.BlendShapePrefix, StringComparison.Ordinal))
            {
                // Blend shape deltas only where a corner moves, the rest reading back as the declared zero
                var at = new List<int>();
                var moved = new List<float>();
                for (int i = 0; i < elements.Count; i++)
                    if (elements[i].TryGetAttribute(definition.Name, out GeometryData.AttributeValue? value) && value is GeometryData.FloatAttributeValue f
                        && f.Data.Length == size && Array.Exists(f.Data, x => x != 0f))
                    {
                        at.Add(i);
                        moved.AddRange(f.Data);
                    }
                column["elements"] = Ints([.. at]);
                column["floats"] = Floats([.. moved]);
            }
            else if (isFloat)
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
            if (type == "float" && column.TryGet("elements", out EchoObject? listed))
            {
                int[] at = IntsOf(listed!);
                float[] values = FloatsOf(column["floats"]);
                for (int i = 0; i < at.Length && (i + 1) * size <= values.Length; i++)
                    if (at[i] < elements.Count) elements[at[i]].SetAttribute(name, new GeometryData.FloatAttributeValue(values.AsSpan(i * size, size).ToArray()));
            }
            else if (type == "float")
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
