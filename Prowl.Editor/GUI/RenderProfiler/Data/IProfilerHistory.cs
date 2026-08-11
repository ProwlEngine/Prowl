using System.Collections.Generic;

using Prowl.Editor.Profiling;

namespace Prowl.Editor.GUI.RenderProfiler.Data;

public interface IProfilerHistory
{
    IReadOnlyList<ProfiledFrame> Frames { get; }
    IReadOnlyList<double> Counter(string name);
}
