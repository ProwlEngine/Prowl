using System;
using System.Diagnostics;
using System.IO;


namespace Prowl.Graphite.Samples;


public class RenderMSTracker
{
    GraphicsDevice gd;
    Stopwatch sw = new();
    float fpsTime = 0;
    float smoothedDelta = 0.0001f;
    const float smoothing = 0.05f;
    int _top;


    public RenderMSTracker(GraphicsDevice gd)
    {
        this.gd = gd;
        _top = Console.CursorTop;
    }


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


    public void Update(string text)
    {
        Console.Clear();
        Console.Write(text);
    }
}
