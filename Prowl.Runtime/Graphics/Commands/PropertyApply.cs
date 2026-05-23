// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using Prowl.Runtime.Rendering;
using Prowl.Runtime.Rendering.Shaders;
using Prowl.Runtime.Resources;
using Prowl.Vector;

using Silk.NET.OpenGL;

namespace Prowl.Runtime;

/// <summary>
/// Bridges between <see cref="PropertyState"/> (engine-side property dictionaries) and
/// the OpenGL uniform/texture/buffer-binding APIs. Every method that touches GL goes
/// through here so the executor stays focused on opcode dispatch.
///
/// Uniform locations are cached per program via the program's own dictionaries; uniform
/// values are cached on <see cref="GraphicsProgram.uniformCache"/> so redundant
/// uploads are skipped at the per-uniform level.
/// </summary>
internal static class PropertyApply
{
    // ─────────────────────── Per-uniform cached writes ───────────────────────

    public static void SetFloatCached(GraphicsProgram p, string name, float v)
    {
        var cache = p.uniformCache;
        if (cache.floats.TryGetValue(name, out var cv) && cv == v) return;
        int loc = LocationOf(p, name);
        if (loc < 0) return;
        Graphics.GL.Uniform1(loc, v);
        cache.floats[name] = v;
    }

    public static void SetIntCached(GraphicsProgram p, string name, int v)
    {
        var cache = p.uniformCache;
        if (cache.ints.TryGetValue(name, out var cv) && cv == v) return;
        int loc = LocationOf(p, name);
        if (loc < 0) return;
        Graphics.GL.Uniform1(loc, v);
        cache.ints[name] = v;
    }

    public static void SetVec2Cached(GraphicsProgram p, string name, Float2 v)
    {
        var cache = p.uniformCache;
        if (cache.vectors2.TryGetValue(name, out var cv) && cv.Equals(v)) return;
        int loc = LocationOf(p, name);
        if (loc < 0) return;
        Graphics.GL.Uniform2(loc, v);
        cache.vectors2[name] = v;
    }

    public static void SetVec3Cached(GraphicsProgram p, string name, Float3 v)
    {
        var cache = p.uniformCache;
        if (cache.vectors3.TryGetValue(name, out var cv) && cv.Equals(v)) return;
        int loc = LocationOf(p, name);
        if (loc < 0) return;
        Graphics.GL.Uniform3(loc, v);
        cache.vectors3[name] = v;
    }

    public static void SetVec4Cached(GraphicsProgram p, string name, Float4 v)
    {
        var cache = p.uniformCache;
        if (cache.vectors4.TryGetValue(name, out var cv) && cv.Equals(v)) return;
        int loc = LocationOf(p, name);
        if (loc < 0) return;
        Graphics.GL.Uniform4(loc, v);
        cache.vectors4[name] = v;
    }

    public static void SetMatrixCached(GraphicsProgram p, string name, in Float4x4 m)
    {
        // Matrices skip the cache compare (struct is large; comparison cost dwarfs
        // the upload savings). Matches the previous Graphics.SetUniformMatrix behavior.
        int loc = LocationOf(p, name);
        if (loc < 0) return;
        Graphics.GL.UniformMatrix4(loc, 1u, false, in m.c0.X);
    }

    public static void SetMatrixArray(GraphicsProgram p, string name, uint count, ReadOnlySpan<Float4x4> data)
    {
        if (data.Length == 0) return;
        int loc = LocationOf(p, name);
        if (loc < 0) return;
        Graphics.GL.UniformMatrix4(loc, count, false, in data[0].c0.X);
    }

    public static void BindUniformBuffer(GraphicsProgram p, string name, GraphicsBuffer buf, uint bindingPoint)
    {
        uint blockIdx = BlockIndexOf(p, name);
        if (blockIdx == 0xFFFFFFFFu) return; // not found
        Graphics.GL.UniformBlockBinding(p.Handle, blockIdx, bindingPoint);
        Graphics.GL.BindBufferBase(BufferTargetARB.UniformBuffer, bindingPoint, buf.Handle);
    }

    // ─────────────────────── PropertyState walking ───────────────────────

    public static void ApplyGlobals(GraphicsProgram p, CommandExecutor exec)
    {
        // Global PropertyState statics (set via PropertyState.SetGlobal*).
        WalkFloats(PropertyState.s_globalFloats, p);
        WalkInts(PropertyState.s_globalInts, p);
        WalkVec2(PropertyState.s_globalVectors2, p);
        WalkVec3(PropertyState.s_globalVectors3, p);
        WalkVec4(PropertyState.s_globalVectors4, p);
        WalkColors(PropertyState.s_globalColors, p);
        WalkMatrices(PropertyState.s_globalMatrices, p);
        WalkMatrixArraysNumeric(PropertyState.s_globalMatrixArr, p);
        WalkBuffers(PropertyState.s_globalBuffers, PropertyState.s_globalBufferBindings, p);
        WalkGlobalTextures(PropertyState.s_globalTextures, p, exec);
        WalkGlobalTextures3D(PropertyState.s_globalTextures3D, p, exec);
    }

    public static void ApplyMaterial(PropertyState state, GraphicsProgram p, CommandExecutor exec)
    {
        WalkFloats(state._floats, p);
        WalkInts(state._ints, p);
        WalkVec2(state._vectors2, p);
        WalkVec3(state._vectors3, p);
        WalkVec4(state._vectors4, p);
        WalkColors(state._colors, p);
        WalkMatrices(state._matrices, p);
        WalkMatrixArrays(state._matrixArr, p);
        WalkBuffers(state._buffers, state._bufferBindings, p);
        WalkAssetTextures(state._textures, p, exec);
        WalkAssetTextures3D(state._textures3D, p, exec);
    }

    public static void ApplyInstance(PropertyState state, GraphicsProgram p, CommandExecutor exec)
    {
        // Same walk; instance just runs AFTER material so it overrides.
        ApplyMaterial(state, p, exec);
    }

    /// <summary>For every property the shader declares but <paramref name="overrides"/>
    /// doesn't override, push the shader's default value through the cached uniform
    /// setters. Pass <c>null</c> overrides to apply all defaults unconditionally.</summary>
    public static void FillShaderDefaults(Resources.Shader shader, PropertyState? overrides, GraphicsProgram p, CommandExecutor exec)
    {
        foreach (var prop in shader.Properties)
        {
            string name = prop.Name;
            switch (prop.PropertyType)
            {
                case ShaderPropertyType.Float:
                    if (overrides == null || !overrides._floats.ContainsKey(name))
                        SetFloatCached(p, name, (float)prop.Value.X);
                    break;
                case ShaderPropertyType.Int:
                    if (overrides == null || !overrides._ints.ContainsKey(name))
                        SetIntCached(p, name, (int)prop.Value.X);
                    break;
                case ShaderPropertyType.Vector2:
                    if (overrides == null || !overrides._vectors2.ContainsKey(name))
                        SetVec2Cached(p, name, new Float2((float)prop.Value.X, (float)prop.Value.Y));
                    break;
                case ShaderPropertyType.Vector3:
                    if (overrides == null || !overrides._vectors3.ContainsKey(name))
                        SetVec3Cached(p, name, new Float3((float)prop.Value.X, (float)prop.Value.Y, (float)prop.Value.Z));
                    break;
                case ShaderPropertyType.Vector4:
                case ShaderPropertyType.Color:
                    if (overrides == null || (!overrides._vectors4.ContainsKey(name) && !overrides._colors.ContainsKey(name)))
                        SetVec4Cached(p, name, prop.Value);
                    break;
                case ShaderPropertyType.Matrix:
                    if (overrides == null || !overrides._matrices.ContainsKey(name))
                    {
                        var m = prop.MatrixValue;
                        SetMatrixCached(p, name, in m);
                    }
                    break;
                case ShaderPropertyType.Texture2D:
                    if (overrides == null || !overrides._textures.ContainsKey(name))
                    {
                        if (prop.Texture2DValue.IsValid())
                            BindTexUniform(p, name, prop.Texture2DValue.Handle, exec);
                    }
                    break;
                case ShaderPropertyType.Texture3D:
                    if (overrides == null || !overrides._textures3D.ContainsKey(name))
                    {
                        if (prop.Texture3DValue.IsValid())
                            BindTexUniform(p, name, prop.Texture3DValue.Handle, exec);
                    }
                    break;
            }
        }
    }

    // ─────────────────────── Bulk walkers ───────────────────────

    private static void WalkFloats(Dictionary<string, float> d, GraphicsProgram p)
    {
        foreach (var kv in d) SetFloatCached(p, kv.Key, kv.Value);
    }

    private static void WalkInts(Dictionary<string, int> d, GraphicsProgram p)
    {
        foreach (var kv in d) SetIntCached(p, kv.Key, kv.Value);
    }

    private static void WalkVec2(Dictionary<string, Float2> d, GraphicsProgram p)
    {
        foreach (var kv in d) SetVec2Cached(p, kv.Key, kv.Value);
    }

    private static void WalkVec3(Dictionary<string, Float3> d, GraphicsProgram p)
    {
        foreach (var kv in d) SetVec3Cached(p, kv.Key, kv.Value);
    }

    private static void WalkVec4(Dictionary<string, Float4> d, GraphicsProgram p)
    {
        foreach (var kv in d) SetVec4Cached(p, kv.Key, kv.Value);
    }

    private static void WalkColors(Dictionary<string, Color> d, GraphicsProgram p)
    {
        foreach (var kv in d)
        {
            Float4 v = new((float)kv.Value.R, (float)kv.Value.G, (float)kv.Value.B, (float)kv.Value.A);
            SetVec4Cached(p, kv.Key, v);
        }
    }

    private static void WalkMatrices(Dictionary<string, Float4x4> d, GraphicsProgram p)
    {
        foreach (var kv in d) { var v = kv.Value; SetMatrixCached(p, kv.Key, in v); }
    }

    private static void WalkMatrixArrays(Dictionary<string, Float4x4[]> d, GraphicsProgram p)
    {
        foreach (var kv in d)
        {
            if (kv.Value == null || kv.Value.Length == 0) continue;
            int loc = LocationOf(p, kv.Key);
            if (loc < 0) continue;
            Graphics.GL.UniformMatrix4(loc, (uint)kv.Value.Length, false, in kv.Value[0].c0.X);
        }
    }

    private static void WalkMatrixArraysNumeric(Dictionary<string, System.Numerics.Matrix4x4[]> d, GraphicsProgram p)
    {
        foreach (var kv in d)
        {
            if (kv.Value == null || kv.Value.Length == 0) continue;
            int loc = LocationOf(p, kv.Key);
            if (loc < 0) continue;
            Graphics.GL.UniformMatrix4(loc, (uint)kv.Value.Length, false, in kv.Value[0].M11);
        }
    }

    private static void WalkBuffers(Dictionary<string, GraphicsBuffer> d, Dictionary<string, uint> bindings, GraphicsProgram p)
    {
        var cache = p.uniformCache;
        foreach (var kv in d)
        {
            if (cache.buffers.TryGetValue(kv.Key, out var cached) && ReferenceEquals(cached, kv.Value)) continue;
            uint bp = bindings.TryGetValue(kv.Key, out uint b) ? b : 0u;
            BindUniformBuffer(p, kv.Key, kv.Value, bp);
            cache.buffers[kv.Key] = kv.Value;
        }
    }

    private static void WalkAssetTextures(Dictionary<string, AssetRef<Texture2D>> d, GraphicsProgram p, CommandExecutor exec)
    {
        foreach (var kv in d)
        {
            var tex = kv.Value.Res;
            if (!tex.IsValid()) continue;
            BindTexUniform(p, kv.Key, tex.Handle, exec);
        }
    }

    private static void WalkAssetTextures3D(Dictionary<string, AssetRef<Texture3D>> d, GraphicsProgram p, CommandExecutor exec)
    {
        foreach (var kv in d)
        {
            var tex = kv.Value.Res;
            if (!tex.IsValid()) continue;
            BindTexUniform(p, kv.Key, tex.Handle, exec);
        }
    }

    private static void WalkGlobalTextures(Dictionary<string, Texture2D> d, GraphicsProgram p, CommandExecutor exec)
    {
        foreach (var kv in d)
        {
            var tex = kv.Value;
            if (!tex.IsValid()) continue;
            BindTexUniform(p, kv.Key, tex.Handle, exec);
        }
    }

    private static void WalkGlobalTextures3D(Dictionary<string, Texture3D> d, GraphicsProgram p, CommandExecutor exec)
    {
        foreach (var kv in d)
        {
            var tex = kv.Value;
            if (!tex.IsValid()) continue;
            BindTexUniform(p, kv.Key, tex.Handle, exec);
        }
    }

    private static void BindTexUniform(GraphicsProgram p, string name, GraphicsTexture tex, CommandExecutor exec)
    {
        int slot = exec.AllocateTextureSlot();
        exec.BindTextureToUnit(slot, tex);
        // Sampler slot uniforms cannot use the int cache: PrepareDraw resets the
        // slot counter every draw, so the same uniform may legitimately need a
        // different slot value next time. A cache hit would skip the Uniform1
        // update and the shader would sample whatever texture is at the stale
        // slot. Always write directly.
        int loc = LocationOf(p, name);
        if (loc < 0) return;
        Graphics.GL.Uniform1(loc, slot);
    }

    // ─────────────────────── Uniform location / block index ───────────────────────
    // Caches live on the program itself so they're freed when the program is.

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int LocationOf(GraphicsProgram p, string name)
    {
        if (p.uniformLocations.TryGetValue(name, out int loc)) return loc;
        loc = Graphics.GL.GetUniformLocation(p.Handle, name);
        p.uniformLocations[name] = loc;
        return loc;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static uint BlockIndexOf(GraphicsProgram p, string name)
    {
        if (p.blockIndices.TryGetValue(name, out uint idx)) return idx;
        idx = Graphics.GL.GetUniformBlockIndex(p.Handle, name);
        p.blockIndices[name] = idx;
        return idx;
    }
}
