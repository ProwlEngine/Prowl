using System.Reflection;

using Microsoft.CodeAnalysis;

using Prowl.CodeGen.Networking;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Networking.Enums;
using Prowl.Runtime.Networking.Serialization;
using Prowl.Runtime.Networking.Utilities;
using Prowl.Vector;
using Prowl.Warp;

using Xunit;

namespace Prowl.CodeGen.Test.Networking;

public class StaticRpcTests
{
    [Fact]
    public void StaticClassesCompileAndRunOfflineWithoutAComponent()
    {
        Type type = Scripts.Compile("""
                                    using Prowl.Runtime.Networking;
                                    using Prowl.Runtime.Networking.Attributes;
                                    using Prowl.Runtime.Networking.Collections;
                                    using Prowl.Runtime.Networking.Enums;
                                    using Prowl.Runtime.Networking.Serialization;
                                    using Prowl.Vector;
                                    public static class Sounds
                                    {
                                        public static string Last;
                                        [Rpc.Broadcast] public static void Play(string name, Float3 position)
                                        {
                                            Last = name + " at " + position.X;
                                        }
                                        [Rpc.Host(RpcFlags.HostOnly)] public static void Play(int id) => Last = "host " + id;
                                    }
                                    """).GetType("Sounds")!;
        type.GetMethod("Play", [typeof(string), typeof(Float3)])!.Invoke(null, ["bell", new Float3(3, 0, 0)]);
        Assert.Equal("bell at 3", type.GetField("Last")!.GetValue(null));
        type.GetMethod("Play", [typeof(int)])!.Invoke(null, [9]);
        Assert.Equal("host 9", type.GetField("Last")!.GetValue(null));
    }

    [Fact]
    public void ReceiversFindUncalledPrivateNestedStaticRpcsAndGeneratedSerializers()
    {
        Assembly assembly = Scripts.Compile("""
                                            using Prowl.Runtime.Networking;
                                            using Prowl.Runtime.Networking.Attributes;
                                            using Prowl.Runtime.Networking.Collections;
                                            using Prowl.Runtime.Networking.Enums;
                                            using Prowl.Runtime.Networking.Serialization;
                                            public struct SoundInfo { public int Volume; }
                                            public class Container
                                            {
                                                private static partial class Sounds
                                                {
                                                    public static int Last;
                                                    [Rpc.Broadcast(RpcFlags.Unreliable)] private static void Play(SoundInfo sound) => Last = sound.Volume;
                                                }
                                            }
                                            """);
        Type type = assembly.GetType("Container+Sounds")!;
        string typeName = $"{type.FullName}, {assembly.GetName().Name}";
        StaticRpcMember rpc = StaticRpcMember.Find(typeName, NetworkCodeGen.NetworkId("Play(SoundInfo)"))!;
        Assert.NotNull(rpc);
        Assert.Null(StaticRpcMember.Find(typeName, 0));
        Assert.Equal(RpcFlags.Unreliable, rpc.Flags);
        Assert.Equal(RpcMode.Broadcast, rpc.Mode);
        using NetWriter arguments = new();
        NetSerializer<int>.Write(arguments, 14);
        rpc.ValidateArguments(arguments.WrittenSpan);
        Assert.Equal(0, type.GetField("Last")!.GetValue(null));
        NetReader reader = new(arguments.WrittenSpan);
        rpc.Invoke(ref reader);
        Assert.Equal(14, type.GetField("Last")!.GetValue(null));
    }

    [Fact]
    public void StaticRpcsWorkInPartialClassesAndAlongsideComponentRpcs()
    {
        Assembly assembly = Scripts.Compile("""
                                            using Prowl.Runtime.Networking;
                                            using Prowl.Runtime.Networking.Attributes;
                                            using Prowl.Runtime.Networking.Collections;
                                            using Prowl.Runtime.Networking.Enums;
                                            using Prowl.Runtime.Networking.Serialization;
                                            public static partial class Events
                                            {
                                                public static int Last;
                                                [Rpc.Host] public static void Set(int value) => Last = value;
                                            }
                                            public class Actor : Prowl.Runtime.Component
                                            {
                                                [Rpc.Broadcast] public static void Global() { }
                                                [Rpc.Broadcast] public void Local() { }
                                            }
                                            """, """
                                                 using Prowl.Runtime.Networking;
                                                 using Prowl.Runtime.Networking.Attributes;
                                                 using Prowl.Runtime.Networking.Collections;
                                                 using Prowl.Runtime.Networking.Enums;
                                                 using Prowl.Runtime.Networking.Serialization;
                                                 public static partial class Events
                                                 {
                                                     [Rpc.Broadcast] public static void Clear() => Last = 0;
                                                 }
                                                 """);
        Type type = assembly.GetType("Events")!;
        type.GetMethod("Set")!.Invoke(null, [12]);
        Assert.Equal(12, type.GetField("Last")!.GetValue(null));
        type.GetMethod("Clear")!.Invoke(null, []);
        Assert.Equal(0, type.GetField("Last")!.GetValue(null));
        Scripts.WithComponent(assembly.GetType("Actor")!, actor =>
        {
            Assert.NotNull(NetworkMembers.Of(actor).Rpc(NetworkCodeGen.NetworkId("Local()")));
            Assert.Null(NetworkMembers.Of(actor).Rpc(NetworkCodeGen.NetworkId("Global()")));
        });
    }

    [Theory]
    [InlineData("public static class Events { [Rpc.Owner] public static void F() { } }")]
    [InlineData("public static class Events { [Rpc.Broadcast(RpcFlags.OwnerOnly)] public static void F() { } }")]
    [InlineData("public static class Events<T> { [Rpc.Host] public static void F() { } }")]
    [InlineData("public class Outer<T> { public static class Events { [Rpc.Host] public static void F() { } } }")]
    public void StaticRpcsRejectOwnershipAndGenericContainers(string source) =>
        Assert.Contains(Scripts.Build("using Prowl.Runtime.Networking.Attributes; using Prowl.Runtime.Networking.Enums; " + source).Errors, e => e.Id == "PROWLNET008");

    [Fact]
    public void StaticRewritePreservesSourceLines()
    {
        SyntaxTree original = Scripts.Parse("""
                                            using Prowl.Runtime.Networking;
                                            using Prowl.Runtime.Networking.Attributes;
                                            using Prowl.Runtime.Networking.Collections;
                                            using Prowl.Runtime.Networking.Enums;
                                            using Prowl.Runtime.Networking.Serialization;
                                            public static class Events
                                            {
                                                [Rpc.Host]
                                                public static void Run(int value)
                                                {
                                                    System.Console.WriteLine(value);
                                                }
                                            }
                                            """);
        string[] before = original.GetText().ToString().Split('\n');
        string[] after = Scripts.Generate(original).Trees[0].GetText().ToString().Split('\n');
        Assert.Equal(before.Length, after.Length);
        int body = Array.FindIndex(before, line => line.Contains("Console.WriteLine"));
        Assert.Equal(before[body], after[body]);
    }
}
