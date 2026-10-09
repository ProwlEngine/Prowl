// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Runtime.InteropServices;

using Prowl.Runtime.Resources;
using Prowl.Vector;

namespace Prowl.Runtime.Rendering;

/// <summary>
/// A growable array of vec4 that shaders index, kept in a storage buffer where the stage reading it can use one and
/// in a float texture otherwise. Writes land in a CPU copy and only the changed range goes up. Bound either as a
/// global every shader sees, or onto one draw's <see cref="PropertyState"/>.
/// <para/>
/// A shader declares both forms and picks with the storage define of its stage, buffer block <c>BlockName</c> holding
/// <c>vec4 Member[]</c>, or <c>sampler2D TextureName</c> plus <c>int TextureName + "Shift"</c>, the log2 of its width:
/// <code>
/// #ifdef PROWL_FRAGMENT_STORAGE_BUFFERS
/// layout(std430) readonly buffer ProwlLights { vec4 _Lights[]; };
/// vec4 LightTexel(int i) { return _Lights[i]; }
/// #else
/// uniform sampler2D _LightsTex; uniform int _LightsTexShift;
/// vec4 LightTexel(int i) { return texelFetch(_LightsTex, ivec2(i &amp; ((1 &lt;&lt; _LightsTexShift) - 1), i &gt;&gt; _LightsTexShift), 0); }
/// #endif
/// </code>
/// </summary>
internal sealed class ShaderDataTable : IDisposable
{
    private const int TextureWidthShift = 10;
    private const int TextureWidth = 1 << TextureWidthShift;

    private readonly string _blockName;
    private readonly string _textureName;
    private readonly GraphicsFeature _storage;

    private Float4[] _data = [];
    private int _dirtyLo = int.MaxValue, _dirtyHi = -1;
    private GraphicsBuffer? _buffer;
    private Texture2D? _texture;
    private bool _usesBuffer;

    /// <summary>Texels the table holds, the most that can be written.</summary>
    public int Capacity => _data.Length;

    /// <param name="blockName">The storage block the shaders declare.</param>
    /// <param name="textureName">The sampler the shaders fall back to.</param>
    /// <param name="storage">The storage buffer feature of the stage that reads the table.</param>
    public ShaderDataTable(string blockName, string textureName, GraphicsFeature storage)
    {
        _blockName = blockName;
        _textureName = textureName;
        _storage = storage;
    }

    /// <summary>
    /// Grows to hold at least <paramref name="texels"/>, keeping what was written. Growing reuploads everything, so
    /// a table that grows again at least doubles.
    /// </summary>
    public void EnsureCapacity(int texels)
    {
        if (texels <= _data.Length) return;
        int rows = (texels + TextureWidth - 1) >> TextureWidthShift;
        int capacity = Math.Max(rows << TextureWidthShift, _data.Length * 2);
        Array.Resize(ref _data, capacity);
        ReleaseGpu();
        MarkDirty(0, _data.Length);
    }

    public Float4 this[int texel]
    {
        get => _data[texel];
        set
        {
            if (_data[texel] == value) return;
            _data[texel] = value;
            MarkDirty(texel, 1);
        }
    }

    /// <summary>Overwrites a run of texels from <paramref name="start"/>.</summary>
    public void Write(int start, ReadOnlySpan<Float4> values)
    {
        if (values.IsEmpty) return;
        values.CopyTo(_data.AsSpan(start));
        MarkDirty(start, values.Length);
    }

    private void MarkDirty(int start, int count)
    {
        _dirtyLo = Math.Min(_dirtyLo, start);
        _dirtyHi = Math.Max(_dirtyHi, start + count - 1);
    }

    /// <summary>Sends what changed to the GPU and binds the table as a global for every shader. Encoded, so it lands in order.</summary>
    public void Bind(CommandBuffer cmd)
    {
        if (!Upload(cmd)) return;
        if (_usesBuffer)
        {
            cmd.SetGlobalBuffer(_blockName, _buffer!, 0);
        }
        else
        {
            cmd.SetGlobalTexture(_textureName, _texture!);
            cmd.SetGlobalInt(_textureName + "Shift", TextureWidthShift);
        }
    }

    /// <summary>Sends what changed to the GPU and binds the table on one draw's properties. False when it could not be.</summary>
    public bool Bind(PropertyState props)
    {
        if (_dirtyLo <= _dirtyHi || (_buffer == null && _texture.IsNotValid()))
        {
            using CommandBuffer cmd = Graphics.GetCommandBuffer("ShaderDataTable");
            bool ready = Upload(cmd);
            Graphics.Submit(cmd);
            if (!ready) return false;
        }

        if (_usesBuffer)
        {
            props.SetBuffer(_blockName, _buffer!);
        }
        else
        {
            props.SetTexture(_textureName, _texture!);
            props.SetInt(_textureName + "Shift", TextureWidthShift);
        }
        return true;
    }

    // Creates the storage on first use or after growing, then encodes the changed range
    private bool Upload(CommandBuffer cmd)
    {
        if (_data.Length == 0) return false;
        bool useBuffer = Graphics.Capabilities.Has(_storage);
        if (useBuffer != _usesBuffer || (_buffer == null && _texture.IsNotValid()))
        {
            ReleaseGpu();
            _usesBuffer = useBuffer;
            if (useBuffer)
            {
                _buffer = new GraphicsBuffer(BufferType.StructuredBuffer, MemoryMarshal.AsBytes(_data.AsSpan()), dynamic: true);
                _dirtyLo = int.MaxValue;
                _dirtyHi = -1;
            }
            else
            {
                int rows = _data.Length >> TextureWidthShift;
                if (rows > Graphics.MaxTextureSize)
                {
                    Debug.LogErrorOnce($"ShaderDataTable.{_textureName}", $"{_textureName} needs a {TextureWidth} x {rows} texture, taller than the {Graphics.MaxTextureSize} this GPU allows, so it is left out.");
                    return false;
                }
                _texture = new Texture2D(TextureWidth, (uint)rows, false, TextureImageFormat.Float4);
                _texture.SetTextureFilters(TextureMin.Nearest, TextureMag.Nearest);
                MarkDirty(0, _data.Length);
            }
        }

        if (_dirtyLo <= _dirtyHi)
        {
            if (_usesBuffer)
            {
                cmd.UpdateBuffer<Float4>(_buffer!, _data.AsSpan(_dirtyLo, _dirtyHi - _dirtyLo + 1), (uint)(_dirtyLo * 16));
            }
            else
            {
                // Whole rows, since a texture update is a rectangle
                int loRow = _dirtyLo >> TextureWidthShift;
                int hiRow = _dirtyHi >> TextureWidthShift;
                ReadOnlySpan<Float4> rows = _data.AsSpan(loRow * TextureWidth, (hiRow - loRow + 1) * TextureWidth);
                cmd.UpdateTexture(_texture!, 0, loRow, TextureWidth, (uint)(hiRow - loRow + 1), rows);
            }
            _dirtyLo = int.MaxValue;
            _dirtyHi = -1;
        }
        return true;
    }

    private void ReleaseGpu()
    {
        _buffer?.Dispose();
        _buffer = null;
        if (_texture.IsValid()) _texture!.Dispose();
        _texture = null;
    }

    public void Dispose()
    {
        ReleaseGpu();
        _data = [];
    }
}
