using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

using Prowl.Vector;

namespace Prowl.Graphite.Rendering;

/// <summary>
/// Points to find the water at, with the latest answers. Set <see cref="Points"/>, and read the results once
/// <see cref="Ready"/>; they lag the points by the frames the GPU takes to read back, usually one to three. Host thread.
/// </summary>
public sealed class OceanQuery
{
    public OceanQuery(int count)
    {
        Points = new Double2[count];
        Heights = new double[count];
        Normals = new Float3[count];
        Velocities = new float[count];
        Depths = new float[count];
        Flows = new Float2[count];
    }

    /// <summary>The points' x and z in the world.</summary>
    public Double2[] Points { get; }

    /// <summary>Waves shorter than this many metres are left out, so a large hull is not shaken by small ripples.</summary>
    public float SpatialLength { get; set; } = 1f;

    /// <summary>The water's height at each point in the world, the planet's curve included.</summary>
    public double[] Heights { get; }

    /// <summary>The water's normal at each point.</summary>
    public Float3[] Normals { get; }

    /// <summary>How fast the water rises at each point in m/s, from the last two answers.</summary>
    public float[] Velocities { get; }

    /// <summary>How deep the water is under each point, very large in open sea.</summary>
    public float[] Depths { get; }

    /// <summary>The current at each point, in m/s along x and z.</summary>
    public Float2[] Flows { get; }

    /// <summary>Whether answers have arrived.</summary>
    public bool Ready { get; internal set; }

    internal long Asked;
}

/// <summary>
/// Finds the water at <see cref="Queries"/>' points on the GPU every frame, as the ocean's surface draws it, and reads
/// the answers back. Add <see cref="Features"/> to the ocean's world beside it.
/// </summary>
public sealed class OceanQueries : IDisposable
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Push
    {
        public Float4 Sizes, Depth, Shore;
    }

    private readonly Renderer _renderer;
    private readonly OceanFeature _ocean;
    private readonly DeviceBuffer _points, _results;
    private readonly Float4[] _packed;
    private readonly List<(OceanQuery Query, int First)> _batch = new();
    private readonly UploadFeature _upload;
    private readonly DispatchFeature _dispatch;

    /// <param name="capacity">The most points answered in a frame.</param>
    public OceanQueries(Renderer renderer, OceanFeature ocean, int capacity = 4096)
    {
        _renderer = renderer;
        _ocean = ocean;
        Capacity = Math.Clamp(capacity, 1, 4096);
        _packed = new Float4[Capacity * 2];
        ResourceFactory factory = renderer.Device.ResourceFactory;
        _points = factory.CreateBuffer(new BufferDescription((uint)(Capacity * 32), BufferUsage.StructuredBufferReadOnly));
        _results = factory.CreateBuffer(new BufferDescription((uint)(Capacity * 32), BufferUsage.StructuredBufferReadWrite));
        _upload = new UploadFeature(this);
        _dispatch = new DispatchFeature(this, renderer.Effects.Compute("Internal/OceanQuery.glsl"), renderer.Resources.LinearRepeat, renderer.Resources.LinearClamp);
        Features = [_upload, _dispatch];
    }

    public int Capacity { get; }

    /// <summary>The queries answered each frame, the first ones first when they hold more points than the capacity.</summary>
    public List<OceanQuery> Queries { get; } = new();

    /// <summary>The upload of the points and the queries' pass, for the ocean's world's frame features.</summary>
    public IReadOnlyList<FrameFeature> Features { get; }

    private int _count;
    private Push _push;

    // Packs the points, and asks for this frame's answers, which arrive at a later frame
    private void Gather()
    {
        _batch.Clear();
        _count = 0;
        Double3 viewer = _ocean.Viewer;
        double largest = _ocean.CascadeSize(0);
        foreach (OceanQuery query in Queries)
        {
            if (_count + query.Points.Length > Capacity)
                break;
            _batch.Add((query, _count));
            foreach (Double2 point in query.Points)
            {
                double x = point.X - Math.Floor(point.X / largest) * largest, z = point.Y - Math.Floor(point.Y / largest) * largest;
                _packed[_count * 2] = new Float4((float)x, (float)z, (float)(point.X - viewer.X), (float)(point.Y - viewer.Z));
                _packed[_count * 2 + 1] = new Float4(MathF.Max(query.SpatialLength, 1e-3f), 0f, 0f, 0f);
                _count++;
            }
        }
        _push = new Push
        {
            Sizes = new Float4(_ocean.CascadeSize(0), _ocean.CascadeSize(1), _ocean.CascadeSize(2), _ocean.CascadeSize(3)),
            Depth = _ocean.DepthParams,
            Shore = new Float4(Math.Clamp(_ocean.ShallowAttenuation, 0f, 1f), OceanFeature.DepthSlices, _count, _ocean.Resolution),
        };
        if (_count == 0)
            return;

        (OceanQuery Query, int First)[] batch = [.. _batch];
        long asked = Stopwatch.GetTimestamp();
        double seaLevel = _ocean.SeaLevel, radius = _ocean.PlanetRadius;
        _renderer.Readback(_results, result => Answer(result, batch, asked, viewer, seaLevel, radius), 0, (uint)(_count * 32));
    }

    private static void Answer(BufferReadback result, (OceanQuery Query, int First)[] batch, long asked, Double3 viewer, double seaLevel, double radius)
    {
        if (result.Failed)
            return;
        ReadOnlySpan<Float4> answers = MemoryMarshal.Cast<byte, Float4>(result.Data);
        foreach ((OceanQuery query, int first) in batch)
        {
            if (query.Asked > asked)
                continue;
            double seconds = Stopwatch.GetElapsedTime(query.Asked, asked).TotalSeconds;
            bool timed = query.Ready && seconds > 1e-4;
            int count = Math.Min(query.Points.Length, answers.Length / 2 - first);
            for (int i = 0; i < count; i++)
            {
                Float4 answer = answers[(first + i) * 2];
                Float4 flow = answers[(first + i) * 2 + 1];
                Double2 point = query.Points[i];
                double dx = point.X - viewer.X, dz = point.Y - viewer.Z;
                double drop = radius > 0 ? (dx * dx + dz * dz) / (2 * radius) : 0;
                double height = seaLevel + answer.X - drop;
                query.Velocities[i] = timed ? (float)((height - query.Heights[i]) / seconds) : 0f;
                query.Heights[i] = height;
                query.Normals[i] = Float3.Normalize(new Float3(answer.Y, 1f, answer.Z));
                query.Depths[i] = answer.W;
                query.Flows[i] = new Float2(flow.X, flow.Y);
            }
            query.Asked = asked;
            query.Ready = true;
        }
    }

    public void Dispose()
    {
        _points.Dispose();
        _results.Dispose();
    }

    // Copies the frame's points into their buffer, a pass of its own so the queries' pass waits for the copy
    private sealed class UploadFeature(OceanQueries owner) : FrameFeature(FrameSlot.BeforeViews, order: -10)
    {
        public override void Extract() => owner.Gather();

        public override bool IsActive() => owner._count > 0;

        public override void Setup(FrameBuilder builder) => builder.Write(owner._points, BufferAccess.TransferWrite);

        public override void Render(FrameContext context)
            => context.Command.UpdateBuffer(owner._points, 0, owner._packed.AsSpan(0, owner._count * 2));
    }

    private sealed class DispatchFeature(OceanQueries owner, InternalCompute program, Sampler waves, Sampler slices) : FrameFeature(FrameSlot.BeforeViews, order: 10)
    {
        private readonly PropertySet _properties = new();

        public override bool IsActive() => owner._count > 0;

        public override void Setup(FrameBuilder builder)
        {
            builder.Read(owner._points);
            builder.Read(owner._ocean.Displacement);
            builder.Read(owner._ocean.Slopes);
            builder.Read(owner._ocean.Depth);
            builder.Read(owner._ocean.Levels);
            builder.Read(owner._ocean.Flow);
            builder.Write(owner._results);
        }

        public override void Render(FrameContext context)
        {
            CommandBuffer cmd = context.Command;
            _properties.SetTexture("Ocean_Displacement", owner._ocean.Displacement, waves);
            _properties.SetTexture("Ocean_Slopes", owner._ocean.Slopes, waves);
            _properties.SetTexture("Ocean_Depth", owner._ocean.Depth, slices);
            _properties.SetTexture("Ocean_Levels", owner._ocean.Levels, slices);
            _properties.SetTexture("Ocean_Flow", owner._ocean.Flow, slices);
            _properties.SetBuffer("Ocean_Points", owner._points);
            _properties.SetBuffer("Ocean_Results", owner._results);
            cmd.SetComputeShader(program.Program);
            cmd.SetProperties(_properties);
            cmd.SetPushConstants(owner._push);
            cmd.Dispatch((uint)(owner._count + 63) / 64, 1, 1);
        }
    }
}
