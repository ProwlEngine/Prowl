// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Text;

using Prowl.Runtime.Rendering;

using Veldrid;
using Prowl.Echo;

namespace Prowl.Runtime;

public sealed class ComputeShader : EngineObject, ISerializationCallbackReceiver
{
    [SerializeField, HideInInspector]
    private readonly ComputeKernel[] _kernels;
    public IEnumerable<ComputeKernel> Kernels => _kernels;
    public int KernelCount => _kernels.Length;

    private readonly Dictionary<string, int> _nameIndexLookup = new();


    internal ComputeShader() : base("New Compute Shader") { }

    public ComputeShader(string name, ComputeKernel[] kernels) : base(name)
    {
        _kernels = kernels;
        OnAfterDeserialize();
    }

    private void RegisterKernel(ComputeKernel kernel, int index)
    {
        if (!string.IsNullOrWhiteSpace(kernel.Name))
        {
            if (!_nameIndexLookup.TryAdd(kernel.Name, index))
                throw new InvalidOperationException($"Kernel with name {kernel.Name} conflicts with existing kernel at index {_nameIndexLookup[kernel.Name]}. Ensure no two passes have equal names.");
        }
    }

    public ComputeKernel GetKernel(int kernelIndex)
    {
        return _kernels[kernelIndex];
    }

    public int GetKernelIndex(string kernelName)
    {
        return _nameIndexLookup.GetValueOrDefault(kernelName, -1);
    }


    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize()
    {
        for (int i = 0; i < _kernels.Length; i++)
            RegisterKernel(_kernels[i], i);
    }
}
