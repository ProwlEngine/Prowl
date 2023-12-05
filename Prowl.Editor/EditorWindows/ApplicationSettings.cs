using Prowl.Runtime;

namespace Prowl.Editor.EditorWindows;

public class ApplicationSettings : IProjectSetting
{
    [Header("Rendering")]
    public bool largeWorldSupport = true;
}
