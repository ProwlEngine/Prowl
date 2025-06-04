// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Concurrent;
using System.Linq.Expressions;
using System.Reflection;

using Prowl.Vector;

using SoftwareRasterizer.Rasterizer.Engines;

namespace SoftwareRasterizer.Rasterizer;

public abstract class Shader
{
    // Stores statically determined info about a shader's vertex input property
    protected class ShaderVertexInputInfo
    {
        public PropertyInfo Property { get; }
        public int AttributeLocation { get; }
        public Action<Shader, object> Setter { get; } // Compiled setter

        public ShaderVertexInputInfo(PropertyInfo property, int location)
        {
            Property = property;
            AttributeLocation = location;

            // Compile a fast setter for this property
            var shaderInstanceParam = Expression.Parameter(typeof(Shader), "shaderInstance");
            var valueParam = Expression.Parameter(typeof(object), "valueToSet");

            // Cast shaderInstance to the actual declaring type of the property
            var typedShaderInstance = Expression.Convert(shaderInstanceParam, property.DeclaringType);
            var propertyExpression = Expression.Property(typedShaderInstance, property);

            // Cast the valueToSet to the actual property type
            var typedValue = Expression.Convert(valueParam, property.PropertyType);

            var assignExpression = Expression.Assign(propertyExpression, typedValue);

            Setter = Expression.Lambda<Action<Shader, object>>(assignExpression, shaderInstanceParam, valueParam).Compile();
        }
    }

    // Links a cached ShaderVertexInputInfo to a specific data array from a VertexBuffer for the current draw call
    private class ActiveVertexBinding
    {
        public ShaderVertexInputInfo InputInfo { get; }
        public Array DataArray { get; } // The actual data array (e.g., Float3[] positions)

        public ActiveVertexBinding(ShaderVertexInputInfo inputInfo, Array dataArray)
        {
            InputInfo = inputInfo;
            DataArray = dataArray;
        }

        public void SetValue(Shader shaderInstance, int vertexIndex)
        {
            object value = DataArray.GetValue(vertexIndex);
            InputInfo.Setter(shaderInstance, value);
        }
    }

    // Static cache for ShaderVertexInputInfo arrays, keyed by shader type
    private static readonly ConcurrentDictionary<Type, ShaderVertexInputInfo[]> _cachedVertexInputInfos = new();

    // Instance field to hold the active bindings for the current VertexBuffer
    private ActiveVertexBinding[] _activeAttributeBindings;


    public struct ShaderOutput
    {
        public Float4 GlPosition;
        public Float4[] Varyings;
    }

    public struct FragmentOutput
    {
        public Float4 GlFragColor;
        public float? GlFragDepth;
    }

    [AttributeUsage(AttributeTargets.Property)]
    public class VertexInputAttribute(int location) : Attribute
    {
        public int Location = location;
    }

    public abstract bool CanWriteDepth { get; }
    public abstract ShaderOutput VertexShader();
    public abstract FragmentOutput FragmentShader(Float4[] varyings, FragmentContext context);


    public void BindVertexAttributes(object[] vertexBufferAttributes)
    {
        Type currentShaderType = GetType();

        if (!_cachedVertexInputInfos.TryGetValue(currentShaderType, out var infos))
        {
            var properties = currentShaderType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetCustomAttribute<VertexInputAttribute>() != null);

            infos = properties
                .Select(prop =>
                {
                    var attr = prop.GetCustomAttribute<VertexInputAttribute>();
                    return new ShaderVertexInputInfo(prop, attr.Location);
                })
                .OrderBy(info => info.AttributeLocation) // Optional, but good for consistency
                .ToArray();
            _cachedVertexInputInfos.TryAdd(currentShaderType, infos);
        }

        // Create active bindings for this instance using the cached infos and current buffer data
        _activeAttributeBindings = infos
            .Where(info => info.AttributeLocation < vertexBufferAttributes.Length)
            .Select(info => new ActiveVertexBinding(info, (Array)vertexBufferAttributes[info.AttributeLocation]))
            .ToArray();
    }

    /// <summary>
    /// Set the vertex attributes for the current vertex
    /// The data bound to each attribute is an array of values for each vertex
    /// This method sets the value of the property to the value of the current vertex index
    /// instead of the entire array
    /// </summary>
    internal void SetVertexAttributes(int vertexIndex)
    {
        if (_activeAttributeBindings == null)
        {
            // This might happen if BindVertexAttributes was not called or if the shader has no vertex inputs.
            return;
        }

        foreach (var binding in _activeAttributeBindings)
        {
            binding.SetValue(this, vertexIndex); // Uses the compiled setter
        }
    }
}
