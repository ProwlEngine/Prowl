// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.


namespace Prowl.Runtime.RenderPipelines
{
    public class DefaultRenderPipeline : RenderPipeline
    {
        private static Shader s_DefaultUnlit;

        public override void Render(Framebuffer target, Camera camera, in RenderingData data)
        {
            s_DefaultUnlit ??= Application.AssetProvider.LoadAsset<GameObject>($"Defaults/DefaultUnlit.shader");
        }
    }
}
