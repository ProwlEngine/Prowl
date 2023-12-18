using JetBrains.Annotations;
using Prowl.Runtime;

namespace Prowl.Standalone;

public class StandaloneApplication : Application
{
    public static FileInfo AssemblyDLL => new FileInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "net8.0", "CSharp.dll"));

    public StandaloneApplication()
    {

    }

    public override void Initialize()
    {
        base.Initialize();

        _AssemblyManager.LoadExternalAssembly(AssemblyDLL.FullName, true);
    }
}
