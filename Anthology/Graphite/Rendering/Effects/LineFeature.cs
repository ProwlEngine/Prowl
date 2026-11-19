using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

[StructLayout(LayoutKind.Sequential)]
internal struct LineVertex
{
    public const uint SizeInBytes = 36;

    public Float3 A;
    public Float3 B;

    /// <summary>The width in pixels, and the kind: 0 line, 1 round point, 2 square point.</summary>
    public Float2 Shape;
    public Color32 Color;
}

/// <summary>
/// Lines and points with widths in pixels, built on the host thread, one record each, drawn as instances of a quad.
/// Positions are relative to <see cref="Origin"/>.
/// A batch added to a <see cref="LineFeature"/> is uploaded again only after it changes.
/// </summary>
public sealed class LineBatch
{
    internal LineVertex[] Vertices = new LineVertex[256];
    internal int Count;
    internal int Version;

    /// <summary>Where the batch's positions are measured from, in the world.</summary>
    public Double3 Origin;

    public bool Visible = true;

    /// <summary>How much of a line shows where the scene is in front of it: 0 hides it, 1 draws it over everything.</summary>
    public float HiddenAlpha;

    /// <summary>How much nearer the lines count as when tested against the scene, as a part of their distance, so lines on a surface stay above it.</summary>
    public float DepthBias = 0.002f;

    /// <summary>The distances from the camera the lines fade out between, no fade when the end is 0.</summary>
    public float FadeStart, FadeEnd;

    public int PrimitiveCount => Count;

    public void Clear()
    {
        if (Count == 0)
            return;
        Count = 0;
        Version++;
    }

    /// <summary>Makes room for this many more lines and points at once, so adding them does not grow the batch step by step.</summary>
    public void Reserve(int primitives)
    {
        int needed = Count + primitives;
        if (needed > Vertices.Length)
            Array.Resize(ref Vertices, Math.Max(needed, Vertices.Length * 2));
    }

    public void Line(Float3 a, Float3 b, Color32 color, float width = 1.5f) => Quad(a, b, color, width, 0);

    public void Point(Float3 position, Color32 color, float size = 6f, bool round = true) => Quad(position, position, color, size, round ? 1 : 2);

    public void Polyline(ReadOnlySpan<Float3> points, Color32 color, float width = 1.5f, bool closed = false)
    {
        for (int i = 1; i < points.Length; i++)
            Line(points[i - 1], points[i], color, width);
        if (closed && points.Length > 2)
            Line(points[^1], points[0], color, width);
    }

    /// <summary>The edges of a box.</summary>
    public void Box(Float3 min, Float3 max, Color32 color, float width = 1.5f)
    {
        Span<Float3> c = stackalloc Float3[8];
        for (int i = 0; i < 8; i++)
            c[i] = new Float3((i & 1) == 0 ? min.X : max.X, (i & 2) == 0 ? min.Y : max.Y, (i & 4) == 0 ? min.Z : max.Z);
        for (int i = 0; i < 8; i++)
        {
            for (int bit = 1; bit < 8; bit <<= 1)
            {
                if ((i & bit) == 0)
                    Line(c[i], c[i | bit], color, width);
            }
        }
    }

    /// <summary>
    /// A ground grid on the XZ plane around the origin: a line every step, every majorEvery of them in the major colour,
    /// and the two through the origin in the axis colours.
    /// </summary>
    public void Grid(float extent, float step, int majorEvery, Color32 minor, Color32 major, Color32 axisX, Color32 axisZ, float width = 1f)
    {
        int count = (int)MathF.Floor(extent / step);
        float reach = count * step;
        for (int i = -count; i <= count; i++)
        {
            float at = i * step;
            bool isMajor = majorEvery > 0 && i % majorEvery == 0;
            Line(new Float3(-reach, 0, at), new Float3(reach, 0, at), i == 0 ? axisX : isMajor ? major : minor, i == 0 ? width * 1.5f : width);
            Line(new Float3(at, 0, -reach), new Float3(at, 0, reach), i == 0 ? axisZ : isMajor ? major : minor, i == 0 ? width * 1.5f : width);
        }
    }

    private void Quad(Float3 a, Float3 b, Color32 color, float width, float kind)
    {
        if (Count == Vertices.Length)
            Array.Resize(ref Vertices, Vertices.Length * 2);
        ref LineVertex vertex = ref Vertices[Count++];
        vertex.A = a;
        vertex.B = b;
        vertex.Shape = new Float2(width, kind);
        vertex.Color = color;
        Version++;
    }
}

/// <summary>
/// Draws line batches over a view's colour, tested against the scene's depth in the fragment stage so hidden parts can
/// be dropped or drawn faint. It runs after the tonemapper by default, so colours are display colours and are not
/// exposed or bloomed. The view needs a feature that wrote a colour of its own before it, as a tonemapper does.
/// Single layer views only.
/// </summary>
public sealed class LineFeature : RenderFeature, IDisposable
{
    private const string Source = """
        Shared
        {
            uniform mat4 _LineViewProjection;
            uniform vec4 _LineOffset;
            uniform vec4 _LineScreen;
            uniform vec4 _LineDepth;
            uniform vec4 _LineFade;
            uniform sampler2D _LineSceneDepth;
        }
        Vertex
        {
            in vec3 POSITION0;
            in vec3 TEXCOORD0;
            in vec2 TEXCOORD1;
            in vec4 COLOR0;

            out vec4 vColor;
            out vec4 vShape;
            out float vDistance;

            // The corners of the quad every line and point is an instance of: which end, then which side
            const vec2 corners[6] = vec2[6](vec2(0.0, -1.0), vec2(0.0, 1.0), vec2(1.0, -1.0), vec2(1.0, -1.0), vec2(0.0, 1.0), vec2(1.0, 1.0));

            void main()
            {
                vec2 corner = corners[gl_VertexIndex];
                float width = TEXCOORD1.x;
                float kind = TEXCOORD1.y;
                vec4 a = _LineViewProjection * vec4(POSITION0 + _LineOffset.xyz, 1.0);
                vec4 b = _LineViewProjection * vec4(TEXCOORD0 + _LineOffset.xyz, 1.0);
                float halfWidth = width * 0.5;
                float pad = halfWidth + 1.0;
                vec4 clip;
                if (kind > 0.5)
                {
                    vec2 square = vec2(corner.x * 2.0 - 1.0, corner.y);
                    clip = a;
                    clip.xy += square * pad * 2.0 * _LineScreen.zw * clip.w;
                    vShape = vec4(square * pad, halfWidth, kind);
                }
                else
                {
                    // Bring an end behind the camera up to it, so the line keeps its direction on screen
                    const float nearW = 1e-4;
                    if (a.w < nearW && b.w > nearW)
                        a = mix(a, b, (nearW - a.w) / (b.w - a.w));
                    else if (b.w < nearW && a.w > nearW)
                        b = mix(b, a, (nearW - b.w) / (a.w - b.w));
                    vec2 direction = (b.xy / b.w - a.xy / a.w) * _LineScreen.xy;
                    float len = length(direction);
                    direction = len > 1e-5 ? direction / len : vec2(1.0, 0.0);
                    vec2 normal = vec2(-direction.y, direction.x);
                    bool first = corner.x < 0.5;
                    clip = first ? a : b;
                    vec2 pixels = normal * corner.y * pad + direction * (first ? -0.5 : 0.5);
                    clip.xy += pixels * 2.0 * _LineScreen.zw * clip.w;
                    vShape = vec4(corner.y * pad, 0.0, halfWidth, 0.0);
                }
                vColor = COLOR0;
                vDistance = clip.w;
                gl_Position = clip;
            }
        }
        Fragment
        {
            in vec4 vColor;
            in vec4 vShape;
            in float vDistance;

            layout(location = 0) out vec4 outColor;

            void main()
            {
                float distance = vShape.w < 0.5 ? abs(vShape.x) : vShape.w < 1.5 ? length(vShape.xy) : max(abs(vShape.x), abs(vShape.y));
                float alpha = clamp(vShape.z + 0.5 - distance, 0.0, 1.0);

                // Reversed Z: a larger depth is nearer the camera
                float scene = texelFetch(_LineSceneDepth, ivec2(gl_FragCoord.xy * _LineDepth.zw), 0).r;
                if (gl_FragCoord.z * (1.0 + _LineDepth.y) < scene)
                    alpha *= _LineDepth.x;
                if (_LineFade.y > 0.0)
                    alpha *= 1.0 - smoothstep(_LineFade.x, _LineFade.y, vDistance);
                if (alpha <= 0.0)
                    discard;
                outColor = vec4(vColor.rgb, vColor.a * alpha);
            }
        }
        """;

    private static readonly PropertyID s_viewProjection = "_LineViewProjection", s_offset = "_LineOffset", s_screen = "_LineScreen",
        s_depth = "_LineDepth", s_fade = "_LineFade", s_sceneDepth = "_LineSceneDepth";

    private const ulong RetireFrames = 8;

    private readonly struct Draw
    {
        public readonly LineBatch Batch;
        public readonly LineVertex[]? Immediate;
        public readonly int Count;
        public readonly Double3 Origin;
        public readonly Float4 Depth;
        public readonly Float4 Fade;

        public Draw(LineBatch batch, LineVertex[]? immediate)
        {
            Batch = batch;
            Immediate = immediate;
            Count = batch.Count;
            Origin = batch.Origin;
            Depth = new Float4(batch.HiddenAlpha, batch.DepthBias, 0, 0);
            Fade = new Float4(batch.FadeStart, batch.FadeEnd, 0, 0);
        }
    }

    private sealed class GpuBatch
    {
        public DeviceBuffer? Buffer;
        public LineVertex[]? Pending;
        public int PendingCount;
    }

    private readonly GraphicsDevice _device;
    private readonly GraphicsProgram _program;
    private readonly Sampler _point;

    // Host thread
    private readonly List<LineBatch> _batches = new();
    private readonly Dictionary<LineBatch, int> _sent = new();
    private readonly ConcurrentQueue<(LineBatch Batch, LineVertex[]? Vertices, int Count)> _uploads = new();
    private volatile Draw[] _frame = [];

    // Render thread
    private readonly Dictionary<LineBatch, GpuBatch> _gpu = new();
    private readonly List<(DeviceBuffer Buffer, ulong Frame)> _retired = new();

    public LineFeature(Renderer renderer, RenderSlot slot = RenderSlot.DisplayPost, int order = 0) : base(slot, order)
    {
        _device = renderer.Device;
        _point = renderer.Resources.PointClamp;
        ShaderPass pass = new() { State = new PassState(), Source = Source, SourceName = "Graphite/Lines" };
        ShaderDescription description = pass.Variants(renderer.Compiler, _device.BackendType).Require();
        description.VertexLayouts =
        [
            new VertexLayoutDescription(0, LineVertex.SizeInBytes, VertexStepRate.PerInstance,
                new VertexElementDescription("POSITION0", VertexElementFormat.Float3, 0),
                new VertexElementDescription("TEXCOORD0", VertexElementFormat.Float3, 12),
                new VertexElementDescription("TEXCOORD1", VertexElementFormat.Float2, 24),
                new VertexElementDescription("COLOR0", VertexElementFormat.Byte4_Norm, 32)),
        ];
        description.BlendState = BlendStateDescription.SingleAlphaBlend;
        description.DepthStencilState = DepthStencilStateDescription.Disabled;
        description.RasterizerState = RasterizerStateDescription.CullNone;
        _program = _device.ResourceFactory.CreateGraphicsProgram(description);
        _program.Name = "Graphite/Lines";
    }

    /// <summary>Lines for the next frame only, cleared when it is submitted.</summary>
    public LineBatch Immediate { get; } = new() { HiddenAlpha = 1 };

    /// <summary>Draws a batch every frame until it is removed. Host thread.</summary>
    public void Add(LineBatch batch)
    {
        if (!_batches.Contains(batch))
            _batches.Add(batch);
    }

    public void Remove(LineBatch batch)
    {
        if (!_batches.Remove(batch))
            return;
        _sent.Remove(batch);
        _uploads.Enqueue((batch, null, 0));
    }

    public override void Extract()
    {
        List<Draw> draws = new(_batches.Count + 1);
        foreach (LineBatch batch in _batches)
        {
            if (!_sent.TryGetValue(batch, out int version) || version != batch.Version)
            {
                _sent[batch] = batch.Version;
                _uploads.Enqueue((batch, batch.Vertices.AsSpan(0, batch.Count).ToArray(), batch.Count));
            }
            if (batch.Visible && batch.Count > 0)
                draws.Add(new Draw(batch, null));
        }
        if (Immediate.Count > 0 && Immediate.Visible)
            draws.Add(new Draw(Immediate, Immediate.Vertices.AsSpan(0, Immediate.Count).ToArray()));
        Immediate.Clear();
        _frame = draws.ToArray();
    }

    public override bool IsActive(in FeatureView view) => view.LayerCount == 1;

    public override bool HasWork(in FeatureView view) => _frame.Length > 0 || !_uploads.IsEmpty;

    public override void Setup(FeatureBuilder builder, in FeatureView view)
    {
        builder.ReadScene();
        builder.DrawToColor();
    }

    public override void Render(FeatureContext context)
    {
        context.BeginOutput();
        CommandBuffer cmd = context.Command;
        ulong frameIndex = context.Time.FrameIndex;
        Retire(frameIndex, false);
        TakeUploads();

        Draw[] frame = _frame;
        if (frame.Length == 0)
            return;

        ViewBlock block = context.Block;
        Double3 camera = new((double)block.CameraHigh.X + block.CameraLow.X, (double)block.CameraHigh.Y + block.CameraLow.Y, (double)block.CameraHigh.Z + block.CameraLow.Z);
        Texture depth = context.Depth!;
        FeatureView view = context.View;
        Float4 screen = new(view.Width, view.Height, 1f / view.Width, 1f / view.Height);

        foreach (Draw draw in frame)
        {
            DeviceBuffer? vertices = draw.Immediate != null ? Transient(context, cmd, draw.Immediate) : Resident(cmd, draw.Batch, frameIndex);
            if (vertices == null)
                continue;

            Double3 offset = draw.Origin - camera;
            PropertySet properties = new();
            properties.SetMatrix(s_viewProjection, block.ViewProjection);
            properties.SetFloat4(s_offset, new Float4((float)offset.X, (float)offset.Y, (float)offset.Z, 0));
            properties.SetFloat4(s_screen, screen);
            properties.SetFloat4(s_depth, new Float4(draw.Depth.X, draw.Depth.Y, (float)depth.Width / view.Width, (float)depth.Height / view.Height));
            properties.SetFloat4(s_fade, draw.Fade);
            properties.SetTexture(s_sceneDepth, depth, _point);

            VertexSource source = new VertexSource().SetBuffer("POSITION0", vertices);
            cmd.SetShader(_program);
            cmd.SetVertexSource(source);
            cmd.SetProperties(properties);
            cmd.Draw(6, (uint)draw.Count, 0, 0);
        }
    }

    private void TakeUploads()
    {
        while (_uploads.TryDequeue(out var upload))
        {
            if (upload.Vertices == null)
            {
                if (_gpu.Remove(upload.Batch, out GpuBatch? removed) && removed.Buffer != null)
                    _retired.Add((removed.Buffer, ulong.MaxValue));
                continue;
            }
            if (!_gpu.TryGetValue(upload.Batch, out GpuBatch? gpu))
                _gpu[upload.Batch] = gpu = new GpuBatch();
            gpu.Pending = upload.Vertices;
            gpu.PendingCount = upload.Count;
        }
    }

    private DeviceBuffer? Resident(CommandBuffer cmd, LineBatch batch, ulong frameIndex)
    {
        if (!_gpu.TryGetValue(batch, out GpuBatch? gpu))
            return null;
        if (gpu.Pending != null)
        {
            uint size = (uint)Math.Max(gpu.PendingCount, 16) * LineVertex.SizeInBytes;
            if (gpu.Buffer == null || gpu.Buffer.SizeInBytes < size)
            {
                if (gpu.Buffer != null)
                    _retired.Add((gpu.Buffer, frameIndex));
                gpu.Buffer = _device.ResourceFactory.CreateBuffer(new BufferDescription(size + size / 2, BufferUsage.VertexBuffer));
                gpu.Buffer.Name = "Line batch";
            }
            cmd.UpdateBuffer<LineVertex>(gpu.Buffer, 0, gpu.Pending.AsSpan(0, gpu.PendingCount));
            gpu.Pending = null;
        }
        return gpu.Buffer;
    }

    private DeviceBuffer Transient(FeatureContext context, CommandBuffer cmd, LineVertex[] vertices)
    {
        DeviceBuffer buffer = _device.RentTransientBuffer(context.Graph.Task, new BufferDescription((uint)vertices.Length * LineVertex.SizeInBytes, BufferUsage.VertexBuffer));
        cmd.UpdateBuffer<LineVertex>(buffer, 0, vertices);
        return buffer;
    }

    // Buffers a frame still in flight may read are freed a few frames after they were replaced
    private void Retire(ulong frameIndex, bool all)
    {
        for (int i = _retired.Count - 1; i >= 0; i--)
        {
            (DeviceBuffer buffer, ulong frame) = _retired[i];
            if (frame == ulong.MaxValue && !all)
            {
                _retired[i] = (buffer, frameIndex);
                continue;
            }
            if (all || frameIndex - frame > RetireFrames)
            {
                buffer.Dispose();
                _retired.RemoveAt(i);
            }
        }
    }

    public void Dispose()
    {
        Retire(0, true);
        foreach (GpuBatch gpu in _gpu.Values)
            gpu.Buffer?.Dispose();
        _gpu.Clear();
        _program.Dispose();
    }
}
