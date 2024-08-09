using Prowl.Runtime.NodeSystem;
using Silk.NET.OpenAL;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Vortice.DXGI;
using static Prowl.Runtime.Light;

namespace Prowl.Runtime.RenderPipelines
{
    [Node("Rendering/Create Light Buffer")]
    public class CreateLightsBufferNode : InOutFlowNode
    {
        public override string Title => "Create Light Buffer";

        public override float Width => 100f;

        [Output, SerializeIgnore] public GraphicsBuffer Buffer;
        [Output, SerializeIgnore] public double LightCount;

        [Output, SerializeIgnore] public NodeRenderTexture ShadowMap;
        [Output, SerializeIgnore] public Camera.CameraData LightCamera;

        [Output(ConnectionType.Override, TypeConstraint.Strict), SerializeIgnore]
        public FlowNode DrawShadowMap;

        public override void Execute(NodePort input)
        {
            var context = (graph as RenderPipeline).Context;

            List<Light> lights = new List<Light>();

            Camera.CameraData cam = (graph as RenderPipeline).CurrentCamera;

            ShadowAtlas.TryInitialize();

            ShadowAtlas.Clear();
            var atlasClear = new CommandBuffer();
            atlasClear.SetRenderTarget(ShadowAtlas.GetAtlas());
            atlasClear.ClearRenderTarget(true, false, Color.black);

            context.ExecuteCommandBuffer(atlasClear);

            // Find all Directional Lights
            foreach (var gameObj in SceneManagement.SceneManager.AllGameObjects)
                foreach (var l in gameObj.GetComponentsInChildren<Light>())
                    lights.Add(l);

            // We have AtlasWidth slots for shadow maps
            // a single shadow map can consume multiple slots if its larger then 128x128
            // We need to distribute these slots and resolutions out to lights
            // based on their distance from the camera
            int width = ShadowAtlas.GetAtlasWidth();

            // Sort lights by distance from camera
            lights = lights.OrderBy(l => {
                if (l is DirectionalLight)
                    return 0; // Directional Lights always get highest priority
                else
                    return Vector3.Distance(cam.Position, l.GameObject.Transform.position);
            }).ToList();

            List<GPULight> gpuLights = [];
            foreach(var light in lights)
            {
                // Calculate resolution based on distance
                int res = CalculateResolution(Vector3.Distance(cam.Position, light.Transform.position)); // Directional lights are always 1024
                if (light is DirectionalLight dir)
                    res = (int)dir.shadowResolution;

                var camData = light.GetCameraData(res);
                if (light.castShadows && camData != null)
                {
                    var gpu = light.GetGPULight(ShadowAtlas.GetSize());

                    // Find a slot for the shadow map
                    var slot = ShadowAtlas.ReserveTiles(res, res, light.InstanceID);

                    if (slot != null)
                    {
                        gpu.AtlasX = slot.Value.x;
                        gpu.AtlasY = slot.Value.y;
                        gpu.AtlasWidth = res;

                        // Draw the shadow map
                        ShadowMap = new(ShadowAtlas.GetAtlas());
                        LightCamera = camData.Value;
                        context.PushCamera(camData.Value);

                        context.SetViewports(slot.Value.x, slot.Value.y, res, res, 0f, 1000f);
                        try
                        {
                            ExecuteNext(nameof(DrawShadowMap));
                        }
                        finally
                        {
                            context.PopCamera();
                            context.SetFullViewports();
                        }
                    }
                    else
                    {
                        gpu.AtlasX = -1;
                        gpu.AtlasY = -1;
                        gpu.AtlasWidth = 0;
                    }

                    gpuLights.Add(gpu);
                }
                else
                {
                    var gpu = light.GetGPULight(0);
                    gpu.AtlasX = -1;
                    gpu.AtlasY = -1;
                    gpu.AtlasWidth = 0;
                    gpuLights.Add(gpu);
                }

            }


            unsafe
            {
                if (lights.Count > 0)
                { 
                    Buffer = new GraphicsBuffer(Veldrid.BufferUsage.StructuredBufferReadOnly, (uint)(sizeof(GPULight) * lights.Count));
                    Buffer.SetData(gpuLights.ToArray());
                }
                else
                {
                    Buffer = new GraphicsBuffer(Veldrid.BufferUsage.StructuredBufferReadOnly, 1);
                }

                LightCount = lights.Count;
            }

            context.SetTexture("_ShadowAtlas", ShadowAtlas.GetAtlas().DepthBuffer);

            ExecuteNext();
        }

        private int CalculateResolution(double distance)
        {
            double t = MathD.Clamp(distance / 16f, 0, 1);
            var tileSize = ShadowAtlas.GetTileSize();
            int resolution = MathD.RoundToInt(MathD.Lerp(ShadowAtlas.GetMaxShadowSize(), tileSize, t));

            // Round to nearest multiple of tile size
            return MathD.Max(tileSize, (resolution / tileSize) * tileSize);
        }

        public override object GetValue(NodePort port)
        {
            if (port.fieldName == nameof(Buffer))
                return Buffer;
            if (port.fieldName == nameof(LightCount))
                return LightCount;

            if (port.fieldName == nameof(ShadowMap))
                return ShadowMap;
            if (port.fieldName == nameof(LightCamera))
                return LightCamera;

            return null;
        }
    }
}
