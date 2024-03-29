namespace Prowl.Runtime.Audio.Null
{
    public class NullAudioBuffer : AudioBuffer
    {
        public override void BufferData<T>(T[] buffer, BufferAudioFormat format, int frequency)
        {
        }

        public override void Dispose() { }
    }
}