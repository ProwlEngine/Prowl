// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

using Veldrid;


namespace Prowl.Runtime.RenderPipelines;

public struct RenderingData
{
    public required Vector2 TargetResolution;

    public bool IsSceneViewCamera;
    public bool DisplayGrid;
    public Matrix4x4 GridMatrix;
    public Color GridColor;
    public Vector3 GridSizes;
}


public struct RenderBatch
{
    public Material material;
    public List<IRenderable> renderables;
}


public abstract class RenderPipeline : EngineObject
{
    private static readonly Dictionary<Material, List<IRenderable>> s_batchedRenderables = [];

    private static readonly List<IRenderableLight> s_lights = [];


    public static void AddRenderable(IRenderable renderable)
    {
        Material material = renderable.GetMaterial();

        if (!s_batchedRenderables.TryGetValue(material, out List<IRenderable> renderables))
        {
            renderables = [];
            s_batchedRenderables.Add(material, renderables);
        }

        renderables.Add(renderable);
    }


    public static void AddLight(IRenderableLight light)
    {
        s_lights.Add(light);
    }


    public static void ClearRenderables()
    {
        foreach (List<IRenderable> batch in s_batchedRenderables.Values)
            batch.Clear();

        s_lights.Clear();
    }


    public static IEnumerable<RenderBatch> EnumerateBatches()
    {
        return s_batchedRenderables.Select(x => Unsafe.As<KeyValuePair<Material, List<IRenderable>>, RenderBatch>(ref x));
    }


    public static List<IRenderable> GetRenderables(Material material)
    {
        return s_batchedRenderables[material];
    }


    public static List<IRenderableLight> GetLights()
    {
        return s_lights;
    }


    public abstract void Render(Framebuffer target, Camera camera, in RenderingData data);
}
