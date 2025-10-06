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
        public abstract void Render(Camera camera, in RenderingData data);
    }
}
