using System;
using System.Collections.Generic;
using System.Linq;

using Prowl.Vector;
using Prowl.Vector.Geometry;

using Xunit;

namespace Prowl.Vector.Tests;

public class CSGSweepProbe
{
    [Fact]
    public void Sweep()
    {
        var bad = new List<string>();
        int runs = 0;
        var random = new System.Random(7);
        for (int trial = 0; trial < 40; trial++)
        {
            Float3 center = new Float3(random.NextSingle() * 2f - 1f, random.NextSingle() * 2f - 1f, random.NextSingle() * 2f - 1f) * 0.8f;
            Float3 size = new Float3(0.3f + random.NextSingle(), 0.3f + random.NextSingle(), 0.3f + random.NextSingle());
            int axis = random.Next(3);
            var scene = new CSGScene();
            Float3 modelPos = new Float3(random.NextSingle() * 10f, random.NextSingle() * 3f, random.NextSingle() * 10f);
            Float4x4 toModel = Float4x4.CreateTranslation(modelPos);
            scene.Add(GeometryGenerator.Box(new Float3(2f), modelPos), CSGOperation.Additive);
            CSGBrushHandle? sub = null;
            for (int step = 0; step < 600; step++)
            {
                float length = size[axis] + step * 0.0137f + random.NextSingle() * 0.01f;
                Float3 s = size;
                s[axis] = length;
                Float3 min = center - size * 0.5f;
                Float3 c = min + s * 0.5f;
                GeometryData box = GeometryGenerator.Box(s, c + modelPos);
                if (sub != null) scene.Remove(sub);
                sub = scene.Add(box, CSGOperation.Subtractive);
                runs++;
                GeometryData output = scene.Build();
                Float3 lo = Maths.Max(min, new Float3(-1f)), hi = Maths.Min(min + s, new Float3(1f));
                Float3 d = Maths.Max(hi - lo, Float3.Zero);
                float expected = 8f - d.X * d.Y * d.Z;
                bool closed = output.IsClosed();
                float volume = output.Volume();
                if (!closed || MathF.Abs(volume - expected) > 1e-2f)
                    bad.Add($"trial {trial} step {step} axis {axis} min {min} s {s}: closed {closed}, volume {volume} expected {expected}");
            }
        }
        Assert.True(bad.Count == 0, $"{bad.Count} of {runs} bad\n" + string.Join("\n", bad.Take(25)));
    }
}
