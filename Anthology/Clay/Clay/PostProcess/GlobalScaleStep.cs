using Prowl.Clay.Importer;
using Prowl.Clay.Internal.Intermediate;
using Prowl.Vector;

namespace Prowl.Clay.PostProcess;

/// <summary>
/// Bakes a uniform scale factor into vertex positions, node translations, bone bind matrices,
/// and position animation curves. Use this for "Scale Factor" style import settings.
/// </summary>
/// <remarks>
/// Affects only translation data; rotations, normals/tangents (unit length), UV coords, and
/// non-position curves are untouched. Combines naturally with FBX-style cm-to-m conversion
/// (set <see cref="ModelImporterSettings.GlobalScale"/> to 0.01).
/// </remarks>
internal sealed class GlobalScaleStep : IPostProcess
{
    public PostProcessFlags Flag => PostProcessFlags.GlobalScale;
    public string Name => "GlobalScale";

    public void Execute(IntermediateScene scene, ImportContext context)
    {
        // Compose the per-file unit-conversion factor (FBX is centimeters by default;
        // SourceUnitToMeters carries 0.01 there) with the caller's explicit GlobalScale. Both
        // multiply onto every position and translation in the scene.
        float scale = context.Settings.GlobalScale * scene.SourceUnitToMeters;
        if (scale == 1f) return;

        foreach (var mesh in scene.Meshes)
        {
            for (int i = 0; i < mesh.Positions.Count; i++)
                mesh.Positions[i] = Mul(mesh.Positions[i], scale);

            foreach (var bs in mesh.BlendShapes)
            {
                foreach (var frame in bs.Frames)
                {
                    var dp = frame.DeltaPositions;
                    for (int i = 0; i < dp.Length; i++)
                        dp[i] = Mul(dp[i], scale);
                }
            }
        }

        foreach (var node in scene.Nodes)
            node.LocalPosition = Mul(node.LocalPosition, scale);

        foreach (var skin in scene.Skins)
        {
            for (int i = 0; i < skin.InverseBindPoses.Count; i++)
                skin.InverseBindPoses[i] = ScaleTranslation(skin.InverseBindPoses[i], scale);
        }

        foreach (var anim in scene.Animations)
        {
            foreach (var b in anim.Bindings)
            {
                if (b.Property != AnimatedProperty.Position) continue;
                for (int i = 0; i < b.Values.Count; i++)
                    b.Values[i] *= scale;
            }
        }
    }

    private static Float3 Mul(Float3 v, float s) => new(v.X * s, v.Y * s, v.Z * s);

    private static Float4x4 ScaleTranslation(Float4x4 m, float s)
    {
        // Scale only the translation column (c3.xyz), keeping rotation/scale of the basis intact.
        var c3 = new Float4(m.c3.X * s, m.c3.Y * s, m.c3.Z * s, m.c3.W);
        return new Float4x4(m.c0, m.c1, m.c2, c3);
    }
}
