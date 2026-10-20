using Prowl.Runtime;
using Prowl.Runtime.Video;

namespace Prowl.Editor;

public static class VideoPlayerMenu
{
    [MenuItem("GameObject/Video/Video Player")]
    public static void CreateVideoPlayer()
    {
        var go = new GameObject("Video Player");
        go.AddComponent<VideoPlayer>();

        // Optional: Pre-attach a MeshRenderer if you want a physical screen setup
        // var meshRenderer = go.AddComponent<MeshRenderer>();
        // go.AddComponent<MeshFilter>();
    }
}
