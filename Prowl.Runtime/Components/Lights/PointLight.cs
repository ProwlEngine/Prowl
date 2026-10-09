// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Runtime.Rendering;
using Prowl.Vector;

namespace Prowl.Runtime;

[AddComponentMenu("Light/Point Light")]
[ComponentIcon("\uf0eb")] // Lightbulb
public class PointLight : Light
{
    public enum Resolution : int
    {
        _256 = 256,
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
    }

    /// <summary>Largest tile each cube face may take. The face is sized from how big the light is on screen up to this.</summary>
    public Resolution ShadowResolution = Resolution._512;
    public float Range = 10.0f;

    private const float ShadowNear = 0.1f;

    // Forward and up of each cube face
    private static readonly (Float3 Forward, Float3 Up)[] s_faceOrientations =
    [
        (Float3.UnitX,  -Float3.UnitY),
        (-Float3.UnitX, -Float3.UnitY),
        (Float3.UnitY,   Float3.UnitZ),
        (-Float3.UnitY, -Float3.UnitZ),
        (Float3.UnitZ,  -Float3.UnitY),
        (-Float3.UnitZ, -Float3.UnitY),
    ];

    internal override int MaxShadowResolution => (int)ShadowResolution;

    public override void OnRenderCollect(Camera camera, List<IRenderable> renderables, List<IRenderableLight> lights)
    {
        lights.Add(this);
    }

    public override void DrawGizmos()
    {
        var icon = Resources.Texture2D.LoadDefault(Resources.DefaultTexture.IconLight);
        if (icon != null) Debug.DrawIcon(icon, Transform.Position, 0.5f, Color.White);
    }

    public override void DrawGizmosSelected()
    {
        Debug.DrawWireSphere(Transform.Position, Range, Color.Yellow);
    }

    public override LightType GetLightType() => LightType.Point;

    /// <summary>View and projection of one cube face, in the order the shaders pick faces: +X, -X, +Y, -Y, +Z, -Z.</summary>
    internal void GetShadowFace(int face, out Float4x4 view, out Float4x4 projection)
    {
        (Float3 forward, Float3 up) = s_faceOrientations[face];
        view = Float4x4.CreateLookTo(Transform.Position, forward, up);
        projection = Float4x4.CreatePerspectiveFov(Maths.PI / 2.0f, 1.0f, ShadowNear, Maths.Max(Range, 0.2f));
    }

    public override ForwardLightData GetForwardLightData()
    {
        return new ForwardLightData
        {
            Type = LightType.Point,
            Position = Transform.Position,
            Direction = Transform.Forward,
            Color = new Float3(this.Color.R, this.Color.G, this.Color.B),
            Intensity = Intensity,
            Range = Range,
            SpotAngle = 0,
            InnerSpotAngle = 0,

            ShadowEnabled = CastShadows,
            ShadowDepthBias = DepthBias,
            ShadowNormalBias = NormalBias,
            ShadowStrength = ShadowStrength,
            ShadowQuality = (float)ShadowQuality,
        };
    }
}
