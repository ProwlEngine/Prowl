using System;
using System.Collections.Generic;


namespace Prowl.Graphite.ShaderDef;


/// <summary>
/// A set of precompiled shader variants selected at runtime by keyword state. Generic over the
/// resolved variant type <typeparamref name="T"/> (for example a <c>GraphicsProgram</c> in
/// Graphite, or a higher-level material/asset type in the engine runtime).
/// </summary>
public sealed class VariantSet<T>
{
    /// <summary>
    /// The variant currently selected by the keyword state. Updated by <see cref="SetKeyword"/>
    /// and <see cref="SetKeywords"/>.
    /// </summary>
    public T ActiveVariant { get; private set; }


    private readonly Dictionary<int, int> _nameIDToSlot;
    private readonly T[] _variants;
    private readonly KeywordMap _keywordMap;
    private KeywordState _state;


    /// <summary>
    /// Creates a variant set from a list of values and their matching keyword sets. The current
    /// active variant can be changed by setting different keywords on the set.
    /// </summary>
    /// <param name="variants">The variant values. Each variant should have a corresponding
    /// keyword set at the same index in <paramref name="keywords"/>.</param>
    /// <param name="keywords">The keyword sets for each variant.</param>
    public VariantSet(T[] variants, Keyword[][] keywords)
    {
        if (variants.Length > keywords.Length)
            throw new ArgumentOutOfRangeException(nameof(variants), "More variants than keywords have been specified. Please ensure each variant has a matching keyword set.");

        _variants = variants;
        _nameIDToSlot = [];

        Keyword[] baseKeywords = keywords[0];
        for (int i = 0; i < baseKeywords.Length; i++)
        {
            Keyword keyword = baseKeywords[i];

            _nameIDToSlot[keyword.NameId] = i;
        }

        // Keyword sets are capped by the amount of variants. Extra keyword sets are ignored.
        KeywordState[] mapStates = new KeywordState[variants.Length];
        for (int i = 0; i < mapStates.Length; i++)
            mapStates[i] = new KeywordState(_nameIDToSlot, keywords[i]);

        _keywordMap = new KeywordMap(mapStates);

        _state = new KeywordState(_nameIDToSlot, keywords[0]);

        ActiveVariant = _variants[_keywordMap.Find(_state)];
    }


    /// <summary>
    /// Sets a given keyword on the set's current state and updates the active variant to the
    /// closest compiled variant. Returns <c>false</c> if the keyword's name is not part of this set
    /// (the unknown keyword is ignored); otherwise <c>true</c>.
    /// </summary>
    /// <param name="keyword">The keyword to apply.</param>
    public bool SetKeyword(Keyword keyword)
    {
        bool known = _state.SetKeyword(keyword);
        ActiveVariant = _variants[_keywordMap.FindNearest(_state)];
        return known;
    }


    /// <summary>
    /// Sets a list of keywords on the set's current state and updates the active variant to the
    /// closest compiled variant. Faster than calling <see cref="SetKeyword"/> in succession since
    /// it batches the lookup. Returns <c>false</c> if any keyword name is not part of this set
    /// (unknown keywords are ignored); otherwise <c>true</c>.
    /// </summary>
    public bool SetKeywords(params Keyword[] keywords)
    {
        bool allKnown = true;
        for (int i = 0; i < keywords.Length; i++)
            allKnown &= _state.SetKeyword(keywords[i]);

        ActiveVariant = _variants[_keywordMap.FindNearest(_state)];
        return allKnown;
    }
}
