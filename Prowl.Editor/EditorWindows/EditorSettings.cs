using Prowl.Runtime;
using Prowl.Runtime.Resources;

namespace Prowl.Editor.EditorWindows;

public class EditorSettings : IProjectSetting
{
    [Header("Assets")]
    public bool m_HideExtensions = true;
    public float m_ThumbnailSize = 0.0f;


    [Header("Viewports"), Text("Controls:")]
    public float LookSensitivity = 1f;
    public float PanSensitivity = 1f;
    [Space, Text("Rendering Settings:")]
    public float NearClip = 0.02f;
    public float FarClip = 10000f;
    [Space] 
    public float RenderResolution = 1f;
}

public class BuildSettings : IProjectSetting
{
    [Header("Assets")]
    public AssetRef<Scene> StartingScene;
}
