using System.Collections.Generic;

using Xunit;

namespace Prowl.Graphite.Variants.Tests;


public class KeywordMapTests
{
    private static Dictionary<int, int> SlotMap(params Keyword[] baseKeywords)
    {
        Dictionary<int, int> map = [];
        for (int i = 0; i < baseKeywords.Length; i++)
            map[baseKeywords[i].NameId] = i;

        return map;
    }


    private static KeywordState[] States(Dictionary<int, int> slots, params Keyword[][] sets)
    {
        KeywordState[] states = new KeywordState[sets.Length];
        for (int i = 0; i < sets.Length; i++)
            states[i] = new KeywordState(slots, sets[i]);

        return states;
    }


    [Fact]
    public void Find_ReturnsIndexForEachState()
    {
        Dictionary<int, int> slots = SlotMap(new Keyword("MODE", "A"));

        KeywordState[] states = States(slots,
            [new Keyword("MODE", "A")],
            [new Keyword("MODE", "B")],
            [new Keyword("MODE", "C")]);

        KeywordMap map = new(states);

        Assert.Equal(0, map.Find(states[0]));
        Assert.Equal(1, map.Find(states[1]));
        Assert.Equal(2, map.Find(states[2]));
    }


    [Fact]
    public void Find_UnknownState_ReturnsMinusOne()
    {
        Dictionary<int, int> slots = SlotMap(new Keyword("MODE", "A"));

        KeywordState[] states = States(slots,
            [new Keyword("MODE", "A")],
            [new Keyword("MODE", "B")]);

        KeywordMap map = new(states);

        KeywordState missing = new(slots, [new Keyword("MODE", "C")]);

        Assert.Equal(-1, map.Find(missing));
    }


    [Fact]
    public void Find_DistinguishesSymmetricCollisions()
    {
        // X=0,Y=1 and X=1,Y=0 share a hash bucket; Find must still resolve them separately.
        Dictionary<int, int> slots = SlotMap(new Keyword("X", "0"), new Keyword("Y", "0"));

        KeywordState[] states = States(slots,
            [new Keyword("X", "0"), new Keyword("Y", "1")],
            [new Keyword("X", "1"), new Keyword("Y", "0")]);

        Assert.Equal(states[0].LongHash(), states[1].LongHash());

        KeywordMap map = new(states);

        Assert.Equal(0, map.Find(states[0]));
        Assert.Equal(1, map.Find(states[1]));
    }


    [Fact]
    public void Find_MatchesEquivalentStateByValue()
    {
        Dictionary<int, int> slots = SlotMap(new Keyword("MODE", "A"));

        KeywordState[] states = States(slots,
            [new Keyword("MODE", "A")],
            [new Keyword("MODE", "B")]);

        KeywordMap map = new(states);

        // A freshly built state with the same keywords should resolve to the stored variant.
        KeywordState query = new(slots, [new Keyword("MODE", "B")]);

        Assert.Equal(1, map.Find(query));
    }


    [Fact]
    public void Find_ResolvesAfterSetKeyword()
    {
        Dictionary<int, int> slots = SlotMap(new Keyword("MODE", "A"));

        KeywordState[] states = States(slots,
            [new Keyword("MODE", "A")],
            [new Keyword("MODE", "B")],
            [new Keyword("MODE", "C")]);

        KeywordMap map = new(states);

        KeywordState query = new(slots, [new Keyword("MODE", "A")]);
        query.SetKeyword(new Keyword("MODE", "C"));

        Assert.Equal(2, map.Find(query));
    }
}
