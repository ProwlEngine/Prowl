using Prowl.Icons;
using Prowl.Runtime.SceneManagement;
using Silk.NET.OpenGL;

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

    Material lightMat;

    RenderTexture? shadowMap;
    Matrix4x4 depthMVP;

    public void OnEnable()
    {
        Graphics.UpdateShadowmaps += UpdateShadowmap;
    }

    public void OnDisable()
    {
        Graphics.UpdateShadowmaps -= UpdateShadowmap;
    }

    public void OnRenderObject()
    {
        lightMat ??= new Material(Shader.Find("Defaults/Directionallight.shader"));
        lightMat.SetVector("LightDirection", Vector3.TransformNormal(GameObject.Forward, Graphics.MatView));
        lightMat.SetColor("LightColor", color);
        lightMat.SetFloat("LightIntensity", intensity);

        lightMat.SetTexture("gAlbedoAO", Camera.Current.gBuffer.AlbedoAO);
        lightMat.SetTexture("gNormalMetallic", Camera.Current.gBuffer.NormalMetallic);
        lightMat.SetTexture("gPositionRoughness", Camera.Current.gBuffer.PositionRoughness);

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

        Graphics.Blit(lightMat);

        var s = Matrix4x4.CreateScale(0.5f);
        var r = Matrix4x4.CreateFromQuaternion(GameObject.GlobalOrientation);
        var t = Matrix4x4.CreateTranslation(GameObject.GlobalPosition);
        Gizmos.Matrix = s * r * t;
        Gizmos.DirectionalLight(Color.yellow, 2f);
        Gizmos.Matrix = Matrix4x4.Identity;
    }

    public void UpdateShadowmap()
    {
        // Populate Shadowmap
        if (castShadows)
        {
            shadowMap ??= new RenderTexture(4096, 4096, 0);

            // Compute the MVP matrix from the light's point of view
            //Graphics.MatDepthProjection = Matrix4x4.CreateOrthographicOffCenter(-25, 25, -25, 25, 1, 256);
            Graphics.MatDepthProjection = Matrix4x4.CreateOrthographic(shadowDistance, shadowDistance, 0, 100);

            Graphics.MatDepthView = Matrix4x4.CreateLookToLeftHanded(-GameObject.Forward * 50, -GameObject.Forward, GameObject.Up);

            depthMVP = Matrix4x4.Identity;
            depthMVP = Matrix4x4.Multiply(depthMVP, Graphics.MatDepthView);
            depthMVP = Matrix4x4.Multiply(depthMVP, Graphics.MatDepthProjection);

            //Graphics.MatDepth = depthMVP;

            shadowMap.Begin();
            Graphics.Clear(1, 1, 1, 1);
            using (Graphics.UseColorBlend(false)) {
                using (Graphics.UseFaceCull(TriangleFace.Front)) {
                    foreach (var go in SceneManager.AllGameObjects)
                        if (go.EnabledInHierarchy)
                            foreach (var comp in go.GetComponents())
                                if (comp.Enabled && comp.RenderOrder == RenderingOrder.Opaque)
                                    comp.Internal_OnRenderObjectDepth();
                }
            }
            shadowMap.End();
        }
    }

}