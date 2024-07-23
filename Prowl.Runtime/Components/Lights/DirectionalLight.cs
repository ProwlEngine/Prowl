using Prowl.Icons;
using Prowl.Runtime.RenderPipelines;
using Veldrid;

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

    public class AmbientLighting
    {
        public Color Sky = Color.white;
        public float SkyIntensity = 0.4f;
        public Color Color = Color.white;
        public float Intensity = 0.1f;
    }

    public Color color = Color.white;
    public float intensity = 8f;
    public AmbientLighting ambientLighting = new AmbientLighting();
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
    Camera.CameraData cam;

    //public override void OnPreRender()
    //{
    //    UpdateShadowmap();
    //}

    public override void Update()
    {
        lightMat ??= new Material(Application.AssetProvider.LoadAsset<Shader>("Defaults/DirectionalLight.shader"));

        var forward = GameObject.Transform.forward;
        //var proj = Matrix4x4.CreateOrthographic(shadowDistance, shadowDistance, 0, shadowDistance * 2);
        //var view = Matrix4x4.CreateLookToLeftHanded(-forward * shadowDistance, -forward, GameObject.Transform.up);
        cam = Camera.CameraData.CreateOrthographic(0, Transform.position, Transform.forward, Transform.up, shadowDistance, 0.001f, shadowDistance * 2, true, Color.clear, new(0,0,1,1), LayerMask.Everything, shadowMap);

        PropertyState properties = new();

        if (castShadows && shadowMap != null) 
        {
            properties.SetTexture("shadowMap", shadowMap.DepthBuffer);
            properties.SetMatrix("matShadowView", cam.View);
        }

        properties.SetVector("_LightDirection", GameObject.Transform.forward);// Vector3.TransformNormal(GameObject.Transform.forward, Graphics.MatView));
        properties.SetColor("_LightColor", color);
        properties.SetFloat("_LightIntensity", intensity);

        ambientLighting ??= new AmbientLighting();
        properties.SetColor("_AmbientSkyLightColor", ambientLighting.Sky);
        properties.SetFloat("_AmbientSkyLightIntensity", ambientLighting.SkyIntensity);
        properties.SetColor("_AmbientLightColor", ambientLighting.Color);
        properties.SetFloat("_AmbientLightIntensity", ambientLighting.Intensity);

        var fsMesh = Mesh.GetFullscreenQuad();
        MeshRenderable renderable = new MeshRenderable(fsMesh, lightMat, Matrix4x4.Identity, this.GameObject.layerIndex, null, properties);

        Graphics.DrawRenderable(renderable);


#warning Veldrid change
        /*

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

    public override void LateUpdate()
    {
        if (castShadows)
        {
            uint res = (uint)shadowResolution;
            shadowMap ??= new RenderTexture(res, res, [ ], PixelFormat.D32_Float, true);
             
            RenderingContext context = new("Shadow", Graphics.Renderables, shadowMap);

            Graphics.Render([ cam ], context);
        }
        else
        {
            shadowMap?.DestroyImmediate();
            shadowMap = null;
        }
    }
}