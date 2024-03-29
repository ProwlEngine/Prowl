namespace Prowl.Runtime.Audio
{
    public enum BufferAudioFormat 
    { 
        Mono8, 
        Mono16,
        MonoF,
        Stereo8, 
        Stereo16,
        StereoF
    }

    public abstract class AudioBuffer : System.IDisposable
    {
        public abstract void BufferData(byte[] buffer, BufferAudioFormat format, int frequency);
        public abstract void Dispose();
    }
}
