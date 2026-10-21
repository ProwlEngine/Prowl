// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.OrigamiUI;
using Prowl.OrigamiUI.CSharp;

using Xunit;

namespace Prowl.Origami.CSharp.Tests;

public class NavigationTests
{
    private const string Code = @"
interface IShape { float Area(); }
abstract class Shape : IShape { public abstract float Area(); }
class Circle : Shape { public override float Area() => 3f; }
class Square : Shape { public override float Area() => 1f; }
class User
{
    void M()
    {
        Circle circle = new Circle();
        circle.Area();
        circle.Area();
    }
}";

    private static RoslynCSharpSession Open() => new(new CSharpWorkspace(CSharpWorkspace.GetPlatformReferences()), null, Code);

    private static int At(string marker, int skip = 0) => Code.IndexOf(marker, StringComparison.Ordinal) + skip;

    private static string NameAt(CodeLocation location) => Code.Substring(location.Start, location.Length);

    [Fact]
    public async Task ImplementationsOfInterfaceAndAbstractMember()
    {
        var session = Open();

        var types = await session.GetImplementationsAsync(At("IShape {", 1), default);
        Assert.Contains(types, l => NameAt(l) == "Shape");
        Assert.Contains(types, l => NameAt(l) == "Circle");

        var members = await session.GetImplementationsAsync(At("abstract float Area", 16), default);
        Assert.Equal(2, members.Count(l => NameAt(l) == "Area"));
    }

    [Fact]
    public async Task BaseOfOverrideAndType()
    {
        var session = Open();

        var bases = await session.GetBaseDefinitionsAsync(At("override float Area() => 3f", 16), default);
        var baseMember = Assert.Single(bases);
        Assert.True(baseMember.Start > At("abstract class Shape") && baseMember.Start < At("class Circle"));

        var typeBases = await session.GetBaseDefinitionsAsync(At("class Circle", 7), default);
        Assert.Contains(typeBases, l => NameAt(l) == "Shape");
    }

    [Fact]
    public async Task TypeDefinitionOfLocal()
    {
        var session = Open();
        var locations = await session.GetTypeDefinitionAsync(At("circle.Area", 1), default);
        var type = Assert.Single(locations);
        Assert.Equal("Circle", NameAt(type));
        Assert.Equal(At("class Circle", 6), type.Start);
    }

    [Fact]
    public async Task CodeLensCountsReferences()
    {
        var session = Open();
        var lenses = await session.GetCodeLensesAsync(0, Code.Length, default);
        var circle = Assert.Single(lenses, l => l.Offset == At("class Circle", 6));
        Assert.Equal("2 references", circle.Text);
        var area = Assert.Single(lenses, l => l.Offset == At("override float Area() => 3f", 15));
        Assert.Equal("2 references", area.Text);
    }

    [Fact]
    public async Task PropertyReferencesSkipAccessorDeclarations()
    {
        string code = "class C { public string Name { get; init; } void M() { Name = Name + \"x\"; } }";
        var session = new RoslynCSharpSession(new CSharpWorkspace(CSharpWorkspace.GetPlatformReferences()), null, code);
        var references = await session.FindReferencesAsync(code.IndexOf("Name", StringComparison.Ordinal), default);
        Assert.Equal(3, references.Count);
        Assert.All(references, r => Assert.Equal("Name", code.Substring(r.Start, r.Length)));
    }
}
