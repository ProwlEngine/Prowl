using System;
using System.Diagnostics;
using System.IO;


namespace Prowl.Veldrid.Samples;


public class RenderMSTracker
{
    Stopwatch sw = new();
    float totalTime = 0;
    float fpsTime = 0;
    float smoothedDelta = 0.0001f;
    const float smoothing = 0.05f;


    public void Begin()
    {
        sw.Restart();
    }


    public void End(double deltaTime)
    {
        fpsTime += (float)deltaTime;

        sw.Stop();
        smoothedDelta += ((float)sw.Elapsed.TotalMilliseconds - smoothedDelta) * smoothing;

        if (fpsTime >= 1)
        {
            fpsTime = 0;
            Console.WriteLine($"Rolling Render MS: {smoothedDelta}. (FPS - not accounting swapchain/windowing): {1000.0f / smoothedDelta}");
        }
    }
}
