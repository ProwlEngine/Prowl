using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Converts the scene from its native coordinate system into the target convention
/// (left-handed, Y-up, +Z forward).
/// </summary>
/// <remarks>
/// The transform mirrors the Z axis. To preserve correct rendering:
/// <list type="bullet">
/// <item>Positions, normals, tangents and morph deltas have Z negated.</item>
/// <item>Quaternions go from (x,y,z,w) to (-x,-y,z,w) - axis negation plus rotation reversal.</item>
/// <item>Triangle winding is flipped because mirroring one axis flips face orientation.</item>
/// <item>Bind/inverse-bind matrices are rebuilt from the converted TRS components.</item>
/// </list>
/// Steps that operate on rotations and animation curves (phase 2) will also be coord-converted
/// when they land.
/// </remarks>
internal sealed class ConvertCoordinateSystemStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.ConvertCoordinateSystem;
    public string Name => "ConvertCoordinateSystem";

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        if (scene.SourceCoordinateSystem == CoordinateSystem.LeftHandedYUp)
            return; // already in target convention

        if (scene.SourceCoordinateSystem == CoordinateSystem.RightHandedZUp)
        {
            ConvertRightZUpToLeftYUp(scene);
            scene.SourceCoordinateSystem = CoordinateSystem.LeftHandedYUp;
            return;
        }

        if (scene.SourceCoordinateSystem != CoordinateSystem.RightHandedYUp)
        {
            context.Log.Warning(
                $"Coordinate conversion from {scene.SourceCoordinateSystem} not implemented; geometry will not be re-oriented.",
                Name);
            return;
        }

        foreach (var node in scene.Nodes)
        {
            node.LocalPosition = NegateZ(node.LocalPosition);
            node.LocalRotation = MirrorZ(node.LocalRotation);
        }

        foreach (var mesh in scene.Meshes)
        {
            for (int i = 0; i < mesh.Positions.Count; i++)
                mesh.Positions[i] = NegateZ(mesh.Positions[i]);

            if (mesh.Normals is not null)
                for (int i = 0; i < mesh.Normals.Count; i++)
                    mesh.Normals[i] = NegateZ(mesh.Normals[i]);

            if (mesh.Tangents is not null)
                for (int i = 0; i < mesh.Tangents.Count; i++)
                {
                    var t = mesh.Tangents[i];
                    mesh.Tangents[i] = new Float4(t.X, t.Y, -t.Z, t.W);
                }

            // Mirror-flip changes face winding; reverse triangle order so visible side stays consistent.
            for (int fi = 0; fi < mesh.Faces.Count; fi++)
            {
                var face = mesh.Faces[fi];
                if (face.Indices.Length == 3)
                {
                    (face.Indices[1], face.Indices[2]) = (face.Indices[2], face.Indices[1]);
                }
                else if (face.Indices.Length > 3)
                {
                    Array.Reverse(face.Indices);
                }
            }

            foreach (var bs in mesh.BlendShapes)
            {
                foreach (var frame in bs.Frames)
                {
                    var verts = frame.DeltaPositions;
                    for (int i = 0; i < verts.Length; i++)
                        verts[i] = NegateZ(verts[i]);

                    if (frame.DeltaNormals is { } dn)
                        for (int i = 0; i < dn.Length; i++)
                            dn[i] = NegateZ(dn[i]);

                    if (frame.DeltaTangents is { } dt)
                        for (int i = 0; i < dt.Length; i++)
                            dt[i] = NegateZ(dt[i]);
                }
            }
        }

        foreach (var skin in scene.Skins)
        {
            for (int i = 0; i < skin.InverseBindPoses.Count; i++)
                skin.InverseBindPoses[i] = MirrorZ(skin.InverseBindPoses[i]);
        }

        foreach (var anim in scene.Animations)
        {
            foreach (var binding in anim.Bindings)
                ConvertBinding(binding);
        }

        // After this step, the scene is in the target system.
        scene.SourceCoordinateSystem = CoordinateSystem.LeftHandedYUp;
    }

    private static void ConvertBinding(IntermediateAnimationBinding b)
    {
        int components = b.Dimension;
        int stride = components;
        // For cubic splines values come as (in-tan, value, out-tan) per key.
        // We apply the same per-component sign to every triplet so the math stays consistent.
        bool isCubic = b.Times.Count > 0 && b.Values.Count == b.Times.Count * components * 3;

        if (b.Property == AnimatedProperty.Position)
        {
            // Negate Z component.
            ApplyComponentNegate(b.Values, components, isCubic, componentIndex: 2);
        }
        else if (b.Property == AnimatedProperty.Rotation)
        {
            // Quaternion (x, y, z, w) -> (-x, -y, z, w).
            ApplyComponentNegate(b.Values, components, isCubic, componentIndex: 0);
            ApplyComponentNegate(b.Values, components, isCubic, componentIndex: 1);
        }
        // Scale and BlendShapeWeight bindings need no coord-flip.
        _ = stride;
    }

    private static void ApplyComponentNegate(List<float> values, int components, bool isCubic, int componentIndex)
    {
        int stride = components * (isCubic ? 3 : 1);
        // For cubic splines we negate the same component in in-tangent, value, and out-tangent.
        if (isCubic)
        {
            int keys = values.Count / stride;
            for (int k = 0; k < keys; k++)
            {
                int baseIdx = k * stride;
                for (int t = 0; t < 3; t++)
                {
                    int idx = baseIdx + t * components + componentIndex;
                    values[idx] = -values[idx];
                }
            }
        }
        else
        {
            int count = values.Count / components;
            for (int i = 0; i < count; i++)
                values[i * components + componentIndex] = -values[i * components + componentIndex];
        }
    }

    /// <summary>
    /// Converts an entire scene from RH Z-up (3ds Max default, some FBX exports) to LH Y-up.
    /// The basis transform is (x, y, z) -> (x, z, y), which swaps Y and Z. The determinant is -1
    /// so handedness flips, which means triangle winding reverses. Quaternions transform under
    /// the same axis swap with an extra sign flip on the imaginary part to account for the
    /// chirality change (see ConvertCoordinateSystemStep.SwapYZRotation).
    /// </summary>
    private static void ConvertRightZUpToLeftYUp(IntermediateScene scene)
    {
        foreach (var node in scene.Nodes)
        {
            node.LocalPosition = SwapYZ(node.LocalPosition);
            node.LocalRotation = SwapYZRotation(node.LocalRotation);
            node.LocalScale = SwapYZScale(node.LocalScale);
        }

        foreach (var mesh in scene.Meshes)
        {
            for (int i = 0; i < mesh.Positions.Count; i++)
                mesh.Positions[i] = SwapYZ(mesh.Positions[i]);

            if (mesh.Normals is not null)
                for (int i = 0; i < mesh.Normals.Count; i++)
                    mesh.Normals[i] = SwapYZ(mesh.Normals[i]);

            if (mesh.Tangents is not null)
                for (int i = 0; i < mesh.Tangents.Count; i++)
                {
                    var t = mesh.Tangents[i];
                    mesh.Tangents[i] = new Float4(t.X, t.Z, t.Y, t.W);
                }

            // Handedness flipped (det = -1), so winding reverses.
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
                        verts[i] = SwapYZ(verts[i]);
                    if (frame.DeltaNormals is { } dn)
                        for (int i = 0; i < dn.Length; i++)
                            dn[i] = SwapYZ(dn[i]);
                    if (frame.DeltaTangents is { } dt)
                        for (int i = 0; i < dt.Length; i++)
                            dt[i] = SwapYZ(dt[i]);
                }
            }
        }

        foreach (var skin in scene.Skins)
        {
            for (int i = 0; i < skin.InverseBindPoses.Count; i++)
                skin.InverseBindPoses[i] = SwapYZ(skin.InverseBindPoses[i]);
        }

        foreach (var anim in scene.Animations)
        {
            foreach (var binding in anim.Bindings)
                ConvertBindingYZ(binding);
        }
    }

    private static void ConvertBindingYZ(IntermediateAnimationBinding b)
    {
        int components = b.Dimension;
        bool isCubic = b.Times.Count > 0 && b.Values.Count == b.Times.Count * components * 3;

        if (b.Property == AnimatedProperty.Position || b.Property == AnimatedProperty.Scale)
        {
            // (x, y, z) -> (x, z, y): swap components 1 and 2 in every value triplet.
            SwapComponents(b.Values, components, isCubic, 1, 2);
        }
        else if (b.Property == AnimatedProperty.Rotation)
        {
            // Quaternion (x, y, z, w) -> (-x, -z, -y, w): swap Y/Z and negate imaginary parts.
            // We do the swap+negate per key (also for cubic spline tangent triplets so the math stays consistent).
            SwapComponents(b.Values, components, isCubic, 1, 2);
            ApplyComponentNegate(b.Values, components, isCubic, componentIndex: 0);
            ApplyComponentNegate(b.Values, components, isCubic, componentIndex: 1);
            ApplyComponentNegate(b.Values, components, isCubic, componentIndex: 2);
        }
    }

    private static void SwapComponents(List<float> values, int components, bool isCubic, int a, int b)
    {
        int stride = components * (isCubic ? 3 : 1);
        if (isCubic)
        {
            int keys = values.Count / stride;
            for (int k = 0; k < keys; k++)
            {
                int baseIdx = k * stride;
                for (int t = 0; t < 3; t++)
                {
                    int i = baseIdx + t * components + a;
                    int j = baseIdx + t * components + b;
                    (values[i], values[j]) = (values[j], values[i]);
                }
            }
        }
        else
        {
            int count = values.Count / components;
            for (int k = 0; k < count; k++)
            {
                int i = k * components + a;
                int j = k * components + b;
                (values[i], values[j]) = (values[j], values[i]);
            }
        }
    }

    private static Float3 SwapYZ(Float3 v) => new(v.X, v.Z, v.Y);
    private static Float3 SwapYZScale(Float3 v) => new(v.X, v.Z, v.Y);
    private static Quaternion SwapYZRotation(Quaternion q) => new(-q.X, -q.Z, -q.Y, q.W);

    private static Float4x4 SwapYZ(Float4x4 m)
    {
        // Conjugate by S = swap-rows-1-and-2 matrix: result = S * m * S.
        // S * m swaps rows 1 and 2 of m (i.e. swaps Y and Z components of every column).
        // (S * m) * S swaps columns 1 and 2 of the result.
        Float4 c0 = new(m.c0.X, m.c0.Z, m.c0.Y, m.c0.W);
        Float4 c1 = new(m.c1.X, m.c1.Z, m.c1.Y, m.c1.W);
        Float4 c2 = new(m.c2.X, m.c2.Z, m.c2.Y, m.c2.W);
        Float4 c3 = new(m.c3.X, m.c3.Z, m.c3.Y, m.c3.W);
        // Swap columns 1 and 2.
        return new Float4x4(c0, c2, c1, c3);
    }

    private static Float3 NegateZ(Float3 v) => new(v.X, v.Y, -v.Z);

    private static Quaternion MirrorZ(Quaternion q) =>
        new(-q.X, -q.Y, q.Z, q.W);

    private static Float4x4 MirrorZ(Float4x4 m)
    {
        // S * M * S where S = diag(1,1,-1,1).
        // Effect: negate row 2 and column 2 of the rotation block.
        // Columns of m: c0, c1, c2, c3 each is Float4.
        var c0 = m.c0;
        var c1 = m.c1;
        var c2 = m.c2;
        var c3 = m.c3;

        // S * M: negate the third row (Z component of every column).
        c0 = new Float4(c0.X, c0.Y, -c0.Z, c0.W);
        c1 = new Float4(c1.X, c1.Y, -c1.Z, c1.W);
        c2 = new Float4(c2.X, c2.Y, -c2.Z, c2.W);
        c3 = new Float4(c3.X, c3.Y, -c3.Z, c3.W);

        // (S*M) * S: negate column 2.
        c2 = new Float4(-c2.X, -c2.Y, -c2.Z, -c2.W);

        return new Float4x4(c0, c1, c2, c3);
    }
}
