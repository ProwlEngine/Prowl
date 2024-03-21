using Prowl.Icons;
using Prowl.Runtime.SceneManagement;
using Silk.NET.OpenGL;
using System;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Lightbulb}  Directional Light")]
[ExecuteAlways]
public class DirectionalLight : MonoBehaviour
{
    public override RenderingOrder RenderOrder => RenderingOrder.Lighting;

    public Color color = Color.white;
    public float intensity = 32f;
    public float qualitySamples = 16;
    public float blockerSamples = 16;
    public float shadowDistance = 50f;
    public float shadowRadius = 0.02f;
    public float shadowPenumbra = 80f;
    public float shadowMinimumPenumbra = 0.02f;
    public float shadowBias = 0.0f;
    public float shadowNormalBias = 0.02f;
    public bool castShadows = true;
    public bool useFrontFaceCulling = true;

    Material lightMat;

    RenderTexture? shadowMap;
    Matrix4x4 depthMVP;

    public override void OnPreRender()
    {
        UpdateShadowmap();
    }

    public override void OnRenderObject()
    {
        lightMat ??= new Material(Shader.Find("Defaults\\Directionallight.shader"));
        lightMat.SetVector("LightDirection", Vector3.TransformNormal(GameObject.transform.forward, Graphics.MatView));
        lightMat.SetColor("LightColor", color);
        lightMat.SetFloat("LightIntensity", intensity);

        lightMat.SetTexture("gAlbedoAO", Camera.Current.gBuffer.AlbedoAO);
        lightMat.SetTexture("gNormalMetallic", Camera.Current.gBuffer.NormalMetallic);
        lightMat.SetTexture("gPositionRoughness", Camera.Current.gBuffer.PositionRoughness);

        if (castShadows) {
            lightMat.EnableKeyword("CASTSHADOWS");
            lightMat.SetTexture("shadowMap", shadowMap.InternalDepth);

            lightMat.SetMatrix("matCamViewInverse", Graphics.MatViewInverse);
            lightMat.SetMatrix("matShadowView", Graphics.MatDepthView);
            lightMat.SetMatrix("matShadowSpace", depthMVP);

            lightMat.SetFloat("u_Radius", shadowRadius);
            lightMat.SetFloat("u_Penumbra", shadowPenumbra);
            lightMat.SetFloat("u_MinimumPenumbra", shadowMinimumPenumbra);
            lightMat.SetInt("u_QualitySamples", (int)qualitySamples);
            lightMat.SetInt("u_BlockerSamples", (int)blockerSamples);
            lightMat.SetFloat("u_Bias", shadowBias);
            lightMat.SetFloat("u_NormalBias", shadowNormalBias);
        } else {
            lightMat.DisableKeyword("CASTSHADOWS");
        }

        Graphics.Blit(lightMat);

        Gizmos.Matrix = GameObject.GlobalCamRelative;
        Gizmos.DirectionalLight(Color.yellow);
    }

    public void UpdateShadowmap()
    {
        // Populate Shadowmap
        if (castShadows) {
            shadowMap ??= new RenderTexture(4096, 4096, 0);

            // Compute the MVP matrix from the light's point of view
            //Graphics.MatDepthProjection = Matrix4x4.CreateOrthographicOffCenter(-25, 25, -25, 25, 1, 256);
            Graphics.MatDepthProjection = Matrix4x4.CreateOrthographic(shadowDistance, shadowDistance, 0, 100);

            var forward = GameObject.transform.forward;
            Graphics.MatDepthView = Matrix4x4.CreateLookToLeftHanded(-forward * 50, -forward, GameObject.transform.up);

            depthMVP = Matrix4x4.Identity;
            depthMVP = Matrix4x4.Multiply(depthMVP, Graphics.MatDepthView);
            depthMVP = Matrix4x4.Multiply(depthMVP, Graphics.MatDepthProjection);

            //Graphics.MatDepth = depthMVP;

            shadowMap.Begin();
            Graphics.Clear(1, 1, 1, 1);
            IDisposable? disposable = null;
            if (useFrontFaceCulling)
                disposable = Graphics.UseFaceCull(TriangleFace.Front);
            foreach (var go in SceneManager.AllGameObjects)
                if (go.enabledInHierarchy)
                    foreach (var comp in go.GetComponents())
                        if (comp.Enabled && comp.RenderOrder == RenderingOrder.Opaque)
                            comp.OnRenderObjectDepth();
            disposable?.Dispose();
            shadowMap.End();
        } else {
            shadowMap?.DestroyImmediate();
            shadowMap = null;
        }
    }

}