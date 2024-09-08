// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

namespace Prowl.Runtime.Audio.Null
{
    public class NullAudioBuffer : AudioBuffer
    {
        public override void BufferData(byte[] buffer, BufferAudioFormat format, int frequency)
        {
        }

        public override void Dispose() { }
    }
}
