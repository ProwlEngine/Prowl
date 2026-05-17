// This file is part of the Prowl Game Engine
// Licensed under the MIT License. See the LICENSE file in the project root for details.

using Prowl.Echo;
using Prowl.Scribe;

namespace Prowl.Runtime.Resources;

/// <summary>
/// A font asset that wraps a TrueType/OpenType font file for use in UI text rendering.
/// Stores the raw TTF/OTF bytes so the font can be reconstructed at runtime.
/// The actual glyph atlas is managed per-Canvas by Scribe's FontSystem at runtime.
/// </summary>
public sealed class FontAsset : EngineObject
{
    /// <summary>Raw font file bytes (TTF or OTF). Serialized with the asset.</summary>
    [SerializeField]
    private byte[] _fontData = [];

    [SerializeIgnore]
    private FontFile? _fontFile;

    /// <summary>The Scribe FontFile instance. Lazily created from the raw bytes.</summary>
    public FontFile FontFile
    {
        get
        {
            if (_fontFile == null && _fontData.Length > 0)
                _fontFile = new FontFile(_fontData);
            return _fontFile!;
        }
    }

    /// <summary>Font family name (e.g. "Roboto", "Arial").</summary>
    public string FamilyName => FontFile?.FamilyName ?? "Unknown";

    /// <summary>Font style (Regular, Bold, Italic, etc.).</summary>
    public FontStyle Style => FontFile?.Style ?? FontStyle.Regular;

    public FontAsset() : base("Font") { }

    public FontAsset(byte[] fontData) : base("Font")
    {
        _fontData = fontData;
        _fontFile = new FontFile(fontData);
    }

    public FontAsset(string name, byte[] fontData) : base(name)
    {
        _fontData = fontData;
        _fontFile = new FontFile(fontData);
    }
}
