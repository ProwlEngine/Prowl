using System.Collections.Generic;

using Xunit;

namespace Prowl.Graphite.Shaders.Tests;


public class KeywordStateTests
{
    private static Dictionary<int, int> SlotMap(params Keyword[] baseKeywords)
    {
        Dictionary<int, int> map = [];
        for (int i = 0; i < baseKeywords.Length; i++)
            map[baseKeywords[i].NameId] = i;

        return map;
    }


    [Fact]
    public void SameKeywords_ProduceSameHash()
    {
        Keyword[] keywords = [new Keyword("X", "0"), new Keyword("Y", "1")];
        Dictionary<int, int> slots = SlotMap(keywords);

        KeywordState a = new(slots, keywords);
        KeywordState b = new(slots, keywords);

        Assert.Equal(a.LongHash(), b.LongHash());
        Assert.True(a.Matches(b));
    }


    [Fact]
    public void SetKeyword_ChangesHashAndMatching()
    {
        Keyword[] keywords = [new Keyword("MODE", "A")];
        Dictionary<int, int> slots = SlotMap(keywords);

        KeywordState state = new(slots, keywords);
        KeywordState original = new(slots, keywords);

        state.SetKeyword(new Keyword("MODE", "B"));

        Assert.NotEqual(original.LongHash(), state.LongHash());
        Assert.False(state.Matches(original));
    }


    [Fact]
    public void SetKeyword_RevertingValue_RestoresHash()
    {
        Keyword[] keywords = [new Keyword("MODE", "A")];
        Dictionary<int, int> slots = SlotMap(keywords);

        KeywordState state = new(slots, keywords);
        ulong originalHash = state.LongHash();

        state.SetKeyword(new Keyword("MODE", "B"));
        state.SetKeyword(new Keyword("MODE", "A"));

        Assert.Equal(originalHash, state.LongHash());
    }


    [Fact]
    public void SetKeyword_OnlyAffectsTargetSlot()
    {
        Keyword[] keywords = [new Keyword("X", "0"), new Keyword("Y", "0")];
        Dictionary<int, int> slots = SlotMap(keywords);

        KeywordState state = new(slots, keywords);
        state.SetKeyword(new Keyword("X", "1"));

        KeywordState expected = new(slots, [new Keyword("X", "1"), new Keyword("Y", "0")]);

        Assert.True(state.Matches(expected));
    }


    [Fact]
    public void SymmetricSwap_HashCollides_ButMatchesIsFalse()
    {
        // The per-slot hash is XOR-folded, so swapping values across slots collides.
        Keyword[] slotsBase = [new Keyword("X", "0"), new Keyword("Y", "0")];
        Dictionary<int, int> slots = SlotMap(slotsBase);

        KeywordState xy = new(slots, [new Keyword("X", "0"), new Keyword("Y", "1")]);
        KeywordState yx = new(slots, [new Keyword("X", "1"), new Keyword("Y", "0")]);

        Assert.Equal(xy.LongHash(), yx.LongHash());
        Assert.False(xy.Matches(yx));
    }


    [Fact]
    public void MatchesKeywords_ComparesUpToSharedPrefix()
    {
        Keyword[] keywords = [new Keyword("X", "0"), new Keyword("Y", "1")];
        Dictionary<int, int> slots = SlotMap(keywords);

        KeywordState state = new(slots, keywords);

        Assert.True(state.MatchesKeywords([new Keyword("X", "0")]));
        Assert.False(state.MatchesKeywords([new Keyword("X", "1")]));
    }
}
