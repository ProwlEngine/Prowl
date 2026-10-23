// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Editor.Core.Diagnostics;
using Prowl.Graphite;
using Prowl.Graphite.Debugging;

using Xunit;

namespace Prowl.Editor.Test;

public class SnapshotImagesTests
{
    [Fact]
    public void BgraTexelsComeOutAsRgba()
    {
        byte[] data = [10, 20, 30, 255];
        byte[] rgba = SnapshotImages.ToRgba8(data, new CopyRegion(0, 0, 0, 1, 1, 1, PixelFormat.B8_G8_R8_A8_UNorm), 1f);
        Assert.Equal(new byte[] { 30, 20, 10, 255 }, rgba);
    }

    [Fact]
    public void FloatColorIsScaledByExposureAndClamped()
    {
        byte[] data = new byte[16];
        System.BitConverter.GetBytes(0.25f).CopyTo(data, 0);
        System.BitConverter.GetBytes(2f).CopyTo(data, 4);
        System.BitConverter.GetBytes(0f).CopyTo(data, 8);
        System.BitConverter.GetBytes(1f).CopyTo(data, 12);
        byte[] rgba = SnapshotImages.ToRgba8(data, new CopyRegion(0, 0, 0, 1, 1, 1, PixelFormat.R32_G32_B32_A32_Float), 2f);
        Assert.Equal(new byte[] { 128, 255, 0, 255 }, rgba);
    }
}
