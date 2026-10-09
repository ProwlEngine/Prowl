// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Vector;

using Xunit;

namespace Prowl.Runtime.Test;

public class ReflectionProbeTests : RuntimeTestBase
{
    [Fact]
    public void BakedCapture_SurvivesASaveAndLoadWithItsProbe()
    {
        var go = CreateGameObject("Probe");
        var probe = go.AddComponent<ReflectionProbe>();
        probe.BoxSize = new Float3(4, 5, 6);
        probe.Importance = 3;
        var faces = new byte[6 * 2][];
        for (int i = 0; i < faces.Length; i++)
            faces[i] = [(byte)i, (byte)(i * 3), 7];
        probe.SetBaked(new ReflectionProbe.BakedCubemap { Size = 8, Mips = 2, Faces = faces });

        GameObject copy = Serializer.Deserialize<GameObject>(Serializer.Serialize(typeof(GameObject), go))!;
        var loaded = copy.GetComponent<ReflectionProbe>()!;

        Assert.True(loaded.HasBakedData);
        Assert.Equal(8, loaded.Baked!.Size);
        Assert.Equal(2, loaded.Baked.Mips);
        Assert.Equal(faces, loaded.Baked.Faces);
        Assert.Equal(new Float3(4, 5, 6), loaded.BoxSize);
        Assert.Equal(3, loaded.Importance);
    }
}
