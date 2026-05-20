using System.Collections.Generic;
using OpenTK.Graphics.OpenGL4;
using Prowl.Vector;
using Prowl.Photonic;

namespace Photonic.Demo;

/// <summary>
/// Multi-model, multi-atlas scene renderer. Owns one GpuModel per <see cref="SceneModel"/> and
/// one GL texture per <see cref="LightmapTarget"/>; submeshes are drawn with their own diffuse
/// texture on unit 0 and the matching atlas page on unit 1, transformed by the per-instance
/// UV1 offset / scale produced by <see cref="AutoAtlasPacker"/>.
/// </summary>
internal sealed class SceneRenderer : System.IDisposable
{
    public enum DebugMode
    {
        Off          = 0,
        UV1          = 1,
        Coverage     = 2,
        LightmapOnly = 3,
    }

    public DebugMode CurrentDebug { get; set; } = DebugMode.Off;
    public bool WireframeOverlay { get; set; } = false;
    public float BilateralStrength { get; set; } = 0f;

    private readonly int _program;
    private readonly int _uMVP;
    private readonly int _uExposure;
    private readonly int _uDiffuse;
    private readonly int _uLightmap;
    private readonly int _uHasLightmap;
    private readonly int _uHasDiffuse;
    private readonly int _uDebugMode;
    private readonly int _uBilateral;
    private readonly int _uUVOffset;
    private readonly int _uUVScale;
    private readonly int _white1x1;

    // GPU resources are keyed by reference identity to the SceneModel so we know what's already uploaded.
    private readonly Dictionary<SceneModel, GpuModel> _gpu = new();
    // Atlas textures, indexed in parallel with the bake's LightmapTarget list. _atlas[i] = 0 means
    // "no atlas uploaded yet" (model is shown unlit until the first iteration completes).
    private int[] _atlas = System.Array.Empty<int>();

    public SceneRenderer()
    {
        _program = BuildProgram();
        _uMVP         = GL.GetUniformLocation(_program, "uMVP");
        _uExposure    = GL.GetUniformLocation(_program, "uExposure");
        _uDiffuse     = GL.GetUniformLocation(_program, "uDiffuse");
        _uLightmap    = GL.GetUniformLocation(_program, "uLightmap");
        _uHasLightmap = GL.GetUniformLocation(_program, "uHasLightmap");
        _uHasDiffuse  = GL.GetUniformLocation(_program, "uHasDiffuse");
        _uDebugMode   = GL.GetUniformLocation(_program, "uDebugMode");
        _uBilateral   = GL.GetUniformLocation(_program, "uBilateralStrength");
        _uUVOffset    = GL.GetUniformLocation(_program, "uUV1Offset");
        _uUVScale     = GL.GetUniformLocation(_program, "uUV1Scale");

        _white1x1 = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, _white1x1);
        var white = new float[] { 1, 1, 1 };
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb32f, 1, 1, 0, PixelFormat.Rgb, PixelType.Float, white);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
    }

    /// <summary>(Re-)upload the GPU vertex / index / texture data for one scene model. Call after geometry / UV1 changes.</summary>
    public unsafe void UploadModel(SceneModel sm)
    {
        if (!_gpu.TryGetValue(sm, out var g))
        {
            g = new GpuModel
            {
                Vao = GL.GenVertexArray(),
                Vbo = GL.GenBuffer(),
                Ebo = GL.GenBuffer(),
            };
            _gpu[sm] = g;
        }

        int vc = sm.BakedPositions.Length;
        const int floatsPerVertex = 10; // pos3 + nrm3 + uv0(2) + uv1(2)
        var data = new float[vc * floatsPerVertex];
        for (int i = 0; i < vc; i++)
        {
            int o = i * floatsPerVertex;
            var p = sm.BakedPositions[i];
            var n = sm.BakedNormals[i];
            var u0 = sm.BakedUV0[i];
            var u1 = sm.BakedUV1[i];
            data[o    ] = p.X; data[o + 1] = p.Y; data[o + 2] = p.Z;
            data[o + 3] = n.X; data[o + 4] = n.Y; data[o + 5] = n.Z;
            data[o + 6] = u0.X; data[o + 7] = u0.Y;
            data[o + 8] = u1.X; data[o + 9] = u1.Y;
        }

        GL.BindVertexArray(g.Vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, g.Vbo);
        int vbBytes = data.Length * sizeof(float);
        GL.BufferData(BufferTarget.ArrayBuffer, vbBytes, System.IntPtr.Zero, BufferUsageHint.StaticDraw);
        fixed (float* p = data)
            GL.BufferSubData(BufferTarget.ArrayBuffer, System.IntPtr.Zero, vbBytes, (System.IntPtr)p);

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, g.Ebo);
        int ibBytes = sm.BakedIndices.Length * sizeof(int);
        GL.BufferData(BufferTarget.ElementArrayBuffer, ibBytes, System.IntPtr.Zero, BufferUsageHint.StaticDraw);
        fixed (int* pi = sm.BakedIndices)
            GL.BufferSubData(BufferTarget.ElementArrayBuffer, System.IntPtr.Zero, ibBytes, (System.IntPtr)pi);

        int stride = floatsPerVertex * sizeof(float);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, stride, 6 * sizeof(float));
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribPointer(3, 2, VertexAttribPointerType.Float, false, stride, 8 * sizeof(float));
        GL.EnableVertexAttribArray(3);
        GL.BindVertexArray(0);

        g.IndexCount = sm.BakedIndices.Length;
        g.SubMeshes = sm.BakedSubMeshes;

        // Per-material diffuse textures. Decoded once at model import time; we just re-bind them.
        EnsureMaterialTextures(sm.Source, g);
    }

    private static void EnsureMaterialTextures(LoadedModel src, GpuModel g)
    {
        if (g.MaterialDiffuseTex.Length > 0) return; // already uploaded

        // One GL texture per Clay texture index that's actually referenced.
        var sourceToGl = new int[src.Textures.Length];
        for (int i = 0; i < src.Textures.Length; i++)
        {
            var blob = src.Textures[i];
            if (blob is null) continue;
            int tex = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Srgb8Alpha8,
                          blob.Width, blob.Height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, blob.RGBA);
            GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.LinearMipmapLinear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            sourceToGl[i] = tex;
        }
        var matDiffuse = new int[src.Materials.Length];
        for (int m = 0; m < src.Materials.Length; m++)
        {
            int ti = src.Materials[m].DiffuseTextureIndex;
            matDiffuse[m] = (ti >= 0 && ti < sourceToGl.Length) ? sourceToGl[ti] : 0;
        }
        g.MaterialDiffuseTex = matDiffuse;
        g.OwnedTextures = sourceToGl; // for Dispose to clean up
    }

    /// <summary>Drop GPU state for a model that's been removed from the scene.</summary>
    public void UnloadModel(SceneModel sm)
    {
        if (!_gpu.TryGetValue(sm, out var g)) return;
        if (g.Vao != 0) GL.DeleteVertexArray(g.Vao);
        if (g.Vbo != 0) GL.DeleteBuffer(g.Vbo);
        if (g.Ebo != 0) GL.DeleteBuffer(g.Ebo);
        for (int i = 0; i < g.OwnedTextures.Length; i++)
            if (g.OwnedTextures[i] != 0) GL.DeleteTexture(g.OwnedTextures[i]);
        _gpu.Remove(sm);
    }

    /// <summary>
    /// Resize the internal atlas-page texture array. After this call, atlas index <c>i</c> maps
    /// to <c>UpdateAtlas(i, ...)</c>. Existing atlas textures are deleted.
    /// </summary>
    public void SetAtlasCount(int count)
    {
        for (int i = 0; i < _atlas.Length; i++)
            if (_atlas[i] != 0) GL.DeleteTexture(_atlas[i]);
        _atlas = new int[count];
    }

    /// <summary>
    /// Replace the contents of one atlas page with raw HDR linear irradiance from the bake. The
    /// shader is responsible for tonemapping; the texture itself stays in linear float space so
    /// we don't accidentally double-tonemap on render.
    /// </summary>
    public unsafe void UpdateAtlas(int index, int width, int height, System.ReadOnlySpan<float> rgbPixels)
    {
        if (index < 0 || index >= _atlas.Length) return;
        int tex = _atlas[index];
        if (tex == 0)
        {
            tex = GL.GenTexture();
            _atlas[index] = tex;
            GL.BindTexture(TextureTarget.Texture2D, tex);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.ClampToEdge);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.ClampToEdge);
        }
        GL.BindTexture(TextureTarget.Texture2D, tex);
        GL.PixelStore(PixelStoreParameter.UnpackAlignment, 1);
        fixed (float* p = rgbPixels)
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgb16f, width, height, 0, PixelFormat.Rgb, PixelType.Float, (System.IntPtr)p);
    }

    /// <summary>GL texture handle for atlas page <paramref name="i"/>, or 0 if not yet uploaded.</summary>
    public int GetAtlasTexture(int i) => (i >= 0 && i < _atlas.Length) ? _atlas[i] : 0;
    public int AtlasCount => _atlas.Length;

    /// <summary>Draw every model in the scene.</summary>
    public void Render(Scene scene, Float4x4 view, Float4x4 proj, float exposure)
    {
        GL.UseProgram(_program);
        GL.Uniform1(_uExposure, exposure);
        GL.Uniform1(_uDebugMode, (int)CurrentDebug);
        GL.Uniform1(_uBilateral, BilateralStrength);
        GL.Disable(EnableCap.CullFace);
        GL.Enable(EnableCap.DepthTest);
        GL.Enable(EnableCap.FramebufferSrgb);

        for (int mi = 0; mi < scene.Models.Count; mi++)
        {
            var sm = scene.Models[mi];
            if (!_gpu.TryGetValue(sm, out var g)) continue;

            var mvp = proj * view * Scene.GetModelTransform(sm);
            SetMatrix4(_uMVP, mvp);
            GL.Uniform2(_uUVOffset, (float)sm.UVOffset.X, (float)sm.UVOffset.Y);
            GL.Uniform2(_uUVScale,  (float)sm.UVScale.X,  (float)sm.UVScale.Y);

            int atlasTex = sm.AtlasTargetIndex >= 0 ? GetAtlasTexture(sm.AtlasTargetIndex) : 0;
            bool hasLM = atlasTex != 0;
            GL.ActiveTexture(TextureUnit.Texture1);
            GL.BindTexture(TextureTarget.Texture2D, hasLM ? atlasTex : _white1x1);
            GL.Uniform1(_uLightmap, 1);
            GL.Uniform1(_uHasLightmap, hasLM ? 1 : 0);

            GL.BindVertexArray(g.Vao);
            for (int s = 0; s < g.SubMeshes.Length; s++)
            {
                var sub = g.SubMeshes[s];
                int diff = (sub.MaterialIndex >= 0 && sub.MaterialIndex < g.MaterialDiffuseTex.Length)
                    ? g.MaterialDiffuseTex[sub.MaterialIndex] : 0;
                GL.ActiveTexture(TextureUnit.Texture0);
                GL.BindTexture(TextureTarget.Texture2D, diff != 0 ? diff : _white1x1);
                GL.Uniform1(_uDiffuse, 0);
                GL.Uniform1(_uHasDiffuse, diff != 0 ? 1 : 0);
                GL.DrawElements(PrimitiveType.Triangles, sub.IndexCount, DrawElementsType.UnsignedInt, sub.IndexStart * sizeof(int));
            }

            if (WireframeOverlay)
            {
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
                GL.Enable(EnableCap.PolygonOffsetLine);
                GL.PolygonOffset(-1f, -1f);
                GL.Uniform1(_uHasLightmap, 0);
                GL.Uniform1(_uHasDiffuse, 0);
                GL.Uniform1(_uDebugMode, 4);
                GL.DrawElements(PrimitiveType.Triangles, g.IndexCount, DrawElementsType.UnsignedInt, 0);
                GL.Disable(EnableCap.PolygonOffsetLine);
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
                GL.Uniform1(_uDebugMode, (int)CurrentDebug);
            }
        }

        GL.BindVertexArray(0);
        GL.Disable(EnableCap.FramebufferSrgb);
    }

    private static unsafe void SetMatrix4(int loc, Float4x4 m)
    {
        float* p = stackalloc float[16];
        p[0]  = m.c0.X; p[1]  = m.c0.Y; p[2]  = m.c0.Z; p[3]  = m.c0.W;
        p[4]  = m.c1.X; p[5]  = m.c1.Y; p[6]  = m.c1.Z; p[7]  = m.c1.W;
        p[8]  = m.c2.X; p[9]  = m.c2.Y; p[10] = m.c2.Z; p[11] = m.c2.W;
        p[12] = m.c3.X; p[13] = m.c3.Y; p[14] = m.c3.Z; p[15] = m.c3.W;
        GL.UniformMatrix4(loc, 1, false, p);
    }

    public void Dispose()
    {
        foreach (var kv in _gpu)
        {
            var g = kv.Value;
            if (g.Vao != 0) GL.DeleteVertexArray(g.Vao);
            if (g.Vbo != 0) GL.DeleteBuffer(g.Vbo);
            if (g.Ebo != 0) GL.DeleteBuffer(g.Ebo);
            for (int i = 0; i < g.OwnedTextures.Length; i++)
                if (g.OwnedTextures[i] != 0) GL.DeleteTexture(g.OwnedTextures[i]);
        }
        _gpu.Clear();
        for (int i = 0; i < _atlas.Length; i++)
            if (_atlas[i] != 0) GL.DeleteTexture(_atlas[i]);
        if (_white1x1 != 0) GL.DeleteTexture(_white1x1);
        if (_program != 0) GL.DeleteProgram(_program);
    }

    private sealed class GpuModel
    {
        public int Vao, Vbo, Ebo;
        public int IndexCount;
        public LoadedModel.SubMeshSlice[] SubMeshes = System.Array.Empty<LoadedModel.SubMeshSlice>();
        public int[] MaterialDiffuseTex = System.Array.Empty<int>();
        public int[] OwnedTextures = System.Array.Empty<int>();
    }

    private static int BuildProgram()
    {
        const string vs = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec2 aUV0;
layout(location=3) in vec2 aUV1;
uniform mat4 uMVP;
uniform vec2 uUV1Offset;
uniform vec2 uUV1Scale;
out vec3 vNormal;
out vec2 vUV0;
out vec2 vUV1;
void main(){
    gl_Position = uMVP * vec4(aPos, 1.0);
    vNormal = aNormal;
    vUV0 = aUV0;
    vUV1 = aUV1 * uUV1Scale + uUV1Offset;
}";
        const string fs = @"#version 330 core
in vec3 vNormal;
in vec2 vUV0;
in vec2 vUV1;
uniform sampler2D uDiffuse;
uniform sampler2D uLightmap;
uniform float uExposure;
uniform int uHasLightmap;
uniform int uHasDiffuse;
uniform int uDebugMode;
uniform float uBilateralStrength;
out vec4 fragColor;

vec3 sampleLightmap(vec2 uv) {
    vec3 center = texture(uLightmap, uv).rgb;
    if (uBilateralStrength <= 0.0) return center;
    float centerLum = dot(center, vec3(0.2126, 0.7152, 0.0722));
    vec2 ts = 1.0 / vec2(textureSize(uLightmap, 0));
    vec3 acc = center;
    float totalW = 1.0;
    float sigmaLum = max(0.02, 0.15 / max(uBilateralStrength, 0.01));
    for (int dy = -1; dy <= 1; dy++) {
        for (int dx = -1; dx <= 1; dx++) {
            if (dx == 0 && dy == 0) continue;
            vec3 n = texture(uLightmap, uv + vec2(dx, dy) * ts).rgb;
            float nLum = dot(n, vec3(0.2126, 0.7152, 0.0722));
            float dLum = abs(centerLum - nLum);
            float wLum = exp(-(dLum * dLum) / (sigmaLum * sigmaLum));
            float spatial = (dx == 0 || dy == 0) ? 1.0 : 0.7071;
            float w = wLum * spatial * uBilateralStrength;
            acc += n * w;
            totalW += w;
        }
    }
    return acc / totalW;
}

void main(){
    if (uDebugMode == 1) { fragColor = vec4(vUV1, 0.0, 1.0); return; }
    if (uDebugMode == 2) {
        vec3 lm = uHasLightmap == 1 ? texture(uLightmap, vUV1).rgb : vec3(0.0);
        float lum = dot(lm, vec3(0.2126, 0.7152, 0.0722));
        if (lum < 1e-5) { fragColor = vec4(1.0, 0.0, 1.0, 1.0); return; }
        lm = lm * uExposure; lm = lm / (vec3(1.0) + lm);
        fragColor = vec4(lm, 1.0); return;
    }
    if (uDebugMode == 3) {
        vec3 lm = uHasLightmap == 1 ? sampleLightmap(vUV1) : vec3(0.0);
        lm = lm * uExposure; lm = lm / (vec3(1.0) + lm);
        fragColor = vec4(lm, 1.0); return;
    }
    if (uDebugMode == 4) {
        fragColor = vec4(0.0, 1.0, 0.4, 1.0); return;
    }

    vec3 albedo = uHasDiffuse == 1 ? texture(uDiffuse, vUV0).rgb : vec3(0.7);
    if (uHasLightmap == 1) {
        vec3 light = sampleLightmap(vUV1) * uExposure;
        vec3 col = albedo * light;
        col = col / (vec3(1.0) + col);
        fragColor = vec4(col, 1.0);
    } else {
        vec3 n = normalize(vNormal);
        float l = max(0.0, dot(n, normalize(vec3(0.3, 0.8, 0.4))));
        fragColor = vec4(albedo * (0.3 + 0.7 * l), 1.0);
    }
}";
        int v = Compile(ShaderType.VertexShader, vs);
        int f = Compile(ShaderType.FragmentShader, fs);
        int p = GL.CreateProgram();
        GL.AttachShader(p, v); GL.AttachShader(p, f);
        GL.LinkProgram(p);
        GL.GetProgram(p, GetProgramParameterName.LinkStatus, out int status);
        if (status == 0) throw new System.InvalidOperationException("Shader link failed: " + GL.GetProgramInfoLog(p));
        GL.DetachShader(p, v); GL.DetachShader(p, f);
        GL.DeleteShader(v); GL.DeleteShader(f);
        return p;
    }

    private static int Compile(ShaderType type, string src)
    {
        int s = GL.CreateShader(type);
        GL.ShaderSource(s, src);
        GL.CompileShader(s);
        GL.GetShader(s, ShaderParameter.CompileStatus, out int status);
        if (status == 0) throw new System.InvalidOperationException($"{type} compile: " + GL.GetShaderInfoLog(s));
        return s;
    }
}
