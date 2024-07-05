using System;
using System.Collections.Generic;
using Veldrid;

namespace Prowl.Runtime
{
    public abstract class ShaderResource
    {
        public abstract void GetDescription(List<ResourceLayoutElementDescription> elements);

        public abstract void BindResource(CommandList commandList, List<BindableResource> resources, RenderState state);

        public abstract string GetResourceName();
    }
}