// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;

using Prowl.Vector;

namespace Prowl.Runtime.Resources;

public enum MaterialPropertyType
{
    Float,
    Int,
    Vector2,
    Vector3,
    Vector4,
    Color,
    Matrix,
    Texture2D,
    Texture3D,
    TextureCube
}

/// <summary>
/// A single serializable material property value. Acts as a tagged union the
/// <see cref="Type"/> selects which backing field is meaningful. Numeric values
/// (float/int/vector/color) are packed into <see cref="Value"/>; matrices live in
/// <see cref="Matrix"/>; textures keep their <see cref="AssetRef{T}"/> so unloaded
/// assets still round-trip through serialization. This is the serializable mirror of
/// the values a <see cref="Material"/> later resolves into a runtime
/// <see cref="Prowl.Graphite.PropertySet"/> via <see cref="Material.BuildPropertySet"/>.
/// </summary>
public struct MaterialProperty
{
    public MaterialPropertyType Type;
    public Float4 Value;
    public Float4x4 Matrix;
    public AssetRef<Texture2D> Tex2D;
    public AssetRef<Texture3D> Tex3D;
    public AssetRef<Cubemap> TexCube;

    public static MaterialProperty FromFloat(float v) => new() { Type = MaterialPropertyType.Float, Value = new Float4(v, 0, 0, 0) };
    public static MaterialProperty FromInt(int v) => new() { Type = MaterialPropertyType.Int, Value = new Float4(v, 0, 0, 0) };
    public static MaterialProperty FromVector(Float2 v) => new() { Type = MaterialPropertyType.Vector2, Value = new Float4(v.X, v.Y, 0, 0) };
    public static MaterialProperty FromVector(Float3 v) => new() { Type = MaterialPropertyType.Vector3, Value = new Float4(v.X, v.Y, v.Z, 0) };
    public static MaterialProperty FromVector(Float4 v) => new() { Type = MaterialPropertyType.Vector4, Value = v };
    public static MaterialProperty FromColor(Color v) => new() { Type = MaterialPropertyType.Color, Value = v };
    public static MaterialProperty FromMatrix(Float4x4 v) => new() { Type = MaterialPropertyType.Matrix, Matrix = v };
    public static MaterialProperty FromTexture(AssetRef<Texture2D> v) => new() { Type = MaterialPropertyType.Texture2D, Tex2D = v };
    public static MaterialProperty FromTexture3D(AssetRef<Texture3D> v) => new() { Type = MaterialPropertyType.Texture3D, Tex3D = v };
    public static MaterialProperty FromTextureCube(AssetRef<Cubemap> v) => new() { Type = MaterialPropertyType.TextureCube, TexCube = v };

    public override int GetHashCode()
    {
        HashCode hc = new();
        hc.Add(Type);

        switch (Type)
        {
            case MaterialPropertyType.Matrix: hc.Add(Matrix); break;
            case MaterialPropertyType.Texture2D: hc.Add(Tex2D); break;
            case MaterialPropertyType.Texture3D: hc.Add(Tex3D); break;
            case MaterialPropertyType.TextureCube: hc.Add(TexCube); break;
            default: hc.Add(Value); break;
        }

        return hc.ToHashCode();
    }
}
