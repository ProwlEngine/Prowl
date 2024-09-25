// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Text;

using Veldrid;

namespace Prowl.Runtime;


public enum ShaderPropertyType
{
    Float,
    Vector2,
    Vector3,
    Vector4,
    Color,
    Matrix,
    Texture2D,
    Texture3D
}


public struct SerializedShaderProperty
{
    public string name;
    public string displayName;
    public ShaderPropertyType propertyType;
    public object defaultProperty;
}


public sealed class Shader : EngineObject, ISerializationCallbackReceiver
{
    [SerializeField, HideInInspector]
    private readonly SerializedShaderProperty[] _properties;
    public IEnumerable<SerializedShaderProperty> Properties => _properties;


    [SerializeField, HideInInspector]
    private readonly ShaderPass[] _passes;
    public IEnumerable<ShaderPass> Passes => _passes;


    private readonly Dictionary<string, int> _nameIndexLookup = new();
    private readonly Dictionary<string, List<int>> _tagIndexLookup = new();


    internal Shader() : base("New Shader") { }

    public Shader(string name, SerializedShaderProperty[] properties, ShaderPass[] passes) : base(name)
    {
        this._properties = properties;
        this._passes = passes;

        OnAfterDeserialize();
    }

    private void RegisterPass(ShaderPass pass, int index)
    {
        if (!string.IsNullOrWhiteSpace(pass.Name))
        {
            if (!_nameIndexLookup.TryAdd(pass.Name, index))
                throw new InvalidOperationException($"Pass with name {pass.Name} conflicts with existing pass at index {_nameIndexLookup[pass.Name]}. Ensure no two passes have equal names.");
        }

        foreach (var pair in pass.Tags)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            if (!_tagIndexLookup.TryGetValue(pair.Key, out _))
                _tagIndexLookup.Add(pair.Key, []);

            _tagIndexLookup[pair.Key].Add(index);
        }
    }

    public ShaderPass GetPass(int passIndex)
    {
        return _passes[passIndex];
    }

    public int GetPassIndex(string passName)
    {
        return _nameIndexLookup.GetValueOrDefault(passName, -1);
    }

    public int? GetPassWithTag(string tag, string? tagValue = null)
    {
        List<int> passes = GetPassesWithTag(tag, tagValue);
        return passes.Count > 0 ? passes[0] : null;
    }

    public List<int> GetPassesWithTag(string tag, string? tagValue = null)
    {
        List<int> passes = [];

        if (_tagIndexLookup.TryGetValue(tag, out List<int> passesWithTag))
        {
            foreach (int index in passesWithTag)
            {
                ShaderPass pass = this._passes[index];

                if (pass.HasTag(tag, tagValue))
                    passes.Add(index);
            }
        }

        return passes;
    }

    public void OnBeforeSerialize() { }

    public void OnAfterDeserialize()
    {
        for (int i = 0; i < _passes.Length; i++)
            RegisterPass(_passes[i], i);
    }

    public string GetStringRepresentation()
    {
        StringBuilder builder = new();

        builder.Append($"Shader \"{Name}\"\n\n");

        builder.Append("Properties\n{\n");

        foreach (SerializedShaderProperty property in Properties)
        {
            builder.Append($"\t{property.name}(\"{property.displayName}\", {property.propertyType})\n");
        }

        builder.Append("}\n\n");

        foreach (ShaderPass pass in Passes)
        {
            builder.Append($"Pass {pass.Name}\n{{\n");

            builder.Append("\tTags { ");
            foreach (var pair in pass.Tags)
            {
                builder.Append($"\"{pair.Key}\" = \"{pair.Value}\", ");
            }
            builder.Append("}\n\n");

            builder.Append("\tFeatures \n\t{\n");
            foreach (var pair in pass.Keywords)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                    continue;

                builder.Append($"\t\t{pair.Key} [ {string.Join(" ", pair.Value)} ]\n");
            }
            builder.Append("\t}\n\n");

            builder.Append($"\tZTest {pass.DepthClipEnabled}\n\n");

            builder.Append($"\tCull {pass.CullMode}\n\n");

            if (pass.Blend.AttachmentStates[0].BlendEnabled)
            {
                BlendAttachmentDescription desc = pass.Blend.AttachmentStates[0];

                builder.Append("\tBlend\n\t{\n");
                builder.Append($"\t\tMode Alpha {desc.AlphaFunction}\n");
                builder.Append($"\t\tMode Color {desc.ColorFunction}\n\n");
                builder.Append($"\t\tSrc Alpha {desc.SourceAlphaFactor}\n");
                builder.Append($"\t\tSrc Color {desc.SourceColorFactor}\n\n");
                builder.Append($"\t\tDest Alpha {desc.DestinationAlphaFactor}\n");
                builder.Append($"\t\tDest Color {desc.DestinationColorFactor}\n\n");

                builder.Append($"\t\tMask {desc.ColorWriteMask}\n");
                builder.Append("\t}\n\n");
            }

            builder.Append("\tDepthStencil\n\t{\n");

            DepthStencilStateDescription dDesc = pass.DepthStencilState;

            if (dDesc.DepthTestEnabled)
                builder.Append($"\t\tDepthTest {dDesc.DepthComparison}\n");

            builder.Append($"\t\tDepthWrite {dDesc.DepthWriteEnabled}\n\n");

            builder.Append($"\t\tComparison {dDesc.StencilFront.Comparison} {dDesc.StencilBack.Comparison}\n");
            builder.Append($"\t\tDepthFail {dDesc.StencilFront.DepthFail} {dDesc.StencilBack.DepthFail}\n");
            builder.Append($"\t\tFail {dDesc.StencilFront.Fail} {dDesc.StencilBack.Fail}\n");
            builder.Append($"\t\tPass {dDesc.StencilFront.Pass} {dDesc.StencilBack.Pass}\n\n");

            builder.Append($"\t\tReadMask {dDesc.StencilReadMask}\n");
            builder.Append($"\t\tRef {dDesc.StencilReference}\n");
            builder.Append($"\t\tWriteMask {dDesc.StencilWriteMask}\n");

            builder.Append("\t}\n");

            builder.Append("\tInputs\n\t{\n");

            builder.Append("\t\tVertexInputs\n\t\t{\n");
            foreach (var input in pass.GetVariant(KeywordState.Empty).VertexInputs)
            {
                builder.Append($"\t\t\t{input.semantic}\n");
            }
            builder.Append("\t\t}\n\n");

            builder.Append("\t\tUniforms\n\t\t{\n");
            foreach (var uniform in pass.GetVariant(KeywordState.Empty).Uniforms)
            {
                builder.Append("\t\t\t" + uniform.ToString().Replace("\n", "\n\t\t\t"));
            }
            builder.Append("\n\t\t}\n");

            builder.Append("\t}\n");

            builder.Append("}\n\n");
        }

        return builder.ToString();
    }
}
