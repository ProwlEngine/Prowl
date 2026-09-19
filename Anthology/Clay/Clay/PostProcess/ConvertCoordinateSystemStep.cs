// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Converts the scene from its native coordinate system into the target convention
/// (left-handed, Y-up, +Z forward).
/// </summary>
/// <remarks>
/// A right handed Y-up source has X negated, so a model authored facing +Z still faces +Z.
/// A right handed Z-up source maps (x, y, z) to (-x, z, -y), its Y-up equivalent with X negated.
/// Both are reflections, so:
/// <list type="bullet">
/// <item>Positions, normals, tangents, morph deltas and animated positions go through the axis map.</item>
/// <item>A quaternion's vector part becomes the negated axis map of it, which keeps it a rotation.</item>
/// <item>Scale only has its axes permuted.</item>
/// <item>Triangle winding and the tangent bitangent sign flip.</item>
/// <item>Inverse bind matrices are conjugated by the axis map.</item>
/// </list>
/// </remarks>
internal sealed class ConvertCoordinateSystemStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.ConvertCoordinateSystem;
    public string Name => "ConvertCoordinateSystem";

    private static readonly AxisMap s_fromRightHandedYUp = new(new[] { 0, 1, 2 }, new[] { -1f, 1f, 1f });
    private static readonly AxisMap s_fromRightHandedZUp = new(new[] { 0, 2, 1 }, new[] { -1f, 1f, -1f });

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        AxisMap map;
        switch (scene.SourceCoordinateSystem)
        {
            case CoordinateSystem.LeftHandedYUp:
                return;
            case CoordinateSystem.RightHandedYUp:
                map = s_fromRightHandedYUp;
                break;
            case CoordinateSystem.RightHandedZUp:
                map = s_fromRightHandedZUp;
                break;
            default:
                context.Log.Warning(
                    $"Coordinate conversion from {scene.SourceCoordinateSystem} not implemented; geometry will not be re-oriented.",
                    Name);
                return;
        }

        foreach (var node in scene.Nodes)
        {
            node.LocalPosition = map.Vector(node.LocalPosition);
            node.LocalRotation = map.Rotation(node.LocalRotation);
            node.LocalScale = map.Scale(node.LocalScale);
        }

        foreach (var mesh in scene.Meshes)
        {
            for (int i = 0; i < mesh.Positions.Count; i++)
                mesh.Positions[i] = map.Vector(mesh.Positions[i]);

            if (mesh.Normals is not null)
                for (int i = 0; i < mesh.Normals.Count; i++)
                    mesh.Normals[i] = map.Vector(mesh.Normals[i]);

            if (mesh.Tangents is not null)
                for (int i = 0; i < mesh.Tangents.Count; i++)
                {
                    var t = mesh.Tangents[i];
                    Float3 v = map.Vector(new Float3(t.X, t.Y, t.Z));
                    mesh.Tangents[i] = new Float4(v.X, v.Y, v.Z, -t.W);
                }

            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                var face = mesh.Faces[fi];
                if (face.Indices.Length == 3)
                    (face.Indices[1], face.Indices[2]) = (face.Indices[2], face.Indices[1]);
                else if (face.Indices.Length > 3)
                    Array.Reverse(face.Indices);
            }

            foreach (var bs in mesh.BlendShapes)
            {
                foreach (var frame in bs.Frames)
                {
                    var verts = frame.DeltaPositions;
                    for (int i = 0; i < verts.Length; i++)
                        verts[i] = map.Vector(verts[i]);

                    if (frame.DeltaNormals is { } dn)
                        for (int i = 0; i < dn.Length; i++)
                            dn[i] = map.Vector(dn[i]);

                    if (frame.DeltaTangents is { } dt)
                        for (int i = 0; i < dt.Length; i++)
                            dt[i] = map.Vector(dt[i]);
                }
            }
        }

        foreach (var skin in scene.Skins)
        {
            for (int i = 0; i < skin.InverseBindPoses.Count; i++)
                skin.InverseBindPoses[i] = map.Matrix(skin.InverseBindPoses[i]);
        }

        foreach (var anim in scene.Animations)
        {
            foreach (var binding in anim.Bindings)
                ConvertBinding(binding, map);
        }

        scene.SourceCoordinateSystem = CoordinateSystem.LeftHandedYUp;
    }

    // Cubic spline keys store in tangent, value and out tangent back to back, and each goes through the same map.
    private static void ConvertBinding(IntermediateAnimationBinding b, AxisMap map)
    {
        List<float> values = b.Values;
        int components = b.Dimension;
        for (int i = 0; i + components <= values.Count; i += components)
        {
            if (b.Property == AnimatedProperty.Position && components == 3)
                Write(values, i, map.Vector(new Float3(values[i], values[i + 1], values[i + 2])));
            else if (b.Property == AnimatedProperty.Scale && components == 3)
                Write(values, i, map.Scale(new Float3(values[i], values[i + 1], values[i + 2])));
            else if (b.Property == AnimatedProperty.Rotation && components == 4)
                Write(values, i, map.Rotation(new Quaternion(values[i], values[i + 1], values[i + 2], values[i + 3])));
        }
    }

    private static void Write(List<float> values, int i, Float3 v)
    {
        values[i] = v.X;
        values[i + 1] = v.Y;
        values[i + 2] = v.Z;
    }

    private static void Write(List<float> values, int i, Quaternion q)
    {
        values[i] = q.X;
        values[i + 1] = q.Y;
        values[i + 2] = q.Z;
        values[i + 3] = q.W;
    }

    // A reflection that permutes axes and flips signs: output axis i is Signs[i] times input axis Sources[i].
    private sealed class AxisMap
    {
        private readonly int[] _sources;
        private readonly float[] _signs;
        private readonly Float4x4 _matrix;

        public AxisMap(int[] sources, float[] signs)
        {
            _sources = sources;
            _signs = signs;
            var columns = new Float4[4];
            for (int j = 0; j < 3; j++)
            {
                Float3 column = Vector(new Float3(j == 0 ? 1f : 0f, j == 1 ? 1f : 0f, j == 2 ? 1f : 0f));
                columns[j] = new Float4(column.X, column.Y, column.Z, 0f);
            }
            columns[3] = new Float4(0f, 0f, 0f, 1f);
            _matrix = new Float4x4(columns[0], columns[1], columns[2], columns[3]);
        }

        public Float3 Vector(Float3 v) => new(_signs[0] * v[_sources[0]], _signs[1] * v[_sources[1]], _signs[2] * v[_sources[2]]);

        public Float3 Scale(Float3 s) => new(s[_sources[0]], s[_sources[1]], s[_sources[2]]);

        public Quaternion Rotation(Quaternion q)
        {
            Float3 axis = Vector(new Float3(q.X, q.Y, q.Z));
            return new Quaternion(-axis.X, -axis.Y, -axis.Z, q.W);
        }

        public Float4x4 Matrix(Float4x4 m) => _matrix * m * Float4x4.Transpose(_matrix);
    }
}
