using OpenTK.Graphics.OpenGL4;
using Prowl.Vector;
using Prowl.Photonic;

namespace Photonic.Demo;

/// <summary>
/// Tiny GL_LINES renderer used by the debug viewer to draw the recorded ray segments for a
/// picked texel each frame. Geometry is re-uploaded every call (segment counts are tiny — a
/// dozen lines per texel at most).
/// </summary>
internal sealed class LineRenderer : System.IDisposable
{
    private int _vao, _vbo;
    private int _program, _uMVP;
    private float[] _cpuBuffer = System.Array.Empty<float>();
    private int _lineCount;

    public LineRenderer()
    {
        _program = BuildProgram();
        _uMVP = GL.GetUniformLocation(_program, "uMVP");

        _vao = GL.GenVertexArray();
        _vbo = GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        int stride = 6 * sizeof(float); // vec3 pos + vec3 color
        GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, stride, 0);
        GL.EnableVertexAttribArray(0);
        GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, stride, 3 * sizeof(float));
        GL.EnableVertexAttribArray(1);
        GL.BindVertexArray(0);
    }

    public unsafe void Upload(System.Collections.Generic.List<DebugSegment> segs)
    {
        int n = segs.Count;
        _lineCount = n;
        if (n == 0) return;
        int needed = n * 12; // 2 verts * 6 floats
        if (_cpuBuffer.Length < needed) _cpuBuffer = new float[needed];
        for (int i = 0; i < n; i++)
        {
            var s = segs[i];
            // colour scheme:
            //   BounceIndex < 0  = magenta marker (texel centre -> jittered origin)
            //   shadow + hit     = red    (light blocked)
            //   shadow + miss    = yellow (light reached)
            //   bounce + hit     = green
            //   bounce + miss    = cyan   (ray went off into the sky)
            Float3 c;
            if (s.BounceIndex < 0) c = new Float3(1.0f, 0.2f, 1.0f);
            else if (s.IsShadow)   c = s.Hit ? new Float3(1.0f, 0.2f, 0.2f) : new Float3(1.0f, 0.9f, 0.1f);
            else                   c = s.Hit ? new Float3(0.2f, 1.0f, 0.4f) : new Float3(0.3f, 0.7f, 1.0f);

            int o = i * 12;
            _cpuBuffer[o    ] = s.Start.X; _cpuBuffer[o + 1] = s.Start.Y; _cpuBuffer[o + 2] = s.Start.Z;
            _cpuBuffer[o + 3] = c.X;       _cpuBuffer[o + 4] = c.Y;       _cpuBuffer[o + 5] = c.Z;
            _cpuBuffer[o + 6] = s.End.X;   _cpuBuffer[o + 7] = s.End.Y;   _cpuBuffer[o + 8] = s.End.Z;
            _cpuBuffer[o + 9] = c.X;       _cpuBuffer[o + 10] = c.Y;      _cpuBuffer[o + 11] = c.Z;
        }
        GL.BindBuffer(BufferTarget.ArrayBuffer, _vbo);
        int bytes = needed * sizeof(float);
        GL.BufferData(BufferTarget.ArrayBuffer, bytes, System.IntPtr.Zero, BufferUsageHint.DynamicDraw);
        fixed (float* p = _cpuBuffer)
        {
            GL.BufferSubData(BufferTarget.ArrayBuffer, System.IntPtr.Zero, bytes, (System.IntPtr)p);
        }
    }

    public void Render(Float4x4 mvp)
    {
        if (_lineCount == 0) return;
        GL.UseProgram(_program);
        unsafe
        {
            float* p = stackalloc float[16];
            p[0] = mvp.c0.X; p[1] = mvp.c0.Y; p[2] = mvp.c0.Z; p[3] = mvp.c0.W;
            p[4] = mvp.c1.X; p[5] = mvp.c1.Y; p[6] = mvp.c1.Z; p[7] = mvp.c1.W;
            p[8] = mvp.c2.X; p[9] = mvp.c2.Y; p[10] = mvp.c2.Z; p[11] = mvp.c2.W;
            p[12] = mvp.c3.X; p[13] = mvp.c3.Y; p[14] = mvp.c3.Z; p[15] = mvp.c3.W;
            GL.UniformMatrix4(_uMVP, 1, false, p);
        }
        GL.Disable(EnableCap.DepthTest);
        GL.LineWidth(2f);
        GL.BindVertexArray(_vao);
        GL.DrawArrays(PrimitiveType.Lines, 0, _lineCount * 2);
        GL.BindVertexArray(0);
        GL.Enable(EnableCap.DepthTest);
    }

    private static int BuildProgram()
    {
        const string vs = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aColor;
uniform mat4 uMVP;
out vec3 vColor;
void main(){ gl_Position = uMVP * vec4(aPos, 1.0); vColor = aColor; }";
        const string fs = @"#version 330 core
in vec3 vColor;
out vec4 fragColor;
void main(){ fragColor = vec4(vColor, 1.0); }";
        int v = GL.CreateShader(ShaderType.VertexShader); GL.ShaderSource(v, vs); GL.CompileShader(v);
        int f = GL.CreateShader(ShaderType.FragmentShader); GL.ShaderSource(f, fs); GL.CompileShader(f);
        int p = GL.CreateProgram();
        GL.AttachShader(p, v); GL.AttachShader(p, f);
        GL.LinkProgram(p);
        GL.DeleteShader(v); GL.DeleteShader(f);
        return p;
    }

    public void Dispose()
    {
        if (_vao != 0) GL.DeleteVertexArray(_vao);
        if (_vbo != 0) GL.DeleteBuffer(_vbo);
        if (_program != 0) GL.DeleteProgram(_program);
    }
}
