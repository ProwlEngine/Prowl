// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Veldrid;

using Matrix4x4F = System.Numerics.Matrix4x4;
using Vector2F = System.Numerics.Vector2;
using Vector3F = System.Numerics.Vector3;
using Vector4F = System.Numerics.Vector4;

namespace Prowl.Runtime.Rendering;


public class ComputeDescriptor
{
    public AssetRef<ComputeShader> Shader;

    [NonSerialized]
    internal PropertyState _properties;

    [NonSerialized]
    internal KeywordState _localKeywords;

    [NonSerialized]
    internal BindableResourceSet _resources;

    [NonSerialized]
    internal ComputePipeline _pipeline;

    [NonSerialized]
    internal int _kernelIndex;


    public ComputeDescriptor(AssetRef<ComputeShader> shader, PropertyState? properties = null, KeywordState? keywords = null)
    {
        if (shader.Res == null)
            throw new ArgumentNullException(nameof(shader));

        Shader = shader;
        _properties = properties ?? new();
        _localKeywords = keywords ?? KeywordState.Empty;
        RecreateResources();
    }

    private void RecreateResources()
    {
        ComputeKernel kernel = Shader.Res.GetKernel(_kernelIndex);
        ComputeVariant variant = kernel.GetVariant(_localKeywords);
        ComputePipeline pipeline = ComputePipelineCache.GetPipeline(variant);

        if (_pipeline != pipeline)
        {
            _pipeline = pipeline;
            _resources = pipeline.CreateResources();
        }
    }

    public void SetKernel(int kernelIndex)
    {
        _kernelIndex = kernelIndex;
        RecreateResources();
    }

    public void SetKeyword(string keyword, string value)
    {
        _localKeywords.SetKey(keyword, value);
        RecreateResources();
    }

    public void SetColor(string name, Color value) => _properties.SetColor(name, value);
    public void SetVector(string name, Vector2F value) => _properties.SetVector(name, value);
    public void SetVector(string name, Vector3F value) => _properties.SetVector(name, value);
    public void SetVector(string name, Vector4F value) => _properties.SetVector(name, value);
    public void SetFloat(string name, float value) => _properties.SetFloat(name, value);
    public void SetInt(string name, int value) => _properties.SetInt(name, value);
    public void SetMatrix(string name, Matrix4x4F value) => _properties.SetMatrix(name, value);
    public void SetTexture(string name, Texture value) => _properties.SetTexture(name, value);


    public void SetFloatArray(string name, float[] values) => _properties.SetFloatArray(name, values);
    public void SetIntArray(string name, int[] values) => _properties.SetIntArray(name, values);
    public void SetVectorArray(string name, Vector2F[] values) => _properties.SetVectorArray(name, values);
    public void SetVectorArray(string name, Vector3F[] values) => _properties.SetVectorArray(name, values);
    public void SetVectorArray(string name, Vector4F[] values) => _properties.SetVectorArray(name, values);
    public void SetColorArray(string name, Color[] values) => _properties.SetColorArray(name, values);
    public void SetMatrixArray(string name, Matrix4x4F[] values) => _properties.SetMatrixArray(name, values);

    public void SetBuffer(string name, GraphicsBuffer buffer) => _properties.SetBuffer(name, buffer);
}
