// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Veldrid;


namespace Prowl.Runtime.RenderPipelines
{
    public abstract class RenderPipeline<T> : EngineObject
    {
        public abstract void Render(Framebuffer target, T data);
    }
}
