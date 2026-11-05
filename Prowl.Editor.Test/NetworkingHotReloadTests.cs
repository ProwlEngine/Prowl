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
            public static class Announcements
            {
                EXTRA_STATIC
                [Rpc.Broadcast] public static void Say(string value) { }
            }
            """;
        Assembly v1 = CompileGameAssembly("Actor.cs", script.Replace("EXTRA_STATIC", "").Replace("EXTRA", ""));
        Type actor = v1.GetType("Actor")!;
        var scene = new Scene();
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

        Type global = v1.GetType("Announcements")!;
        FieldInfo staticRpc = RpcField(global);
        uint staticId = ((StaticRpcMember)staticRpc.GetValue(null)!).Id;
        MethodInfo findStatic = typeof(StaticRpcMember).GetMethod("Find", BindingFlags.Static | BindingFlags.NonPublic)!;
        Func<IEnumerable<Assembly>> previous = RuntimeUtils.AssemblySource;
        try
        {
            RuntimeUtils.AssemblySource = () => new[] { v1 };
            Assert.Same(staticRpc.GetValue(null), findStatic.Invoke(null, new object[] { global.AssemblyQualifiedName!, staticId }));

            Assembly v2 = CompileGameAssembly("Actor.cs", script
                .Replace("EXTRA_STATIC", "[Rpc.Host] public static void Added(int value) { }")
                .Replace("EXTRA", "[Sync] public int Score { get; set; }"));
            SceneHotReload.Migrate(scene, v1, v2);
            actor = v2.GetType("Actor")!;
            cached = getMembers.Invoke(null, new object[] { actor })!;
            Assert.Same(RpcField(actor).GetValue(null), getRpc.Invoke(cached, new object[] { id }));

            RuntimeUtils.AssemblySource = () => new[] { v2 };
            global = v2.GetType("Announcements")!;
            staticRpc = global.GetFields(BindingFlags.Static | BindingFlags.NonPublic)
                .Single(f => f.FieldType == typeof(StaticRpcMember) && ((StaticRpcMember)f.GetValue(null)!).Id == staticId);
            Assert.Same(staticRpc.GetValue(null), findStatic.Invoke(null, new object[] { global.AssemblyQualifiedName!, staticId }));
        }
        finally { RuntimeUtils.AssemblySource = previous; }
    }

    private static FieldInfo RpcField(Type type) => type.GetFields(BindingFlags.Static | BindingFlags.NonPublic)
        .Single(f => typeof(RpcMember).IsAssignableFrom(f.FieldType) || f.FieldType == typeof(StaticRpcMember));
}
