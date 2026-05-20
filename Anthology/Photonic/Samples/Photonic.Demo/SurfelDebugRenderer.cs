using OpenTK.Graphics.OpenGL4;
using Prowl.Vector;
using Prowl.Photonic.Surfels;

namespace Photonic.Demo;

/// <summary>
/// Debug viz for the surfel cloud: every surfel is drawn as a small lit sphere using
/// GPU instancing. Colour encodes state (active / direct-lit / brightness-saturated / sky-dominant);
/// per-instance radius is the surfel's own kernel radius multiplied by a UI scalar.
/// </summary>
internal sealed class SurfelDebugRenderer : System.IDisposable
{
    private int _sphereVao, _sphereVbo, _sphereEbo, _instanceVbo;
    private int _sphereIndexCount;
    private int _instanceCapacity;
    private int _program;
    private int _uMVP, _uViewDir;

    private float[] _instanceScratch = System.Array.Empty<float>(); // 7 floats per surfel

    public SurfelDebugRenderer()
    {
        _program = BuildProgram();
        _uMVP     = GL.GetUniformLocation(_program, "uMVP");
        _uViewDir = GL.GetUniformLocation(_program, "uViewDir");
        BuildSphere(latSegments: 10, lonSegments: 14);
    }

    public void Dispose()
    {
        if (_sphereVao != 0) GL.DeleteVertexArray(_sphereVao);
        if (_sphereVbo != 0) GL.DeleteBuffer(_sphereVbo);
        if (_sphereEbo != 0) GL.DeleteBuffer(_sphereEbo);
        if (_instanceVbo != 0) GL.DeleteBuffer(_instanceVbo);
        if (_program != 0) GL.DeleteProgram(_program);
    }

    /// <summary>
    /// Update per-instance buffer from the current surfel cloud and draw. The renderer is a no-op
    /// if the cloud is null or empty.
    /// </summary>
    public void Render(SurfelCloud? cloud, Float4x4 view, Float4x4 proj, Float3 cameraForward, float sizeMultiplier)
    {
        if (cloud is null) return;
        var surfels = cloud.Surfels;
        int n = surfels.Length;
        if (n == 0) return;

        // Per-instance: 3 (pos) + 1 (radius) + 3 (color) = 7 floats.
        const int floatsPerInstance = 7;
        int needed = n * floatsPerInstance;
        if (_instanceScratch.Length < needed) _instanceScratch = new float[needed];

        for (int i = 0; i < n; i++)
        {
            ref var s = ref surfels[i];
            int b = i * floatsPerInstance;
            _instanceScratch[b + 0] = (float)s.Position.X;
            _instanceScratch[b + 1] = (float)s.Position.Y;
            _instanceScratch[b + 2] = (float)s.Position.Z;
            _instanceScratch[b + 3] = s.Radius * sizeMultiplier;

            Float3 col = new Float3(0.15f, 0.85f, 0.20f);
            _instanceScratch[b + 4] = (float)col.X;
            _instanceScratch[b + 5] = (float)col.Y;
            _instanceScratch[b + 6] = (float)col.Z;
        }

        // Re-allocate if the buffer outgrew its capacity.
        GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVbo);
        if (n > _instanceCapacity)
        {
            int newCap = System.Math.Max(n, _instanceCapacity * 2);
            GL.BufferData(BufferTarget.ArrayBuffer, newCap * floatsPerInstance * sizeof(float),
                          System.IntPtr.Zero, BufferUsageHint.DynamicDraw);
            _instanceCapacity = newCap;
        }
        unsafe
        {
            fixed (float* p = _instanceScratch)
            {
                GL.BufferSubData(BufferTarget.ArrayBuffer, System.IntPtr.Zero,
                                 needed * sizeof(float), (System.IntPtr)p);
            }
        }

        var mvp = ToMat4Floats(proj * view);

        GL.UseProgram(_program);
        GL.UniformMatrix4(_uMVP, 1, false, mvp);
        GL.Uniform3(_uViewDir, (float)cameraForward.X, (float)cameraForward.Y, (float)cameraForward.Z);

        GL.BindVertexArray(_sphereVao);
        GL.DrawElementsInstanced(PrimitiveType.Triangles, _sphereIndexCount,
                                 DrawElementsType.UnsignedInt, System.IntPtr.Zero, n);
        GL.BindVertexArray(0);
    }

    private static float[] ToMat4Floats(Float4x4 m)
    {
        // Column-major (matches our GL convention).
        return new float[]
        {
            (float)m.c0.X, (float)m.c0.Y, (float)m.c0.Z, (float)m.c0.W,
            (float)m.c1.X, (float)m.c1.Y, (float)m.c1.Z, (float)m.c1.W,
            (float)m.c2.X, (float)m.c2.Y, (float)m.c2.Z, (float)m.c2.W,
            (float)m.c3.X, (float)m.c3.Y, (float)m.c3.Z, (float)m.c3.W,
        };
    }

    private void BuildSphere(int latSegments, int lonSegments)
    {
        // UV sphere with per-vertex normal (= position for a unit sphere).
        // Vertex layout: vec3 position, vec3 normal -> 6 floats / vertex.
        int vertCount = (latSegments + 1) * (lonSegments + 1);
        var verts = new float[vertCount * 6];
        int vi = 0;
        for (int y = 0; y <= latSegments; y++)
        {
            float v = (float)y / latSegments;
            float phi = v * System.MathF.PI;
            float sinPhi = System.MathF.Sin(phi);
            float cosPhi = System.MathF.Cos(phi);
            for (int x = 0; x <= lonSegments; x++)
            {
                float u = (float)x / lonSegments;
                float theta = u * 2f * System.MathF.PI;
                float px = sinPhi * System.MathF.Cos(theta);
                float py = cosPhi;
                float pz = sinPhi * System.MathF.Sin(theta);
                verts[vi++] = px; verts[vi++] = py; verts[vi++] = pz;
                verts[vi++] = px; verts[vi++] = py; verts[vi++] = pz; // normal = position on unit sphere
            }
        }

        int rowStride = lonSegments + 1;
        var indices = new uint[latSegments * lonSegments * 6];
        int ii = 0;
        for (int y = 0; y < latSegments; y++)
        for (int x = 0; x < lonSegments; x++)
        {
            uint i0 = (uint)(y * rowStride + x);
            uint i1 = (uint)(y * rowStride + x + 1);
            uint i2 = (uint)((y + 1) * rowStride + x);
            uint i3 = (uint)((y + 1) * rowStride + x + 1);
            indices[ii++] = i0; indices[ii++] = i2; indices[ii++] = i1;
            indices[ii++] = i1; indices[ii++] = i2; indices[ii++] = i3;
        }
        _sphereIndexCount = indices.Length;

        _sphereVao = GL.GenVertexArray();
        _sphereVbo = GL.GenBuffer();
        _sphereEbo = GL.GenBuffer();
        _instanceVbo = GL.GenBuffer();
        GL.BindVertexArray(_sphereVao);

        GL.BindBuffer(BufferTarget.ArrayBuffer, _sphereVbo);
        unsafe
        {
            fixed (float* p = verts)
                GL.BufferData(BufferTarget.ArrayBuffer, verts.Length * sizeof(float),
                              (System.IntPtr)p, BufferUsageHint.StaticDraw);
        }

        GL.BindBuffer(BufferTarget.ElementArrayBuffer, _sphereEbo);
        unsafe
        {
            fixed (uint* p = indices)
                GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint),
                              (System.IntPtr)p, BufferUsageHint.StaticDraw);
        }

        int vStride = 6 * sizeof(float);
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, vStride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, vStride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);

        // Per-instance buffer: vec3 pos (loc 2), float radius (loc 3), vec3 color (loc 4).
        GL.BindBuffer(BufferTarget.ArrayBuffer, _instanceVbo);
        int iStride = 7 * sizeof(float);
        GL.VertexAttribPointer(2, 3, VertexAttribPointerType.Float, false, iStride, 0);
        GL.EnableVertexAttribArray(2);
        GL.VertexAttribDivisor(2, 1);
        GL.VertexAttribPointer(3, 1, VertexAttribPointerType.Float, false, iStride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(3);
        GL.VertexAttribDivisor(3, 1);
        GL.VertexAttribPointer(4, 3, VertexAttribPointerType.Float, false, iStride, 4 * sizeof(float));
        GL.EnableVertexAttribArray(4);
        GL.VertexAttribDivisor(4, 1);

        GL.BindVertexArray(0);
    }

    private static int BuildProgram()
    {
        const string vs = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aNormal;
layout(location = 2) in vec3 iCenter;
layout(location = 3) in float iRadius;
layout(location = 4) in vec3 iColor;

uniform mat4 uMVP;

out vec3 vNormal;
out vec3 vColor;

void main()
{
    vec3 worldPos = iCenter + aPos * iRadius;
    gl_Position = uMVP * vec4(worldPos, 1.0);
    vNormal = aNormal;
    vColor  = iColor;
}";
        const string fs = @"#version 330 core
in vec3 vNormal;
in vec3 vColor;
out vec4 FragColor;

uniform vec3 uViewDir;

void main()
{
    // Cheap directional shading: NdotV against a fixed sun-ish offset of the view direction.
    vec3 N = normalize(vNormal);
    vec3 L = normalize(-uViewDir + vec3(0.3, 0.6, 0.2));
    float ndl = max(dot(N, L), 0.0);
    vec3 shaded = vColor * (0.35 + 0.65 * ndl);
    FragColor = vec4(shaded, 1.0);
}";
        int v = GL.CreateShader(ShaderType.VertexShader);
        GL.ShaderSource(v, vs);
        GL.CompileShader(v);
        GL.GetShader(v, ShaderParameter.CompileStatus, out int vOk);
        if (vOk == 0)
            throw new System.Exception("Surfel debug vertex shader: " + GL.GetShaderInfoLog(v));

        int f = GL.CreateShader(ShaderType.FragmentShader);
        GL.ShaderSource(f, fs);
        GL.CompileShader(f);
        GL.GetShader(f, ShaderParameter.CompileStatus, out int fOk);
        if (fOk == 0)
            throw new System.Exception("Surfel debug fragment shader: " + GL.GetShaderInfoLog(f));

        int p = GL.CreateProgram();
        GL.AttachShader(p, v);
        GL.AttachShader(p, f);
        GL.LinkProgram(p);
        GL.GetProgram(p, GetProgramParameterName.LinkStatus, out int pOk);
        if (pOk == 0)
            throw new System.Exception("Surfel debug program link: " + GL.GetProgramInfoLog(p));
        GL.DeleteShader(v);
        GL.DeleteShader(f);
        return p;
    }
}
