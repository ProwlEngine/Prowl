// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Runtime.InteropServices;

namespace Prowl.Clay.Internal.Intermediate;

/// <summary>
/// The faces of an <see cref="IntermediateMesh"/>, with every face's indices packed into one flat buffer.
/// </summary>
/// <remarks>
/// Spans returned by <see cref="IndicesOf(int)"/> and <see cref="AllIndices"/> point into the buffer,
/// so they stop tracking it once a face is added.
/// </remarks>
internal sealed class FaceList
{
    private readonly List<IntermediateFace> _faces;
    private readonly List<int> _indices;

    public FaceList() : this(0, 0) { }

    public FaceList(int faceCapacity, int indexCapacity)
    {
        _faces = new List<IntermediateFace>(faceCapacity);
        _indices = new List<int>(indexCapacity);
    }

    public int Count => _faces.Count;

    public IntermediateFace this[int face] => _faces[face];

    public Span<int> IndicesOf(int face) => IndicesOf(_faces[face]);

    public Span<int> IndicesOf(IntermediateFace face) => CollectionsMarshal.AsSpan(_indices).Slice(face.Start, face.Count);

    /// <summary>Every index of every face, in face order.</summary>
    public Span<int> AllIndices => CollectionsMarshal.AsSpan(_indices);

    public ReadOnlySpan<IntermediateFace> AsSpan() => CollectionsMarshal.AsSpan(_faces);

    public List<IntermediateFace>.Enumerator GetEnumerator() => _faces.GetEnumerator();

    public void EnsureCapacity(int faces, int indices)
    {
        _faces.EnsureCapacity(faces);
        _indices.EnsureCapacity(indices);
    }

    public void Add(ReadOnlySpan<int> indices, int material = IntermediateFace.InheritMaterial, int smoothingGroup = IntermediateFace.NoSmoothingGroup)
    {
        _faces.Add(new IntermediateFace { Start = _indices.Count, Count = indices.Length, Material = material, SmoothingGroup = smoothingGroup });
        _indices.AddRange(indices);
    }

    public void AddTriangle(int a, int b, int c, int material = IntermediateFace.InheritMaterial, int smoothingGroup = IntermediateFace.NoSmoothingGroup)
    {
        _faces.Add(new IntermediateFace { Start = _indices.Count, Count = 3, Material = material, SmoothingGroup = smoothingGroup });
        _indices.Add(a);
        _indices.Add(b);
        _indices.Add(c);
    }

    /// <summary>Appends one triangle per three entries of <paramref name="indices"/>, ignoring any remainder.</summary>
    public void AddTriangles(ReadOnlySpan<uint> indices)
    {
        int triCount = indices.Length / 3;
        int firstFace = _faces.Count, firstIndex = _indices.Count;
        CollectionsMarshal.SetCount(_faces, firstFace + triCount);
        CollectionsMarshal.SetCount(_indices, firstIndex + triCount * 3);
        var faces = CollectionsMarshal.AsSpan(_faces).Slice(firstFace);
        var dst = CollectionsMarshal.AsSpan(_indices).Slice(firstIndex);
        for (int t = 0; t < triCount; t++)
        {
            faces[t] = new IntermediateFace { Start = firstIndex + t * 3, Count = 3, Material = IntermediateFace.InheritMaterial, SmoothingGroup = IntermediateFace.NoSmoothingGroup };
            dst[t * 3] = (int)indices[t * 3];
            dst[t * 3 + 1] = (int)indices[t * 3 + 1];
            dst[t * 3 + 2] = (int)indices[t * 3 + 2];
        }
    }

    /// <summary>Appends a face with the same material and smoothing group as <paramref name="like"/>.</summary>
    public void Add(ReadOnlySpan<int> indices, IntermediateFace like) => Add(indices, like.Material, like.SmoothingGroup);

    public void Clear()
    {
        _faces.Clear();
        _indices.Clear();
    }

    /// <summary>Removes every face flagged in <paramref name="remove"/> and packs the survivors' indices together.</summary>
    public int RemoveAll(ReadOnlySpan<bool> remove)
    {
        var faces = CollectionsMarshal.AsSpan(_faces);
        var indices = CollectionsMarshal.AsSpan(_indices);
        int keptFaces = 0, keptIndices = 0;
        for (int f = 0; f < faces.Length; f++)
        {
            if (remove[f]) continue;
            var face = faces[f];
            if (face.Start != keptIndices)
            {
                indices.Slice(face.Start, face.Count).CopyTo(indices.Slice(keptIndices));
                face.Start = keptIndices;
            }
            faces[keptFaces++] = face;
            keptIndices += face.Count;
        }
        int removed = faces.Length - keptFaces;
        _faces.RemoveRange(keptFaces, removed);
        _indices.RemoveRange(keptIndices, _indices.Count - keptIndices);
        return removed;
    }
}
