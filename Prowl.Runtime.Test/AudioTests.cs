// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Runtime.Audio;

using Xunit;

namespace Prowl.Runtime.Test;

/// <summary>Tests for audio buffer/data handling (no native device required).</summary>
public class AudioTests
{
    // Read(ref output) must allocate the output buffer when it is null instead of dereferencing null.
    [Fact]
    public void AudioBuffer_Read_AllocatesWhenOutputNull()
    {
        var buffer = new AudioBuffer(8192);
        float[] output = null!;

        var ex = Record.Exception(() => buffer.Read(ref output));

        Assert.Null(ex);
        Assert.NotNull(output);
        Assert.Equal(8192, output.Length);
    }
}
