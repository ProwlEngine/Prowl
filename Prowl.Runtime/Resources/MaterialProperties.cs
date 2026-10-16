// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Collections.Generic;

using Prowl.Echo;
using Prowl.Vector;

namespace Prowl.Runtime.Resources;

/// <summary>The values a material sets, by property name, as a material file stores them.</summary>
public sealed class MaterialProperties
{
    [SerializeField] internal Dictionary<string, Color> _colors = [];
    [SerializeField] internal Dictionary<string, Float2> _vectors2 = [];
    [SerializeField] internal Dictionary<string, Float3> _vectors3 = [];
    [SerializeField] internal Dictionary<string, Float4> _vectors4 = [];
    [SerializeField] internal Dictionary<string, float> _floats = [];
    [SerializeField] internal Dictionary<string, int> _ints = [];
    [SerializeField] internal Dictionary<string, Float4x4> _matrices = [];
    [SerializeField] internal Dictionary<string, Texture2D> _textures = [];
    [SerializeField] internal Dictionary<string, Texture3D> _textures3D = [];
    [SerializeField] internal Dictionary<string, Cubemap> _texturesCube = [];

    public MaterialProperties() { }

    public MaterialProperties(MaterialProperties clone)
    {
        _colors = new(clone._colors);
        _vectors2 = new(clone._vectors2);
        _vectors3 = new(clone._vectors3);
        _vectors4 = new(clone._vectors4);
        _floats = new(clone._floats);
        _ints = new(clone._ints);
        _matrices = new(clone._matrices);
        _textures = new(clone._textures);
        _textures3D = new(clone._textures3D);
        _texturesCube = new(clone._texturesCube);
    }

    public bool IsEmpty => _colors.Count == 0 && _vectors2.Count == 0 && _vectors3.Count == 0 && _vectors4.Count == 0 && _floats.Count == 0
        && _ints.Count == 0 && _matrices.Count == 0 && _textures.Count == 0 && _textures3D.Count == 0 && _texturesCube.Count == 0;

    public Color GetColor(string name) => _colors.TryGetValue(name, out Color value) ? value : Color.White;
    public Float2 GetVector2(string name) => _vectors2.GetValueOrDefault(name);
    public Float3 GetVector3(string name) => _vectors3.GetValueOrDefault(name);
    public Float4 GetVector4(string name) => _vectors4.GetValueOrDefault(name);
    public float GetFloat(string name) => _floats.GetValueOrDefault(name);
    public int GetInt(string name) => _ints.GetValueOrDefault(name);
    public Float4x4 GetMatrix(string name) => _matrices.TryGetValue(name, out Float4x4 value) ? value : Float4x4.Identity;
    public Texture2D? GetTexture(string name) => _textures.GetValueOrDefault(name);
    public Texture3D? GetTexture3D(string name) => _textures3D.GetValueOrDefault(name);
    public Cubemap? GetTextureCube(string name) => _texturesCube.GetValueOrDefault(name);

    public bool HasColor(string name) => _colors.ContainsKey(name);
    public bool HasVector2(string name) => _vectors2.ContainsKey(name);
    public bool HasVector3(string name) => _vectors3.ContainsKey(name);
    public bool HasVector4(string name) => _vectors4.ContainsKey(name);
    public bool HasFloat(string name) => _floats.ContainsKey(name);
    public bool HasInt(string name) => _ints.ContainsKey(name);
    public bool HasMatrix(string name) => _matrices.ContainsKey(name);
    public bool HasTexture(string name) => _textures.ContainsKey(name);
    public bool HasTexture3D(string name) => _textures3D.ContainsKey(name);
    public bool HasTextureCube(string name) => _texturesCube.ContainsKey(name);

    /// <summary>Stores one value under a name, dropping any value of another kind it had.</summary>
    internal void SetColor(string name, Color value) { Remove(name); _colors[name] = value; }
    internal void SetVector(string name, Float2 value) { Remove(name); _vectors2[name] = value; }
    internal void SetVector(string name, Float3 value) { Remove(name); _vectors3[name] = value; }
    internal void SetVector(string name, Float4 value) { Remove(name); _vectors4[name] = value; }
    internal void SetFloat(string name, float value) { Remove(name); _floats[name] = value; }
    internal void SetInt(string name, int value) { Remove(name); _ints[name] = value; }
    internal void SetMatrix(string name, Float4x4 value) { Remove(name); _matrices[name] = value; }
    internal void SetTexture(string name, Texture2D? value) { Remove(name); if (value != null) _textures[name] = value; }
    internal void SetTexture3D(string name, Texture3D? value) { Remove(name); if (value != null) _textures3D[name] = value; }
    internal void SetTextureCube(string name, Cubemap? value) { Remove(name); if (value != null) _texturesCube[name] = value; }

    public void Remove(string name)
    {
        _colors.Remove(name);
        _vectors2.Remove(name);
        _vectors3.Remove(name);
        _vectors4.Remove(name);
        _floats.Remove(name);
        _ints.Remove(name);
        _matrices.Remove(name);
        _textures.Remove(name);
        _textures3D.Remove(name);
        _texturesCube.Remove(name);
    }

    public IEnumerable<string> EnumerateNames()
    {
        foreach (string name in _colors.Keys) yield return name;
        foreach (string name in _vectors2.Keys) yield return name;
        foreach (string name in _vectors3.Keys) yield return name;
        foreach (string name in _vectors4.Keys) yield return name;
        foreach (string name in _floats.Keys) yield return name;
        foreach (string name in _ints.Keys) yield return name;
        foreach (string name in _matrices.Keys) yield return name;
        foreach (string name in _textures.Keys) yield return name;
        foreach (string name in _textures3D.Keys) yield return name;
        foreach (string name in _texturesCube.Keys) yield return name;
    }
}
