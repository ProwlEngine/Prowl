// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Vector;

namespace Prowl.Runtime.Resources;

/// <summary>
/// Editor-side typed read helpers over a material's serialized <see cref="MaterialProperty"/>
/// dictionary. Mirrors the accessor API the inspector relied on before
/// <see cref="Material"/> moved its overrides into a plain dictionary. Getters return the
/// type's default when the property is absent or stored as a different type.
/// </summary>
internal static class MaterialPropertyReadExtensions
{
    public static bool HasFloat(this Dictionary<string, MaterialProperty> p, string name)
        => p.TryGetValue(name, out MaterialProperty v) && v.Type == MaterialPropertyType.Float;

    public static float GetFloat(this Dictionary<string, MaterialProperty> p, string name)
        => p.TryGetValue(name, out MaterialProperty v) ? (float)v.Value.X : 0f;

    public static bool HasInt(this Dictionary<string, MaterialProperty> p, string name)
        => p.TryGetValue(name, out MaterialProperty v) && v.Type == MaterialPropertyType.Int;

    public static int GetInt(this Dictionary<string, MaterialProperty> p, string name)
        => p.TryGetValue(name, out MaterialProperty v) ? (int)v.Value.X : 0;

    public static bool HasColor(this Dictionary<string, MaterialProperty> p, string name)
        => p.TryGetValue(name, out MaterialProperty v) && v.Type == MaterialPropertyType.Color;

    public static Color GetColor(this Dictionary<string, MaterialProperty> p, string name)
        => p.TryGetValue(name, out MaterialProperty v) ? (Color)v.Value : default;

    public static bool HasVector2(this Dictionary<string, MaterialProperty> p, string name)
        => p.TryGetValue(name, out MaterialProperty v) && v.Type == MaterialPropertyType.Vector2;

    public static Float2 GetVector2(this Dictionary<string, MaterialProperty> p, string name)
        => p.TryGetValue(name, out MaterialProperty v) ? new Float2((float)v.Value.X, (float)v.Value.Y) : default;

    public static bool HasVector3(this Dictionary<string, MaterialProperty> p, string name)
        => p.TryGetValue(name, out MaterialProperty v) && v.Type == MaterialPropertyType.Vector3;

    public static Float3 GetVector3(this Dictionary<string, MaterialProperty> p, string name)
        => p.TryGetValue(name, out MaterialProperty v) ? new Float3((float)v.Value.X, (float)v.Value.Y, (float)v.Value.Z) : default;

    public static bool HasVector4(this Dictionary<string, MaterialProperty> p, string name)
        => p.TryGetValue(name, out MaterialProperty v) && v.Type == MaterialPropertyType.Vector4;

    public static Float4 GetVector4(this Dictionary<string, MaterialProperty> p, string name)
        => p.TryGetValue(name, out MaterialProperty v) ? v.Value : default;

    public static bool HasTexture(this Dictionary<string, MaterialProperty> p, string name)
        => p.TryGetValue(name, out MaterialProperty v) && v.Type == MaterialPropertyType.Texture2D;

    public static Texture2D GetTexture(this Dictionary<string, MaterialProperty> p, string name)
        => p.TryGetValue(name, out MaterialProperty v) ? v.Tex2D.Res : null;

    public static AssetRef<Texture2D> GetTextureRef(this Dictionary<string, MaterialProperty> p, string name)
        => p.TryGetValue(name, out MaterialProperty v) ? v.Tex2D : default;

    public static bool HasTexture3D(this Dictionary<string, MaterialProperty> p, string name)
        => p.TryGetValue(name, out MaterialProperty v) && v.Type == MaterialPropertyType.Texture3D;

    public static Texture3D GetTexture3D(this Dictionary<string, MaterialProperty> p, string name)
        => p.TryGetValue(name, out MaterialProperty v) ? v.Tex3D.Res : null;

    public static bool RemoveProperty(this Dictionary<string, MaterialProperty> p, string name)
        => p.Remove(name);
}
