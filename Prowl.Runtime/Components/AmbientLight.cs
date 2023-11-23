using Prowl.Icons;
using Prowl.Runtime.Resources;
using Prowl.Runtime.SceneManagement;
using Raylib_cs;
using System.Numerics;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Components;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Lightbulb}  Ambient Light")]
[ExecuteAlways]
public class AmbientLight : MonoBehaviour
{
    public override RenderingOrder RenderOrder => RenderingOrder.Lighting;

    public Color skyColor = Color.blue;
    public Color groundColor = Color.grey;
    public float skyIntensity = 1f;
    public float groundIntensity = 1f;

    Resources.Material lightMat;

    public void OnRenderObject()
    {
        if (lightMat == null)
        {
            lightMat = new Resources.Material(Shader.Find("Defaults/AmbientLight.shader"));
        }
        else
        {
            lightMat.SetColor("SkyColor", skyColor);
            lightMat.SetColor("GroundColor", groundColor);
            lightMat.SetFloat("SkyIntensity", skyIntensity);
            lightMat.SetFloat("GroundIntensity", groundIntensity);

            lightMat.SetTexture("gAlbedoAO", Camera.Current.gBuffer.AlbedoAO);
            lightMat.SetTexture("gNormalMetallic", Camera.Current.gBuffer.NormalMetallic);
            lightMat.SetTexture("gPositionRoughness", Camera.Current.gBuffer.PositionRoughness);


            Graphics.Blit(lightMat);
        }
    }

}