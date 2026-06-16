using System;
using System.Collections.Generic;


namespace Prowl.Graphite.Shaders;


/// <summary>
/// A set of precompiled shader variants selected at runtime by keyword state. Generic over the
/// resolved variant type <typeparamref name="T"/> (for example a <c>GraphicsProgram</c> in
/// Graphite, or a higher-level material/asset type in the engine runtime).
/// <para>
/// This is the keyword-driven successor to the old core <c>ShaderPass</c>. It owns no render or
/// pipeline state; that now lives inside each compiled variant.
/// </para>
/// </summary>
public sealed class ShaderVariantSet<T>
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
    /// Creates a variant set from precompiled variants and their matching keyword sets. The current
    /// active variant can be changed by setting different keywords on the set.
    /// </summary>
    /// <param name="variants">The precompiled variants. Each variant should have a corresponding
    /// keyword set at the same index in <paramref name="keywords"/>.</param>
    /// <param name="keywords">The keyword sets for each variant.</param>
    public ShaderVariantSet(T[] variants, Keyword[][] keywords)
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


    public void SetKeyword(Keyword keyword)
    {
        _state.SetKeyword(keyword);
        ActiveVariant = _variants[_keywordMap.Find(_state)];
    }


    public void SetKeywords(params Keyword[] keywords)
    {
        for (int i = 0; i < keywords.Length; i++)
            _state.SetKeyword(keywords[i]);

        ActiveVariant = _variants[_keywordMap.Find(_state)];
    }
}
