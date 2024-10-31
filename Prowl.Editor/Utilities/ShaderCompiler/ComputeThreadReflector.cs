// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime;

using SPIRVCross.NET;

using Veldrid;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Pipelines;

using SpecializationConstant = SPIRVCross.NET.SpecializationConstant;

#pragma warning disable

namespace Prowl.Editor;

public static class ComputeThreadReflector
{
    public static void GetThreadgroupSizes(Reflector reflector, out uint xSize, out uint ySize, out uint zSize)
    {
        xSize = reflector.GetExecutionModeArgumentByIndex(ExecutionMode.LocalSize, 0);
        ySize = reflector.GetExecutionModeArgumentByIndex(ExecutionMode.LocalSize, 1);
        zSize = reflector.GetExecutionModeArgumentByIndex(ExecutionMode.LocalSize, 2);
    }
}
