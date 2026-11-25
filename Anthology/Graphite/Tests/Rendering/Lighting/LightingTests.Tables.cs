using System;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Xunit;

using Vector3 = System.Numerics.Vector3;

namespace Prowl.Graphite.Rendering.Tests;

// Graphite/PBR/Energy.glsl reads the tables every view binds, filtered from texel centres, and its Turquin helpers
// agree with the CPU's.
public sealed partial class LightingTests
{

    private static void AssertNear(double expected, float got, string what, int pixel)
        => Assert.True(Math.Abs(expected - got) <= 1e-3 + 1e-3 * Math.Abs(expected), $"pixel {pixel} {what}: expected {expected}, got {got}");

}
