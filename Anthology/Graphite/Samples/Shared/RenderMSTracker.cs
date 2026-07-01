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

        /*
        string text = $"Rolling Render MS: {smoothedDelta}. (FPS - not accounting swapchain/windowing): {1000.0f / smoothedDelta}\n";

        ProfileSnapshot snapshot = gd.GetProfile();

        text += LogBin("Allocated", snapshot.Allocated);
        text += LogBin("Buffer Memory", snapshot.BufferMem);
        text += LogBin("Buffer Operations", snapshot.BufferOps);
        text += LogBin("Freed", snapshot.Freed);
        text += LogBin("Live", snapshot.Live);
        text += LogBin("Swaps", snapshot.Swaps);

        Update(text);
        */
    }


    public string LogBin<T>(string groupName, ProfileBinGroup<T> group) where T : unmanaged, Enum
    {
        string log = $"{groupName}:\n";

        foreach (T value in Enum.GetValues<T>())
        {
            ProfileCounter o = group[value];
            log += $"\t{value}: Bytes ({o.Bytes}), Count ({o.Count})\n";
        }

        return log;
    }


    public void Update(string text)
    {
        Console.Clear();
        Console.Write(text);
    }
}
