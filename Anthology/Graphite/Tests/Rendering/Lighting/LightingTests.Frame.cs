using System;

using Prowl.Graphite.ShaderDef;
using Prowl.Vector;

using Xunit;

using Vector2 = System.Numerics.Vector2;
using Vector3 = System.Numerics.Vector3;

namespace Prowl.Graphite.Rendering.Tests;

// A model with no surface of its own walks the lights with a Graphite_LightFrame, its coat lobe on a normal and alpha of
// its own and the light reaching its back, each checked against the CPU.
public sealed partial class LightingTests
{

}
