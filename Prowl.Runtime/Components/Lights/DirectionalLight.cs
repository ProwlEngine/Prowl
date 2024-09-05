// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Icons;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Lightbulb}  Directional Light")]
[ExecuteAlways]
public class DirectionalLight : Light
{
    public enum Resolution : int
    {
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048
    }

    public Resolution shadowResolution = Resolution._1024;

    public float ambientIntensity = 0.05f;
    public float qualitySamples = 16;
    public float blockerSamples = 16;
    public float shadowDistance = 50f;
    public float shadowRadius = 0.02f;
    public float shadowPenumbra = 80f;
    public float shadowMinimumPenumbra = 0.02f;

    public override GPULight GetGPULight(int res)
    {
        var forward = Transform.forward;
        var proj = Matrix4x4.CreateOrthographic(shadowDistance, shadowDistance, -1000f, 1000f);
        //var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathD.ToRad(80), 1f, shadowRadius, 100f);
        var view = Matrix4x4.CreateLookToLeftHanded(Transform.position, -forward, Transform.up);

        var depthMVP = Matrix4x4.Identity;
        depthMVP = Matrix4x4.Multiply(depthMVP, view);
        depthMVP = Matrix4x4.Multiply(depthMVP, proj);

        //var camData = GetCameraData().Value;
        //var shadowMatrix = camData.GetProjectionMatrix(res, res).ToFloat();
        //System.Numerics.Matrix4x4.Invert(shadowMatrix, out shadowMatrix);
        return new GPULight
        {
            PositionType = new Vector4(qualitySamples, 0, 0, 0),
            DirectionRange = new Vector4(GameObject.Transform.forward, shadowDistance),
            Color = color.GetUInt(),
            Intensity = intensity,
            SpotData = new Vector2(ambientIntensity, 0),
            ShadowData = new Vector4(shadowRadius, shadowPenumbra, shadowBias, shadowNormalBias),
            ShadowMatrix = (view * proj).ToFloat()
        };
    }

    //public override Camera.CameraData? GetCameraData(int res)
    //{
    //    var forward = Transform.forward;
    //    var proj = Matrix4x4.CreateOrthographic(shadowDistance, shadowDistance, -1000f, 1000f);
    //    //var proj = Matrix4x4.CreatePerspectiveFieldOfView(MathD.ToRad(80), 1f, shadowRadius, 100f);
    //    var view = Matrix4x4.CreateLookToLeftHanded(Transform.position, -forward, Transform.up);
    //    return new Camera.CameraData(0, Transform.position, view, proj, shadowDistance, 0, 10f, true, Color.clear, new(0, 0, res, res), 1f, LayerMask.Everything, null);
    //}
}
