// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Veldrid;

namespace Prowl.Runtime.Rendering;

public static class ComputeDispatcher
{

    public static void Dispatch(ComputeDescriptor descriptor, int kernelIndex, uint groupsX, uint groupsY, uint groupsZ, GraphicsFence? fence = null)
    {
        CommandList cl = Graphics.GetCommandList();

        ComputeKernel kernel = descriptor.Shader.Res.GetKernel(kernelIndex);
        ComputeVariant variant = kernel.GetVariant(descriptor._localKeywords);
        ComputePipeline pipeline = ComputePipelineCache.GetPipeline(variant);

        cl.SetPipeline(pipeline.GetPipeline());

        BindableResourceSet bindable = pipeline.CreateResources();

        List<IDisposable> toDispose = new();

        ResourceSet set = bindable.BindResources(cl, descriptor._properties, toDispose);

        cl.SetComputeResourceSet(0, set);

        cl.Dispatch(groupsX, groupsY, groupsZ);

        Graphics.SubmitCommandList(cl, fence);

        Graphics.SubmitResourcesForDisposal(toDispose);
    }
}
