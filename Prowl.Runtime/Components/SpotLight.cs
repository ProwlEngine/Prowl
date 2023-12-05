using Prowl.Runtime.Resources;
using Prowl.Icons;
using Raylib_cs;
using System;
using Material = Prowl.Runtime.Resources.Material;
using Mesh = Prowl.Runtime.Resources.Mesh;
using Shader = Prowl.Runtime.Resources.Shader;

namespace Prowl.Runtime.Components;

[AddComponentMenu($"{FontAwesome6.Tv}  Rendering/{FontAwesome6.Lightbulb}  Spot Light")]
public class SpotLight : MonoBehaviour
{
    public override RenderingOrder RenderOrder => RenderingOrder.Lighting;

    public Color color = Color.white;
    public float distance = 4.0f;
    public float angle = 0.97f;
    public float falloff = 0.96f;
    public float intensity = 1.0f;

    Material lightMat;
    Mesh mesh;
    int lastCamID = -1;

    public void OnRenderObject()
    {
        if (mesh == null)
            mesh = Mesh.CreateSphere(1f, 16, 16);

        if (lightMat == null)
        {
            lightMat = new Material(Shader.Find("Defaults/Spotlight.shader"));
        }
        else
        {
            if(lastCamID != Camera.Current.InstanceID)
            {
                lastCamID = Camera.Current.InstanceID;
                lightMat.SetTexture("gAlbedoAO", Camera.Current.gBuffer.AlbedoAO);
                lightMat.SetTexture("gNormalMetallic", Camera.Current.gBuffer.NormalMetallic);
                lightMat.SetTexture("gPositionRoughness", Camera.Current.gBuffer.PositionRoughness);
            }

            lightMat.SetVector("LightPosition", Vector3.Transform(this.GameObject.GlobalPosition - Camera.Current.GameObject.GlobalPosition, Graphics.MatView));
            lightMat.SetVector("LightDirection", Vector3.TransformNormal(this.GameObject.Forward, Graphics.MatView));
            //lightMat.SetVector("LightDirection",this.GameObject.Forward);

            lightMat.SetFloat("LightDistance", distance);
            lightMat.SetFloat("Angle", angle);
            lightMat.SetFloat("Falloff", falloff);

            lightMat.SetColor("LightColor", color);
            lightMat.SetFloat("LightIntensity", intensity);

            //Camera.Current.Stop3D();
            lightMat.SetPass(0);
            //Camera.Current.DrawFullScreenTexture(Camera.Current.gBuffer.depth);
            //Raylib.DrawRectangle(0, 0, 9999, 9999, Color.white);
            // set matrix scale to radius
            var mat = Matrix4x4.CreateScale(distance) * GameObject.GlobalCamRelative;
            Graphics.DrawMeshNow(mesh, mat, lightMat);
            lightMat.EndPass();
            //Camera.Current.Start3D();


            var r = Matrix4x4.CreateFromQuaternion(GameObject.GlobalOrientation);
            var t = Matrix4x4.CreateTranslation(GameObject.GlobalPosition);
            Gizmos.Matrix = r * t;
            Gizmos.Spotlight(distance, (1.0f - angle) * 5f, Color.yellow, 2f);
            var b = Color.blue;
            b.a = 0.4f;
            Gizmos.Spotlight(distance, (1.0f - falloff) * 5f, b, 2f);
            Gizmos.Matrix = Matrix4x4.Identity;
        }
    }
}
