using Prowl.Icons;
using Prowl.Runtime.SceneManagement;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Lightbulb}  Directional Light")]
[ExecuteAlways]
public class DirectionalLight : MonoBehaviour
{
    public enum Resolution : int
    {
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
        _4096 = 4096
    }

    public Resolution shadowResolution = Resolution._1024;

    public Color color = Color.white;
    public float intensity = 8f;
    public float qualitySamples = 16;
    public float blockerSamples = 16;
    public float shadowDistance = 50f;
    public float shadowRadius = 0.02f;
    public float shadowPenumbra = 80f;
    public float shadowMinimumPenumbra = 0.02f;
    public float shadowBias = 0.00004f;
    public float shadowNormalBias = 0.02f;
    public bool castShadows = true;

    Material lightMat;

    RenderTexture? shadowMap;
    Matrix4x4 depthMVP;

    //public override void OnPreRender()
    //{
    //    UpdateShadowmap();
    //}

    public override void Update()
    {
        #warning Veldrid change
        /*
        lightMat ??= new Material(Shader.Find("Defaults/Directionallight.shader"));
        lightMat.SetVector("LightDirection", Vector3.TransformNormal(GameObject.Transform.forward, Graphics.MatView));
        lightMat.SetColor("LightColor", color);
        lightMat.SetFloat("LightIntensity", intensity);

        lightMat.SetTexture("gAlbedoAO", Camera.Current.gBuffer.AlbedoAO);
        lightMat.SetTexture("gNormalMetallic", Camera.Current.gBuffer.NormalMetallic);
        lightMat.SetTexture("gPositionRoughness", Camera.Current.gBuffer.PositionRoughness);

        if (castShadows)
        {
            lightMat.EnableKeyword("CASTSHADOWS");
            lightMat.SetTexture("shadowMap", shadowMap.InternalDepth);

            Matrix4x4.Invert(Graphics.MatView, out var viewInverse);

            lightMat.SetMatrix("matCamViewInverse", viewInverse);
            lightMat.SetMatrix("matShadowView", Graphics.MatDepthView);
            lightMat.SetMatrix("matShadowSpace", depthMVP);

            lightMat.SetFloat("u_Radius", shadowRadius);
            lightMat.SetFloat("u_Penumbra", shadowPenumbra);
            lightMat.SetFloat("u_MinimumPenumbra", shadowMinimumPenumbra);
            lightMat.SetInt("u_QualitySamples", (int)qualitySamples);
            lightMat.SetInt("u_BlockerSamples", (int)blockerSamples);
            lightMat.SetFloat("u_Bias", shadowBias);
            lightMat.SetFloat("u_NormalBias", shadowNormalBias);
        }
        else
        {
            lightMat.DisableKeyword("CASTSHADOWS");
        }

        Graphics.Blit(lightMat);

        //Gizmos.Matrix = GameObject.Transform.localToWorldMatrix;
        //Gizmos.Color = Color.yellow;
        //Gizmos.DrawDirectionalLight(Vector3.zero);
        */
    }

    public void UpdateShadowmap()
    {
        #warning Veldrid change
        /*
        // Populate Shadowmap
        if (castShadows)
        {
            int res = (int)shadowResolution;
            shadowMap ??= new RenderTexture(res, res, 0);

            // Compute the MVP matrix from the light's point of view
            //Graphics.MatDepthProjection = Matrix4x4.CreateOrthographicOffCenter(-25, 25, -25, 25, 1, 256);
            Graphics.MatDepthProjection = Matrix4x4.CreateOrthographic(shadowDistance, shadowDistance, 0, shadowDistance*2);

            var forward = GameObject.Transform.forward;
            Graphics.MatDepthView = Matrix4x4.CreateLookToLeftHanded(-forward * shadowDistance, -forward, GameObject.Transform.up);

            depthMVP = Matrix4x4.Identity;
            depthMVP = Matrix4x4.Multiply(depthMVP, Graphics.MatDepthView);
            depthMVP = Matrix4x4.Multiply(depthMVP, Graphics.MatDepthProjection);

            //Graphics.MatDepth = depthMVP;

            shadowMap.Begin();
            Graphics.Clear(1, 1, 1, 1);
            foreach (var go in SceneManager.AllGameObjects)
                if (go.enabledInHierarchy)
                    foreach (var comp in go.GetComponents())
                        if (comp.Enabled && comp.RenderOrder == RenderingOrder.Opaque)
                            comp.OnRenderObjectDepth();
            shadowMap.End();
        }
        else
        {
            shadowMap?.DestroyImmediate();
            shadowMap = null;
        }
        */
    }

}