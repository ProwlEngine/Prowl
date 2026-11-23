using System.Reflection;

using Prowl.Editor.Projects.Scripting;
using Prowl.Runtime;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Resources;

using Xunit;

namespace Prowl.Editor.Test;

[Trait("Category", "Build")]
public class NetworkingHotReloadTests : EditorTestHarness
{
    [Fact]
    public void RecompileRebuildsCachedRpcDescriptorsInsteadOfMigratingTheirDelegates()
    {
        const string script = """
            using Prowl.Runtime;
            using Prowl.Runtime.Networking;
            public class Actor : Component
            {
                [Sync] public int Health { get; set; }
                EXTRA
                [Rpc.Host] public void Act(string value) { }
            }
            """;
        Assembly v1 = CompileGameAssembly("Actor.cs", script.Replace("EXTRA", ""));
        Type actor = v1.GetType("Actor")!;
        Scene scene = Scene.Create("Reload");
        var go = new GameObject("Actor");
        go.AddComponent(actor);
        scene.Add(go);

        var members = typeof(Component).Assembly.GetType("Prowl.Runtime.Networking.NetworkMembers")!;
        MethodInfo getMembers = members.GetMethod("Of", new[] { typeof(Type) })!;
        MethodInfo getRpc = members.GetMethod("Rpc")!;
        FieldInfo rpc = RpcField(actor);
        uint id = ((RpcMember)rpc.GetValue(null)!).Id;
        object cached = getMembers.Invoke(null, new object[] { actor })!;
        Assert.Same(rpc.GetValue(null), getRpc.Invoke(cached, new object[] { id }));

        Assembly v2 = CompileGameAssembly("Actor.cs", script.Replace("EXTRA", "[Sync] public int Score { get; set; }"));
        SceneHotReload.Migrate(scene, v1, v2);
        actor = v2.GetType("Actor")!;
        cached = getMembers.Invoke(null, new object[] { actor })!;
        Assert.Same(RpcField(actor).GetValue(null), getRpc.Invoke(cached, new object[] { id }));
    }

    private static FieldInfo RpcField(Type type) => type.GetFields(BindingFlags.Static | BindingFlags.NonPublic)
        .Single(f => typeof(RpcMember).IsAssignableFrom(f.FieldType));
}
