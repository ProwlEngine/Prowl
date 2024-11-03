// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;

using Veldrid;

namespace Prowl.Runtime.Rendering;


public static class ComputeDispatcher
{
    public static void Dispatch(ComputeDescriptor descriptor, int kernelIndex, uint groupsX, uint groupsY, uint groupsZ, GraphicsFence? fence = null)
    {
        CommandList cl = Graphics.GetCommandList();

        Dispatch(descriptor, cl, kernelIndex, groupsX, groupsY, groupsZ, fence);

        Graphics.SubmitResourceForDisposal(cl);
    }


    internal static void Dispatch(ComputeDescriptor descriptor, CommandList list, int kernelIndex, uint groupsX, uint groupsY, uint groupsZ, GraphicsFence? fence = null)
    {
        ComputeKernel kernel = descriptor.Shader.Res.GetKernel(kernelIndex);
        ComputeVariant variant = kernel.GetVariant(descriptor._localKeywords);
        ComputePipeline pipeline = ComputePipelineCache.GetPipeline(variant);

        list.SetPipeline(pipeline.GetPipeline());

        BindableResourceSet bindable = pipeline.CreateResources();

        List<IDisposable> toDispose = [];

        ResourceSet set = bindable.BindResources(list, descriptor._properties, toDispose);

        list.SetComputeResourceSet(0, set);

        list.Dispatch(groupsX, groupsY, groupsZ);

        Graphics.SubmitCommandList(list, fence);

        Graphics.SubmitResourcesForDisposal(toDispose);
    }
}
