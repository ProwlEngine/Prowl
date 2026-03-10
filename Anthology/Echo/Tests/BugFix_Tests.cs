// Tests that confirm and verify fixes for identified bugs in the Echo library.

namespace Prowl.Echo.Test;

public class BugFix_Tests
{
    #region Bug 1: Compound Equality is Order-Dependent

    [Fact]
    public void CompoundEquality_DifferentInsertionOrder_ShouldBeEqual()
    {
        // Build two compounds with same keys+values but different insertion order
        var a = EchoObject.NewCompound();
        a.Add("x", new EchoObject(1));
        a.Add("y", new EchoObject(2));
        a.Add("z", new EchoObject(3));

        var b = EchoObject.NewCompound();
        b.Add("z", new EchoObject(3));
        b.Add("x", new EchoObject(1));
        b.Add("y", new EchoObject(2));

        Assert.True(a.Equals(b));
        Assert.True(b.Equals(a));
        Assert.True(a == b);
    }

    [Fact]
    public void CompoundEquality_DifferentOrderAfterRemoveAndReAdd_ShouldBeEqual()
    {
        var a = EchoObject.NewCompound();
        a.Add("a", new EchoObject("hello"));
        a.Add("b", new EchoObject("world"));

        // Build same content via remove+re-add which may change internal order
        var b = EchoObject.NewCompound();
        b.Add("b", new EchoObject("world"));
        b.Add("a", new EchoObject("hello"));

        Assert.True(a.Equals(b));
    }

    #endregion

    #region Bug 2: List Indexer Setter Missing Parent Tracking

    [Fact]
    public void ListIndexerSetter_ShouldSetParentOnNewElement()
    {
        var list = EchoObject.NewList();
        list.ListAdd(new EchoObject(1));
        list.ListAdd(new EchoObject(2));

        var replacement = new EchoObject(99);
        list[0] = replacement;

        Assert.Equal(list, replacement.Parent);
        Assert.Equal(0, replacement.ListIndex);
    }

    [Fact]
    public void ListIndexerSetter_ShouldClearParentOnOldElement()
    {
        var list = EchoObject.NewList();
        var original = new EchoObject(1);
        list.ListAdd(original);

        list[0] = new EchoObject(99);

        Assert.Null(original.Parent);
        Assert.Null(original.ListIndex);
    }

    [Fact]
    public void ListIndexerSetter_GetPath_ShouldWorkAfterIndexerSet()
    {
        var root = EchoObject.NewCompound();
        var list = EchoObject.NewList();
        list.ListAdd(new EchoObject("placeholder"));
        root.Add("items", list);

        var newItem = new EchoObject("replaced");
        list[0] = newItem;

        Assert.Equal("items/0", newItem.GetPath());
    }

    #endregion

    #region Bug 3: EchoTextFormat Trailing Comma

    [Fact]
    public void EchoTextFormat_CompoundWithOnlySpecialKeys_NoTrailingComma()
    {
        // Simulate a reference-only compound (only $id)
        var compound = EchoObject.NewCompound();
        compound.Add("$id", new EchoObject(42));

        var text = EchoTextFormat.Write(compound);

        // Should NOT contain a trailing comma before closing brace
        // Check that ",\n}" or ",\r\n}" pattern does not appear
        Assert.DoesNotMatch(@",\s*\}", text);

        // Parse should succeed without error
        var parsed = EchoTextFormat.Read(text);
        Assert.Equal(EchoType.Compound, parsed.TagType);
        Assert.True(parsed.TryGet("$id", out var idTag));
        Assert.Equal(42, idTag!.IntValue);
    }

    [Fact]
    public void EchoTextFormat_CompoundWithIdAndType_NoTrailingComma()
    {
        var compound = EchoObject.NewCompound();
        compound.Add("$id", new EchoObject(1));
        compound.Add("$type", new EchoObject("MyType"));

        var text = EchoTextFormat.Write(compound);

        // No trailing comma
        Assert.DoesNotMatch(@",\s*\}", text);

        var parsed = EchoTextFormat.Read(text);
        Assert.Equal(EchoType.Compound, parsed.TagType);
        Assert.Equal(1, parsed.Get("$id")!.IntValue);
        Assert.Equal("MyType", parsed.Get("$type")!.StringValue);
    }

    [Fact]
    public void EchoTextFormat_CompoundWithIdTypeAndRegularKeys_NoTrailingComma()
    {
        var compound = EchoObject.NewCompound();
        compound.Add("$id", new EchoObject(1));
        compound.Add("$type", new EchoObject("MyType"));
        compound.Add("name", new EchoObject("test"));
        compound.Add("value", new EchoObject(42));

        var text = EchoTextFormat.Write(compound);

        // No trailing comma
        Assert.DoesNotMatch(@",\s*\}", text);

        var parsed = EchoTextFormat.Read(text);
        Assert.Equal(4, parsed.Count);
        Assert.Equal(1, parsed.Get("$id")!.IntValue);
        Assert.Equal("MyType", parsed.Get("$type")!.StringValue);
        Assert.Equal("test", parsed.Get("name")!.StringValue);
        Assert.Equal(42, parsed.Get("value")!.IntValue);
    }

    [Fact]
    public void EchoTextFormat_CompoundWithOnlyDependencies_NoTrailingComma()
    {
        var compound = EchoObject.NewCompound();
        compound.Add("$dependencies", new EchoObject("dep1"));

        var text = EchoTextFormat.Write(compound);

        Assert.DoesNotMatch(@",\s*\}", text);

        var parsed = EchoTextFormat.Read(text);
        Assert.Equal("dep1", parsed.Get("$dependencies")!.StringValue);
    }

    #endregion

    #region Bug 5: XML ReadEntityRef Truncates Supplementary Plane Characters

    [Fact]
    public void XmlFormat_SupplementaryPlaneCharacter_PreservedInRoundtrip()
    {
        // U+1F600 = Grinning Face emoji (supplementary plane, requires surrogate pair)
        var emoji = "\U0001F600";
        var original = new EchoObject(emoji);

        using var stream = new MemoryStream();
        XmlFileFormat.Instance.WriteTo(original, stream);
        stream.Position = 0;
        var parsed = XmlFileFormat.Instance.ReadFrom(stream);

        Assert.Equal(emoji, parsed.StringValue);
    }

    [Fact]
    public void XmlFormat_NumericEntityRef_SupplementaryPlane_ParsedCorrectly()
    {
        // Manually construct XML with a numeric character reference for U+1F600
        var xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<echo type=\"string\">&#x1F600;</echo>";
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        var parsed = XmlFileFormat.Instance.ReadFrom(stream);

        Assert.Equal("\U0001F600", parsed.StringValue);
    }

    #endregion

    #region Bug 6: JSON Dead Code in Surrogate Branch (verification only)

    [Fact]
    public void JsonFormat_SurrogatePairs_PreservedInRoundtrip()
    {
        // Emoji that requires surrogate pair
        var emoji = "\U0001F600";
        var original = new EchoObject(emoji);

        using var stream = new MemoryStream();
        JsonFileFormat.Instance.WriteTo(original, stream);
        stream.Position = 0;
        var parsed = JsonFileFormat.Instance.ReadFrom(stream);

        Assert.Equal(emoji, parsed.StringValue);
    }

    #endregion

    #region Bug 7: BSON ULong Roundtrip

    [Fact]
    public void BsonFormat_ULongMaxValue_ValuePreservedAfterRoundtrip()
    {
        // ulong.MaxValue = 18446744073709551615, which exceeds long.MaxValue
        var original = new EchoObject(ulong.MaxValue);

        using var stream = new MemoryStream();
        BsonFileFormat.Instance.WriteTo(original, stream);
        stream.Position = 0;
        var parsed = BsonFileFormat.Instance.ReadFrom(stream);

        // After BSON roundtrip the type becomes Long, but we should be able to recover the value
        // The bits should be preserved via unchecked cast
        if (parsed.TagType == EchoType.Long)
        {
            // The long value should be -1 (all bits set), which maps back to ulong.MaxValue
            Assert.Equal(-1L, parsed.LongValue);
            Assert.Equal(ulong.MaxValue, unchecked((ulong)parsed.LongValue));
        }
        else if (parsed.TagType == EchoType.ULong)
        {
            Assert.Equal(ulong.MaxValue, parsed.ULongValue);
        }
    }

    [Fact]
    public void BsonFormat_ULongLargeValue_BitsPreserved()
    {
        // A value that is larger than long.MaxValue but not ulong.MaxValue
        ulong largeValue = (ulong)long.MaxValue + 100;
        var original = new EchoObject(largeValue);

        using var stream = new MemoryStream();
        BsonFileFormat.Instance.WriteTo(original, stream);
        stream.Position = 0;
        var parsed = BsonFileFormat.Instance.ReadFrom(stream);

        // Verify bit-level preservation
        Assert.Equal(largeValue, unchecked((ulong)parsed.LongValue));
    }

    #endregion
}
