// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using System.IO;

using Xunit;

using static Prowl.Echo.Test.RoundtripTestHelpers;

namespace Prowl.Echo.Test;

// The same object graph should survive a round trip through every format, plus format-specific value
// edge cases (unicode in the size-optimized binary format, whitespace-only XML text, BSON's ulong).
public class FormatConsistency_Tests
{
    public class Graph { public string? Name; public List<int>? Nums; public Graph? Child; }

    private static Graph Sample() => new()
    {
        Name = "root",
        Nums = new() { 1, 2, 3 },
        Child = new Graph { Name = "child", Nums = new() { 4 } }
    };

    [Fact]
    public void FormatConsistency_Text()
    {
        var back = RoundtripText(Sample());
        Assert.Equal("child", back.Child!.Name);
        Assert.Equal(new[] { 1, 2, 3 }, back.Nums);
    }

    [Fact]
    public void FormatConsistency_Json()
    {
        var back = RoundtripJson(Sample());
        Assert.Equal("child", back.Child!.Name);
        Assert.Equal(new[] { 4 }, back.Child.Nums);
    }

    [Fact]
    public void FormatConsistency_Binary()
    {
        var back = RoundtripBinary(Sample());
        Assert.Equal("child", back.Child!.Name);
        Assert.Equal(new[] { 1, 2, 3 }, back.Nums);
    }

    [Fact]
    public void FormatConsistency_Bson()
    {
        var back = Serializer.Deserialize<Graph>(EchoObject.ReadFromBson(Serializer.Serialize(Sample()).WriteToBson()))!;
        Assert.Equal("child", back.Child!.Name);
    }

    [Fact]
    public void FormatConsistency_Yaml()
    {
        var back = Serializer.Deserialize<Graph>(EchoObject.ReadFromYaml(Serializer.Serialize(Sample()).WriteToYaml()))!;
        Assert.Equal("child", back.Child!.Name);
    }

    [Fact]
    public void FormatConsistency_Xml()
    {
        var back = Serializer.Deserialize<Graph>(EchoObject.ReadFromXml(Serializer.Serialize(Sample()).WriteToXml()))!;
        Assert.Equal("child", back.Child!.Name);
    }

    [Fact]
    public void BinarySizeMode_UnicodeString_Roundtrips()
    {
        var echo = new EchoObject("中文");
        var opts = new BinarySerializationOptions { EncodingMode = BinaryEncodingMode.Size };
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true))
            echo.WriteToBinary(w, opts);
        ms.Position = 0;
        using var r = new BinaryReader(ms);
        Assert.Equal("中文", EchoObject.ReadFromBinary(r, opts).StringValue);
    }

    [Fact]
    public void Xml_WhitespaceOnlyString_Preserved()
    {
        var back = EchoObject.ReadFromXml(new EchoObject("   ").WriteToXml());
        Assert.Equal("   ", back.StringValue);
    }

    [Fact]
    public void Bson_ULongMax_Roundtrips()
    {
        var back = EchoObject.ReadFromBson(new EchoObject(ulong.MaxValue).WriteToBson());
        Assert.Equal(ulong.MaxValue, back.ULongValue);
    }
}
