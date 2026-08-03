using System;

namespace Prowl.Editor.Build;

public class BuildResult
{
    public bool Success { get; set; }

    /// <summary>The user stopped it. Distinct from a failure, which is something going wrong.</summary>
    public bool Cancelled { get; set; }

    public string OutputPath { get; set; } = "";
    public string Log { get; set; } = "";
    public string Errors { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public int AssetCount { get; set; }
}
