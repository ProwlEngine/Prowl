using System;

namespace Prowl.Editor.Build;

/// <summary> Represents the result of a build operation, including success status, output, and metrics. </summary>
public class BuildResult
{
    /// <summary> Whether the build completed successfully. </summary>
    public bool Success { get; set; }

    /// <summary>The user stopped it. Distinct from a failure, which is something going wrong.</summary>
    public bool Cancelled { get; set; }

    /// <summary> The file system path where the build output was written. </summary>
    public string OutputPath { get; set; } = "";
    public string Log { get; set; } = "";
    public string Errors { get; set; } = "";
    public TimeSpan Duration { get; set; }
    public int AssetCount { get; set; }
}
