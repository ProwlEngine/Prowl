// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Matrix4x4F = System.Numerics.Matrix4x4;
using Vector2F = System.Numerics.Vector2;
using Vector3F = System.Numerics.Vector3;
using Vector4F = System.Numerics.Vector4;

namespace Prowl.Runtime;

public sealed class Material : EngineObject
{
    public AssetRef<Shader> Shader;
    public readonly PropertyState Properties;

    [NonSerialized]
    public readonly KeywordState LocalKeywords;

    internal Material() : base("New Material")
    {
        Properties = new();
        LocalKeywords = KeywordState.Default;
    }

    public Material(AssetRef<Shader> shader, PropertyState? properties = null, KeywordState? keywords = null) : base("New Material")
    {
        if (shader.Res == null)
            throw new ArgumentNullException(nameof(shader));

        Shader = shader;
        Properties = properties ?? new();
        LocalKeywords = keywords ?? KeywordState.Default;
    }

    public void SetKeyword(string keyword, string value) => LocalKeywords.SetKey(keyword, value);

    public void SetColor(string name, Color value) => Properties.SetColor(name, value);
    public void SetVector(string name, Vector2F value) => Properties.SetVector(name, value);
    public void SetVector(string name, Vector3F value) => Properties.SetVector(name, value);
    public void SetVector(string name, Vector4F value) => Properties.SetVector(name, value);
    public void SetFloat(string name, float value) => Properties.SetFloat(name, value);
    public void SetInt(string name, int value) => Properties.SetInt(name, value);
    public void SetMatrix(string name, Matrix4x4F value) => Properties.SetMatrix(name, value);
    public void SetTexture(string name, AssetRef<Texture> value) => Properties.SetTexture(name, value);


    public void SetFloatArray(string name, float[] values) => Properties.SetFloatArray(name, values);
    public void SetIntArray(string name, int[] values) => Properties.SetIntArray(name, values);
    public void SetVectorArray(string name, Vector2F[] values) => Properties.SetVectorArray(name, values);
    public void SetVectorArray(string name, Vector3F[] values) => Properties.SetVectorArray(name, values);
    public void SetVectorArray(string name, Vector4F[] values) => Properties.SetVectorArray(name, values);
    public void SetColorArray(string name, Color[] values) => Properties.SetColorArray(name, values);
    public void SetMatrixArray(string name, Matrix4x4F[] values) => Properties.SetMatrixArray(name, values);


    //public CompoundTag Serialize(string tagName, TagSerializer.SerializationContext ctx)
    //{
    //    CompoundTag compoundTag = new CompoundTag(tagName);
    //    compoundTag.Add(TagSerializer.Serialize(Shader, "Shader", ctx));
    //    compoundTag.Add(TagSerializer.Serialize(PropertyBlock, "PropertyBlock", ctx));
    //    return compoundTag;
    //}
    //
    //public void Deserialize(CompoundTag value, TagSerializer.SerializationContext ctx)
    //{
    //    Shader = TagSerializer.Deserialize<AssetRef<Shader>>(value["Shader"], ctx);
    //    PropertyBlock = TagSerializer.Deserialize<MaterialPropertyBlock>(value["PropertyBlock"], ctx);
    //}
}
