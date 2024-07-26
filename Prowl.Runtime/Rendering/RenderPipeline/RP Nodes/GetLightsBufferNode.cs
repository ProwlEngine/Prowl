using Prowl.Runtime.NodeSystem;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

namespace Prowl.Runtime.RenderPipelines
{
    [Node("Rendering/Create Light Buffer")]
    public class CreateLightsBufferNode : InOutFlowNode
    {
        public override string Title => "Create Light Buffer";

        public override float Width => 100f;

        [Output, SerializeIgnore] public ComputeBuffer Buffer;
        [Output, SerializeIgnore] public int LightCount;

        public override void Execute(NodePort input)
        {
            List<GPULight> lights = new List<GPULight>();

            Camera.CameraData cam = (graph as RenderPipeline).CurrentCamera;

            // Find all Directional Lights
            foreach (var gameObj in SceneManagement.SceneManager.AllGameObjects)
            {
                var dLights = gameObj.GetComponentsInChildren<DirectionalLight>();
                foreach (var l in dLights)
                    lights.Add(new GPULight
                    {
                        PositionType = new Vector4(l.qualitySamples, 0, 0, 0),
                        DirectionRange = new Vector4(l.GameObject.Transform.forward, l.shadowDistance),
                        Color = l.color.GetUInt(),
                        Intensity = l.intensity,
                        SpotData = new Vector2(l.ambientLighting.Intensity, 0),
                        ShadowData = new Vector4(l.shadowRadius, l.shadowPenumbra, l.shadowBias, l.shadowNormalBias)
                    });

                var pLights = gameObj.GetComponentsInChildren<PointLight>();
                foreach (var l in pLights)
                    lights.Add(new GPULight
                    {
                        PositionType = new Vector4(l.GameObject.Transform.position, 1),
                        DirectionRange = new Vector4(0, 0, 0, l.radius),
                        Color = l.color.GetUInt(),
                        Intensity = l.intensity,
                        SpotData = new Vector2(0, 0),
                        ShadowData = new Vector4(0, 0, 0, 0)
                    });

                var sLights = gameObj.GetComponentsInChildren<SpotLight>();
                foreach (var l in sLights)
                    lights.Add(new GPULight
                    {
                        PositionType = new Vector4(l.GameObject.Transform.position, 2),
                        DirectionRange = new Vector4(l.GameObject.Transform.forward, l.distance),
                        Color = l.color.GetUInt(),
                        Intensity = l.intensity,
                        SpotData = new Vector2(l.angle, l.falloff),
                        ShadowData = new Vector4(0, 0, 0, 0)
                    });
            }

            unsafe
            {
                if (lights.Count > 0)
                { 
                    Buffer = new ComputeBuffer((uint)(sizeof(GPULight) * lights.Count));
                    Buffer.SetData(lights.ToArray());
                }
                else
                {
                    Buffer = new ComputeBuffer(1);
                }

                LightCount = lights.Count;
            }

            ExecuteNext();
        }

        public override object GetValue(NodePort port)
        {
            if (port.fieldName == nameof(Buffer))
                return Buffer;
            if (port.fieldName == nameof(LightCount))
                return LightCount;

            return null;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct GPULight
        {
            public System.Numerics.Vector4 PositionType; // 4 float - 16 bytes
            public System.Numerics.Vector4 DirectionRange; // 4 float - 16 bytes - 32 bytes
            public uint Color; // 1 uint - 4 bytes - 36 bytes
            public float Intensity; // 1 float - 4 bytes - 40 bytes
            public System.Numerics.Vector2 SpotData; // 2 float - 8 bytes - 48 bytes
            public System.Numerics.Vector4 ShadowData; // 4 float - 16 bytes - 64 bytes
        }
    }
}
