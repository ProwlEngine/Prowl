// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.Reflection;

using Prowl.CodeGen.Networking;
using Prowl.Editor.Projects.Scripting;
using Prowl.Runtime;
using Prowl.Runtime.Networking;
using Prowl.Runtime.Networking.Utilities;

using Xunit;

namespace Prowl.Editor.Test;

/// <summary>
/// The editor's script compiler runs script code generation (Prowl.CodeGen, tested there) on game scripts:
/// incrementally, reporting its errors with the compiler's, and without upsetting hot reload.
/// </summary>
[Trait("Category", "Build")]
public class ScriptCompilerCodeGenTests : EditorTestHarness
{
    private const string ShipPartA = """
        using Prowl.Runtime;
        using Prowl.Runtime.Networking;
        using Prowl.Runtime.Networking.Attributes;
        using Prowl.Runtime.Networking.Collections;
        using Prowl.Runtime.Networking.Enums;
        using Prowl.Runtime.Networking.Serialization;

        namespace Fleet;

        public partial class Ship : Component
        {
            [Sync] public int Health { get; set; } = 100;

            [Rpc.Broadcast] public void Fire(int power) => Fired += power;

            public int Fired;
        }
        """;

    private const string ShipPartB = """
        using Prowl.Runtime.Networking;
        using Prowl.Runtime.Networking.Attributes;
        using Prowl.Runtime.Networking.Collections;
        using Prowl.Runtime.Networking.Enums;
        using Prowl.Runtime.Networking.Serialization;

        namespace Fleet
        {
            public partial class Ship
            {
                [Sync(SyncFlags.FromHost)] public int Ammo { get; set; } = 30;

                public struct Cargo { public int Crates; }

                [Rpc.Host] public void Load(Cargo cargo) => Ammo += cargo.Crates;
            }
        }
        """;

    private List<uint> SlotsOfFreshShip()
    {
        ScriptCompiler.CompileResult result = ScriptCompiler.CompileAll(Project);
        Assert.True(result.Success, result.Errors);
        Assert.DoesNotContain("EMBA", result.Output);

        GameObject go = new("Ship");
        try
        {
            Type ship = Assembly.Load(File.ReadAllBytes(Project.GameAssemblyPath)).GetType("Fleet.Ship")!;
            return NetworkMembers.Of((Component)go.AddComponent(ship)!).Syncs.Select(s => s.Slot).ToList();
        }
        finally
        {
            go.Dispose();
        }
    }

    [Fact]
    public void EditingOnePartUpdatesTheGeneratedMembersInAnother()
    {
        WriteScript("ShipA.cs", ShipPartA);
        WriteScript("ShipB.cs", "namespace Fleet { public partial class Ship { } }");
        Assert.Equal(new[] { NetworkCodeGen.NetworkId("Health") }, SlotsOfFreshShip());

        // ShipA.cs is untouched; only the other part gains a [Sync], and a type its RPC sends.
        WriteScript("ShipB.cs", ShipPartB);
        Assert.Equal(new[] { NetworkCodeGen.NetworkId("Health"), NetworkCodeGen.NetworkId("Ammo") }, SlotsOfFreshShip());
    }

    [Fact]
    public void GenerationErrorsFailTheCompile()
    {
        WriteScript("Bad.cs", "public class A : Prowl.Runtime.Component { [Prowl.Runtime.Networking.Attributes.Sync] public System.Action X { get; set; } }");

        ScriptCompiler.CompileResult result = ScriptCompiler.CompileAll(Project);

        Assert.False(result.Success);
        Assert.Contains("PROWLNET005", result.Errors + result.Output);
    }
}
