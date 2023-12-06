using Prowl.Runtime;

namespace Prowl.Standalone;

internal class Program {

    public static int Main(string[] args) {

        StandaloneApplication standaloneApplication = new();
        standaloneApplication.Initialize();
        Application.AssetProvider = new StandaloneAssetProvider();
#warning TODO: Load Default Scene
        standaloneApplication.Run();

        return 0;
    }
    
}
