// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using Veldrid;

namespace Prowl.Runtime.Rendering;

public static class ComputeDispatcher
{

    public static void Dispatch(ComputeDescriptor descriptor, int kernelIndex, uint threadsX, uint threadsY, uint threadsZ)
    {
        CommandList cl = Graphics.GetCommandList();

        ComputeKernel kernel = descriptor.Shader.Res.GetKernel(kernelIndex);
        ComputeVariant variant = kernel.GetVariant(descriptor._localKeywords);
        ComputePipeline pipeline = ComputePipelineCache.GetPipeline(variant);

        cl.SetPipeline(pipeline.GetPipeline());

        BindableResourceSet set = pipeline.CreateResources();

        List<IDisposable> toDispose = new();
        set.Bind(cl, descriptor._properties, toDispose);

        Graphics.SubmitCommandList(cl);
    }
}
