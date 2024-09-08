// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Prowl.Runtime;

public abstract class Light : MonoBehaviour, IRenderableLight
{

    public Color color = Color.white;
    public float intensity = 8.0f;
    public float shadowBias = 0.00001f;
    public float shadowNormalBias = 0.0025f;
    public bool castShadows = true;


    public override void Update()
    {
        RenderPipelines.RenderPipeline.AddLight(this);
    }


    public abstract Material GetMaterial();

    public abstract void GetRenderingData(out LightType type, out Vector3 facingDirection);

    public abstract void GetCullingData(out bool isRenderable, out bool isCullable, out Bounds bounds);
}
