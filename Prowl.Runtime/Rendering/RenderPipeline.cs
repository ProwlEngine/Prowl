using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Runtime.Resources;

namespace Prowl.Runtime.Rendering
{
    public struct RenderingData
    {
        public bool DisplayGizmo;
        public Matrix4x4 GridMatrix;
        public Color GridColor;
        public Vector3 GridSizes;
    }

    public interface IRenderable
    {
        public Material GetMaterial();
        public int GetLayer();

        public void GetRenderingData(out PropertyState properties, out Mesh drawData, out Matrix4x4 model);

        public void GetCullingData(out bool isRenderable, out Bounds bounds);
    }

    public enum LightType
    {
        Directional,
        //Spot,
        //Point,
        //Area
    }

    public interface IRenderableLight
    {
        public int GetLightID();
        public int GetLayer();
        public LightType GetLightType();
        public Vector3 GetLightPosition();
        public Vector3 GetLightDirection();
        public bool DoCastShadows();
        public void GetShadowMatrix(out Matrix4x4 view, out Matrix4x4 projection);
    }

    public abstract class RenderPipeline : EngineObject
    {
        private static readonly List<IRenderable> s_renderables = [];
        public static int RenderableCount => s_renderables.Count;
        
        private static readonly List<IRenderableLight> s_lights = [];
        
        
        public static void AddRenderable(IRenderable renderable)
        {
            s_renderables.Add(renderable);
        }
        
        
        public static void AddLight(IRenderableLight light)
        {
            s_lights.Add(light);
        }

        public static void ClearRenderables()
        {
            s_renderables.Clear(); // Clear renderables

            s_lights.Clear(); // Clear lights
        }

        public static IRenderable GetRenderable(int index)
        {
            return s_renderables[index];
        }
        
        
        public static IReadOnlyList<IRenderable> GetRenderables()
        {
            return s_renderables;
        }
        
        
        public static IReadOnlyList<IRenderableLight> GetLights()
        {
            return s_lights;
        }
        
        
        public abstract void Render(Camera camera, in RenderingData data);
    }
}
