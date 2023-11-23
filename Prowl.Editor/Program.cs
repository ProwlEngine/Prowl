using Prowl.Runtime;
using Prowl.Editor.Assets;

namespace Prowl.Editor;

public static class Program {
    
    public static int Main(string[] args) {

        EditorApplication editorApplication = new();
        Application.AssetProvider = new EditorAssetProvider();
        editorApplication.Initialize();
        editorApplication.Run();

        return 0;
    }
}
