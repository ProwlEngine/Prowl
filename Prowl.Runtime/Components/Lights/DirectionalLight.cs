// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Vector;

namespace Prowl.Runtime;

[AddComponentMenu("Light/Directional Light")]
public class DirectionalLight : Light
{
    public enum Resolution : int
    {
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
    }

    public enum CascadeCount : int
    {
        One = 1,
        Two = 2,
        Four = 4,
    }

    public Resolution ShadowResolution = Resolution._1024;
    public CascadeCount Cascades = CascadeCount.Two;

    /// <summary>How far along the view shadows reach, in world units. They fade out over the last tenth.</summary>
    public float ShadowDistance = 50f;

    // Blend between a logarithmic and an even spread of the cascade splits, the log side keeps the near slice short
    private const float CascadeSplitLogBlend = 0.75f;

    // Log splits from a tiny near plane would shrink the first cascade to a sliver
    internal const float MinSplitNear = 0.1f;

    /// <summary>Tile size of each cascade. Scenes saved with a larger size than the enum now offers clamp to the largest.</summary>
    internal int ShadowMapResolution => Maths.Clamp((int)ShadowResolution, (int)Resolution._512, (int)Resolution._2048);

    public override void OnRenderCollect(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights)
    {
        lights.Add(this);
    }

    public override void DrawGizmos()
    {
        var icon = Resources.Texture2D.LoadDefault(Resources.DefaultTexture.IconLight);
        if (icon != null) Debug.DrawIcon(icon, Transform.Position, 0.5f, Color.White);

        Debug.DrawArrow(Transform.Position, Transform.Forward, Color.Yellow);
        Debug.DrawWireCircle(Transform.Position, Transform.Forward, 0.5f, Color.Yellow);

        //// Create and Draw each Frustum
        //foreach (var cascade in _cascadeShadowMatrices)
        //{
        //    Frustum frustum = Frustum.FromMatrix(cascade);
        //    var corners = frustum.GetCorners();
        //
        //    // Corner indices from GetCorners():
        //    // 0: Near-Left-Bottom,  1: Near-Right-Bottom,  2: Near-Left-Top,  3: Near-Right-Top
        //    // 4: Far-Left-Bottom,   5: Far-Right-Bottom,   6: Far-Left-Top,   7: Far-Right-Top
        //
        //    Debug.DrawLine(corners[0], corners[1], Color.Cyan);
        //    Debug.DrawLine(corners[1], corners[3], Color.Cyan);
        //    Debug.DrawLine(corners[3], corners[2], Color.Cyan);
        //    Debug.DrawLine(corners[2], corners[0], Color.Cyan);
        //
        //    Debug.DrawLine(corners[4], corners[5], Color.Cyan);
        //    Debug.DrawLine(corners[5], corners[7], Color.Cyan);
        //    Debug.DrawLine(corners[7], corners[6], Color.Cyan);
        //    Debug.DrawLine(corners[6], corners[4], Color.Cyan);
        //
        //    Debug.DrawLine(corners[0], corners[4], Color.Cyan);
        //    Debug.DrawLine(corners[1], corners[5], Color.Cyan);
        //    Debug.DrawLine(corners[2], corners[6], Color.Cyan);
        //    Debug.DrawLine(corners[3], corners[7], Color.Cyan);
        //}
    }


    public override LightType GetLightType() => LightType.Directional;

    /// <summary>View depth where the cascade <paramref name="index"/> ends, out of <paramref name="count"/>.</summary>
    internal static float GetCascadeSplit(int index, int count, float near, float distance)
    {
        if (index >= count) return distance;
        float t = index / (float)count;
        float log = near * Maths.Pow(distance / near, t);
        float even = near + (distance - near) * t;
        return Maths.Lerp(even, log, CascadeSplitLogBlend);
    }

    /// <summary>
    /// The cascade's view and projection: a box around the snapped focus point that holds every receiver
    /// within <paramref name="cascadeRadius"/> of it. Casters further toward the light than the box are
    /// kept by the depth clamp in the caster pass, see <see cref="GetCasterFrustum"/>.
    /// </summary>
    internal void GetShadowMatrix(Float3 focusPosition, int shadowResolution, float cascadeRadius, out Float4x4 view, out Float4x4 projection)
    {
        Float3 forward = Transform.Forward;
        float width = cascadeRadius * 2f;
        projection = Float4x4.CreateOrtho(width, width, -cascadeRadius, cascadeRadius);

        float texelSize = width / shadowResolution;

        // Build orthonormal basis for light space
        Float3 lightUp = Float3.Normalize(Transform.Up);
        Float3 lightRight = Float3.Normalize(Float3.Cross(lightUp, forward));
        lightUp = Float3.Normalize(Float3.Cross(forward, lightRight)); // Recompute to ensure orthogonality

        // Project the focus position onto light space axes
        float x = Float3.Dot(focusPosition, lightRight);
        float y = Float3.Dot(focusPosition, lightUp);
        float z = Float3.Dot(focusPosition, forward); // KEEP the Z component! god damnit lost so much time to this

        // Snap only X and Y to texel grid in light space
        x = Maths.Round(x / texelSize) * texelSize;
        y = Maths.Round(y / texelSize) * texelSize;

        // Reconstruct the snapped position (X and Y snapped, Z preserved)
        Float3 snappedPosition = (lightRight * x) + (lightUp * y) + (forward * z);

        // Position the shadow map at the snapped position
        view = Float4x4.CreateLookTo(snappedPosition, forward, Transform.Up);
    }

    /// <summary>The volume casters are culled against: the cascade box with no near plane, since the depth
    /// clamp flattens casters any distance toward the light onto the near plane instead of clipping them.</summary>
    internal static Frustum GetCasterFrustum(Float4x4 view, Float4x4 projection)
    {
        Frustum frustum = Frustum.FromMatrix(projection * view);
        frustum.Planes[0].D = float.NegativeInfinity;
        return frustum;
    }

    public override ForwardLightData GetForwardLightData()
    {
        return new ForwardLightData
        {
            Type = LightType.Directional,
            Position = Transform.Position,
            // The shaders take the direction toward the light, the light itself shines along Forward.
            Direction = -Transform.Forward,
            Color = new Float3(this.Color.R, this.Color.G, this.Color.B),
            Intensity = Intensity,
            Range = 0,
            SpotAngle = 0,
            InnerSpotAngle = 0,

            ShadowEnabled = CastShadows,
            ShadowDepthBias = DepthBias,
            ShadowNormalBias = NormalBias,
            ShadowStrength = ShadowStrength,
            ShadowQuality = (float)ShadowQuality,

            ShadowDistance = Maths.Max(ShadowDistance, 0.01f),
        };
    }
}
