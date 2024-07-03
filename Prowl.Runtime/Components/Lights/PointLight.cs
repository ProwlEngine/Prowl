using Prowl.Icons;
using Material = Prowl.Runtime.Material;
using Mesh = Prowl.Runtime.Mesh;
using Shader = Prowl.Runtime.Shader;

namespace Prowl.Runtime;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Lightbulb}  Point Light")]
public class PointLight : MonoBehaviour
{
    public override RenderingOrder RenderOrder => RenderingOrder.Lighting;

    public Color color = Color.white;
    public float radius = 4.0f;
    public float intensity = 1.0f;

    Material lightMat;
    Mesh mesh;
    int lastCamID = -1;

    public override void OnRenderObject()
    {
        #warning Veldrid change
        /*
        if (mesh == null)
            mesh = Mesh.CreateSphere(1f, 16, 16);

        var mat = Matrix4x4.CreateScale(radius) * GameObject.GlobalCamRelative;
        if (lightMat == null)
        {
            lightMat = new Material(Shader.Find("Defaults/Pointlight.shader"));
        }
        else
        {
            if (lastCamID != Camera.Current.InstanceID)
            {
                lastCamID = Camera.Current.InstanceID;
                lightMat.SetTexture("gAlbedoAO", Camera.Current.gBuffer.AlbedoAO);
                lightMat.SetTexture("gNormalMetallic", Camera.Current.gBuffer.NormalMetallic);
                lightMat.SetTexture("gPositionRoughness", Camera.Current.gBuffer.PositionRoughness);
            }

            lightMat.SetVector("LightPosition", Vector3.Transform(GameObject.Transform.position - Camera.Current.GameObject.Transform.position, Graphics.MatView));
            lightMat.SetColor("LightColor", color);
            lightMat.SetFloat("LightRadius", radius);
            lightMat.SetFloat("LightIntensity", intensity);

            //Camera.Current.Stop3D();
            lightMat.SetPass(0);
            //Camera.Current.DrawFullScreenTexture(Camera.Current.gBuffer.depth);
            //Raylib.DrawRectangle(0, 0, 9999, 9999, Color.white);
            // set matrix scale to radius
            Graphics.DrawMeshNow(mesh, mat, lightMat);
            //Camera.Current.Start3D();
        }

        //Gizmos.Matrix = GameObject.Transform.localToWorldMatrix;
        //Gizmos.Color = Color.yellow;
        //Gizmos.DrawSphere(Vector3.zero, radius);
        */
    }
}
