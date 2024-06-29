using Prowl.Icons;

namespace Prowl.Runtime;

[ExecuteAlways, AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Lightbulb}  Ambient Light")]
public class AmbientLight : MonoBehaviour
{
    public override RenderingOrder RenderOrder => RenderingOrder.Lighting;

    public Color skyColor = Color.white;
    public Color groundColor = Color.white;
    public float skyIntensity = 1f;
    public float groundIntensity = 1f;

    Material? lightMat;

    public override void OnRenderObject()
    {
        lightMat ??= new Material(Shader.Find("Defaults/AmbientLight.shader"));

        lightMat.SetColor("SkyColor", skyColor);
        lightMat.SetColor("GroundColor", groundColor);
        lightMat.SetFloat("SkyIntensity", skyIntensity);
        lightMat.SetFloat("GroundIntensity", groundIntensity);

        #warning Veldrid change
        /*
        lightMat.SetTexture("gAlbedoAO", Camera.Current.gBuffer.AlbedoAO);
        lightMat.SetTexture("gNormalMetallic", Camera.Current.gBuffer.NormalMetallic);
        lightMat.SetTexture("gPositionRoughness", Camera.Current.gBuffer.PositionRoughness);

        Graphics.Blit(lightMat);
        */
    }

}