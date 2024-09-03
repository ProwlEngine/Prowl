using System;

using Silk.NET.OpenAL;

namespace Prowl.Runtime.Audio.OpenAL
{
    public class OpenALEngine : AudioEngine, System.IDisposable
    {
        public static ALContext alContext { get; private set; }
        public static AL al { get; private set; }
        public static unsafe Context* context { get; private set; }

        public OpenALEngine()
        {
            alContext = ALContext.GetApi();
            al = AL.GetApi();
            unsafe
            {
                var device = alContext.OpenDevice("");
                if (device == null)
                {
                    Console.WriteLine("Could not create device");
                    return;
                }

                context = alContext.CreateContext(device, null);
                alContext.MakeContextCurrent(context);
            }

            var err = al.GetError();
            if (err != AudioError.NoError)
            {
                Console.WriteLine("OpenAL error: " + err);
            }
        }

        public override void SetListenerPosition(Vector3 position)
        {
            al.SetListenerProperty(ListenerVector3.Position, (float)position.x, (float)position.y, (float)position.z);
        }

        public override void SetListenerVelocity(Vector3 velocity)
        {
            al.SetListenerProperty(ListenerVector3.Velocity, (float)velocity.x, (float)velocity.y, (float)velocity.z);
        }

        public override void SetListenerOrientation(Vector3 forward, Vector3 up)
        {
            unsafe
            {
                float* orientationPtr = stackalloc float[6];

                orientationPtr[0] = (float)forward.x;
                orientationPtr[1] = (float)forward.y;
                orientationPtr[2] = (float)forward.z;
                orientationPtr[3] = (float)up.x;
                orientationPtr[4] = (float)up.y;
                orientationPtr[5] = (float)up.z;

                al.SetListenerProperty(ListenerFloatArray.Orientation, orientationPtr);
            }
        }

        public override AudioBuffer CreateAudioBuffer()
        {
            return new OpenALAudioBuffer();
        }

        public override ActiveAudio CreateAudioSource()
        {
            return new OpenALActiveAudio();
        }

        public void Dispose()
        {
            unsafe
            {
                alContext.DestroyContext(context);
                alContext.CloseDevice(alContext.GetContextsDevice(context));
                alContext.Dispose();
            }
        }
    }
}
